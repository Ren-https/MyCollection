namespace MyCollection.Models;

public sealed record GameResult(int Id, string Name, IReadOnlyList<PlatformOption> Platforms);
