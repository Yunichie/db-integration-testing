namespace BeanCart.Data;

// Shared
internal static class SqlFragments
{
    public const string RevenueStatuses = "('Paid', 'Shipped')";

    public static string Where(List<string> conditions) =>
        conditions.Count == 0 ? "" : "WHERE " + string.Join(" AND ", conditions);
}
