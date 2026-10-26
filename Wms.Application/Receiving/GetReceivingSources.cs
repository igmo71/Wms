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

public sealed record ReceivingLinkedSourceSummary(
    Guid RefKey, string? Number, string? Date, string? Sender,
    string? SenderType, string? WarehouseOperation, string? BusinessOperation,
    string? BasisDocument, string? BasisDocumentType,
    string? SenderName, string? LastAttemptError);

public sealed record ReceivingSourceHeader(
    Guid RefKey, string? SnapshotJson, string? LastAttemptError);

public interface IReceivingOrderStore
{
    Task<IReadOnlyList<ReceivingSourceState>> LoadSourcesAsync(
        IReadOnlyCollection<Guid> refKeys, CancellationToken ct);
    Task<IReadOnlyList<ReceivingSourceHeader>> LoadSourceHeadersAsync(
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
    private const string PartnerSenderType = "StandardODATA.Catalog_Партнеры";
    private const string WarehouseSenderType = "StandardODATA.Catalog_Склады";

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

    public async Task<IReadOnlyList<ReceivingLinkedSourceSummary>> GetLinkedSummariesAsync(
        IReadOnlyCollection<Guid> refKeys, CancellationToken ct = default)
    {
        if (refKeys is null || refKeys.Count == 0)
            return [];
        var headers = await store.LoadSourceHeadersAsync(refKeys.Distinct().ToArray(), ct);
        if (headers.Count == 0)
            return [];
        var summaries = headers.Select(header =>
        {
            var parsed = header.SnapshotJson is { } json
                ? parser.Parse(header.RefKey, json).Value : null;
            return new ReceivingLinkedSourceSummary(header.RefKey,
                parsed?.Number, parsed?.Date, parsed?.Sender, parsed?.SenderType,
                parsed?.WarehouseOperation, parsed?.BusinessOperation,
                parsed?.BasisDocument, parsed?.BasisDocumentType,
                null, header.LastAttemptError);
        }).ToArray();
        var warehouseIds = summaries.Where(x => x.SenderType == WarehouseSenderType
                && Guid.TryParse(x.Sender, out _))
            .Select(x => Guid.Parse(x.Sender!)).Distinct().ToArray();
        var partnerIds = summaries.Where(x => x.SenderType == PartnerSenderType
                && Guid.TryParse(x.Sender, out _))
            .Select(x => Guid.Parse(x.Sender!)).Distinct().ToArray();
        if (warehouseIds.Length == 0 && partnerIds.Length == 0)
            return summaries;
        var references = await names.GetAsync(warehouseIds, [], partnerIds, ct);
        return summaries.Select(x => x with
        {
            SenderName = ResolveSenderName(x.SenderType, x.Sender, references)
        }).ToArray();
    }

    private static string? ResolveSenderName(
        string? senderType, string? sender, ReceivingReferenceNames references)
    {
        if (!Guid.TryParse(sender, out var senderId))
            return null;
        if (senderType == PartnerSenderType)
            return references.Partners.GetValueOrDefault(senderId);
        return senderType == WarehouseSenderType
            ? references.Warehouses.GetValueOrDefault(senderId) : null;
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
            .Concat(results.Where(x => x.SenderType == WarehouseSenderType
                    && Guid.TryParse(x.Sender, out _))
                .Select(x => Guid.Parse(x.Sender!))).Distinct().ToArray();
        var skuIds = results.SelectMany(x => x.PlanBySku.Keys
            .Concat(x.Lines.Where(line => line.StockKeepingUnitId.HasValue)
                .Select(line => line.StockKeepingUnitId!.Value))).Distinct().ToArray();
        var partnerIds = results.Where(x => x.SenderType == PartnerSenderType
                && Guid.TryParse(x.Sender, out _))
            .Select(x => Guid.Parse(x.Sender!)).Distinct().ToArray();
        var references = await names.GetAsync(warehouseIds, skuIds, partnerIds, ct);
        return results.Select(source => source with
        {
            WarehouseName = source.WarehouseId is Guid warehouseId
                && references.Warehouses.TryGetValue(warehouseId, out var warehouseName)
                    ? warehouseName : null,
            SenderName = ResolveSenderName(source.SenderType, source.Sender, references),
            SkuNames = references.Skus
        }).ToArray();
    }
}
