using Wms.Common;

namespace Wms.Application.SkuBarcodes;

public sealed record SkuBarcodeSourceItem(Guid SkuId, string? Value);

public sealed record SkuBarcodePair(Guid SkuId, string Value);

public interface ISkuBarcodeMutationLock
{
    Task<IAsyncDisposable> AcquireAsync(CancellationToken ct);
}

public interface ISkuBarcodeSynchronizationStore
{
    Task ReconcileAsync(Guid? skuId, IReadOnlyCollection<SkuBarcodePair> sourcePairs,
        CancellationToken ct);
}

public sealed class SynchronizeSkuBarcodes(
    ISkuBarcodeMutationLock mutationLock,
    ISkuBarcodeSynchronizationStore store)
{
    public async Task<OperationResult> ExecuteAsync(
        Guid? skuId,
        Func<CancellationToken, Task<OperationResult<IReadOnlyList<SkuBarcodeSourceItem>>>> readSource,
        CancellationToken ct = default)
    {
        if (skuId == Guid.Empty || readSource is null)
            return OperationError.Invalid("Область синхронизации штрихкодов не указана.");

        // The lock spans the source read and the database save. An older full read
        // cannot commit after a newer single-SKU read on another host.
        await using var lease = await mutationLock.AcquireAsync(ct);
        var source = await readSource(ct);
        if (!source.IsSuccess)
            return source.Error!;
        if (source.Value is null)
            return OperationError.Failure("1С вернула некорректный список штрихкодов.");

        var pairs = new List<SkuBarcodePair>(source.Value.Count);
        foreach (var item in source.Value)
        {
            var value = item.Value?.Trim();
            if (item.SkuId == Guid.Empty || string.IsNullOrEmpty(value)
                || value.Length > 32 || skuId is Guid expected && item.SkuId != expected)
                return OperationError.Invalid("1С вернула некорректную запись штрихкода SKU.");

            pairs.Add(new SkuBarcodePair(item.SkuId, value));
        }

        await store.ReconcileAsync(skuId, pairs, ct);
        return OperationResult.Success();
    }
}
