using Dapper;
using Npgsql;

namespace BeanCart.Data;

public sealed class NpgsqlConnectionFactory(string connectionString)
{
    static NpgsqlConnectionFactory() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    public NpgsqlConnection Create() => new(connectionString);
}
