using Dapper;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace BeanCart.Tests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    private Respawner _respawner = null!;

    public PostgresFixture()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public string ConnectionString => _container.GetConnectionString();

    public NpgsqlConnection CreateConnection() => new(ConnectionString);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var conn = CreateConnection();
        await conn.OpenAsync();

        var schemaPath = Path.Combine(AppContext.BaseDirectory, "Sql", "schema.sql");
        await conn.ExecuteAsync(await File.ReadAllTextAsync(schemaPath));

        // Create the Respawner AFTER the schema exists: it reads the tables and
        // foreign keys once and works out a safe order for deleting rows.
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = new[] { "public" }
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
