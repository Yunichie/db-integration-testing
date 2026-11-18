using BeanCart.Data;
using BeanCart.Data.Models;
using BeanCart.Data.Repositories;
using BeanCart.Tests.Infrastructure;
using Dapper;
using Npgsql;

namespace BeanCart.Tests;

[Collection(DatabaseCollection.Name)]
public class RollbackTests : DatabaseTestBase
{
    public RollbackTests(PostgresFixture db) : base(db) { }

    private OrderRepository Orders => new(new NpgsqlConnectionFactory(Db.ConnectionString));

    private async Task<SeedIds> SeedAsync()
    {
        await using var conn = Db.CreateConnection();
        return await SeedData.InsertBaselineAsync(conn);
    }

    // Everything a failed Place order could have touched: row counts and every product's stock.
    private sealed record Snapshot(long Orders, long Lines, string Stock);

    private async Task<Snapshot> SnapshotAsync()
    {
        await using var conn = Db.CreateConnection();
        return await conn.QuerySingleAsync<Snapshot>("""
            SELECT (SELECT count(*) FROM orders) AS orders,
                   (SELECT count(*) FROM order_items) AS lines,
                   (SELECT string_agg(id || ':' || stock, ',' ORDER BY id) FROM products) AS stock
            """);
    }

    private async Task<int> StockAsync(int productId)
    {
        await using var conn = Db.CreateConnection();
        return await conn.QuerySingleAsync<int>("SELECT stock FROM products WHERE id = @productId", new { productId });
    }

    // R1
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Out_of_stock_line_undoes_the_whole_order(bool filtersFirst)
    {
        var ids = await SeedAsync();
        var before = await SnapshotAsync();
        OrderLineRequest arabica = new(ids.Products["Arabica"], 1), filters = new(ids.Products["Filters"], 3);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Orders.PlaceOrderAsync(ids.Customers["Alice"], filtersFirst ? [filters, arabica] : [arabica, filters]));

        Assert.Contains("Not enough stock", ex.Message);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal((5L, 8L), (before.Orders, before.Lines));
        Assert.Equal(10, await StockAsync(ids.Products["Arabica"]));
        Assert.Equal(2, await StockAsync(ids.Products["Filters"]));
    }

    // R2
    [Fact]
    public async Task Line_with_quantity_zero_is_rejected_by_CHECK_constraint()
    {
        var ids = await SeedAsync();
        var before = await SnapshotAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            Orders.PlaceOrderAsync(ids.Customers["Alice"], [new(ids.Products["Arabica"], 1), new(ids.Products["Robusta"], 0)]));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal("order_items_quantity_check", ex.ConstraintName);
        Assert.Equal(before, await SnapshotAsync());
    }

    // R2
    [Fact]
    public async Task Line_with_unknown_product_is_rejected_by_foreign_key()
    {
        var ids = await SeedAsync();
        var before = await SnapshotAsync();
        var unknown = ids.Products.Values.Max() + 1000;

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            Orders.PlaceOrderAsync(ids.Customers["Alice"], [new(ids.Products["Arabica"], 1), new(unknown, 1)]));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
        Assert.Equal(before, await SnapshotAsync());
    }

    // R3
    [Fact]
    public async Task Second_order_for_remaining_Toraja_fails_and_keeps_the_first()
    {
        var ids = await SeedAsync();
        var toraja = ids.Products["Toraja"];

        var first = await Orders.PlaceOrderAsync(ids.Customers["Alice"], [new(toraja, 3)]);
        Assert.Equal(2, await StockAsync(toraja));
        var afterFirst = await SnapshotAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Orders.PlaceOrderAsync(ids.Customers["Budi"], [new(toraja, 3)]));

        Assert.Equal(afterFirst, await SnapshotAsync());
        Assert.Equal(6L, afterFirst.Orders);
        Assert.Equal(2, await StockAsync(toraja));
        Assert.Single(await Orders.GetOrderDetailsAsync(first));
    }

    // R4
    [Fact]
    public async Task Disposing_a_transaction_without_commit_rolls_it_back()
    {
        await using (var conn = Db.CreateConnection())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await conn.ExecuteAsync(
                "INSERT INTO customers (name, city, email) VALUES ('Eka', 'Medan', 'eka@example.com')", transaction: tx);
            // no CommitAsync: disposing tx rolls back
        }

        await using var other = Db.CreateConnection();
        Assert.Equal(0L, await other.QuerySingleAsync<long>("SELECT count(*) FROM customers"));
    }
}
