using StinkyFootboard.Core;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace StinkyFootboard.MacroHost;

/// <summary>
/// Windows SendInput executor. Never changes footboard firmware. Text uses
/// Unicode input pairs without added delays; Steps execute serially.
/// Outstanding down events are released on completion, failure, or Stop.
/// </summary>
internal static class HostMacroPlayback
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const uint KeyExtended = 0x0001;
    private const uint KeyUnicode = 0x0004;
    private const uint MouseLeftDown = 0x0002, MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008, MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020, MouseMiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;
    private const int WheelDelta = 120;

    public static async Task PlayAsync(HostMacroBinding binding,
        int defaultPressMilliseconds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        HostMacroTiming.ValidateDelay(defaultPressMilliseconds);
        if (binding.Mode == HostMacroMode.Text)
        {
            SendText(binding.Text, cancellationToken);
            return;
        }

        if (binding.Mode != HostMacroMode.Steps || binding.Steps is null)
            throw new InvalidDataException("Unsupported or incomplete macro.");

        var heldKeys = new HashSet<int>();
        var heldButtons = new HashSet<int>();
        try
        {
            foreach (HostMacroStep step in binding.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                step.Validate();
                int duration = step.Milliseconds == 0
                    ? defaultPressMilliseconds : step.Milliseconds;

                switch (step.Action)
                {
                    case HostMacroAction.Delay:
                        await Task.Delay(step.Milliseconds, cancellationToken);
                        break;
                    case HostMacroAction.KeyDown:
                        SendKey(step.Key, false);
                        heldKeys.Add(step.Key);
                        break;
                    case HostMacroAction.KeyUp:
                        SendKey(step.Key, true);
                        heldKeys.Remove(step.Key);
                        break;
                    case HostMacroAction.KeyPress:
                        SendKey(step.Key, false);
                        heldKeys.Add(step.Key);
                        await Task.Delay(duration, cancellationToken);
                        SendKey(step.Key, true);
                        heldKeys.Remove(step.Key);
                        break;
                    case HostMacroAction.LeftMouseDown:
                    case HostMacroAction.RightMouseDown:
                    case HostMacroAction.MiddleMouseDown:
                        {
                            int button = ButtonFor(step.Action);
                            SendMouseButton(button, false);
                            heldButtons.Add(button);
                            break;
                        }
                    case HostMacroAction.LeftMouseUp:
                    case HostMacroAction.RightMouseUp:
                    case HostMacroAction.MiddleMouseUp:
                        {
                            int button = ButtonFor(step.Action);
                            SendMouseButton(button, true);
                            heldButtons.Remove(button);
                            break;
                        }
                    case HostMacroAction.LeftMouseClick:
                    case HostMacroAction.RightMouseClick:
                    case HostMacroAction.MiddleMouseClick:
                        await ClickAsync(ButtonFor(step.Action), duration, heldButtons,
                            cancellationToken);
                        break;
                    case HostMacroAction.LeftMouseDoubleClick:
                    case HostMacroAction.RightMouseDoubleClick:
                    case HostMacroAction.MiddleMouseDoubleClick:
                        {
                            int button = ButtonFor(step.Action);
                            await ClickAsync(button, duration, heldButtons, cancellationToken);
                            await Task.Delay(Math.Min(duration, 60), cancellationToken);
                            await ClickAsync(button, duration, heldButtons, cancellationToken);
                            break;
                        }
                    case HostMacroAction.Beep:
                        // Console.Beep is synchronous; offload it so cancellation
                        // stops future steps without blocking the UI/hook thread.
                        await Task.Run(() => Console.Beep(
                            step.FrequencyHz,
                            step.Milliseconds == 0 ? 120 : step.Milliseconds), cancellationToken);
                        break;
                    case HostMacroAction.WheelUp:
                    case HostMacroAction.WheelDown:
                        SendWheel(step.Action == HostMacroAction.WheelUp
                            ? step.WheelNotches : -step.WheelNotches);
                        break;
                    default:
                        throw new InvalidDataException($"Unknown action {step.Action}.");
                }
            }
        }
        finally
        {
            // A macro may deliberately use separate KeyDown/KeyUp actions.
            // Release any that remain held when it finishes or is cancelled:
            // a truncated macro must never strand Ctrl, Shift or mouse buttons.
            foreach (int key in heldKeys)
            {
                try { SendKey(key, true); }
                catch { /* preserve primary error, continue releasing */ }
            }
            foreach (int button in heldButtons)
            {
                try { SendMouseButton(button, true); }
                catch { /* preserve primary error, continue releasing */ }
            }
        }
    }

    private static async Task ClickAsync(int button, int duration,
        HashSet<int> heldButtons, CancellationToken token)
    {
        SendMouseButton(button, false);
        heldButtons.Add(button);
        await Task.Delay(duration, token);
        SendMouseButton(button, true);
        heldButtons.Remove(button);
    }

    private static int ButtonFor(HostMacroAction action) => action switch
    {
        HostMacroAction.LeftMouseDown or HostMacroAction.LeftMouseUp or
        HostMacroAction.LeftMouseClick or HostMacroAction.LeftMouseDoubleClick => 0,
        HostMacroAction.RightMouseDown or HostMacroAction.RightMouseUp or
        HostMacroAction.RightMouseClick or HostMacroAction.RightMouseDoubleClick => 1,
        _ => 2
    };

    private static void SendText(string value, CancellationToken token)
    {
        // Each UTF-16 unit is sent as down/up with KEYEVENTF_UNICODE.
        // Consecutive surrogate units support emoji and other non-BMP text
        // in Unicode-aware windows. No artificial per-character wait.
        for (int start = 0; start < value.Length; start += 128)
        {
            token.ThrowIfCancellationRequested();
            int count = Math.Min(128, value.Length - start);
            var inputs = new Input[count * 2];
            for (int i = 0; i < count; i++)
            {
                ushort codeUnit = value[start + i];
                inputs[i * 2] = new Input
                {
                    Type = InputKeyboard,
                    Union = new InputUnion { Keyboard =
                        new KeyboardInput { Scan = codeUnit, Flags = KeyUnicode } }
                };
                inputs[i * 2 + 1] = new Input
                {
                    Type = InputKeyboard,
                    Union = new InputUnion { Keyboard =
                        new KeyboardInput { Scan = codeUnit, Flags = KeyUnicode | KeyUp } }
                };
            }
            Send(inputs);
        }
    }

    private static bool IsExtendedKey(int vk) =>
        vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
        or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90
        or 0xA3 or 0xA5;

    private static void SendKey(int vk, bool up)
    {
        uint flags = (up ? KeyUp : 0) | (IsExtendedKey(vk) ? KeyExtended : 0);
        Send([new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput { VirtualKey = (ushort)vk, Flags = flags }
            }
        }]);
    }

    private static void SendMouseButton(int button, bool up)
    {
        uint flags = button switch
        {
            0 => up ? MouseLeftUp : MouseLeftDown,
            1 => up ? MouseRightUp : MouseRightDown,
            _ => up ? MouseMiddleUp : MouseMiddleDown
        };
        Send([new Input
        {
            Type = InputMouse,
            Union = new InputUnion { Mouse = new MouseInput { Flags = flags } }
        }]);
    }

    private static void SendWheel(int notches)
    {
        Send([new Input
        {
            Type = InputMouse,
            Union = new InputUnion
            {
                Mouse = new MouseInput
                {
                    MouseData = unchecked((uint)(notches * WheelDelta)),
                    Flags = MouseWheel
                }
            }
        }]);
    }

    private static void Send(Input[] inputs)
    {
        int size = Marshal.SizeOf<Input>();
        if (size != (Environment.Is64BitProcess ? 40 : 28))
            throw new InvalidOperationException($"Unexpected native INPUT size {size}.");
        uint result = SendInput((uint)inputs.Length, inputs, size);
        if (result != inputs.Length)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error,
                $"Windows SendInput accepted {result}/{inputs.Length} events (error {error}). " +
                "Elevated/protected or raw-input applications may reject injection.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey, Scan;
        public uint Flags, Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParamLow, ParamHigh;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);
}
