namespace BeanCart.Data.Models;

public static class OrderStatus
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";
    public const string Shipped = "Shipped";
    public const string Cancelled = "Cancelled";
}

public sealed record Order
{
    public int Id { get; init; }
    public int CustomerId { get; init; }
    public string Status { get; init; } = "";
    public DateTime OrderedAt { get; init; }
}

public sealed record OrderLineRequest(int ProductId, int Quantity);

public sealed record OrderDetailLine
{
    public string CustomerName { get; init; } = "";
    public string ProductName { get; init; } = "";
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }
}
