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
