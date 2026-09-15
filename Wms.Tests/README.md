# Wms.Tests

`Wms.Tests` is the only automated test project. Run it on Windows with SQL
Server LocalDB available. The complete suite is a manual checkpoint and must
not be run automatically without an explicit request:

```powershell
dotnet test Wms.Tests/Wms.Tests.csproj
```

The 14-test suite is intentionally small. It protects migration drift; central
command replay, payload conflict, receipt races, and transaction rollback; LPN
uniqueness and non-reuse after maintenance; one receiving location under a
first-participant race; receiving synchronization quantity protection; complete
and atomic putaway; competing stock withdrawal; inventory-count correction and
stock-drift rejection; once-only picking/shipping; and atomic compensating
shipping rollback.

Do not add tests for coverage. Add one only for a distinct critical business or
inventory invariant, transaction boundary, SQL concurrency guarantee, or serious
regression. Generic `CommandExecutor` behavior belongs in the infrastructure
tests and must not be copied into every feature.

During implementation, run only one or two directly relevant tests with a
`--filter` expression. In the final handoff, state which focused checks ran and
leave the complete command above for the developer.
