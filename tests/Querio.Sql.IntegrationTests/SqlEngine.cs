using System.Data.Common;
using Microsoft.Data.Sqlite;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Querio.Sql.IntegrationTests;

/// <summary>
/// A real database to render into and then actually run against.
/// <para>
/// Every other SQL test in this repo asserts on generated text. Text that reads correctly can still
/// be rejected by a server, and - worse - can be accepted and mean something other than it looks
/// like. The only way to tell is to run it and look at the rows, which is what these do.
/// </para>
/// </summary>
public abstract class SqlEngine : IAsyncLifetime
{
    private DateTime _seededAt;

    /// <summary>The dialect Querio renders with for this engine.</summary>
    public abstract SqlDialect Dialect { get; }

    /// <summary>Why this engine could not be reached, or null when it is up.</summary>
    public string? Unavailable { get; private set; }

    /// <summary>The moment the rows were seeded relative to, for assertions about a window.</summary>
    public DateTime SeededAt => _seededAt;

    /// <summary>The statements that create the two tables, in this engine's DDL.</summary>
    protected abstract IReadOnlyList<string> Ddl { get; }

    /// <summary>Opens a connection to the running instance.</summary>
    protected abstract DbConnection Open();

    /// <summary>Starts the instance, when it needs starting.</summary>
    protected virtual Task StartAsync() => Task.CompletedTask;

    /// <summary>Stops it again.</summary>
    protected virtual Task StopAsync() => Task.CompletedTask;

    /// <summary>
    /// Refuses to start a container-backed engine unless asked for. Pulling a SQL Server image costs
    /// more than the rest of CI put together, so the containers run in their own job rather than on
    /// every push; SQLite needs none of this and always runs, which keeps a real database in the
    /// default signal instead of behind a flag nobody sets.
    /// </summary>
    protected static void RequireOptIn()
    {
        if (Environment.GetEnvironmentVariable("QUERIO_INTEGRATION") != "1")
        {
            throw new InvalidOperationException(
                "container-backed engines are opt-in; set QUERIO_INTEGRATION=1 to run them");
        }
    }

    public async ValueTask InitializeAsync()
    {
        try
        {
            await StartAsync();
            await CreateAndSeedAsync();
        }
        catch (Exception error)
        {
            // A developer without Docker still gets the SQLite results rather than a wall of red.
            Unavailable = $"{GetType().Name} is not available: {error.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
        }
        catch (Exception)
        {
            // Nothing useful to do about a container that will not stop; the test run is over.
        }
    }

    /// <summary>Renders the query for this engine and returns the rows the server actually gave back.</summary>
    /// <param name="spec">The query to run.</param>
    /// <param name="schema">The schema it was built against.</param>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Run(QuerySpec spec, QuerySchema schema)
    {
        var rendered = SqlRenderer.Render(spec, schema, Dialect);

        using var connection = Open();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = rendered.Sql;
        foreach (var parameter in rendered.Parameters)
        {
            var bound = command.CreateParameter();
            bound.ParameterName = parameter.Name;
            bound.Value = parameter.Value ?? DBNull.Value;
            command.Parameters.Add(bound);
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private async Task CreateAndSeedAsync()
    {
        _seededAt = DateTime.UtcNow;

        using var connection = Open();
        await connection.OpenAsync();

        foreach (var statement in Ddl)
        {
            using var create = connection.CreateCommand();
            create.CommandText = statement;
            await create.ExecuteNonQueryAsync();
        }

        foreach (var (id, name) in IntegrationSchema.ApiKeys)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO api_keys (api_key_id, key_name) VALUES (@p0, @p1)";
            Bind(insert, "@p0", id);
            Bind(insert, "@p1", name);
            await insert.ExecuteNonQueryAsync();
        }

        foreach (var row in IntegrationSchema.Requests)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO request_log (request_id, route, logged_at, duration_ms, is_error, status_code, api_key_id) "
                + "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6)";
            Bind(insert, "@p0", Guid.NewGuid());
            Bind(insert, "@p1", row.Route);
            Bind(insert, "@p2", _seededAt.AddDays(-row.DaysAgo));
            Bind(insert, "@p3", row.DurationMs);
            Bind(insert, "@p4", row.Error);
            Bind(insert, "@p5", row.Status);
            Bind(insert, "@p6", row.ApiKeyId);
            await insert.ExecuteNonQueryAsync();
        }
    }

