using BeanCart.Data.Models;
using Dapper;
using Npgsql;
using static BeanCart.Data.SqlFragments;

namespace BeanCart.Data.Repositories;

public sealed class OrderRepository(NpgsqlConnectionFactory connections)
{
    public async Task<IReadOnlyList<OrderDetailLine>> GetOrderDetailsAsync(int orderId)
    {
        await using var conn = connections.Create();
        var rows = await conn.QueryAsync<OrderDetailLine>("""
            SELECT c.name AS customer_name, p.name AS product_name,
                   oi.quantity, oi.unit_price, oi.quantity * oi.unit_price AS line_total
            FROM orders o
            JOIN customers c    ON c.id = o.customer_id
            JOIN order_items oi ON oi.order_id = o.id
            JOIN products p     ON p.id = oi.product_id
            WHERE o.id = @OrderId
            ORDER BY oi.id
            """, new { OrderId = orderId });
        return rows.AsList();
    }

    public async Task<IReadOnlyList<Order>> SearchOrdersAsync(
        IReadOnlyCollection<string>? statuses = null, DateTime? from = null, DateTime? to = null, string? city = null)
    {
        var where = new List<string>();
        var args = new DynamicParameters();
        if (statuses is { Count: > 0 }) { where.Add("o.status = ANY(@Statuses)"); args.Add("Statuses", statuses.ToArray()); }
        if (from is not null) { where.Add("o.ordered_at >= @From"); args.Add("From", from); }
        if (to is not null) { where.Add("o.ordered_at < @To"); args.Add("To", to); }
        if (!string.IsNullOrWhiteSpace(city)) { where.Add("c.city = @City"); args.Add("City", city); }

        await using var conn = connections.Create();
        var rows = await conn.QueryAsync<Order>($"""
            SELECT o.id, o.customer_id, o.status, o.ordered_at
            FROM orders o JOIN customers c ON c.id = o.customer_id
            {Where(where)}
            ORDER BY o.ordered_at, o.id
            """, args);
        return rows.AsList();
    }

    public async Task<int> PlaceOrderAsync(int customerId, IReadOnlyList<OrderLineRequest> lines)
    {
        if (lines.Count == 0) throw new ArgumentException("An order needs at least one line.", nameof(lines)); // rule 1

        await using var conn = connections.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var orderId = await conn.QuerySingleAsync<int>(
            "INSERT INTO orders (customer_id) VALUES (@CustomerId) RETURNING id",
            new { CustomerId = customerId }, tx);

        foreach (var line in lines)
        {
            decimal? price;
            try
            {
                price = await conn.QuerySingleOrDefaultAsync<decimal?>(
                    "UPDATE products SET stock = stock - @Quantity WHERE id = @ProductId RETURNING price",
                    line, tx);
            }
            catch (PostgresException ex) when (ex.ConstraintName == "products_stock_check")
            {
                throw new InvalidOperationException(
                    $"Not enough stock for product {line.ProductId} (requested {line.Quantity}).", ex);
            }

            // Unknown product: price is null; insert anyway so the foreign key rejects the line in the database.
            await conn.ExecuteAsync("""
                INSERT INTO order_items (order_id, product_id, quantity, unit_price)
                VALUES (@OrderId, @ProductId, @Quantity, @UnitPrice)
                """, new { OrderId = orderId, line.ProductId, line.Quantity, UnitPrice = price ?? 0 }, tx);
        }

        await tx.CommitAsync();
        return orderId;
    }

    public async Task CancelOrderAsync(int orderId)
    {
        await using var conn = connections.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var updated = await conn.ExecuteAsync(
            "UPDATE orders SET status = 'Cancelled' WHERE id = @OrderId AND status <> 'Cancelled'",
            new { OrderId = orderId }, tx);
        if (updated == 0) throw new InvalidOperationException($"Order {orderId} does not exist or is already cancelled.");

        await conn.ExecuteAsync("""
            UPDATE products p SET stock = p.stock + oi.quantity
            FROM (SELECT product_id, sum(quantity) AS quantity
                  FROM order_items WHERE order_id = @OrderId GROUP BY product_id) oi
            WHERE p.id = oi.product_id
            """, new { OrderId = orderId }, tx);

        await tx.CommitAsync();
    }
}
