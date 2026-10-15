using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using MyCollection.Models;

var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var appFolder = Path.Combine(appData, "GameConsoleCollector");
var keyFolder = Path.Combine(appFolder, "keys");
Directory.CreateDirectory(keyFolder);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5100");
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("GameConsoleCollector")
    .PersistKeysToFileSystem(new DirectoryInfo(keyFolder));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();
builder.Services.AddHttpClient();
builder.Services.AddSingleton(sp => new CredentialStore(appFolder, "credentials.json",
    sp.GetRequiredService<IDataProtectionProvider>()));
builder.Services.AddSingleton<IgdbClient>();
builder.Services.AddSingleton(new CollectionStore(Path.Combine(appFolder, "collection.sqlite3")));

var app = builder.Build();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapGet("/api/settings", (CredentialStore store) =>
{
    var credentials = store.Read();
    return Results.Ok(new ApiSettingsStatus(credentials is not null, credentials?.ClientId));
});

app.MapPost("/api/settings", (SaveApiSettingsRequest request, CredentialStore store, IgdbClient igdb) =>
{
    if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.ClientSecret))
        return Results.BadRequest(new { message = "Informe o Client ID e o Client Secret da Twitch." });

    store.Save(request.ClientId.Trim(), request.ClientSecret.Trim());
    igdb.ResetAuthentication();
    return Results.NoContent();
});

app.MapDelete("/api/settings", (CredentialStore store, IgdbClient igdb) =>
{
    store.Delete();
    igdb.ResetAuthentication();
    return Results.NoContent();
});

app.MapGet("/api/collection", (CollectionStore store) => Results.Ok(store.GetAll()));

app.MapPut("/api/collection/{id:int}", (int id, CollectionSaveRequest request, CollectionStore store) =>
{
    if (id <= 0 || request.Id != id)
        return Results.BadRequest(new { message = "O ID do jogo é inválido." });
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 300)
        return Results.BadRequest(new { message = "O título deve ter entre 1 e 300 caracteres." });
    if (request.Status is not ("Wanted" or "Owned"))
        return Results.BadRequest(new { message = "Escolha Quero ter ou Tenho." });
    if (request.Notes?.Length > 2000)
        return Results.BadRequest(new { message = "A nota pode ter no máximo 2000 caracteres." });
    if (request.Platforms is null || request.Platforms.Count > 100
        || request.Platforms.Any(platform => platform.Id <= 0 || string.IsNullOrWhiteSpace(platform.Name)))
        return Results.BadRequest(new { message = "Os dados de plataforma são inválidos." });

    var platforms = request.Platforms
        .GroupBy(platform => platform.Id)
        .Select(group => group.First())
        .ToArray();
    store.Save(new CollectionGame(id, request.Name.Trim(), platforms, request.Status,
        string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(), DateTimeOffset.UtcNow));
    return Results.NoContent();
});

app.MapDelete("/api/collection/{id:int}", (int id, CollectionStore store) =>
    id <= 0 ? Results.BadRequest(new { message = "O ID do jogo é inválido." })
        : store.Delete(id) ? Results.NoContent() : Results.NotFound());

app.MapGet("/api/platforms", async (IgdbClient igdb, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await igdb.GetPlatformsAsync(cancellationToken)); }
    catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: 400); }
    catch (HttpRequestException ex) { return Results.Problem($"Falha ao consultar o IGDB: {ex.Message}", statusCode: 502); }
});

app.MapPost("/api/search", async (SearchRequest request, IgdbClient igdb, CancellationToken cancellationToken) =>
{
    if (request.IncludePlatformIds.Count == 0)
        return Results.BadRequest(new { message = "Selecione pelo menos uma plataforma para incluir." });

    var included = request.IncludePlatformIds.ToHashSet();
    if (included.Overlaps(request.ExcludePlatformIds))
        return Results.BadRequest(new { message = "Uma plataforma não pode estar incluída e excluída ao mesmo tempo." });

    try { return Results.Ok(await igdb.SearchAsync(request, cancellationToken)); }
    catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: 400); }
    catch (HttpRequestException ex) { return Results.Problem($"Falha ao consultar o IGDB: {ex.Message}", statusCode: 502); }
});

app.MapFallbackToFile("index.html");
app.Run();

sealed record StoredCredentials(string ClientId, string ProtectedClientSecret);

sealed class CredentialStore(string folder, string fileName, IDataProtectionProvider protection)
{
    private readonly string _filePath = Path.Combine(folder, fileName);
    private readonly IDataProtector _protector = protection.CreateProtector("IGDB.ClientSecret.v1");
    private readonly object _sync = new();

