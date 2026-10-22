using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Receiving;

public sealed record ReceivingSourceState(
    ReceivingSourceDocument Source, ReceivingSourceVersion? Version)
{
    public Guid? LinkedOrderId { get; init; }
    public string? LinkedOrderNumber { get; init; }
}

public sealed record ReceivingSourceView(
    Guid RefKey, Guid? WarehouseId, string? Number,
    string? Date, string? Sender, string? SenderType,
    string? WarehouseOperation, string? BusinessOperation,
    string? BasisDocument, string? BasisDocumentType,
    bool? Posted, bool? DeletionMark, Guid? VersionId,
    DateTimeOffset? LastSuccessfulCheckAtUtc, DateTimeOffset? LastAttemptAtUtc,
    string? LastAttemptError, IReadOnlyDictionary<Guid, decimal?> PlanBySku,
    bool HasUnknownSku, IReadOnlyList<ReceivingSourceLineView> Lines)
{
    public string? WarehouseName { get; init; }
    public string? SenderName { get; init; }
    public IReadOnlyDictionary<Guid, string> SkuNames { get; init; } = new Dictionary<Guid, string>();
    public Guid? LinkedOrderId { get; init; }
    public string? LinkedOrderNumber { get; init; }
}

public sealed record ReceivingSourceLineView(
    int LineNumber, Guid? SourceSkuRefKey, Guid? StockKeepingUnitId,
    string? RawQuantity, decimal? Quantity, string? Error);

public interface IReceivingOrderStore
{
    Task<IReadOnlyList<ReceivingSourceState>> LoadSourcesAsync(
        IReadOnlyCollection<Guid> refKeys, CancellationToken ct);

    Task<IReadOnlyList<ReceivingSourceState>> ListUnlinkedSourcesAsync(CancellationToken ct);

    Task<IReadOnlyList<ReceivingSourceState>> ListAllSourcesAsync(CancellationToken ct);

    Task<OperationResult<Guid>> CreateAsync(
        ReceivingOrder order, IReadOnlyList<ReceivingOrderSourceLink> links,
        IReadOnlyList<ReceivingOrderItem> items,
        IReadOnlyList<ReceivingSourceExpectation> expectedSources,
        CommandReceiptSnapshot receipt, WarehouseAuditEvent createdAudit,
        CancellationToken ct);

    Task<CommandReceiptSnapshot?> FindReceiptAsync(
        string userId, string commandType, Guid requestId, CancellationToken ct);
}

public sealed record ReceivingSourceExpectation(
    Guid SourceId, Guid VersionId, DateTimeOffset CheckedAtUtc);

public interface IReceivingReferenceNames
{
    Task<ReceivingReferenceNames> GetAsync(
        IReadOnlyCollection<Guid> warehouseIds, IReadOnlyCollection<Guid> skuIds,
        IReadOnlyCollection<Guid> partnerIds, CancellationToken ct);
}

public sealed record ReceivingReferenceNames(
    IReadOnlyDictionary<Guid, string> Warehouses,
    IReadOnlyDictionary<Guid, string> Skus,
    IReadOnlyDictionary<Guid, string> Partners);

