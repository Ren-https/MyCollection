using System.Net.Http.Json;
using System.Text.Json;
using MyCollection.Models;

namespace MyCollection.Services;

public sealed class CollectorApiClient(HttpClient http)
{
    public async Task<ApiSettingsStatus> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<ApiSettingsStatus>("api/settings", cancellationToken)
        ?? new ApiSettingsStatus(false, null);

    public async Task SaveSettingsAsync(string clientId, string clientSecret,
        CancellationToken cancellationToken = default)
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

    public async Task<IReadOnlyList<GameResult>> SearchAsync(SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/search", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<GameResult>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<CollectionSummary>> GetCollectionsAsync(
        CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<List<CollectionSummary>>("api/collections", cancellationToken) ?? [];

    public async Task<CollectionSummary> CreateCollectionAsync(string name,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await http.PostAsJsonAsync("api/collections", new CollectionNameRequest(name), cancellationToken);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<CollectionSummary>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("A API não retornou a lista criada.");
    }

    public async Task RenameCollectionAsync(int id, string name, CancellationToken cancellationToken = default)
    {
        using var response =
            await http.PutAsJsonAsync($"api/collections/{id}", new CollectionNameRequest(name), cancellationToken);
        await EnsureSuccessAsync(response);
    }

    public async Task DeleteCollectionAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync($"api/collections/{id}", cancellationToken);
        await EnsureSuccessAsync(response);
    }

    public async Task<IReadOnlyList<CollectionGame>> GetCollectionAsync(int collectionId,
        CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<List<CollectionGame>>($"api/collections/{collectionId}/games", cancellationToken) ??
        [];

    public async Task AddGameToCollectionAsync(int collectionId, CollectionSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync($"api/collections/{collectionId}/games/{request.Id}", request,
            cancellationToken);
        await EnsureSuccessAsync(response);
    }

    public async Task UpdateGameAsync(CollectionSaveRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync($"api/games/{request.Id}", request, cancellationToken);
        await EnsureSuccessAsync(response);
    }

    public async Task DeleteCollectionGameAsync(int collectionId, int gameId,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await http.DeleteAsync($"api/collections/{collectionId}/games/{gameId}", cancellationToken);
        await EnsureSuccessAsync(response);
    }

    public string GetCollectionExportUrl(int collectionId) => $"api/collections/{collectionId}/export";

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        var message = response.ReasonPhrase ?? "A solicitação falhou.";
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("message", out var apiMessage))
                    message = apiMessage.GetString() ?? message;
                else if (document.RootElement.TryGetProperty("detail", out var detail))
                    message = detail.GetString() ?? message;
            }
            catch (JsonException)
            {
            }
        }

        throw new HttpRequestException(message, null, response.StatusCode);
    }
}