using BeanCart.Data;
using BeanCart.Data.Models;
using BeanCart.Data.Repositories;
using BeanCart.Tests.Infrastructure;
using Dapper;

namespace BeanCart.Tests;

[Collection(DatabaseCollection.Name)]
public class TransactionTests : DatabaseTestBase
{
    public TransactionTests(PostgresFixture db) : base(db) { }

    private OrderRepository Orders => new(new NpgsqlConnectionFactory(Db.ConnectionString));

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

    private async Task<int> StockAsync(int productId)
    {
        await using var conn = Db.CreateConnection();
        return await conn.QuerySingleAsync<int>("SELECT stock FROM products WHERE id = @productId", new { productId });
    }

    private async Task<string> StatusAsync(int orderId)
    {
        await using var conn = Db.CreateConnection();
        return await conn.QuerySingleAsync<string>("SELECT status FROM orders WHERE id = @orderId", new { orderId });
    }

    // T1
    [Fact]
    public async Task Place_order_saves_order_lines_and_stock_together()
    {
        var ids = await SeedAsync();
        int arabica = ids.Products["Arabica"], toraja = ids.Products["Toraja"];

        var orderId = await Orders.PlaceOrderAsync(ids.Customers["Alice"], [new(arabica, 2), new(toraja, 1)]);

        Assert.DoesNotContain(orderId, ids.Orders.Values);
        Assert.Equal(6L, await CountAsync("orders"));
        Assert.Equal(10L, await CountAsync("order_items"));
        Assert.Equal(OrderStatus.Pending, await StatusAsync(orderId));
        Assert.Equal(8, await StockAsync(arabica));
        Assert.Equal(4, await StockAsync(toraja));

        var lines = await Orders.GetOrderDetailsAsync(orderId);
        Assert.Equal([("Arabica Gayo 250g", 2, 80_000m), ("Toraja Kalosi 250g", 1, 95_000m)],
            lines.Select(l => (l.ProductName, l.Quantity, l.UnitPrice)));
        Assert.All(lines, l => Assert.Equal("Alice", l.CustomerName));
        Assert.Equal(255_000m, lines.Sum(l => l.LineTotal));
    }

    // T1
    [Fact]
    public async Task Later_price_change_does_not_rewrite_placed_order()
    {
        var ids = await SeedAsync();
        int arabica = ids.Products["Arabica"];
        var orderId = await Orders.PlaceOrderAsync(ids.Customers["Alice"], [new(arabica, 2), new(ids.Products["Toraja"], 1)]);

        await using (var conn = Db.CreateConnection())
            await conn.ExecuteAsync("UPDATE products SET price = 90000 WHERE id = @arabica", new { arabica });

        var lines = await Orders.GetOrderDetailsAsync(orderId);
        Assert.Equal(80_000m, lines.Single(l => l.ProductName == "Arabica Gayo 250g").UnitPrice);
        Assert.Equal(255_000m, lines.Sum(l => l.LineTotal));
    }

    // T2
    [Fact]
    public async Task Cancel_order_sets_status_and_returns_stock_together()
    {
        var ids = await SeedAsync();
        var o5 = ids.Orders["O5"];

        await Orders.CancelOrderAsync(o5);

        Assert.Equal(OrderStatus.Cancelled, await StatusAsync(o5));
        Assert.Equal(11, await StockAsync(ids.Products["Arabica"]));
        Assert.Equal(3, await StockAsync(ids.Products["Filters"]));
    }

    // T2
    [Fact]
    public async Task Cancelling_an_already_cancelled_order_fails_and_returns_no_stock()
    {
        var ids = await SeedAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.CancelOrderAsync(ids.Orders["O4"]));

        Assert.Equal(20, await StockAsync(ids.Products["Robusta"]));
    }
}
