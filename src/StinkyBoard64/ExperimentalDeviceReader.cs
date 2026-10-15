using Microsoft.Win32.SafeHandles;
using StinkyFootboard.Core;
using StinkyFootboard.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace StinkyFootboard.Editor;

/// <summary>
/// Explicit opt-in, strictly read-only QUERY protocol seen during the
/// original Stinky.exe startup in an actual USBPcap recording.
/// Queries travel as HID output control reports (0x12 and 0x06); replies
/// arrive asynchronously on the vendor interrupt input endpoint 0x82.
/// No EEPROM writes, configuration saves, or reset commands are sent.
/// </summary>
internal static class ExperimentalDeviceReader
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint SharedReadWrite = 3;
    private const uint FileFlagOverlapped = 0x40000000;

    public static async Task<FootboardConfiguration> ReadAsync(
        StinkyBoardIdentity selected,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Stinky HID readback requires Windows.");

        var connected = HidDiscovery.Enumerate()
            .Where(ExperimentalDeviceWriter.IsSupportedBoard)
            .Select(b => new StinkyBoardIdentity(b.Path, b.ContainerId))
            .ToArray();
        if (connected.Length > 2)
            throw new InvalidOperationException("Only two Stinky boards are supported.");
        StinkyBoardIdentityGuard.RequireCurrentTarget(selected, connected);

        // Use a synchronous output handle for documented HID class
        // SET_REPORT requests and a separate overlapped read handle for
        // interrupt IN. This avoids trying to issue HID GET_REPORT, which
        // is not what the original application did.
        using var outputHandle = CreateFile(selected.Path, GenericWrite,
            SharedReadWrite, nint.Zero, 3, 0, nint.Zero);
        if (outputHandle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot open Stinky vendor HID output. Close original Stinky.exe.");

        using var inputHandle = CreateFile(selected.Path, GenericRead,
            SharedReadWrite, nint.Zero, 3, FileFlagOverlapped, nint.Zero);
        if (inputHandle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot open Stinky vendor HID input. Close original Stinky.exe.");

        using var input = new FileStream(inputHandle, FileAccess.Read,
            bufferSize: StinkyDeviceReadbackDecoder.WindowsReportLength, isAsync: true);

        var responses = new List<StinkyButtonReadback>(4);
        for (byte button = 1; button <= 4; button++)
        {
            // Match exact order seen in original-app USB startup: 0x12, 0x06.
            byte[] key = await QueryAsync(outputHandle, input,
                StinkyDeviceReadbackDecoder.ReadKeyPageCommand, button,
                cancellationToken);
            byte[] mode = await QueryAsync(outputHandle, input,
                StinkyDeviceReadbackDecoder.ReadModeCommand, button,
                cancellationToken);
            responses.Add(new StinkyButtonReadback(button, key, mode));
        }

        return StinkyDeviceReadbackDecoder.Decode(responses);
    }

    private static async Task<byte[]> QueryAsync(SafeFileHandle output, FileStream input,
        byte opcode, byte button, CancellationToken cancellationToken)
    {
        byte[] command = StinkyDeviceReadbackDecoder.MakeReadCommand(opcode, button);

        // The actual USBPcap shows an extra one-byte zero transfer after
        // some 0x12 replies. Ignore such packets; never mistake them for
        // valid readback data. Bound attempts AND elapsed time.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        byte[] buffer = new byte[StinkyDeviceReadbackDecoder.WindowsReportLength];

        // Post the interrupt-IN read BEFORE transmitting the query. The
        // device replies promptly and we must not risk losing a fast reply
        // because there was no pending Windows HID reader yet.
        Task<int> pendingRead = input.ReadAsync(buffer.AsMemory(), timeout.Token).AsTask();
        if (!HidD_SetOutputReport(output, command, (uint)command.Length))
        {
            int error = Marshal.GetLastWin32Error();
            timeout.Cancel();
            try { await pendingRead; }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            throw new Win32Exception(error,
                $"Read-only command 0x{opcode:X2}, button {button} was rejected.");
        }

        try
        {
            for (int attempts = 0; attempts < 12; attempts++)
            {
                int n = await pendingRead;

                // Windows HID includes a leading zero report ID; USBPcap
                // frames show only the 64 bytes after that ID.
                byte[]? payload = null;
                if (n == 65 && buffer[0] == 0)
                    payload = buffer[1..];
                else if (n == 64)
                    payload = buffer[..64];

                if (payload is null)
                {
                    Array.Clear(buffer);
                    pendingRead = input.ReadAsync(buffer.AsMemory(), timeout.Token).AsTask();
                    continue;
                }
                bool isReply = opcode switch
                {
                    0x12 => payload[0] == 0xB7 && payload[1] == 0x12 &&
                            payload[2] == button - 1,
                    0x06 => payload[0] == 0x06 && payload[1] == button - 1,
                    _ => false
                };
                if (isReply) return payload;
                Array.Clear(buffer);
                pendingRead = input.ReadAsync(buffer.AsMemory(), timeout.Token).AsTask();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"No matching vendor HID reply for read-only 0x{opcode:X2}, " +
                $"button {button} within 3 seconds. No settings were changed.");
        }

        throw new IOException(
            $"The board sent too many unexpected HID packets during 0x{opcode:X2} " +
            $"readback of button {button}; no configuration was loaded.");
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string path, uint access, uint share, nint security, uint disposition,
        uint flags, nint template);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetOutputReport(
        SafeFileHandle handle, [In] byte[] buffer, uint length);
}
