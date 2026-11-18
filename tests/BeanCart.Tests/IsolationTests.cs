using BeanCart.Data;
using BeanCart.Data.Repositories;
using BeanCart.Tests.Infrastructure;
using Dapper;

namespace BeanCart.Tests;

[Collection(DatabaseCollection.Name)]
[TestCaseOrderer(AlphabeticalOrderer.TypeName, AlphabeticalOrderer.AssemblyName)]
public class IsolationTests : DatabaseTestBase
{
    public IsolationTests(PostgresFixture db) : base(db) { }

    private async Task<SeedIds> SeedAsync()
    {
        await using var conn = Db.CreateConnection();
        return await SeedData.InsertBaselineAsync(conn);
    }

    private async Task<long> CountAsync(string table)
    {
        await using var conn = Db.CreateConnection();
        return await conn.QuerySingleAsync<long>($"SELECT count(*) FROM {table}");
    }

    private async Task InsertAliceAsync()
    {
        foreach (var table in new[] { "customers", "products", "orders", "order_items" })
            Assert.Equal(0L, await CountAsync(table));

        await using var conn = Db.CreateConnection();
        await conn.ExecuteAsync(
            "INSERT INTO customers (name, city, email) VALUES ('Alice', 'Jakarta', 'alice@example.com')");
        Assert.Equal(1L, await CountAsync("customers"));
    }

    // D1
    [Fact]
    public Task D1_first_test_starts_empty_and_inserts_alice() => InsertAliceAsync();

    // D1
    [Fact]
    public Task D1_second_test_starts_empty_and_inserts_the_same_email() => InsertAliceAsync();

    // D2: rows run in step order (see AlphabeticalOrderer), so Y runs before X, between, and after.
    [Theory]
    [InlineData(1, "Y")]
    [InlineData(2, "X")]
    [InlineData(3, "Y")]
    [InlineData(4, "X")]
    [InlineData(5, "Y")]
    public async Task D2_seeded_test_sees_5_orders_whatever_ran_before(int step, string test)
    {
        _ = step; // only there to put the rows in display-name order
        var ids = await SeedAsync();

        if (test == "X")
        {
            var orders = new OrderRepository(new NpgsqlConnectionFactory(Db.ConnectionString));
            for (var i = 0; i < 3; i++)
                await orders.PlaceOrderAsync(ids.Customers["Dewi"], [new(ids.Products["Robusta"], 1)]);
            Assert.Equal(8L, await CountAsync("orders"));
        }
        else
        {
            Assert.Equal(5L, await CountAsync("orders"));
        }
    }

    // D3
    [Theory]
    [InlineData(true, 1L)]
    [InlineData(false, 0L)]
    public async Task D3_uncommitted_insert_is_invisible_to_another_connection(bool commit, long countAfter)
    {
        await using var a = Db.CreateConnection();
        await a.OpenAsync();
        await using var b = Db.CreateConnection();

        Assert.Equal("read committed", await b.QuerySingleAsync<string>("SHOW default_transaction_isolation"));

        await using (var tx = await a.BeginTransactionAsync())
        {
            await a.ExecuteAsync(
                "INSERT INTO customers (name, city, email) VALUES ('Eka', 'Medan', 'eka@example.com')", transaction: tx);
            Assert.Equal(1L, await a.QuerySingleAsync<long>("SELECT count(*) FROM customers", transaction: tx));
            Assert.Equal(0L, await b.QuerySingleAsync<long>("SELECT count(*) FROM customers"));

            if (commit) await tx.CommitAsync();
            else await tx.RollbackAsync();
        }

        Assert.Equal(countAfter, await b.QuerySingleAsync<long>("SELECT count(*) FROM customers"));
    }
}
