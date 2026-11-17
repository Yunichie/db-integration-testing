namespace BeanCart.Data.Models;

public sealed record Product
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public decimal Price { get; init; }
    public int Stock { get; init; }
}