    // Seeding binds the same CLR types the renderer binds, so a value written and a value compared
    // against are encoded the same way. Getting that wrong is how a SQLite suite silently compares a
    // Guid against its own string form and finds nothing.
    private static void Bind(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

/// <summary>SQLite, in a file of its own. Needs no container, so it runs everywhere the tests do.</summary>
public sealed class SqliteEngine : SqlEngine
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"querio-{Guid.NewGuid():N}.db");

    public override SqlDialect Dialect => SqliteDialect.Instance;

    protected override IReadOnlyList<string> Ddl =>
    [
        """
        CREATE TABLE api_keys (
            api_key_id TEXT NOT NULL PRIMARY KEY,
            key_name   TEXT NOT NULL)
        """,
        """
        CREATE TABLE request_log (
            request_id  TEXT NOT NULL PRIMARY KEY,
            route       TEXT NOT NULL,
            logged_at   TEXT NOT NULL,
            duration_ms INTEGER NOT NULL,
            is_error    INTEGER NOT NULL,
            status_code INTEGER NOT NULL,
            api_key_id  TEXT NULL)
        """,
    ];

    protected override DbConnection Open() => new SqliteConnection($"Data Source={_path}");

    protected override Task StopAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
        return Task.CompletedTask;
    }
}

/// <summary>PostgreSQL in a container.</summary>
public sealed class PostgreSqlEngine : SqlEngine
{
    // The image is named here rather than left to the library default, so a CI run and a developer
    // machine measure the same engine.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public override SqlDialect Dialect => PostgreSqlDialect.Instance;

    protected override IReadOnlyList<string> Ddl =>
    [
        """
        CREATE TABLE api_keys (
            api_key_id UUID NOT NULL PRIMARY KEY,
            key_name   TEXT NOT NULL)
        """,
        """
        CREATE TABLE request_log (
            request_id  UUID NOT NULL PRIMARY KEY,
            route       TEXT NOT NULL,
            logged_at   TIMESTAMPTZ NOT NULL,
            duration_ms INTEGER NOT NULL,
            is_error    BOOLEAN NOT NULL,
            status_code INTEGER NOT NULL,
            api_key_id  UUID NULL)
        """,
    ];

    protected override DbConnection Open() => new NpgsqlConnection(_container.GetConnectionString());

    protected override Task StartAsync()
    {
        RequireOptIn();
        return _container.StartAsync();
    }

    protected override Task StopAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>SQL Server in a container.</summary>
public sealed class SqlServerEngine : SqlEngine
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public override SqlDialect Dialect => SqlServerDialect.Instance;

    protected override IReadOnlyList<string> Ddl =>
    [
        """
        CREATE TABLE api_keys (
            api_key_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
            key_name   NVARCHAR(200) NOT NULL)
        """,
        """
        CREATE TABLE request_log (
            request_id  UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
            route       NVARCHAR(400) NOT NULL,
            logged_at   DATETIME2 NOT NULL,
            duration_ms INT NOT NULL,
            is_error    BIT NOT NULL,
            status_code INT NOT NULL,
            api_key_id  UNIQUEIDENTIFIER NULL)
        """,
    ];

    protected override DbConnection Open()
        => new Microsoft.Data.SqlClient.SqlConnection(_container.GetConnectionString() + ";TrustServerCertificate=true");

    protected override Task StartAsync()
    {
        RequireOptIn();
        return _container.StartAsync();
    }

    protected override Task StopAsync() => _container.DisposeAsync().AsTask();
}
