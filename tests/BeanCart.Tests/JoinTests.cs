using BeanCart.Data;
using BeanCart.Data.Repositories;
using BeanCart.Tests.Infrastructure;
using Dapper;

namespace BeanCart.Tests;

[Collection(DatabaseCollection.Name)]
public class JoinTests : DatabaseTestBase
{
    public JoinTests(PostgresFixture db) : base(db) { }

    private async Task<SeedIds> SeedAsync()
    {
        await using var conn = Db.CreateConnection();
        return await SeedData.InsertBaselineAsync(conn);
    }

    // J1
    [Fact]
    public async Task Order_details_join_four_tables()
    {
        var ids = await SeedAsync();
        var orders = new OrderRepository(new NpgsqlConnectionFactory(Db.ConnectionString));

        var lines = await orders.GetOrderDetailsAsync(ids.Orders["O1"]);

        Assert.Collection(lines,
            l => Assert.Equal(("Alice", "Arabica Gayo 250g", 2, 80_000m, 160_000m),
                              (l.CustomerName, l.ProductName, l.Quantity, l.UnitPrice, l.LineTotal)),
            l => Assert.Equal(("Alice", "Robusta Lampung 250g", 1, 50_000m, 50_000m),
                              (l.CustomerName, l.ProductName, l.Quantity, l.UnitPrice, l.LineTotal)));
    }

    // J2
    [Fact]
    public async Task Left_join_keeps_customers_without_orders_inner_join_drops_them()
    {
        await SeedAsync();
        var reports = new ReportRepository(new NpgsqlConnectionFactory(Db.ConnectionString));

        var left = await reports.ListCustomersWithOrderCountAsync();
        Assert.Equal(
            [("Alice", 2L), ("Budi", 2L), ("Citra", 1L), ("Dewi", 0L)],
            left.Select(r => (r.Name, r.OrderCount)));

        await using var conn = Db.CreateConnection();
        var inner = await conn.QueryAsync<(string Name, long Count)>("""
            SELECT c.name, count(o.id)
            FROM customers c JOIN orders o ON o.customer_id = c.id
            GROUP BY c.id, c.name ORDER BY c.name
            """);
        Assert.Equal([("Alice", 2L), ("Budi", 2L), ("Citra", 1L)], inner);
    }

    // J3a
    [Fact]
    public async Task Filter_in_ON_keeps_zero_rows_filter_in_WHERE_removes_them()
    {
        await SeedAsync();
        await using var conn = Db.CreateConnection();

        var inOn = await conn.QueryAsync<(string, long)>("""
            SELECT c.name, count(o.id)
            FROM customers c LEFT JOIN orders o ON o.customer_id = c.id AND o.status = 'Paid'
            GROUP BY c.id, c.name ORDER BY c.name
            """);
        Assert.Equal([("Alice", 1L), ("Budi", 1L), ("Citra", 0L), ("Dewi", 0L)], inOn);

        var inWhere = await conn.QueryAsync<(string, long)>("""
            SELECT c.name, count(o.id)
            FROM customers c LEFT JOIN orders o ON o.customer_id = c.id
            WHERE o.status = 'Paid'
            GROUP BY c.id, c.name ORDER BY c.name
            """);
        Assert.Equal([("Alice", 1L), ("Budi", 1L)], inWhere);
    }

    // J3b
    [Fact]
    public async Task Joining_orders_to_lines_multiplies_rows()
    {
        var ids = await SeedAsync();
        await using var conn = Db.CreateConnection();

        var (rows, distinctOrders) = await conn.QuerySingleAsync<(long, long)>("""
            SELECT count(*), count(DISTINCT o.id)
            FROM orders o JOIN order_items oi ON oi.order_id = o.id
            WHERE o.customer_id = @alice
            """, new { alice = ids.Customers["Alice"] });

        Assert.Equal(4L, rows);
        Assert.Equal(2L, distinctOrders);
    }
}
