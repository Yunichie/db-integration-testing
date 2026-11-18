using BeanCart.Data;
using BeanCart.Data.Models;
using BeanCart.Data.Repositories;
using BeanCart.Tests.Infrastructure;
using Dapper;

namespace BeanCart.Tests;

[Collection(DatabaseCollection.Name)]
public class FilteringTests : DatabaseTestBase
{
    public FilteringTests(PostgresFixture db) : base(db) { }

    private ProductRepository Products => new(new NpgsqlConnectionFactory(Db.ConnectionString));
    private OrderRepository Orders => new(new NpgsqlConnectionFactory(Db.ConnectionString));

    private async Task<SeedIds> SeedAsync()
    {
        await using var conn = Db.CreateConnection();
        return await SeedData.InsertBaselineAsync(conn);
    }

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static IEnumerable<int> Labels(SeedIds ids, params string[] orders) => orders.Select(o => ids.Orders[o]);

    // F1
    [Fact]
    public async Task Status_Paid_finds_O1_and_O3()
    {
        var ids = await SeedAsync();
        var orders = await Orders.SearchOrdersAsync(statuses: [OrderStatus.Paid]);

        Assert.Equal(Labels(ids, "O1", "O3"), orders.Select(o => o.Id));
    }

    // F1
    [Fact]
    public async Task Empty_order_filters_are_ignored_and_return_all_five()
    {
        var ids = await SeedAsync();
        var none = await Orders.SearchOrdersAsync();
        var empty = await Orders.SearchOrdersAsync(statuses: [], city: "  ");

        Assert.Equal(Labels(ids, "O1", "O2", "O3", "O4", "O5"), none.Select(o => o.Id));
        Assert.Equal(Labels(ids, "O1", "O2", "O3", "O4", "O5"), empty.Select(o => o.Id));
    }

    // F2
    [Fact]
    public async Task Date_range_includes_start_and_excludes_end()
    {
        var ids = await SeedAsync();
        var orders = await Orders.SearchOrdersAsync(from: Utc(2026, 10, 10), to: Utc(2026, 11, 1));

        // O1 (1 Oct) is too early, O5 (2 Nov) too late.
        Assert.Equal(Labels(ids, "O2", "O3", "O4"), orders.Select(o => o.Id));
    }

    // F2
    [Fact]
    public async Task Order_on_the_end_date_falls_in_the_next_range_only()
    {
        var ids = await SeedAsync();
        var before = await Orders.SearchOrdersAsync(from: Utc(2026, 10, 1), to: Utc(2026, 10, 15));
        var after = await Orders.SearchOrdersAsync(from: Utc(2026, 10, 15), to: Utc(2026, 11, 1));

        // O2 is ordered exactly on 15 Oct.
        Assert.Equal(Labels(ids, "O1"), before.Select(o => o.Id));
        Assert.Equal(Labels(ids, "O2", "O3", "O4"), after.Select(o => o.Id));
    }

    // F2
    [Fact]
    public async Task Jakarta_with_Paid_or_Shipped_finds_O1_and_O2()
    {
        var ids = await SeedAsync();
        var orders = await Orders.SearchOrdersAsync(statuses: [OrderStatus.Paid, OrderStatus.Shipped], city: "Jakarta");

        // Citra's O5 is in Jakarta but Pending.
        Assert.Equal(Labels(ids, "O1", "O2"), orders.Select(o => o.Id));
    }

    // F3
    [Fact]
    public async Task Beans_priced_at_least_60000_are_Arabica_and_Toraja()
    {
        await SeedAsync();
        var products = await Products.SearchProductsAsync(category: "Beans", minPrice: 60_000m);

        Assert.Equal(["Arabica Gayo 250g", "Toraja Kalosi 250g"], products.Select(p => p.Name));
    }

    // F3
    [Fact]
    public async Task Price_range_includes_both_boundary_prices()
    {
        await SeedAsync();
        var products = await Products.SearchProductsAsync(minPrice: 50_000m, maxPrice: 95_000m);

        Assert.Equal([("Arabica Gayo 250g", 80_000m), ("Robusta Lampung 250g", 50_000m), ("Toraja Kalosi 250g", 95_000m)],
            products.Select(p => (p.Name, p.Price)));
    }

    // F3
    [Fact]
    public async Task Stock_below_5_excludes_Toraja_at_exactly_5()
    {
        await SeedAsync();
        var products = await Products.SearchProductsAsync(stockBelow: 5);

        Assert.Equal([("V60 Dripper", 3), ("Paper Filters (100)", 2)], products.Select(p => (p.Name, p.Stock)));
    }

    // F4
    [Fact]
    public async Task Name_search_ignores_letter_case()
    {
        await SeedAsync();
        var products = await Products.SearchProductsAsync(nameContains: "arabica");

        Assert.Equal(["Arabica Gayo 250g"], products.Select(p => p.Name));
    }

    // F4
    [Fact]
    public async Task Injection_text_travels_as_a_parameter_and_drops_nothing()
    {
        await SeedAsync();
        var products = await Products.SearchProductsAsync(nameContains: "'; DROP TABLE products; --");

        Assert.Empty(products);
        await using var conn = Db.CreateConnection();
        Assert.Equal(5L, await conn.QuerySingleAsync<long>("SELECT count(*) FROM products"));
    }
}
