using System.Text.Json;
using Microsoft.AspNetCore.Components;
using MyCollection.Models;
using MyCollection.Services;

namespace MyCollection.Pages;

public partial class Home
{
    private IReadOnlyList<PlatformOption> platforms = [];
    private IReadOnlyList<GameResult> results = [];
    private IReadOnlyList<CollectionSummary> collections = [];
    private IReadOnlyList<CollectionGame> collectionGames = [];
    private Dictionary<int, CollectionGame> collectionById = [];
    private Dictionary<int, CollectionEdit> collectionEdits = [];
    private HashSet<int> includeIds = [48];
    private HashSet<int> excludeIds = [6];
    private string clientId = "";
    private string clientSecret = "";
    private string includeSearch = "";
    private string excludeSearch = "";
    private string collectionSearch = "";
    private string collectionStatusFilter = "";
    private string collectionNameDraft = "";
    private string? errorMessage;
    private string? settingsMessage;
    private string? collectionMessage;
    private bool configured;
    private bool credentialsOpen;
    private bool busy;
    private bool hasSearched;
    private bool collectionEditorOpen;
    private bool editingCollectionName;
    private bool deleteConfirmationOpen;
    private int selectedCollectionId;

    private CollectionSummary? SelectedCollection =>
        collections.FirstOrDefault(collection => collection.Id == selectedCollectionId);

    private bool CanDeleteCollection => collections.Count > 1;

