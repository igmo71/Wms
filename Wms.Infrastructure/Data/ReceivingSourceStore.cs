using System.Data;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Receiving;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data;

public sealed class ReceivingSourceStore(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    IReceivingSourceParser parser)
    : IReceivingSourceStore
{
    public async Task<OperationResult<ReceivingSourceVersion>> SaveSuccessfulAsync(
        ParsedReceivingSource parsed, string snapshotJson, string fingerprint,
        DateTimeOffset checkedAtUtc, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);

        var source = await GetOrCreateAsync(context, parsed.RefKey, ct);
        if (!source.IsSuccess)
            return source.Error!;

        var document = source.Value!;
        var version = document.LastSuccessfulVersionId is Guid versionId
            ? await context.ReceivingSourceVersions.Include(x => x.Lines)
                .SingleAsync(x => x.Id == versionId, ct)
            : null;
        var contentChanged = version is null || version.ContentFingerprint != fingerprint
            || version.SnapshotJson != snapshotJson;
        if (contentChanged)
        {
            var created = ReceivingSourceVersion.Create(
                document.Id, checkedAtUtc, fingerprint, snapshotJson);
            if (!created.IsSuccess)
                return created.Error!;
            version = created.Value!;
            context.ReceivingSourceVersions.Add(version);
        }

        var lines = parsed.Lines;
        var skuKeys = lines.Select(x => x.SkuRefKey).OfType<Guid>().Distinct().ToArray();
        var knownSkus = await context.StockKeepingUnits
            .Where(x => skuKeys.Contains(x.Id))
            .Select(x => x.Id).ToHashSetAsync(ct);
        var projections = new List<ReceivingSourceLineProjection>(lines.Count);
        foreach (var line in lines)
        {
            var skuId = line.SkuRefKey is Guid key && knownSkus.Contains(key)
                ? key : (Guid?)null;
            var error = line.Error ?? (skuId is null ? "SKU 1С не найден в WMS." : null);
            var projected = ReceivingSourceLineProjection.Create(
                version.Id, line.LineNumber, line.SkuRefKey, skuId,
                line.RawQuantity, line.Quantity, error);
            if (!projected.IsSuccess)
                return projected.Error!;
            projections.Add(projected.Value!);
        }

        // The source snapshot is immutable; its catalog-dependent projection may be rebuilt.
        if (contentChanged || !SameProjection(version.Lines, projections))
        {
            if (!contentChanged)
                context.ReceivingSourceLineProjections.RemoveRange(version.Lines);
            context.ReceivingSourceLineProjections.AddRange(projections);
        }

        var recorded = document.RecordSuccessfulCheck(version, checkedAtUtc);
        if (!recorded.IsSuccess)
            return recorded.Error!;

        var planUpdate = await UpdateLinkedOrderPlanAsync(
            context, document, parsed, projections, ct);
        if (!planUpdate.IsSuccess)
            return planUpdate.Error!;

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return version;
    }

    private async Task<OperationResult> UpdateLinkedOrderPlanAsync(
        ApplicationDbContext context, ReceivingSourceDocument currentDocument,
        ParsedReceivingSource currentSource,
        IReadOnlyList<ReceivingSourceLineProjection> currentLines, CancellationToken ct)
    {
        var orderId = await context.ReceivingOrderSourceLinks.AsNoTracking()
            .Where(x => x.ReceivingSourceDocumentId == currentDocument.Id
                && x.DetachedAtUtc == null)
            .Select(x => (Guid?)x.ReceivingOrderId).SingleOrDefaultAsync(ct);
        if (orderId is not Guid id)
            return OperationResult.Success();

        var order = await context.ReceivingOrders.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (order is null)
            return OperationError.Conflict("Связанный приходный ордер не найден.");
        var otherIds = await context.ReceivingOrderSourceLinks.AsNoTracking()
            .Where(x => x.ReceivingOrderId == id && x.DetachedAtUtc == null
                && x.ReceivingSourceDocumentId != currentDocument.Id)
            .Select(x => x.ReceivingSourceDocumentId).ToArrayAsync(ct);
        var otherDocuments = await context.ReceivingSourceDocuments.AsNoTracking()
            .Where(x => otherIds.Contains(x.Id)).ToArrayAsync(ct);
        if (otherDocuments.Length != otherIds.Length
            || otherDocuments.Any(x => x.LastSuccessfulVersionId is null))
            return OperationError.Conflict("Версия связанного источника не найдена.");
        var versionIds = otherDocuments.Select(x => x.LastSuccessfulVersionId!.Value).ToArray();
        var otherVersions = await context.ReceivingSourceVersions.AsNoTracking()
            .Include(x => x.Lines).Where(x => versionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var sources = new List<(ParsedReceivingSource Source, ReceivingSourcePlan Plan)>
        {
            (currentSource, ReceivingSourceVersion.CalculatePlan(currentLines))
        };
        foreach (var document in otherDocuments)
        {
            if (!otherVersions.TryGetValue(document.LastSuccessfulVersionId!.Value,
                    out var version))
                return OperationError.Conflict("Версия связанного источника не найдена.");
            var parsed = parser.Parse(document.RefKey, version.SnapshotJson);
            if (!parsed.IsSuccess)
                return parsed.Error!;
            sources.Add((parsed.Value!, version.CalculatePlan()));
        }

        var plan = ReceivingOrderPlanProjection.Sum(order.WarehouseId, sources);
        var applied = order.ApplyCurrentSourcePlan(plan);
        if (!applied.IsSuccess)
            return applied.Error!;
        context.ReceivingOrderItems.AddRange(applied.Value!);
        return OperationResult.Success();
    }

    public async Task RecordFailureAsync(
        Guid refKey, string error, DateTimeOffset attemptedAtUtc, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        var source = await GetOrCreateAsync(context, refKey, ct);
        if (!source.IsSuccess)
            throw new InvalidOperationException(source.Error!.Message);
        var recorded = source.Value!.RecordFailedCheck(attemptedAtUtc, error);
        if (!recorded.IsSuccess)
            throw new InvalidOperationException(recorded.Error!.Message);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task<OperationResult<ReceivingSourceDocument>> GetOrCreateAsync(
        ApplicationDbContext context, Guid refKey, CancellationToken ct)
    {
        var existing = await context.ReceivingSourceDocuments
            .SingleOrDefaultAsync(x => x.RefKey == refKey, ct);
        if (existing is not null)
            return existing;

        var created = ReceivingSourceDocument.Create(refKey);
        if (created.IsSuccess)
            context.ReceivingSourceDocuments.Add(created.Value!);
        return created;
    }

    private static bool SameProjection(
        IReadOnlyCollection<ReceivingSourceLineProjection> previous,
        IReadOnlyList<ReceivingSourceLineProjection> current) =>
        previous.Count == current.Count
        && previous.OrderBy(x => x.LineNumber).ThenBy(x => x.SourceSkuRefKey)
            .Zip(current.OrderBy(x => x.LineNumber).ThenBy(x => x.SourceSkuRefKey))
            .All(pair => pair.First.LineNumber == pair.Second.LineNumber
                && pair.First.SourceSkuRefKey == pair.Second.SourceSkuRefKey
                && pair.First.StockKeepingUnitId == pair.Second.StockKeepingUnitId
                && pair.First.RawQuantity == pair.Second.RawQuantity
                && pair.First.Quantity == pair.Second.Quantity
                && pair.First.ProjectionError == pair.Second.ProjectionError);
}
