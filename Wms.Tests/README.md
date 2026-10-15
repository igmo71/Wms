# Wms.Tests

`Wms.Tests` is the only automated test project and uses xUnit.
The [testing policy](../.specify/memory/architecture.md#testing-strategy) defines when a
test is necessary and how to keep verification minimal.

Run commands from the repository root on Windows with the project's .NET SDK
and SQL Server LocalDB (`MSSQLLocalDB`) available. SQL tests create a uniquely
named temporary database, apply migrations and delete it during fixture cleanup.

The developer runs builds and tests; Codex runs them only on explicit request.
For a focused check, replace the placeholder with the selected test method name:

```powershell
dotnet test Wms.Tests/Wms.Tests.csproj --filter "FullyQualifiedName~<TestMethodName>"
```

For an agreed full-suite checkpoint:

```powershell
dotnet test Wms.Tests/Wms.Tests.csproj
```