    private string CollectionExportUrl =>
        selectedCollectionId > 0 ? Api.GetCollectionExportUrl(selectedCollectionId) : "#";

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await RefreshCollections();
            var status = await Api.GetSettingsAsync();
            configured = status.IsConfigured;
            clientId = status.ClientId ?? "";
            if (configured) await LoadPlatforms();
        }
        catch (Exception ex)
        {
            errorMessage = FriendlyMessage(ex);
        }
    }

    private IReadOnlyList<PlatformOption> FilterPlatforms(string filter) => string.IsNullOrWhiteSpace(filter)
        ? platforms
        : platforms.Where(platform => platform.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                      || (platform.Abbreviation?.Contains(filter, StringComparison.OrdinalIgnoreCase) ??
                                          false)).ToArray();

    private IReadOnlyList<PlatformOption> SelectedPlatforms(HashSet<int> ids) =>
        platforms.Where(platform => ids.Contains(platform.Id)).ToArray();

    private IReadOnlyList<CollectionGame> VisibleCollection => collectionGames
        .Where(game => string.IsNullOrWhiteSpace(collectionStatusFilter) || game.Status == collectionStatusFilter)
        .Where(game => string.IsNullOrWhiteSpace(collectionSearch)
                       || game.Name.Contains(collectionSearch, StringComparison.OrdinalIgnoreCase))
        .ToArray();

    private async Task RefreshCollections()
    {
        collections = await Api.GetCollectionsAsync();
        if (collections.All(collection => collection.Id != selectedCollectionId))
            selectedCollectionId = collections.FirstOrDefault()?.Id ?? 0;
        await LoadCollectionGames();
    }

    private async Task LoadCollectionGames()
    {
        collectionGames = selectedCollectionId > 0
            ? await Api.GetCollectionAsync(selectedCollectionId)
            : [];
        collectionById = collectionGames.ToDictionary(game => game.Id);
        collectionEdits = collectionGames.ToDictionary(
            game => game.Id,
            game => new CollectionEdit(game.Status, game.Notes ?? ""));
    }

    private async Task OnCollectionSelected(ChangeEventArgs args)
    {
        if (!int.TryParse(args.Value?.ToString(), out var id) ||
            collections.All(collection => collection.Id != id)) return;
        selectedCollectionId = id;
        collectionSearch = "";
        collectionStatusFilter = "";
        collectionMessage = null;
        collectionEditorOpen = false;
        deleteConfirmationOpen = false;
        await LoadCollectionGames();
    }

    private void StartCreatingCollection()
    {
        collectionNameDraft = "";
        editingCollectionName = false;
        collectionEditorOpen = true;
        deleteConfirmationOpen = false;
    }

    private void StartRenamingCollection()
    {
        if (SelectedCollection is null) return;
        collectionNameDraft = SelectedCollection.Name;
        editingCollectionName = true;
        collectionEditorOpen = true;
        deleteConfirmationOpen = false;
    }

    private void CancelCollectionEditor() => collectionEditorOpen = false;

    private async Task SaveCollectionName()
    {
        var name = collectionNameDraft.Trim();
        if (name.Length is 0 or > 80)
        {
            collectionMessage = "Use um nome de lista entre 1 e 80 caracteres.";
            return;
        }

        busy = true;
        collectionMessage = null;
        try
        {
            if (editingCollectionName)
                await Api.RenameCollectionAsync(selectedCollectionId, name);
            else
            {
                var created = await Api.CreateCollectionAsync(name);
                selectedCollectionId = created.Id;
            }

            collectionEditorOpen = false;
            await RefreshCollections();
            collectionMessage = editingCollectionName ? "Lista renomeada." : "Lista criada.";
        }
        catch (Exception ex)
        {
            collectionMessage = FriendlyMessage(ex);
        }
        finally
        {
            busy = false;
        }
    }

    private void StartDeletingCollection()
    {
        if (CanDeleteCollection) deleteConfirmationOpen = true;
        collectionEditorOpen = false;
    }

    private async Task DeleteCurrentCollection()
    {
        if (!CanDeleteCollection || SelectedCollection is null) return;
        busy = true;
        collectionMessage = null;
        try
        {
            await Api.DeleteCollectionAsync(selectedCollectionId);
            deleteConfirmationOpen = false;
            selectedCollectionId = 0;
            await RefreshCollections();
            collectionMessage = "Lista apagada.";
        }
        catch (Exception ex)
        {
            collectionMessage = FriendlyMessage(ex);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task AddToCollection(GameResult game)
    {
        if (busy || selectedCollectionId <= 0 || collectionById.ContainsKey(game.Id)) return;
        busy = true;
        collectionMessage = null;
        try
        {
            await Api.AddGameToCollectionAsync(selectedCollectionId,
                new CollectionSaveRequest(game.Id, game.Name, game.Platforms, "Wanted", null));
            await RefreshCollections();
            collectionMessage = $"{game.Name} adicionado à lista {SelectedCollection?.Name}.";
        }
        catch (Exception ex)
        {
            collectionMessage = FriendlyMessage(ex);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task SaveCollectionEdit(int id)
    {
        if (busy || !collectionById.TryGetValue(id, out var game) || !collectionEdits.TryGetValue(id, out var edit)) return;
        busy = true;
        collectionMessage = null;
        try
        {
            await Api.UpdateGameAsync(new CollectionSaveRequest(game.Id, game.Name, game.Platforms, edit.Status,
                edit.Notes));
            await RefreshCollections();
            collectionMessage = $"{game.Name} atualizado.";
        }
        catch (Exception ex)
        {
            collectionMessage = FriendlyMessage(ex);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task RemoveFromCollection(int id)
    {
        if (busy || !collectionById.TryGetValue(id, out var game)) return;
        busy = true;
        collectionMessage = null;
        try
        {
            await Api.DeleteCollectionGameAsync(selectedCollectionId, id);
            await RefreshCollections();
            collectionMessage = $"{game.Name} removido desta lista.";
        }
        catch (Exception ex)
        {
            collectionMessage = FriendlyMessage(ex);
        }
        finally
        {
            busy = false;
        }
    }

    private sealed class CollectionEdit(string status, string notes)
    {
        public string Status { get; set; } = status;
        public string Notes { get; set; } = notes;
    }

    private void ToggleInclude(int id)
    {
        if (!includeIds.Add(id)) includeIds.Remove(id);
        else excludeIds.Remove(id);
        errorMessage = null;
    }

    private void ToggleExclude(int id)
    {
        if (!excludeIds.Add(id)) excludeIds.Remove(id);
        else includeIds.Remove(id);
        errorMessage = null;
    }

    private void ToggleCredentials() => credentialsOpen = !credentialsOpen;

    private async Task SaveCredentials()
    {
        busy = true;
        settingsMessage = null;
        errorMessage = null;
        try
        {
            await Api.SaveSettingsAsync(clientId, clientSecret);
            configured = true;
            clientSecret = "";
            settingsMessage = "Credentials saved on this device.";
            await LoadPlatforms();
        }
        catch (Exception ex)
        {
            settingsMessage = FriendlyMessage(ex);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task ForgetCredentials()
    {
        busy = true;
        try
        {
            await Api.DeleteSettingsAsync();
            configured = false;
            clientSecret = "";
            settingsMessage = "Saved credentials removed.";
        }
        catch (Exception ex)
        {
            settingsMessage = FriendlyMessage(ex);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task LoadPlatforms()
    {
        platforms = await Api.GetPlatformsAsync();
        includeIds.RemoveWhere(id => platforms.All(platform => platform.Id != id));
        excludeIds.RemoveWhere(id => platforms.All(platform => platform.Id != id));
    }

    private async Task Search()
    {
        if (includeIds.Count == 0)
        {
            errorMessage = "Select at least one platform to include.";
            return;
        }

        busy = true;
        errorMessage = null;
        try
        {
            results = await Api.SearchAsync(new SearchRequest(includeIds.Order().ToArray(),
                excludeIds.Order().ToArray()));
            hasSearched = true;
        }
        catch (Exception ex)
        {
            errorMessage = FriendlyMessage(ex);
        }
        finally
        {
            busy = false;
        }
    }

    private static string FriendlyMessage(Exception ex)
    {
        var message = ex.Message;
        try
        {
            using var json = JsonDocument.Parse(message);
            if (json.RootElement.TryGetProperty("detail", out var detail)) return detail.GetString() ?? message;
            if (json.RootElement.TryGetProperty("message", out var apiMessage))
                return apiMessage.GetString() ?? message;
        }
        catch (JsonException)
        {
        }

        return message;
    }
}
