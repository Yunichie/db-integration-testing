namespace BeanCart.Tests.Infrastructure;

[CollectionDefinition(Name)]
public class DatabaseCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Database";
}
