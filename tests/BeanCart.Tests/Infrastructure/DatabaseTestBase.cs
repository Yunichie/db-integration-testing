namespace BeanCart.Tests.Infrastructure;

public abstract class DatabaseTestBase : IAsyncLifetime
{
    protected PostgresFixture Db { get; }

    protected DatabaseTestBase(PostgresFixture db) => Db = db;

    // xUnit builds a new instance of the test class for every test,
    // so this runs before each test and gives it an empty database.
    public Task InitializeAsync() => Db.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
