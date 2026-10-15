using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MyCollection.Models;

sealed class CollectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public CollectionStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS collection_games (
                                  id INTEGER NOT NULL PRIMARY KEY,
                                  name TEXT NOT NULL,
                                  platforms_json TEXT NOT NULL,
                                  status TEXT NOT NULL CHECK (status IN ('Wanted', 'Owned')),
                                  notes TEXT NULL,
                                  added_at TEXT NOT NULL
                              );
                              CREATE TABLE IF NOT EXISTS collections (
                                  id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                                  name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                                  created_at TEXT NOT NULL
                              );
                              CREATE TABLE IF NOT EXISTS collection_memberships (
                                  collection_id INTEGER NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
                                  game_id INTEGER NOT NULL REFERENCES collection_games(id) ON DELETE CASCADE,
                                  PRIMARY KEY (collection_id, game_id)
                              );
                              INSERT OR IGNORE INTO collections (name, created_at)
                              VALUES ('My collection', $created_at);
                              INSERT OR IGNORE INTO collection_memberships (collection_id, game_id)
                              SELECT (SELECT id FROM collections WHERE name = 'My collection'), id
                              FROM collection_games;
                              """;
        command.Parameters.AddWithValue("$created_at",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public IReadOnlyList<CollectionSummary> GetCollections()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT c.id, c.name, COUNT(m.game_id)
                              FROM collections c
                              LEFT JOIN collection_memberships m ON m.collection_id = c.id
                              GROUP BY c.id, c.name
                              ORDER BY c.name COLLATE NOCASE, c.id;
                              """;
        using var reader = command.ExecuteReader();
        var collections = new List<CollectionSummary>();
        while (reader.Read())
            collections.Add(new CollectionSummary(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
        return collections;
    }

    public bool CollectionExists(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM collections WHERE id = $id);";
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    public bool CollectionNameExists(string name, int? exceptId = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = exceptId.HasValue
            ? "SELECT EXISTS (SELECT 1 FROM collections WHERE name = $name COLLATE NOCASE AND id <> $except_id);"
            : "SELECT EXISTS (SELECT 1 FROM collections WHERE name = $name COLLATE NOCASE);";
        command.Parameters.AddWithValue("$name", name);
        if (exceptId.HasValue) command.Parameters.AddWithValue("$except_id", exceptId.Value);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    public CollectionSummary CreateCollection(string name)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO collections (name, created_at) VALUES ($name, $created_at); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$created_at",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        var id = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        return new CollectionSummary(id, name, 0);
    }

    public bool RenameCollection(int id, string name)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE collections SET name = $name WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteNonQuery() > 0;
    }

    public CollectionDeleteResult DeleteCollection(int id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM collections;";
        var count = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (count == 0 || !CollectionExists(connection, transaction, id))
        {
            transaction.Rollback();
            return CollectionDeleteResult.NotFound;
        }

        if (count == 1)
        {
            transaction.Rollback();
            return CollectionDeleteResult.LastCollection;
        }

        command.CommandText = "DELETE FROM collections WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText =
            "DELETE FROM collection_games WHERE NOT EXISTS (SELECT 1 FROM collection_memberships m WHERE m.game_id = collection_games.id);";
        command.ExecuteNonQuery();
        transaction.Commit();
        return CollectionDeleteResult.Deleted;
    }

    public IReadOnlyList<CollectionGame> GetGames(int collectionId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT g.id, g.name, g.platforms_json, g.status, g.notes, g.added_at
                              FROM collection_memberships m
                              JOIN collection_games g ON g.id = m.game_id
                              WHERE m.collection_id = $collection_id
                              ORDER BY g.name COLLATE NOCASE, g.id;
                              """;
        command.Parameters.AddWithValue("$collection_id", collectionId);
        using var reader = command.ExecuteReader();
        var games = new List<CollectionGame>();
        while (reader.Read())
        {
            games.Add(new CollectionGame(
                reader.GetInt32(0),
                reader.GetString(1),
                JsonSerializer.Deserialize<List<PlatformOption>>(reader.GetString(2), JsonOptions) ?? [],
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture)));
        }

        return games;
    }

    public void AddGameToCollection(int collectionId, CollectionSaveRequest game)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              INSERT OR IGNORE INTO collection_games (id, name, platforms_json, status, notes, added_at)
                              VALUES ($id, $name, $platforms, $status, $notes, $added_at);
                              INSERT OR IGNORE INTO collection_memberships (collection_id, game_id) VALUES ($collection_id, $id);
                              """;
        AddGameParameters(command, game);
        command.Parameters.AddWithValue("$added_at",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$collection_id", collectionId);
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    public bool UpdateGame(CollectionSaveRequest game)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              UPDATE collection_games
                              SET name = $name, platforms_json = $platforms, status = $status, notes = $notes
                              WHERE id = $id;
                              """;
        AddGameParameters(command, game);
        return command.ExecuteNonQuery() > 0;
    }

    public bool RemoveMembership(int collectionId, int gameId)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM collection_memberships WHERE collection_id = $collection_id AND game_id = $game_id;";
        command.Parameters.AddWithValue("$collection_id", collectionId);
        command.Parameters.AddWithValue("$game_id", gameId);
        var removed = command.ExecuteNonQuery() > 0;
        if (removed)
        {
            command.Parameters.Clear();
            command.CommandText =
                "DELETE FROM collection_games WHERE id = $game_id AND NOT EXISTS (SELECT 1 FROM collection_memberships WHERE game_id = $game_id);";
            command.Parameters.AddWithValue("$game_id", gameId);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return removed;
    }

    private static bool CollectionExists(SqliteConnection connection, SqliteTransaction transaction, int id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM collections WHERE id = $id);";
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    private static void AddGameParameters(SqliteCommand command, CollectionSaveRequest game)
    {
        command.Parameters.AddWithValue("$id", game.Id);
        command.Parameters.AddWithValue("$name", game.Name);
        command.Parameters.AddWithValue("$platforms", JsonSerializer.Serialize(game.Platforms, JsonOptions));
        command.Parameters.AddWithValue("$status", game.Status);
        command.Parameters.AddWithValue("$notes", (object?)game.Notes ?? DBNull.Value);
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}

enum CollectionDeleteResult
{
    Deleted,
    NotFound,
    LastCollection
}
