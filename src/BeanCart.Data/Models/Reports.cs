namespace BeanCart.Data.Models;

public sealed record CustomerOrderCount
{
    public int CustomerId { get; init; }
    public string Name { get; init; } = "";
    public long OrderCount { get; init; }
}

public sealed record CustomerRevenue
{
    public int CustomerId { get; init; }
    public string Name { get; init; } = "";
    public decimal Revenue { get; init; }
}

public sealed record CategoryRevenue
{
    public string Category { get; init; } = "";
    public decimal Revenue { get; init; }
}

public sealed record ProductSales
{
    public int ProductId { get; init; }
    public string Name { get; init; } = "";
    public long UnitsSold { get; init; }
}

public sealed record StatusCount
{
    public string Status { get; init; } = "";
    public long OrderCount { get; init; }
}
