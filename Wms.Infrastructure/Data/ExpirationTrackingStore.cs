using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.StockKeepingUnits;
using Wms.Domain;

namespace Wms.Data;

public sealed class ExpirationTrackingStore(
    IDbContextFactory<ApplicationDbContext> contextFactory) : IExpirationTrackingStore
{
    public async Task<StockKeepingUnit?> LoadForChangeAsync(
        ICommandExecutionContext session, Guid skuId, CancellationToken ct)
    {
        if (session is not CommandExecutionStore.Session commandSession)
            throw new InvalidOperationException("Изменение признака должно использовать сессию команды.");

        var db = commandSession.Db;
        var sku = await db.StockKeepingUnits.SingleOrDefaultAsync(x => x.Id == skuId, ct);
        if (sku is not null)
            db.Entry(sku).Property(x => x.TracksExpirationDate).IsModified = true;
        return sku;
    }

    public async Task<IReadOnlyList<ExpirationTrackingChange>> ListHistoryAsync(
        Guid skuId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var events = await db.WarehouseAuditEvents.AsNoTracking()
            .Where(x => x.StockKeepingUnitId == skuId
                && x.EventType == SetExpirationTracking.ChangedEventType)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => new
            {
                x.OccurredAtUtc, x.ActorUserId,
                x.BeforeValuesJson, x.AfterValuesJson
            }).ToListAsync(ct);
        var actorIds = events.Select(x => x.ActorUserId).OfType<string>().Distinct().ToArray();
        var actors = await db.Users.AsNoTracking()
            .Where(x => actorIds.Contains(x.Id))
            .Select(x => new { x.Id, x.DisplayName, x.UserName })
            .ToDictionaryAsync(x => x.Id, ct);

        return events.Select(x => new ExpirationTrackingChange(
            x.OccurredAtUtc,
            x.ActorUserId is string id && actors.TryGetValue(id, out var actor)
                ? string.IsNullOrWhiteSpace(actor.DisplayName)
                    ? actor.UserName ?? id : actor.DisplayName
                : x.ActorUserId ?? "—",
            ReadFlag(x.BeforeValuesJson), ReadFlag(x.AfterValuesJson))).ToArray();
    }

    private static bool ReadFlag(string? json)
    {
        using var document = JsonDocument.Parse(json
            ?? throw new InvalidOperationException("В истории признака отсутствует значение."));
        return document.RootElement.GetProperty("TracksExpirationDate").GetBoolean();
    }
}
