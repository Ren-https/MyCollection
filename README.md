# Console Index — Game Console Collector

Local Blazor WebAssembly interface and ASP.NET Core API for building IGDB game searches from platform inclusion and exclusion rules.

## Run locally

1. Create a Twitch developer application and copy its **Client ID** and **Client Secret**.
2. Run the local host from the solution directory:

   ```powershell
   dotnet run --project MyCollection.LocalApi
   ```

3. Open <http://127.0.0.1:5100> and choose **API settings** to save your credentials.
4. Select platforms to include and exclude, then search.

The host listens only on the loopback interface. It serves the WASM app and makes IGDB requests from the same origin, so the browser doesn't call IGDB directly. The Client Secret is encrypted with ASP.NET Core Data Protection and stored in the current user's local application data under `GameConsoleCollector`; on Windows, the Data Protection key ring is additionally protected with DPAPI. The secret is not kept in browser storage or bundled with the app. Remove saved credentials from API settings when needed.

## Search behavior

- Inclusion is an OR rule: a game must have at least one selected platform.
- Exclusion is an AND of negative platform filters: a game is rejected if it has any excluded platform.
- Other platforms are allowed and returned with each matching game.
- Results use IGDB `category = 0` and `version_parent = null` filters and are capped at 500 per request.

The IGDB account and Twitch app credentials belong to each person running the app. No shared public API or project-owned credentials are required.

## Personal collections

Search results can be added to named local lists such as a console collection, **Digital**, or **No access**. A game can appear in more than one list; its **Want to have** or **Owned** status and optional note are shared across those lists. Lists can be created, renamed, searched, filtered by status, and exported individually as UTF-8 CSV files.

On the first start after this update, the existing collection is migrated to a default list named **My collection**, preserving saved games, statuses, and notes. The SQLite database remains under the current user's local application data in `GameConsoleCollector/collection.sqlite3`; it is not uploaded or synchronized.
