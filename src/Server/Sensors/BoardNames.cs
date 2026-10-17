namespace HwMonitor.Sensors;

/// <summary>
/// Sensor names for boards LHM does not know (it then shows "Temperature #1", "Fan #1").
/// Copied from a sister board in LHM's <c>SuperIOHardware.cs</c> with the same chip.
/// </summary>
public static class BoardNames
{
    private sealed record Board(string Name, string Chip, Dictionary<string, string> Sensors);

    private static readonly Board[] Boards =
    [
        // Same layout as LHM's X570_AORUS_PRO.
        new("X570 AORUS ELITE", "/lpc/it8688e/", new()
        {
            ["temperature/0"] = "System",
            ["temperature/1"] = "System #2",
            ["temperature/2"] = "CPU",
            ["temperature/3"] = "PCIe x16",
            ["temperature/4"] = "VRM MOS",
            ["temperature/5"] = "PCH",
            ["fan/0"] = "CPU Fan",
            ["fan/1"] = "System Fan #1",
            ["fan/2"] = "System Fan #2",
            ["fan/3"] = "PCH Fan",
            ["fan/4"] = "CPU Optional Fan",
        }),
    ];

    /// <summary>Name for a sensor Id like <c>/lpc/it8688e/0/fan/1</c>, or null when the board or sensor is not in the table.</summary>
    public static string? Find(string? boardName, string sensorId)
    {
        var board = Boards.FirstOrDefault(b => string.Equals(b.Name, boardName?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (board is null || !sensorId.StartsWith(board.Chip, StringComparison.Ordinal))
            return null;

        // Skip the chip index: "/lpc/it8688e/0/fan/1" → "fan/1".
        var rest = sensorId[board.Chip.Length..];
        var slash = rest.IndexOf('/');
        return slash < 0 ? null : board.Sensors.GetValueOrDefault(rest[(slash + 1)..]);
    }
}
