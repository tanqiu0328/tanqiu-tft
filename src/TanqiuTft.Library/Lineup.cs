namespace TanqiuTft.Library;

public sealed record Lineup(
    string Name,
    byte[] ImageBytes,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Tags);
