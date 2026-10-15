using Wms.Common;

namespace Wms.Domain;

public sealed class ReceivingSourceVersion
{
    private readonly List<ReceivingSourceLineProjection> _lines = [];

    private ReceivingSourceVersion() { }

    public Guid Id { get; private set; }
    public Guid ReceivingSourceDocumentId { get; private set; }
    public ReceivingSourceDocument? ReceivingSourceDocument { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public string ContentFingerprint { get; private set; } = null!;
    public string SnapshotJson { get; private set; } = null!;
    public IReadOnlyCollection<ReceivingSourceLineProjection> Lines => _lines;

    public ReceivingSourcePlan CalculatePlan()
    {
        var quantitiesBySku = new Dictionary<Guid, decimal?>();
        var hasUnknownSku = false;

        foreach (var line in _lines)
        {
            if (line.StockKeepingUnitId is not Guid skuId)
            {
                hasUnknownSku = true;
                continue;
            }

            if (line.Quantity is not decimal quantity || line.ProjectionError is not null)
            {
                quantitiesBySku[skuId] = null;
                continue;
            }

            if (!quantitiesBySku.TryGetValue(skuId, out var previous))
            {
                quantitiesBySku[skuId] = quantity;
                continue;
            }

            if (previous is null)
                continue;

            try
            {
                var sum = previous.Value + quantity;
                quantitiesBySku[skuId] = WarehouseQuantity.IsNonNegative(sum) ? sum : null;
            }
            catch (OverflowException)
            {
                quantitiesBySku[skuId] = null;
            }
        }

        return new ReceivingSourcePlan(quantitiesBySku, hasUnknownSku);
    }

    public static OperationResult<ReceivingSourceVersion> Create(
        Guid sourceId, DateTimeOffset receivedAtUtc, string fingerprint, string snapshotJson)
    {
        if (sourceId == Guid.Empty || receivedAtUtc == default
            || string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length > 64
            || string.IsNullOrWhiteSpace(snapshotJson))
            return OperationError.Invalid("Источник, время, отпечаток и снимок версии обязательны.");

        return new ReceivingSourceVersion
        {
            Id = Guid.NewGuid(),
            ReceivingSourceDocumentId = sourceId,
            ReceivedAtUtc = receivedAtUtc,
            ContentFingerprint = fingerprint,
            SnapshotJson = snapshotJson
        };
    }
}

public sealed record ReceivingSourcePlan(
    IReadOnlyDictionary<Guid, decimal?> QuantitiesBySku,
    bool HasUnknownSku)
{
    public decimal? QuantityFor(Guid skuId) =>
        QuantitiesBySku.TryGetValue(skuId, out var quantity) ? quantity : 0m;
}

public sealed class ReceivingSourceLineProjection
{
    private ReceivingSourceLineProjection() { }

    public Guid Id { get; private set; }
    public Guid ReceivingSourceVersionId { get; private set; }
    public ReceivingSourceVersion? ReceivingSourceVersion { get; private set; }
    public int LineNumber { get; private set; }
    public Guid? SourceSkuRefKey { get; private set; }
    public Guid? StockKeepingUnitId { get; private set; }
    public StockKeepingUnit? StockKeepingUnit { get; private set; }
    public string? RawQuantity { get; private set; }
    public decimal? Quantity { get; private set; }
    public string? ProjectionError { get; private set; }

    public static OperationResult<ReceivingSourceLineProjection> Create(
        Guid versionId, int lineNumber, Guid? sourceSkuRefKey, Guid? stockKeepingUnitId,
        string? rawQuantity, decimal? quantity, string? projectionError)
    {
        if (versionId == Guid.Empty || lineNumber <= 0)
            return OperationError.Invalid("Версия и номер строки источника обязательны.");
        if (quantity is decimal value && !WarehouseQuantity.IsNonNegative(value))
            return OperationError.Invalid("Количество строки источника недопустимо.");
        if (quantity is null && string.IsNullOrWhiteSpace(projectionError))
            return OperationError.Invalid("Для неизвестного количества нужна причина ошибки.");
        if (stockKeepingUnitId is null && string.IsNullOrWhiteSpace(projectionError))
            return OperationError.Invalid("Для неизвестного SKU нужна причина ошибки.");

        return new ReceivingSourceLineProjection
        {
            Id = Guid.NewGuid(),
            ReceivingSourceVersionId = versionId,
            LineNumber = lineNumber,
            SourceSkuRefKey = sourceSkuRefKey,
            StockKeepingUnitId = stockKeepingUnitId,
            RawQuantity = rawQuantity,
            Quantity = quantity,
            ProjectionError = projectionError
        };
    }
}
