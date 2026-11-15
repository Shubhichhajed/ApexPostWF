namespace ApexPost.Api.Services;

/// <summary>
/// Allowed shipment status transitions. Kept as a pure function (no database, no clock) so every
/// transition can be checked by a fast parameterised unit test.
/// </summary>
public static class ShipmentStatusRules
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedTransitions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Created"] = ["PickedUp"],
            ["PickedUp"] = ["InTransit"],
            ["InTransit"] = ["OutForDelivery"],
            ["OutForDelivery"] = ["Delivered", "Failed"]
        };

    /// <summary>True when a shipment in <paramref name="currentStatus"/> may move to <paramref name="nextStatus"/>.</summary>
    public static bool CanTransition(string? currentStatus, string? nextStatus) =>
        currentStatus is not null && nextStatus is not null
        && AllowedTransitions.TryGetValue(currentStatus, out var allowed)
        && allowed.Contains(nextStatus, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the next status in its canonical spelling, or throws when the move is not allowed.</summary>
    public static string Apply(string currentStatus, string nextStatus)
    {
        if (!CanTransition(currentStatus, nextStatus))
            throw new ShipmentConflictException($"Status transition from {currentStatus} to {nextStatus} is not allowed.");
        return AllowedTransitions[currentStatus].First(x => string.Equals(x, nextStatus, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Server-side checks for one uploaded scan. The current time is passed in (from <see cref="TimeProvider"/>)
/// instead of reading DateTime.UtcNow, so the "not in the future" rule is deterministic in tests.
/// </summary>
public static class ScanUploadRules
{
    /// <summary>Tolerance for small clock differences between the courier device and the server.</summary>
    public static readonly TimeSpan AllowedClockSkew = TimeSpan.FromSeconds(5);

    private static readonly HashSet<string> ScanTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Pickup", "InTransit", "Delivered", "Failed" };

    /// <summary>Returns the reason the scan is rejected, or null when it is valid.</summary>
    public static string? Validate(OfflineScanUpload scan, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (string.IsNullOrWhiteSpace(scan.ScanId) || !scan.ScanId.StartsWith("scan::", StringComparison.Ordinal) ||
            !Guid.TryParse(scan.ScanId[6..], out _)) return "ScanId must use scan::{Guid} format.";
        if (!ScanTypes.Contains(scan.ScanType)) return $"Unsupported scan type '{scan.ScanType}'.";
        if (string.IsNullOrWhiteSpace(scan.CourierId)) return "CourierId is required.";
        if (string.IsNullOrWhiteSpace(scan.Location)) return "Location is required.";
        if (scan.ScannedAtUtc == default) return "Scan timestamp is required.";
        if (ToUtc(scan.ScannedAtUtc) > nowUtc.UtcDateTime + AllowedClockSkew) return "Scan timestamp cannot be in the future.";
        if (string.Equals(scan.ScanType, "Delivered", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(scan.Notes))
            return "Delivered scans must include recipient signature/name in Notes.";
        return null;
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
