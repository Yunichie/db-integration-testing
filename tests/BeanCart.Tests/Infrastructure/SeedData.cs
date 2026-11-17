using Dapper;
using Npgsql;

namespace BeanCart.Tests.Infrastructure;

public sealed record SeedIds(
    IReadOnlyDictionary<string, int> Customers,
    IReadOnlyDictionary<string, int> Products,
    IReadOnlyDictionary<string, int> Orders);

public static class SeedData
{
    public static async Task<SeedIds> InsertBaselineAsync(NpgsqlConnection conn)
    {
        var customers = new Dictionary<string, int>();
        foreach (var (name, city) in new[] { ("Alice", "Jakarta"), ("Budi", "Bandung"), ("Citra", "Jakarta"), ("Dewi", "Surabaya") })
            customers[name] = await conn.QuerySingleAsync<int>(
                "INSERT INTO customers (name, city, email) VALUES (@name, @city, @email) RETURNING id",
                new { name, city, email = $"{name.ToLowerInvariant()}@example.com" });

        var products = new Dictionary<string, int>();
        foreach (var (label, name, category, price, stock) in new[]
                 {
                     ("Arabica", "Arabica Gayo 250g", "Beans", 80_000m, 10),
                     ("Robusta", "Robusta Lampung 250g", "Beans", 50_000m, 20),
                     ("Toraja", "Toraja Kalosi 250g", "Beans", 95_000m, 5),
                     ("V60", "V60 Dripper", "Equipment", 150_000m, 3),
                     ("Filters", "Paper Filters (100)", "Equipment", 30_000m, 2),
                 })
            products[label] = await conn.QuerySingleAsync<int>(
                "INSERT INTO products (name, category, price, stock) VALUES (@name, @category, @price, @stock) RETURNING id",
                new { name, category, price, stock });

        var orders = new Dictionary<string, int>();
        foreach (var (label, customer, status, date, lines) in new[]
                 {
                     ("O1", "Alice", "Paid", new DateTime(2026, 10, 1), new[] { ("Arabica", 2), ("Robusta", 1) }),
                     ("O2", "Alice", "Shipped", new DateTime(2026, 10, 15), new[] { ("V60", 1), ("Arabica", 1) }),
                     ("O3", "Budi", "Paid", new DateTime(2026, 10, 20), new[] { ("Toraja", 2) }),
                     ("O4", "Budi", "Cancelled", new DateTime(2026, 10, 22), new[] { ("Robusta", 4) }),
                     ("O5", "Citra", "Pending", new DateTime(2026, 11, 2), new[] { ("Arabica", 1), ("Filters", 1) }),
                 })
        {
            var orderId = orders[label] = await conn.QuerySingleAsync<int>(
                "INSERT INTO orders (customer_id, status, ordered_at) VALUES (@customerId, @status, @orderedAt) RETURNING id",
                new { customerId = customers[customer], status, orderedAt = DateTime.SpecifyKind(date, DateTimeKind.Utc) });

            foreach (var (product, quantity) in lines)
                await conn.ExecuteAsync("""
                    INSERT INTO order_items (order_id, product_id, quantity, unit_price)
                    SELECT @orderId, id, @quantity, price FROM products WHERE id = @productId
                    """, new { orderId, productId = products[product], quantity });
        }

        return new SeedIds(customers, products, orders);
    }
}
