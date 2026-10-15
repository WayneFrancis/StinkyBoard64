using StinkyFootboard.Core;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace StinkyFootboard.MacroHost;

/// <summary>
/// Opt-in Windows-only WH_KEYBOARD_LL interception for configured key combos. The hook has NO per-device identity.
/// If it is armed, the same trigger from any keyboard is intercepted.
/// Does not modify the footboard's firmware or send HID control reports.
/// </summary>
internal sealed class KeyboardMacroHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x10;
    private const uint KeyeventfKeyUp = 0x0002;
    private const uint KeyeventfScancode = 0x0008;
    private const uint InputKeyboard = 1;

    private readonly LowLevelKeyboardProc _callback;
    private readonly HostMacroBinding[] _bindings;
    private readonly HostPhysicalModifiers _modifiers = new();
    private readonly HashSet<int> _heldKeys = [];
    private readonly HashSet<int> _suppressed = [];
    private readonly SemaphoreSlim _sequenceGate = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private nint _hook;
    private bool _disposed;
    private int _identifyNextPhysicalKey;
    private int _stepDelayMilliseconds;

    public event Action<string>? StatusChanged;

    /// <summary>
    /// Explicit self-test for the Windows injection path. No footboard,
    /// firmware write or trigger key is involved. Caller arranges a delay
    /// so Notepad can take focus before the first character is sent.
    /// </summary>
    public static Task TestInjectionAsync(string text, int delayMilliseconds,
        CancellationToken cancellationToken)
    {
        HostMacroTiming.ValidateDelay(delayMilliseconds);
        return HostMacroPlayback.PlayAsync(
            new HostMacroBinding { Text = text }, delayMilliseconds, cancellationToken);
    }

    public KeyboardMacroHook(HostMacroProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        _stepDelayMilliseconds = profile.StepDelayMilliseconds;
        _bindings = profile.Bindings.Where(b => b.IsConfigured)
            .Select(b => b.Copy()).ToArray();
        _callback = HookCallback;
    }

    /// <summary>
    /// Records only the next non-injected key-down event, explicitly
    /// requested by the user. No background keylogging or text capture.
    /// Reports VK/scan code only, and does not change suppression rules.
    /// </summary>
    /// <summary>
    /// Change speed without reinstalling the keyboard hook. An in-flight
    /// sequence finishes using its original delay; future triggers use the
    /// new delay captured when their playback begins.
    /// </summary>
    public void SetStepDelay(int milliseconds)
    {
        HostMacroTiming.ValidateDelay(milliseconds);
        if (_disposed) throw new ObjectDisposedException(nameof(KeyboardMacroHook));
        Volatile.Write(ref _stepDelayMilliseconds, milliseconds);
    }

    public void IdentifyNextPhysicalKey()
    {
        if (_disposed || _hook == 0)
            throw new InvalidOperationException("Enable the hook first.");
        Interlocked.Exchange(ref _identifyNextPhysicalKey, 1);
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(KeyboardMacroHook));
        if (_hook != 0) throw new InvalidOperationException("Keyboard hook already running.");
        if (_bindings.Length == 0)
            throw new InvalidOperationException("Enter at least one macro before enabling.");

        // Low-level hooks execute on the installing thread's message pump,
        // avoiding the need to inject a .NET DLL into other applications.
        nint module = GetModuleHandle(null);
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, module, 0);
        if (_hook == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Windows rejected the low-level keyboard hook.");
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code < 0 || _disposed || _hook == 0)
            return CallNextHookEx(_hook, code, wParam, lParam);
        try
        {
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if ((info.Flags & LlkhfInjected) != 0)
                return CallNextHookEx(_hook, code, wParam, lParam);

            uint message = unchecked((uint)wParam.ToInt64());
            bool down = message is WmKeyDown or WmSysKeyDown;
            bool up = message is WmKeyUp or WmSysKeyUp;
            if (!down && !up)
                return CallNextHookEx(_hook, code, wParam, lParam);

            if (down && Interlocked.Exchange(ref _identifyNextPhysicalKey, 0) == 1)
                Report($"IDENTIFIED VK 0x{info.VirtualKey:X2}, " +
                    $"scan 0x{info.ScanCode:X2}, flags 0x{info.Flags:X2}. " +
                    "Only this one physical key-down was inspected.");

            if (HostPhysicalModifiers.IsSyntheticShiftEvent(
                info.VirtualKey, info.ScanCode) &&
                _bindings.Any(b => b.EffectiveTrigger.Shift != HostModifierRequirement.None))
            {
                Report($"Windows synthetic Shift {(down ? "DOWN" : "UP")} " +
                    $"(scan 0x{info.ScanCode:X3}) ignored by physical modifier tracker. " +
                    $"Physical modifiers: {_modifiers.State}.");
            }
            if (_modifiers.Update(info.VirtualKey, info.ScanCode, info.Flags, down))
                return CallNextHookEx(_hook, code, wParam, lParam);

            // Numpad 7 with Shift can be delivered as VK_HOME (0x24)
            // even though the physical key is keypad 7 (scan 0x47).
            // Match on physical keypad identity for all NumLock states.
            int key = HostKeyIdentity.NormalizeVirtualKey(
                info.VirtualKey, info.ScanCode, info.Flags);
            if (up)
            {
                _heldKeys.Remove(key);
                return _suppressed.Remove(key)
                    ? 1 : CallNextHookEx(_hook, code, wParam, lParam);
            }
            if (!_heldKeys.Add(key))
                return _suppressed.Contains(key)
                    ? 1 : CallNextHookEx(_hook, code, wParam, lParam);

            // Exact physical left/right modifier combinations. If both a
            // general and a specialized trigger match, prefer the subset.
            var matches = _bindings
                .Where(b => b.EffectiveTrigger.Key == key &&
                    b.EffectiveTrigger.Matches(_modifiers.State)).ToArray();
            if (matches.Length == 0)
            {
                // Report only candidate hotkeys, not arbitrary key presses.
                if (_bindings.Any(b => b.EffectiveTrigger.Key == key))
                    Report($"HOTKEY NEAR MISS: raw VK 0x{info.VirtualKey:X2}, " +
                        $"scan 0x{info.ScanCode:X2}, flags 0x{info.Flags:X2}, " +
                        $"normalized VK 0x{key:X2}, modifiers {_modifiers.State}. " +
                        "The key matches a configured trigger, but its modifier " +
                        "requirements were not met.");
                return CallNextHookEx(_hook, code, wParam, lParam);
            }

            HostMacroBinding chosen = matches[0];
            foreach (var candidate in matches.Skip(1))
                if (candidate.EffectiveTrigger.IsSubsetOf(chosen.EffectiveTrigger))
                    chosen = candidate;

            _suppressed.Add(key);
            Report($"Trigger {chosen.EffectiveTrigger} matched. Queuing macro " +
                $"({(chosen.Mode == HostMacroMode.Text ? $"{chosen.Text.Length} chars" :
                    $"{chosen.Steps.Count} steps")}).");
            // Hook callbacks must return immediately; serialize playback outside.
            _ = Task.Run(() => PlayAsync(chosen, _stopping.Token));
            return 1;
        }
        catch (Exception ex)
        {
            Report("Hook error: " + ex.Message);
            return CallNextHookEx(_hook, code, wParam, lParam);
        }
    }

    private async Task PlayAsync(HostMacroBinding binding, CancellationToken token)
    {
        bool acquired = false;
        try
        {
            await _sequenceGate.WaitAsync(token);
            acquired = true;
            int timing = Volatile.Read(ref _stepDelayMilliseconds);
            Report($"{binding.EffectiveTrigger}: executing " +
                (binding.Mode == HostMacroMode.Text
                    ? $"Unicode text ({binding.Text.Length} chars; no added delay)"
                    : $"{binding.Steps.Count} steps (default hold {timing} ms)"));
            await HostMacroPlayback.PlayAsync(binding, timing, token);
            Report($"{binding.EffectiveTrigger}: macro completed.");
        }
        catch (OperationCanceledException) { Report("Macro playback stopped."); }
        catch (Exception ex) { Report($"{binding.EffectiveTrigger} failed: {ex.Message}"); }
        finally
        {
            if (acquired) _sequenceGate.Release();
        }
    }

    internal static string InputLayoutDescription =>
        $"Windows {(Environment.Is64BitProcess ? "x64" : "x86")}: " +
        $"INPUT size {Marshal.SizeOf<Input>()} bytes, " +
        $"union offset {Marshal.OffsetOf<Input>(nameof(Input.Union))}.";

    internal static void ValidateInputLayout() => _ = CheckedInputSize();

    private static int CheckedInputSize()
    {
        // The Win32 INPUT union is the largest of MOUSEINPUT, KEYBDINPUT,
        // and HARDWAREINPUT. On x64, sizeof(INPUT) is 40, NOT 32.
        // A union containing KEYBDINPUT alone used to produce 32 bytes
        // and SendInput rejected it (typically ERROR_INVALID_PARAMETER 87).
        int expected = Environment.Is64BitProcess ? 40 : 28;
        int actual = Marshal.SizeOf<Input>();
        int unionOffset = Marshal.OffsetOf<Input>(nameof(Input.Union)).ToInt32();
        int expectedUnionOffset = Environment.Is64BitProcess ? 8 : 4;
        if (actual != expected || unionOffset != expectedUnionOffset)
            throw new InvalidOperationException(
                $"Invalid Windows INPUT layout: expected {expected} bytes " +
                $"with union offset {expectedUnionOffset}, got {actual} bytes " +
                $"and offset {unionOffset}. Cannot safely call SendInput.");
        return actual;
    }

    private static void SendScanCode(ushort virtualKey, bool isUp)
    {
        int inputSize = CheckedInputSize();
        uint scancode = MapVirtualKey(virtualKey, 0);
        if (scancode == 0)
            throw new InvalidOperationException(
                $"Windows cannot map virtual key 0x{virtualKey:X2} to a scan code.");
        var eventFlags = KeyeventfScancode | (isUp ? KeyeventfKeyUp : 0);
        // This first version emits only letter/digit/space and Left Shift
        // scan codes, none of which require the extended-key flag.
        var input = new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    Scan = checked((ushort)scancode),
                    Flags = eventFlags
                }
            }
        };
        if (SendInput(1, [input], inputSize) != 1)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error,
                $"SendInput failed (Win32 error {error}, INPUT {inputSize} bytes, " +
                $"VK 0x{virtualKey:X2}, scan 0x{scancode:X2}, " +
                $"event {(isUp ? "UP" : "DOWN")}). " +
                "An invalid parameter indicates an interop problem; " +
                "access-denied or no Win32 error may indicate Windows input " +
                "integrity restrictions.");
        }
    }

    private void Report(string status)
    {
        try { StatusChanged?.Invoke(status); }
        catch { /* Observers must not prevent hook cleanup. */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopping.Cancel();
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
        // Do not dispose _sequenceGate or _stopping while an in-flight
        // async sequence might still be releasing a held key.
        GC.SuppressFinalize(this);
    }

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    // INPUT's native union is larger than KEYBDINPUT. Including MOUSEINPUT
    // gives the union its correct 32-byte size on x64 and 24-byte size on x86.
    // Without this field Marshal.SizeOf<INPUT>() returned 32 instead of 40
    // on x64, causing SendInput to reject all keyboard events.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParamLow;
        public ushort ParamHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback,
        nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(
        nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(ushort code, uint mapType);
}
