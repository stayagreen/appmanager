using System.IO;
using Microsoft.Data.Sqlite;
using AppManager.Models;

namespace AppManager.Services;

public class DatabaseService : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();

    public DatabaseService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AppManager");
        Directory.CreateDirectory(appData);
        var dbPath = Path.Combine(appData, "data.db");

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Programs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                StartBat TEXT,
                StopBat TEXT,
                RestartBat TEXT,
                ApiPort INTEGER,
                WebPort INTEGER,
                WsPort INTEGER,
                LoginUrl TEXT,
                Directory TEXT,
                Status TEXT DEFAULT 'Stopped',
                SortOrder INTEGER DEFAULT 0,
                CreatedAt TEXT,
                UpdatedAt TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public List<ProgramEntry> GetAll()
    {
        var entries = new List<ProgramEntry>();
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM Programs ORDER BY SortOrder, Id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(MapEntry(reader));
            }
        }
        return entries;
    }

    public ProgramEntry? GetById(int id)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM Programs WHERE Id = @Id";
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return MapEntry(reader);
        }
        return null;
    }

    public void Insert(ProgramEntry entry)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Programs (Name, StartBat, StopBat, RestartBat, ApiPort, WebPort, WsPort,
                    LoginUrl, Directory, Status, SortOrder, CreatedAt, UpdatedAt)
                VALUES (@Name, @StartBat, @StopBat, @RestartBat, @ApiPort, @WebPort, @WsPort,
                    @LoginUrl, @Directory, @Status, @SortOrder, @CreatedAt, @UpdatedAt);
                SELECT last_insert_rowid();
                """;
            AddParams(cmd, entry);
            entry.Id = Convert.ToInt32((long)cmd.ExecuteScalar()!);
        }
    }

    public void Update(ProgramEntry entry)
    {
        lock (_lock)
        {
            entry.UpdatedAt = DateTime.UtcNow.ToString("o");
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE Programs SET Name=@Name, StartBat=@StartBat, StopBat=@StopBat,
                    RestartBat=@RestartBat, ApiPort=@ApiPort, WebPort=@WebPort,
                    WsPort=@WsPort, LoginUrl=@LoginUrl, Directory=@Directory,
                    Status=@Status, SortOrder=@SortOrder, UpdatedAt=@UpdatedAt
                WHERE Id = @Id
                """;
            AddParams(cmd, entry);
            cmd.Parameters.AddWithValue("@Id", entry.Id);
            cmd.ExecuteNonQuery();
        }
    }

    public void Delete(int id)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Programs WHERE Id = @Id";
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateStatus(int id, string status)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE Programs SET Status=@Status, UpdatedAt=@UpdatedAt WHERE Id=@Id";
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public bool ExistsByName(string name, int? excludeId = null)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            if (excludeId.HasValue)
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Programs WHERE Name=@Name AND Id!=@Id";
                cmd.Parameters.AddWithValue("@Id", excludeId.Value);
            }
            else
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Programs WHERE Name=@Name";
            }
            cmd.Parameters.AddWithValue("@Name", name);
            return (long)cmd.ExecuteScalar()! > 0;
        }
    }

    public bool ExistsByDirectory(string directory, int? excludeId = null)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            if (excludeId.HasValue)
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Programs WHERE Directory=@Dir AND Id!=@Id";
                cmd.Parameters.AddWithValue("@Id", excludeId.Value);
            }
            else
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Programs WHERE Directory=@Dir";
            }
            cmd.Parameters.AddWithValue("@Dir", directory);
            return (long)cmd.ExecuteScalar()! > 0;
        }
    }

    private static ProgramEntry MapEntry(SqliteDataReader reader)
    {
        return new ProgramEntry
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            StartBat = reader.IsDBNull(2) ? "" : reader.GetString(2),
            StopBat = reader.IsDBNull(3) ? "" : reader.GetString(3),
            RestartBat = reader.IsDBNull(4) ? "" : reader.GetString(4),
            ApiPort = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            WebPort = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            WsPort = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            LoginUrl = reader.IsDBNull(8) ? "" : reader.GetString(8),
            Directory = reader.IsDBNull(9) ? "" : reader.GetString(9),
            Status = reader.IsDBNull(10) ? "Stopped" : reader.GetString(10),
            SortOrder = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
            CreatedAt = reader.IsDBNull(12) ? "" : reader.GetString(12),
            UpdatedAt = reader.IsDBNull(13) ? "" : reader.GetString(13),
        };
    }

    private static void AddParams(SqliteCommand cmd, ProgramEntry entry)
    {
        cmd.Parameters.AddWithValue("@Name", entry.Name);
        cmd.Parameters.AddWithValue("@StartBat", (object?)entry.StartBat ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@StopBat", (object?)entry.StopBat ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RestartBat", (object?)entry.RestartBat ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ApiPort", (object?)entry.ApiPort ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@WebPort", (object?)entry.WebPort ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@WsPort", (object?)entry.WsPort ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LoginUrl", (object?)entry.LoginUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Directory", (object?)entry.Directory ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Status", entry.Status);
        cmd.Parameters.AddWithValue("@SortOrder", entry.SortOrder);
        cmd.Parameters.AddWithValue("@CreatedAt", entry.CreatedAt);
        cmd.Parameters.AddWithValue("@UpdatedAt", entry.UpdatedAt);
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}
