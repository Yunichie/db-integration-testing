using BeanCart.Data;
using BeanCart.Data.Models;
using BeanCart.Data.Repositories;
using BeanCart.Tests.Infrastructure;

namespace BeanCart.Tests;

[Collection(DatabaseCollection.Name)]
public class AggregationTests : DatabaseTestBase
{
    public AggregationTests(PostgresFixture db) : base(db) { }

    private ReportRepository Reports => new(new NpgsqlConnectionFactory(Db.ConnectionString));

    private async Task<SeedIds> SeedAsync()
    {
        await using var conn = Db.CreateConnection();
        return await SeedData.InsertBaselineAsync(conn);
    }

    // A1
    [Fact]
    public async Task Revenue_per_customer_lists_everyone_with_zero_not_null()
    {
        await SeedAsync();
        var revenues = await Reports.GetRevenuePerCustomerAsync();

        Assert.Equal([("Alice", 440_000m), ("Budi", 190_000m), ("Citra", 0m), ("Dewi", 0m)], revenues.Select(r => (r.Name, r.Revenue)));
    }

    // A1
    [Fact]
    public async Task Revenue_above_minimum_uses_HAVING_and_keeps_only_Alice()
    {
        await SeedAsync();
        var revenues = await Reports.GetRevenuePerCustomerAsync(minRevenue: 200_000m);

        Assert.Equal([("Alice", 440_000m)], revenues.Select(r => (r.Name, r.Revenue)));
    }

    // A2
    [Fact]
    public async Task Revenue_per_category_sums_to_total_revenue()
    {
        await SeedAsync();
        var revenues = await Reports.GetRevenuePerCategoryAsync();
        var total = await Reports.GetTotalRevenueAsync();

        Assert.Equal([("Beans", 480_000m), ("Equipment", 150_000m)], revenues.Select(c => (c.Category, c.Revenue)));
        Assert.Equal(total, revenues.Sum(c => c.Revenue));
        Assert.Equal(630_000m, total);
    }

    // A3
    [Fact]
    public async Task Best_sellers_rank_by_units_sold()
    {
        await SeedAsync();
        var rank = await Reports.GetBestSellingProductsAsync(2);

        Assert.Equal([("Arabica Gayo 250g", 3L), ("Toraja Kalosi 250g", 2L)],
            rank.Select(r => (r.Name, r.UnitsSold)));
    }

    [Fact]
    public async Task Best_sellers_break_ties_by_name()
    {
        await SeedAsync();
        var rank = await Reports.GetBestSellingProductsAsync(3);

        Assert.Equal([("Arabica Gayo 250g", 3L), ("Toraja Kalosi 250g", 2L), ("Robusta Lampung 250g", 1L)],
            rank.Select(r => (r.Name, r.UnitsSold)));
    }

    // A4
    [Fact]
    public async Task Order_count_per_status_includes_every_status()
    {
        await SeedAsync();
        var status = await Reports.GetOrderCountPerStatusAsync();

        Assert.Equal([("Cancelled", 1), ("Paid", 2), ("Pending", 1), ("Shipped", 1)], status.Select(s => (s.Status, s.OrderCount)));
    }

    [Fact]
    public async Task Average_revenue_order_value_is_total_divided_by_revenue_orders()
    {
        await SeedAsync();
        var revenue = await Reports.GetAverageRevenueOrderValueAsync();
        var orders = (await Reports.GetOrderCountPerStatusAsync()).Where(s => s.Status is OrderStatus.Paid or OrderStatus.Shipped).Sum(s => s.OrderCount);
        var total = await Reports.GetTotalRevenueAsync();

        Assert.Equal(630_000m, total);
        Assert.Equal(3L, orders);
        Assert.Equal(210_000m, revenue);
        Assert.Equal(total / orders, revenue);
    }

    [Fact]
    public async Task Total_revenue_on_empty_database_is_zero()
    {
        var revenue = await Reports.GetTotalRevenueAsync();

        Assert.Equal(0m, revenue);
    }
}
