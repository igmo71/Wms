using System.Security.Cryptography;
using System.Text;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Receiving;

public sealed record ReceivingSourceLine(
    int LineNumber, Guid? SkuRefKey, string? RawQuantity,
    decimal? Quantity, string? Error);

public sealed record ParsedReceivingSource(
    Guid RefKey, Guid? WarehouseId, bool? Posted, bool? DeletionMark,
    string? Number, string? Date, string? Sender, string? SenderType,
    string? WarehouseOperation, string? BusinessOperation,
    string? BasisDocument, string? BasisDocumentType,
    IReadOnlyList<ReceivingSourceLine> Lines);

public interface IReceivingSourceParser
{
    OperationResult<ParsedReceivingSource> Parse(Guid refKey, string rawResponseJson);
}

public interface IReceivingSourceStore
{
    Task<OperationResult<ReceivingSourceVersion>> SaveSuccessfulAsync(
        ParsedReceivingSource source, string snapshotJson, string fingerprint,
        DateTimeOffset checkedAtUtc, ReceivingSourceCheckAudit? audit, CancellationToken ct);

    Task RecordFailureAsync(Guid refKey, string error, DateTimeOffset attemptedAtUtc,
        ReceivingSourceCheckAudit? audit, CancellationToken ct);
}

public sealed record ReceivingSourceCheckAudit(Guid OrderId, string UserId);

public sealed class SynchronizeReceivingSource(
    IReceivingSourceReader reader,
    IReceivingSourceParser parser,
    IReceivingSourceStore store,
    TimeProvider clock)
{
    public async Task<OperationResult<ReceivingSourceVersion>> ExecuteAsync(
        Guid refKey, CancellationToken ct = default) =>
        await ExecuteAsync(refKey, null, ct);

    public async Task<OperationResult<ReceivingSourceVersion>> ExecuteAsync(
        Guid refKey, ReceivingSourceCheckAudit? audit, CancellationToken ct = default)
    {
        if (refKey == Guid.Empty)
            return OperationError.Invalid("Ref_Key приходного документа 1С обязателен.");

        var read = await reader.ReadAsync(refKey, ct);
        if (!read.IsSuccess)
            return await FailAsync(refKey, read.Error!, audit, ct);

        var response = read.Value!;
        if (response.RefKey != refKey)
            return await FailAsync(refKey,
                OperationError.Failure("1С вернула другой приходный документ."), audit, ct);

        var parsed = parser.Parse(refKey, response.RawResponseJson);
        if (!parsed.IsSuccess)
            return await FailAsync(refKey, parsed.Error!, audit, ct);

        var fingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(response.RawResponseJson)));
        var saved = await store.SaveSuccessfulAsync(parsed.Value!, response.RawResponseJson, fingerprint,
            clock.GetUtcNow(), audit, ct);
        return !saved.IsSuccess && saved.Error!.Message == ReceivingSourceSenderPolicy.ChangedMessage
            ? await FailAsync(refKey, saved.Error!, audit, ct) : saved;
    }

    private async Task<OperationResult<ReceivingSourceVersion>> FailAsync(
        Guid refKey, OperationError error, ReceivingSourceCheckAudit? audit, CancellationToken ct)
    {
        await store.RecordFailureAsync(refKey, error.Message, clock.GetUtcNow(), audit, ct);
        return error;
    }
}

public static class ReceivingOrderPlanProjection
{
    public static IReadOnlyDictionary<Guid, decimal?> Sum(
        Guid orderWarehouseId,
        IEnumerable<(ParsedReceivingSource Source, ReceivingSourcePlan Plan)> sources)
    {
        var result = new Dictionary<Guid, decimal?>();
        foreach (var (source, plan) in sources)
        {
            var inactive = source.Posted == false || source.DeletionMark == true
                || source.WarehouseId is Guid warehouseId && warehouseId != orderWarehouseId;
            var uncertain = !inactive && (source.Posted is null
                || source.DeletionMark is null || source.WarehouseId is null);
            foreach (var (skuId, quantity) in plan.QuantitiesBySku)
            {
                var contribution = inactive ? 0m : uncertain ? (decimal?)null : quantity;
                if (!result.TryGetValue(skuId, out var previous))
                {
                    result[skuId] = contribution;
                    continue;
                }
                if (previous is null || contribution is null)
                {
                    result[skuId] = null;
                    continue;
                }
                try
                {
                    var sum = checked(previous.Value + contribution.Value);
                    result[skuId] = WarehouseQuantity.IsNonNegative(sum) ? sum : null;
                }
                catch (OverflowException)
                {
                    result[skuId] = null;
                }
            }
        }
        return result;
    }
}