public sealed class GetReceivingSources(
    IReceivingOrderStore store, IReceivingSourceParser parser,
    IReceivingReferenceNames names)
{
    public async Task<IReadOnlyList<ReceivingSourceView>> ListAllAsync(
        CancellationToken ct = default)
    {
        var states = await store.ListAllSourcesAsync(ct);
        return await MapAsync(states, ct);
    }

    public async Task<IReadOnlyList<ReceivingSourceView>> ListUnlinkedAsync(
        CancellationToken ct = default)
    {
        var states = await store.ListUnlinkedSourcesAsync(ct);
        return await MapAsync(states, ct);
    }

    public async Task<IReadOnlyList<ReceivingSourceView>> GetByRefKeysAsync(
        IReadOnlyCollection<Guid> refKeys, CancellationToken ct = default)
    {
        if (refKeys is null || refKeys.Count == 0)
            return [];
        return await MapAsync(await store.LoadSourcesAsync(refKeys.Distinct().ToArray(), ct), ct);
    }

    private async Task<IReadOnlyList<ReceivingSourceView>> MapAsync(
        IReadOnlyList<ReceivingSourceState> states, CancellationToken ct)
    {
        if (states.Count == 0)
            return [];
        var results = new List<ReceivingSourceView>(states.Count);
        foreach (var state in states)
        {
            ParsedReceivingSource? parsed = null;
            if (state.Version is not null)
            {
                var parse = parser.Parse(state.Source.RefKey, state.Version.SnapshotJson);
                if (parse.IsSuccess)
                    parsed = parse.Value;
            }

            var active = parsed?.Posted == true && parsed.DeletionMark == false;
            var plan = state.Version?.CalculatePlan();
            var metadataKnown = parsed?.WarehouseId is not null
                && parsed.Posted is not null && parsed.DeletionMark is not null;
            var quantities = active && plan is not null
                ? plan.QuantitiesBySku
                : plan?.QuantitiesBySku.ToDictionary(x => x.Key,
                    x => metadataKnown ? (decimal?)0 : null)
                    ?? new Dictionary<Guid, decimal?>();
            var lines = state.Version?.Lines
                .OrderBy(x => x.LineNumber)
                .Select(x => new ReceivingSourceLineView(
                    x.LineNumber, x.SourceSkuRefKey, x.StockKeepingUnitId,
                    x.RawQuantity, x.Quantity, x.ProjectionError))
                .ToArray() ?? [];
            results.Add(new ReceivingSourceView(
                state.Source.RefKey, parsed?.WarehouseId, parsed?.Number,
                parsed?.Date, parsed?.Sender, parsed?.SenderType,
                parsed?.WarehouseOperation, parsed?.BusinessOperation,
                parsed?.BasisDocument, parsed?.BasisDocumentType,
                parsed?.Posted, parsed?.DeletionMark, state.Version?.Id,
                state.Source.LastSuccessfulCheckAtUtc, state.Source.LastAttemptAtUtc,
                state.Source.LastAttemptError, quantities,
                !metadataKnown || active && plan?.HasUnknownSku == true, lines)
            {
                LinkedOrderId = state.LinkedOrderId,
                LinkedOrderNumber = state.LinkedOrderNumber
            });
        }
        var warehouseIds = results.Where(x => x.WarehouseId.HasValue)
            .Select(x => x.WarehouseId!.Value)
            .Concat(results.Where(x => x.SenderType == "StandardODATA.Catalog_Склады"
                    && Guid.TryParse(x.Sender, out _))
                .Select(x => Guid.Parse(x.Sender!))).Distinct().ToArray();
        var skuIds = results.SelectMany(x => x.PlanBySku.Keys
            .Concat(x.Lines.Where(line => line.StockKeepingUnitId.HasValue)
                .Select(line => line.StockKeepingUnitId!.Value))).Distinct().ToArray();
        var partnerIds = results.Where(x => x.SenderType == "StandardODATA.Catalog_Партнеры"
                && Guid.TryParse(x.Sender, out _))
            .Select(x => Guid.Parse(x.Sender!)).Distinct().ToArray();
        var references = await names.GetAsync(warehouseIds, skuIds, partnerIds, ct);
        return results.Select(source => source with
        {
            WarehouseName = source.WarehouseId is Guid warehouseId
                && references.Warehouses.TryGetValue(warehouseId, out var warehouseName)
                    ? warehouseName : null,
            SenderName = source.SenderType == "StandardODATA.Catalog_Партнеры"
                && Guid.TryParse(source.Sender, out var partnerId)
                && references.Partners.TryGetValue(partnerId, out var partnerName)
                    ? partnerName : source.SenderType == "StandardODATA.Catalog_Склады"
                        && Guid.TryParse(source.Sender, out var senderWarehouseId)
                        && references.Warehouses.TryGetValue(senderWarehouseId,
                            out var senderWarehouseName)
                            ? senderWarehouseName : null,
            SkuNames = references.Skus
        }).ToArray();
    }
}
