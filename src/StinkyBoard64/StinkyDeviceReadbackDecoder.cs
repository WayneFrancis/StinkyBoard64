namespace StinkyFootboard.Core;

public sealed record StinkyButtonReadback(
    byte ButtonNumber, byte[] KeyResponse, byte[] ModeResponse);

public static class StinkyDeviceReadbackDecoder
{
    public const byte ReadKeyPageCommand = 0x12;
    public const byte ReadModeCommand = 0x06;
    public const int WindowsReportLength = 65;

    public static byte[] MakeReadCommand(byte opcode, byte buttonNumber)
    {
        if (opcode is not (ReadKeyPageCommand or ReadModeCommand) ||
            buttonNumber is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(opcode));
        byte[] report = new byte[WindowsReportLength];
        report[1] = opcode;
        report[2] = buttonNumber;
        return report;
    }

    public static FootboardConfiguration Decode(IReadOnlyList<StinkyButtonReadback> responses)
    {
        if (responses.Count != 4 ||
            !responses.Select(x => (int)x.ButtonNumber).Order()
                .SequenceEqual(new[] { 1, 2, 3, 4 }))
            throw new InvalidDataException("Incomplete four-button readback.");

        var profile = new FootboardConfiguration();
        foreach (var response in responses)
        {
            byte[] key = response.KeyResponse;
            byte[] mode = response.ModeResponse;
            int n = response.ButtonNumber;
            if (key.Length != 64 || mode.Length != 64 ||
                key[0] != 0xB7 || key[1] != 0x12 || key[2] != n - 1 ||
                mode[0] != 0x06 || mode[1] != n - 1 ||
                key[3] == 0 || (key[4] & ~StinkyModifiers.SupportedMask) != 0 ||
                mode[2] > 3 || mode[3] != 0 || mode[6] != 0 ||
                mode.AsSpan(7).IndexOfAnyExcept((byte)0) >= 0)
                throw new InvalidDataException(
                    $"Button {n} has unsupported key, modifier or mode data.");

            var button = profile.Buttons[n - 1];
            button.Mode = (StinkyButtonMode)mode[2];
            button.KeyUsage = key[3];
            button.ModifierMask = key[4];
            button.StoredIntervalMilliseconds = (uint)(mode[4] | (mode[5] << 8));
            if (button.Mode == StinkyButtonMode.Pulse && button.StoredIntervalMilliseconds != 0)
                button.PulseClicksPerSecond = Math.Clamp(
                    (int)Math.Round(1000d / button.StoredIntervalMilliseconds), 1, 30);

            // Informational only. The 0x12 reply cannot reveal the final
            // alternate-bank EEPROM bytes; every write still risks replacing
            // unknown page contents even if all visible bytes are zero.
            button.HasExtendedData = key.AsSpan(5).IndexOfAnyExcept((byte)0) >= 0;
        }
        profile.Validate();
        return profile;
    }
}
