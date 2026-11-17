using BeanCart.Data.Models;
using Dapper;
using static BeanCart.Data.SqlFragments;

namespace BeanCart.Data.Repositories;

public sealed class ReportRepository(NpgsqlConnectionFactory connections)
{
    public async Task<IReadOnlyList<CustomerOrderCount>> ListCustomersWithOrderCountAsync()
    {
        await using var conn = connections.Create();
        var rows = await conn.QueryAsync<CustomerOrderCount>("""
            SELECT c.id AS customer_id, c.name, count(o.id) AS order_count
            FROM customers c
            LEFT JOIN orders o ON o.customer_id = c.id
            GROUP BY c.id, c.name
            ORDER BY c.name
            """);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CustomerRevenue>> GetRevenuePerCustomerAsync(decimal? minRevenue = null)
    {
        const string revenue = "COALESCE(sum(oi.quantity * oi.unit_price), 0)";
        await using var conn = connections.Create();
        var rows = await conn.QueryAsync<CustomerRevenue>($"""
            SELECT c.id AS customer_id, c.name, {revenue} AS revenue
            FROM customers c
            LEFT JOIN orders o       ON o.customer_id = c.id AND o.status IN {RevenueStatuses}
            LEFT JOIN order_items oi ON oi.order_id = o.id
            GROUP BY c.id, c.name
            {(minRevenue is null ? "" : $"HAVING {revenue} > @MinRevenue")}
            ORDER BY revenue DESC, c.name
            """, new { MinRevenue = minRevenue });
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CategoryRevenue>> GetRevenuePerCategoryAsync()
    {
        await using var conn = connections.Create();
        var rows = await conn.QueryAsync<CategoryRevenue>($"""
            SELECT p.category, sum(oi.quantity * oi.unit_price) AS revenue
            FROM order_items oi
            JOIN orders o   ON o.id = oi.order_id
            JOIN products p ON p.id = oi.product_id
            WHERE o.status IN {RevenueStatuses}
            GROUP BY p.category
            ORDER BY p.category
            """);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ProductSales>> GetBestSellingProductsAsync(int top)
    {
        await using var conn = connections.Create();
        var rows = await conn.QueryAsync<ProductSales>($"""
            SELECT p.id AS product_id, p.name, sum(oi.quantity) AS units_sold
            FROM order_items oi
            JOIN orders o   ON o.id = oi.order_id
            JOIN products p ON p.id = oi.product_id
            WHERE o.status IN {RevenueStatuses}
            GROUP BY p.id, p.name
            ORDER BY units_sold DESC, p.name
            LIMIT @Top
            """, new { Top = top });
        return rows.AsList();
    }

    public async Task<IReadOnlyList<StatusCount>> GetOrderCountPerStatusAsync()
    {
        await using var conn = connections.Create();
        var rows = await conn.QueryAsync<StatusCount>("""
            SELECT status, count(*) AS order_count FROM orders GROUP BY status ORDER BY status
            """);
        return rows.AsList();
    }

    public async Task<decimal> GetTotalRevenueAsync()
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleAsync<decimal>($"""
            SELECT COALESCE(sum(oi.quantity * oi.unit_price), 0)
            FROM order_items oi JOIN orders o ON o.id = oi.order_id
            WHERE o.status IN {RevenueStatuses}
            """);
    }

    public async Task<decimal?> GetAverageRevenueOrderValueAsync()
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleAsync<decimal?>($"""
            SELECT avg(total) FROM (
                SELECT sum(oi.quantity * oi.unit_price) AS total
                FROM orders o JOIN order_items oi ON oi.order_id = o.id
                WHERE o.status IN {RevenueStatuses}
                GROUP BY o.id
            ) t
            """);
    }
}
