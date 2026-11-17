using BeanCart.Tests.Infrastructure;
using Dapper;

namespace BeanCart.Tests;

[Collection(DatabaseCollection.Name)]
public class SmokeTests : DatabaseTestBase
{
    public SmokeTests(PostgresFixture db) : base(db) { }

    [Fact]
    public async Task Container_answers_a_query()
    {
        await using var conn = Db.CreateConnection();
        var one = await conn.QuerySingleAsync<int>("SELECT 1");
        Assert.Equal(1, one);
    }

    [Fact]
    public Task First_test_inserts_alice() => InsertAliceAsync();

    [Fact]
    public Task Second_test_inserts_alice() => InsertAliceAsync();

    private async Task InsertAliceAsync()
    {
        await using var conn = Db.CreateConnection();
        await conn.ExecuteAsync(
            "INSERT INTO customers (name, city, email) VALUES (@Name, @City, @Email)",
            new { Name = "Alice", City = "Jakarta", Email = "alice@example.com" });

        var count = await conn.QuerySingleAsync<long>("SELECT count(*) FROM customers");
        Assert.Equal(1L, count);
    }
}
