using System.Net.Http.Json;
using MyCollection.Models;

namespace MyCollection.Services;

public sealed class CollectorApiClient(HttpClient http)
{
    public async Task<ApiSettingsStatus> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<ApiSettingsStatus>("api/settings", cancellationToken)
        ?? new ApiSettingsStatus(false, null);

    public async Task SaveSettingsAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/settings",
            new SaveApiSettingsRequest(clientId, clientSecret), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteSettingsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync("api/settings", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<PlatformOption>> GetPlatformsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<List<PlatformOption>>("api/platforms", cancellationToken) ?? [];

    public async Task<IReadOnlyList<GameResult>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/search", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<GameResult>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<CollectionGame>> GetCollectionAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<List<CollectionGame>>("api/collection", cancellationToken) ?? [];

    public async Task SaveCollectionGameAsync(CollectionSaveRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync($"api/collection/{request.Id}", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteCollectionGameAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync($"api/collection/{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
