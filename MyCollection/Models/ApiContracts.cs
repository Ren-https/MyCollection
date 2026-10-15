namespace MyCollection.Models;

public sealed record ApiSettingsStatus(bool IsConfigured, string? ClientId);

public sealed record SaveApiSettingsRequest(string ClientId, string ClientSecret);

public sealed record SearchRequest(IReadOnlyList<int> IncludePlatformIds, IReadOnlyList<int> ExcludePlatformIds);

public sealed record CollectionSummary(int Id, string Name, int GameCount);

public sealed record CollectionNameRequest(string Name);

public sealed record CollectionSaveRequest(
    int Id,
    string Name,
    IReadOnlyList<PlatformOption> Platforms,
    string Status,
    string? Notes);

public sealed record CollectionGame(
    int Id,
    string Name,
    IReadOnlyList<PlatformOption> Platforms,
    string Status,
    string? Notes,
    DateTimeOffset AddedAt);