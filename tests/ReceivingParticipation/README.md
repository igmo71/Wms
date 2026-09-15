# I02 receiving participation checks

Run on Windows with SQL Server LocalDB:

```powershell
dotnet run --project tests/ReceivingParticipation/ReceivingParticipation.csproj
```

The check creates and removes a unique temporary database. It covers command replay,
two participants, the concurrent first-location race, duplicate SKU blocking, local
lifecycle separation, and absence of outgoing 1C calls.

Manual Mobile check:

1. Open **Приёмка**, select a warehouse, and confirm that **В работе** contains only
   orders already joined by the current user.
2. Open **Добавить ордер** and join a ready order. The first participant must scan an
   active Receiving location; a second account joins the same order without choosing
   another location.
3. Confirm that **Товары** shows the plan read-only and **LPN** is empty.
4. Confirm that a deleted, unposted, duplicate-SKU, or unreadable source order remains
   visible with a blocking reason and cannot be joined.
5. Confirm that direct fact editing, completion, and order-based putaway are unavailable
   in Mobile and Web.
