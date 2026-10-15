using MyCollection.Models;

namespace MyCollection.Services;

/// <summary>Builds safe APICalypse filters from numeric platform IDs.</summary>
public static class IgdbQueryBuilder
{
    public static string BuildGamesQuery(IEnumerable<int> includedPlatformIds,
        IEnumerable<int> excludedPlatformIds, int limit = 500)
    {
        var included = Normalize(includedPlatformIds);
        var excluded = Normalize(excludedPlatformIds);

        if (included.Length == 0)
            throw new ArgumentException("Selecione ao menos uma plataforma para incluir.", nameof(includedPlatformIds));

        limit = Math.Clamp(limit, 1, 500);
        var includeFilter = included.Length == 1
            ? $"platforms = {included[0]}"
            : $"({string.Join(" | ", included.Select(id => $"platforms = {id}"))})";
        var filters = new List<string> { includeFilter };
        filters.AddRange(excluded.Select(id => $"platforms != {id}"));
        filters.Add("category = 0");
        filters.Add("version_parent = null");

        return $"fields name, platforms.name, category, version_parent;\nwhere {string.Join(" & ", filters)};\nlimit {limit};";
    }

    private static int[] Normalize(IEnumerable<int> ids) => ids
        .Where(id => id > 0)
        .Distinct()
        .Order()
        .ToArray();
}
