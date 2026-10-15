using Microsoft.Win32.SafeHandles;
using StinkyFootboard.Core;
using StinkyFootboard.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace StinkyFootboard.Editor;

/// <summary>
/// Experimental, explicitly confirmed write of the 12 configuration reports
/// observed in multiple Stinky.exe USBPcap captures. Never handles firmware
/// updates and never copies unknown captured memory/padding.
/// </summary>
internal static class ExperimentalDeviceWriter
{
    public static void Apply(StinkyBoardIdentity target,
        IReadOnlyList<StinkyBoardIdentity> expectedDevices,
        IReadOnlyList<byte[]> reports)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Stinky HID writes require Windows.");

        // Strong selection guard. The path is a Windows attachment identity,
        // not a permanent serial number, so compare the complete snapshot
        // again after the user confirms the potentially destructive write.
        var currentlyConnected = HidDiscovery.Enumerate()
            .Where(IsSupportedBoard)
            .Select(b => new StinkyBoardIdentity(b.Path, b.ContainerId))
            .ToArray();
        StinkyBoardIdentityGuard.RequireUnchangedWriteTarget(
            target, expectedDevices, currentlyConnected);

        ValidateReportSequence(reports);

        using SafeFileHandle handle = CreateFile(
            target.Path, 0x40000000 /* GENERIC_WRITE */, 0x3 /* shared read/write */,
            nint.Zero, 3 /* OPEN_EXISTING */, 0, nint.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot open vendor HID interface for writing. Close the original Stinky application.");

        for (int i = 0; i < reports.Count; i++)
        {
            var bytes = reports[i];
            if (!HidD_SetOutputReport(handle, bytes, checked((uint)bytes.Length)))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"SET_REPORT {i + 1}/12 failed. The device may now have a partial configuration; " +
                    "recover with the original Stinky software.");

            // Based on measured SET_REPORT request gaps in the original PCAP:
            // 0x04 -> 0x11 ~305ms, otherwise ~104ms.
            if (i < reports.Count - 1)
                Thread.Sleep(bytes[1] == 0x04 ? 305 : 105);
        }
    }

    internal static bool IsSupportedBoard(HidDescriptor d) =>
        d.VendorId == 0x27E0 && d.ProductId == 0x0101 &&
        d.UsagePage == 0xFF00 && d.Usage == 0x0001 &&
        d.OutputReportLength == 65 && d.InputReportLength == 65 &&
        d.Path.Contains("mi_01", StringComparison.OrdinalIgnoreCase);

    internal static void ValidateReportSequence(IReadOnlyList<byte[]> reports)
    {
        if (reports.Count != 12)
            throw new InvalidOperationException("Expected exactly twelve HID reports.");
        for (int i = 0; i < 12; i++)
        {
            var buffer = reports[i];
            int button = i / 3 + 1;
            byte expectedCommand = new byte[] { 0x04, 0x11, 0x0B }[i % 3];
            if (buffer.Length != 65 || buffer[0] != 0 ||
                buffer[1] != expectedCommand || buffer[2] != button ||
                (expectedCommand == 0x04 && (buffer[3] > 3 || buffer[4] != 0)) ||
                (expectedCommand != 0x04 && buffer[3] == 0) ||
                (expectedCommand == 0x11 &&
                 (buffer[4] & ~StinkyModifiers.SupportedMask) != 0))
                throw new InvalidOperationException($"Unexpected contents in report {i + 1}.");

            // Firmware 1.2's primary EEPROM bank consists of 16 two-byte
            // usage/modifier slots. Only 1–15 terminated steps are writable.
            // Never permit nonzero data in the observable alternate bank.
            if (expectedCommand == 0x11)
            {
                if (buffer.AsSpan(5).IndexOfAnyExcept((byte)0) >= 0)
                    throw new InvalidOperationException(
                        $"Button {button}: only one key/modifier pair is supported.");
            }
            else
            {
                // 0x0B is a configuration save, not a second key assignment.
                int knownEndExclusive = expectedCommand == 0x04 ? 9 : 4;
                if (buffer.AsSpan(knownEndExclusive).IndexOfAnyExcept((byte)0) >= 0)
                    throw new InvalidOperationException(
                        $"Report {i + 1} has unexpected nonzero data beyond known fields.");
            }
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string path, uint access, uint share, nint security, uint disposition,
        uint flags, nint template);

    // HID BOOLEAN is one byte, not Win32 four-byte BOOL.
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetOutputReport(
        SafeFileHandle handle, [In] byte[] buffer, uint length);
}
