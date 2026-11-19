# DB and SQL Integration Testing via Testcontainers

## First time set up

### Prerequisites

| Tool | Version | Check |
| --- | --- | --- |
| .NET SDK | 10 (the projects target `net10.0`) | `dotnet --version` |
| Docker | Docker Desktop (Windows: WSL 2 backend, macOS) or Docker Engine (Linux) | `docker run --rm hello-world` |

- Docker must be running before you test. Without it every test fails at startup.
- On Linux, your user must be able to use Docker without `sudo`: `sudo usermod -aG docker $USER`, then log out and back in.
- With Podman, point `DOCKER_HOST` at the Podman socket.
- The first run needs internet so Docker can download the `postgres:17-alpine` image and NuGet can restore packages.
- Optional: a SQL client (DBeaver, pgAdmin or `psql`) to look inside the test database.

First, clone the repository:
```bash
git clone <repo url>
```

Then, from the repository root:

```bash
docker pull postgres:17-alpine   # optional
dotnet restore
dotnet build
```

### Running

```bash
dotnet test                                          # all tests
dotnet test --filter "FullyQualifiedName~Smoke"      # one class
dotnet test --logger "console;verbosity=detailed"    # show each test's name and output
```

### Troubleshooting

| Symptom | Fix |
| --- | --- |
| All tests fail saying Docker is not running or cannot be found | Start Docker. On Linux, check your user is in the `docker` group |
| First run hangs or times out | The image is still downloading. Run `docker pull postgres:17-alpine`, then test again |
| `FileNotFoundException` for `Sql/schema.sql` | The file must be copied to the output folder; check the `None Include="Sql\schema.sql"` item in `BeanCart.Tests.csproj` |
| `Cannot write DateTime with Kind=Unspecified ...` | Use UTC dates: `new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)` |
| Tests pass alone but fail randomly as a suite | A test class is missing `[Collection(DatabaseCollection.Name)]`, so it runs in parallel on the same database |

## Testing Implementation

The tests generally follow the Arrange-Act-Assert pattern. 

First, we need to define the class:
```cs
[Collection(DatabaseCollection.Name)]          // 1. Shared container
public class XxxTests : DatabaseTestBase       // 2. Base class resets the DB before each test
{
    public XxxTests(PostgresFixture db) : base(db) { }

    ...
}
```

And, then, inside the class, we can define the tests:
```cs
[Fact]
public async Task Describes_the_rule_being_proved_1()
{
    ...
}

[Fact]
public async Task Describes_the_rule_being_proved_2()
{
    ...
}
```

Only then, within each of the functions that we have defined, can we follow the Arrange-Act-Assert pattern:
```cs
[Fact]
public async Task Describes_the_rule_being_proved()
{
    // Arrange: start from the empty DB, seed the baseline, get ids by label
    await using var conn = Db.CreateConnection();
    var ids = await SeedData.InsertBaselineAsync(conn);
    var repo = new OrderRepository(new NpgsqlConnectionFactory(Db.ConnectionString));

    // Act: call the repository (or raw SQL when the scenario compares query styles)
    var result = await repo.SomethingAsync(ids.Orders["O1"]);

    // Assert: exact values worked out by hand from the seed tables
    Assert.Equal(expected, result.Select(r => (r.Name, r.Value)));
}
```

## Running the Tests

To run the tests, make sure you are on the root directory:
```bash
$ pwd
/.../BeanCart
```

And then, run:
```bash
dotnet test
```

Running that command should yield similar output to this:
```bash
Restore complete (0.6s)
  BeanCart.Data net10.0 succeeded (2.1s) → src/BeanCart.Data/bin/Debug/net10.0/BeanCart.Data.dll
  BeanCart.Tests net10.0 succeeded (0.4s) → tests/BeanCart.Tests/bin/Debug/net10.0/BeanCart.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v3.1.4+50e68bbb8b (64-bit .NET 10.0.14)
[xUnit.net 00:00:00.06]   Discovering: BeanCart.Tests
[xUnit.net 00:00:00.09]   Discovered:  BeanCart.Tests
[xUnit.net 00:00:00.12]   Starting:    BeanCart.Tests
[testcontainers.org 00:00:00.10] Connected to Docker:
  Host: unix:///var/run/docker.sock
  Server Version: 29.9.3
  Kernel Version: 7.3.3-1-cachyos
  API Version: 1.56
  Operating System: CachyOS
  Total Memory: 31.08 GB
[testcontainers.org 00:00:00.28] Docker container c22a8ef02fee created
[testcontainers.org 00:00:00.31] Start Docker container c22a8ef02fee
[testcontainers.org 00:00:00.43] Wait for Docker container c22a8ef02fee to complete readiness checks
[testcontainers.org 00:00:01.45] Docker container c22a8ef02fee ready
[testcontainers.org 00:00:01.56] Docker container 3d0e7c77b718 created
[testcontainers.org 00:00:01.57] Start Docker container 3d0e7c77b718
[testcontainers.org 00:00:01.69] Wait for Docker container 3d0e7c77b718 to complete readiness checks
[testcontainers.org 00:00:01.70] Execute "pg_isready --host localhost --dbname postgres --username postgres" at Docker container 3d0e7c77b718
[testcontainers.org 00:00:02.75] Execute "pg_isready --host localhost --dbname postgres --username postgres" at Docker container 3d0e7c77b718
[testcontainers.org 00:00:03.78] Execute "pg_isready --host localhost --dbname postgres --username postgres" at Docker container 3d0e7c77b718
[testcontainers.org 00:00:04.83] Execute "pg_isready --host localhost --dbname postgres --username postgres" at Docker container 3d0e7c77b718
[testcontainers.org 00:00:04.87] Docker container 3d0e7c77b718 ready
[testcontainers.org 00:00:05.81] Delete Docker container 3d0e7c77b718
[xUnit.net 00:00:06.24]   Finished:    BeanCart.Tests
  BeanCart.Tests test net10.0 succeeded (6.7s)

Test summary: total: 44, failed: 0, succeeded: 44, skipped: 0, duration: 6.7s
Build succeeded in 10.0s
```
