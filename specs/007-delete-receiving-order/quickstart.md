# Validation guide: receiving order deletion

The developer runs builds, focused tests, migration application and interactive
checks under `AGENTS.md`. Migration `20261029100141_AddReceivingOrderDeletion`
was generated and inspected. From the repository root, run:

```powershell
dotnet build Wms.WebApp/Wms.WebApp.csproj
dotnet test Wms.Tests/Wms.Tests.csproj --filter "FullyQualifiedName~Receiving_order_deletion"
dotnet ef database update --project Wms.Infrastructure/Wms.Infrastructure.csproj --startup-project Wms.WebApp/Wms.WebApp.csproj --connection "<dev connection string>"
```

The focused SQL tests create their own temporary database and apply migrations.
Apply the migration to the development database before interactive checks.

## Stock and history

Prepare an order with two closed LPNs on different SKU/date groups, a quantity correction, an open LPN and a cancelled LPN. Do not put away any. Preview as Manager and confirm. Expect the order and all assigned LPNs deleted, cycles retained, exact reversals for outstanding originals, no unrelated stock change and released 1C links.

## Rejections

Put away any amount from any assigned LPN, including one later deleted separately; order deletion must fail. Operator-only and Administrator-only attempts must be denied. Change an LPN or relevant balance after preview; confirmation must fail without partial effects.

## Replay and clients

Repeat a successful confirmation with the same request ID and data: no second reversals or audit. A new request against the deleted order fails. A formerly linked 1C document can create a new order. The deleted order is absent from Mobile work, and stale Mobile commands cannot resume receiving or putaway.
