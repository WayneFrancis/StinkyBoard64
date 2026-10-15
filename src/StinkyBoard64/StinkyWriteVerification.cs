namespace StinkyFootboard.Core;

public static class StinkyWriteVerification
{
    public static string Compare(IReadOnlyList<byte[]> written, FootboardConfiguration actual)
    {
        actual.Validate();
        if (written.Count != 12)
            throw new InvalidDataException("Exactly twelve write reports are expected.");
        var differences = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            var mode = written[i * 3];
            var key = written[i * 3 + 1];
            var button = actual.Buttons[i];
            uint interval = (uint)(mode[5] | (mode[6] << 8));
            if (mode[3] != (byte)button.Mode)
                differences.Add($"Button {i + 1}: mode");
            if (key[3] != button.KeyUsage)
                differences.Add($"Button {i + 1}: key");
            if (key[4] != button.ModifierMask)
                differences.Add($"Button {i + 1}: modifiers");
            if (interval != button.StoredIntervalMilliseconds)
                differences.Add($"Button {i + 1}: interval");
            if (button.HasExtendedData)
                differences.Add($"Button {i + 1}: extra key-sequence data");
        }
        return differences.Count == 0
            ? "VERIFIED: all four keys, modes, modifiers and intervals match."
            : "READBACK MISMATCH: " + string.Join("; ", differences);
    }
}
