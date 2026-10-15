using System.Text.Json;
using Microsoft.Data.Sqlite;
using MyCollection.Models;

sealed class CollectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public CollectionStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS collection_games (
                id INTEGER NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                platforms_json TEXT NOT NULL,
                status TEXT NOT NULL CHECK (status IN ('Wanted', 'Owned')),
                notes TEXT NULL,
                added_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<CollectionGame> GetAll()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, platforms_json, status, notes, added_at FROM collection_games ORDER BY name COLLATE NOCASE, id;";
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
                DateTimeOffset.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture)));
        }
        return games;
    }

    public void Save(CollectionGame game)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO collection_games (id, name, platforms_json, status, notes, added_at)
            VALUES ($id, $name, $platforms, $status, $notes, $added_at)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                platforms_json = excluded.platforms_json,
                status = excluded.status,
                notes = excluded.notes;
            """;
        command.Parameters.AddWithValue("$id", game.Id);
        command.Parameters.AddWithValue("$name", game.Name);
        command.Parameters.AddWithValue("$platforms", JsonSerializer.Serialize(game.Platforms, JsonOptions));
        command.Parameters.AddWithValue("$status", game.Status);
        command.Parameters.AddWithValue("$notes", (object?)game.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$added_at", game.AddedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    public bool Delete(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM collection_games WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
