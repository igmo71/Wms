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
    string? Number, IReadOnlyList<ReceivingSourceLine> Lines);

public interface IReceivingSourceParser
{
    OperationResult<ParsedReceivingSource> Parse(Guid refKey, string rawResponseJson);
}

public interface IReceivingSourceStore
{
    Task<OperationResult<ReceivingSourceVersion>> SaveSuccessfulAsync(
        Guid refKey, string snapshotJson, string fingerprint,
        IReadOnlyList<ReceivingSourceLine> lines, DateTimeOffset checkedAtUtc,
        CancellationToken ct);

    Task RecordFailureAsync(Guid refKey, string error, DateTimeOffset attemptedAtUtc,
        CancellationToken ct);
}

public sealed class SynchronizeReceivingSource(
    IReceivingSourceReader reader,
    IReceivingSourceParser parser,
    IReceivingSourceStore store,
    TimeProvider clock)
{
    public async Task<OperationResult<ReceivingSourceVersion>> ExecuteAsync(
        Guid refKey, CancellationToken ct = default)
    {
        if (refKey == Guid.Empty)
            return OperationError.Invalid("Ref_Key приходного документа 1С обязателен.");

        var read = await reader.ReadAsync(refKey, ct);
        if (!read.IsSuccess)
            return await FailAsync(refKey, read.Error!, ct);

        var response = read.Value!;
        if (response.RefKey != refKey)
            return await FailAsync(refKey,
                OperationError.Failure("1С вернула другой приходный документ."), ct);

        var parsed = parser.Parse(refKey, response.RawResponseJson);
        if (!parsed.IsSuccess)
            return await FailAsync(refKey, parsed.Error!, ct);

        var fingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(response.RawResponseJson)));
        return await store.SaveSuccessfulAsync(refKey, response.RawResponseJson, fingerprint,
            parsed.Value!.Lines, clock.GetUtcNow(), ct);
    }

    private async Task<OperationResult<ReceivingSourceVersion>> FailAsync(
        Guid refKey, OperationError error, CancellationToken ct)
    {
        await store.RecordFailureAsync(refKey, error.Message, clock.GetUtcNow(), ct);
        return error;
    }
}