    public (string ClientId, string ClientSecret)? Read()
    {
        lock (_sync)
        {
            if (!File.Exists(_filePath)) return null;
            try
            {
                var stored = JsonSerializer.Deserialize<StoredCredentials>(File.ReadAllText(_filePath));
                return stored is null ? null : (stored.ClientId, _protector.Unprotect(stored.ProtectedClientSecret));
            }
            catch (Exception ex) when (ex is JsonException or System.Security.Cryptography.CryptographicException)
            {
                throw new InvalidOperationException("Não foi possível ler as credenciais locais. Remova e configure-as novamente.", ex);
            }
        }
    }

    public void Save(string clientId, string clientSecret)
    {
        lock (_sync)
        {
            var stored = new StoredCredentials(clientId, _protector.Protect(clientSecret));
            File.WriteAllText(_filePath, JsonSerializer.Serialize(stored));
        }
    }

    public void Delete()
    {
        lock (_sync) if (File.Exists(_filePath)) File.Delete(_filePath);
    }
}

sealed class IgdbClient(CredentialStore credentials, IHttpClientFactory httpFactory)
{
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;
    private IReadOnlyList<PlatformOption>? _platformCache;

    public void ResetAuthentication()
    {
        _accessToken = null;
        _tokenExpiresAt = DateTimeOffset.MinValue;
        _platformCache = null;
    }

    public async Task<IReadOnlyList<PlatformOption>> GetPlatformsAsync(CancellationToken cancellationToken)
    {
        if (_platformCache is not null) return _platformCache;
        var json = await QueryAsync("platforms", "fields id, name, abbreviation; sort name asc; limit 500;", cancellationToken);
        using var document = JsonDocument.Parse(json);
        _platformCache = document.RootElement.EnumerateArray().Select(item => new PlatformOption(
            item.GetProperty("id").GetInt32(),
            item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            item.TryGetProperty("abbreviation", out var abbreviation) ? abbreviation.GetString() : null)).ToArray();
        return _platformCache;
    }

    public async Task<IReadOnlyList<GameResult>> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var include = request.IncludePlatformIds.Where(id => id > 0).Distinct().Order().ToArray();
        var exclude = request.ExcludePlatformIds.Where(id => id > 0).Distinct().Order().ToArray();
        if (include.Length == 0) throw new InvalidOperationException("Selecione pelo menos uma plataforma para incluir.");

        var clauses = new List<string>();
        clauses.Add(include.Length == 1
            ? $"platforms = {include[0]}"
            : $"({string.Join(" | ", include.Select(id => $"platforms = {id}"))})");
        clauses.AddRange(exclude.Select(id => $"platforms != {id}"));
        clauses.Add("category = 0");
        clauses.Add("version_parent = null");

        var query = $"fields id, name, platforms.id, platforms.name; where {string.Join(" & ", clauses)}; sort name asc; limit 500;";
        var json = await QueryAsync("games", query, cancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(item =>
        {
            var platforms = item.TryGetProperty("platforms", out var p)
                ? p.EnumerateArray().Select(platform => new PlatformOption(
                    platform.GetProperty("id").GetInt32(),
                    platform.TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "")).ToArray()
                : [];
            return new GameResult(item.GetProperty("id").GetInt32(), item.GetProperty("name").GetString() ?? "", platforms);
        }).ToArray();
    }

    private async Task<string> QueryAsync(string endpoint, string query, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        var (clientId, _) = credentials.Read() ?? throw new InvalidOperationException("Configure as credenciais da Twitch primeiro.");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.igdb.com/v4/{endpoint}");
        request.Headers.Add("Client-ID", clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(query, Encoding.UTF8, "text/plain");
        using var response = await httpFactory.CreateClient().SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"IGDB retornou {(int)response.StatusCode}: {content}");
        return content;
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return _accessToken;
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return _accessToken;
            var (clientId, clientSecret) = credentials.Read()
                ?? throw new InvalidOperationException("Configure as credenciais da Twitch primeiro.");
            using var response = await httpFactory.CreateClient().PostAsync(
                "https://id.twitch.tv/oauth2/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["grant_type"] = "client_credentials"
                }), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Twitch retornou {(int)response.StatusCode}: {body}");
            using var json = JsonDocument.Parse(body);
            _accessToken = json.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("A Twitch não retornou um access token.");
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(json.RootElement.GetProperty("expires_in").GetInt32());
            return _accessToken;
        }
        finally { _tokenLock.Release(); }
    }
}
