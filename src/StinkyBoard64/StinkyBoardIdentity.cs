namespace StinkyFootboard.Core;

/// <summary>
/// A snapshot of one *currently enumerated* vendor HID interface.
/// Windows device paths and Container IDs identify an attachment, NOT a
/// manufacturer-assigned permanent serial number. They can change after
/// unplugging, changing ports or swapping otherwise identical boards.
/// </summary>
public sealed record StinkyBoardIdentity(string Path, Guid? ContainerId);

/// <summary>
/// Pure identity/topology guard used before issuing any active HID request.
/// Prevents a selected board from silently becoming a different interface
/// and stops a previously previewed write when the USB topology changes.
/// </summary>
public static class StinkyBoardIdentityGuard
{
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    public static void RequireCurrentTarget(
        StinkyBoardIdentity selected,
        IReadOnlyList<StinkyBoardIdentity> current)
    {
        if (selected is null || string.IsNullOrWhiteSpace(selected.Path))
            throw new InvalidOperationException("No Stinky device has been selected.");
        if (current.Count == 0 ||
            current.Any(b => string.IsNullOrWhiteSpace(b.Path)) ||
            current.Select(b => b.Path).Distinct(Paths).Count() != current.Count)
            throw new InvalidOperationException("Invalid or ambiguous Stinky HID enumeration.");

        var matching = current.Where(b => Paths.Equals(b.Path, selected.Path)).ToArray();
        if (matching.Length != 1 ||
            matching[0].ContainerId != selected.ContainerId)
            throw new InvalidOperationException(
                "Selected Stinky device is missing or has changed identity. Refresh devices and read again.");
    }

    public static void RequireUnchangedWriteTarget(
        StinkyBoardIdentity selected,
        IReadOnlyList<StinkyBoardIdentity> expected,
        IReadOnlyList<StinkyBoardIdentity> current)
    {
        RequireCurrentTarget(selected, expected);
        RequireCurrentTarget(selected, current);

        if (expected.Count != current.Count ||
            expected.Any(previous => !current.Any(now =>
                Paths.Equals(previous.Path, now.Path) &&
                previous.ContainerId == now.ContainerId)))
            throw new InvalidOperationException(
                "Connected Stinky devices changed since the configuration was previewed. " +
                "Refresh, read the selected board and preview again.");

        if (current.Count > 1 &&
            (current.Any(b => b.ContainerId is null) ||
             current.Select(b => b.ContainerId).Distinct().Count() != current.Count))
            throw new InvalidOperationException(
                "Cannot safely distinguish multiple Stinky boards by their Windows Container IDs. " +
                "Unplug all but the intended board before writing.");

        if (current.Count > 2)
            throw new InvalidOperationException(
                "This experimental editor supports at most two simultaneously connected footboards.");
    }
}
