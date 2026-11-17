using BeanCart.Data.Models;
using Dapper;
using static BeanCart.Data.SqlFragments;

namespace BeanCart.Data.Repositories;

public sealed class ProductRepository(NpgsqlConnectionFactory connections)
{
    public async Task<IReadOnlyList<Product>> SearchProductsAsync(
        string? nameContains = null, string? category = null,
        decimal? minPrice = null, decimal? maxPrice = null, int? stockBelow = null)
    {
        var where = new List<string>();
        var args = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(nameContains)) { where.Add("name ILIKE '%' || @Name || '%'"); args.Add("Name", nameContains); }
        if (!string.IsNullOrWhiteSpace(category)) { where.Add("category = @Category"); args.Add("Category", category); }
        if (minPrice is not null) { where.Add("price >= @MinPrice"); args.Add("MinPrice", minPrice); }
        if (maxPrice is not null) { where.Add("price <= @MaxPrice"); args.Add("MaxPrice", maxPrice); }
        if (stockBelow is not null) { where.Add("stock < @StockBelow"); args.Add("StockBelow", stockBelow); }

        await using var conn = connections.Create();
        var rows = await conn.QueryAsync<Product>($"""
            SELECT id, name, category, price, stock FROM products
            {Where(where)}
            ORDER BY id
            """, args);
        return rows.AsList();
    }
}
