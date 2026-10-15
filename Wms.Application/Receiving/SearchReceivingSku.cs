using Wms.Common;

namespace Wms.Application.Receiving;

public enum ReceivingSkuSearchScope
{
    Order,
    Catalog
}

public sealed record ReceivingSkuCandidate(
    Guid SkuId, string? Name, string? Code, bool TracksExpirationDate,
    bool IsInOrder, decimal? CurrentPlanQuantity, decimal? FactQuantity);

public sealed record ReceivingSkuAddState(
    bool OrderExists, bool SkuExists, bool SkuDeleted, bool IsInOrder);

public interface IReceivingSkuStore
{
    Task<bool> OrderExistsAsync(Guid orderId, CancellationToken ct);
    Task<IReadOnlyList<ReceivingSkuCandidate>> SearchAsync(
        Guid orderId, string term, ReceivingSkuSearchScope scope, int take,
        CancellationToken ct);
    Task<IReadOnlyList<ReceivingSkuCandidate>> FindByBarcodeAsync(
        Guid orderId, string barcode, CancellationToken ct);
    Task<ReceivingSkuAddState> LoadAddStateAsync(Guid orderId, Guid skuId,
        CancellationToken ct);
}

public sealed class SearchReceivingSku(IReceivingSkuStore store)
{
    public async Task<OperationResult<IReadOnlyList<ReceivingSkuCandidate>>> SearchAsync(
        Guid orderId, string? searchText, ReceivingSkuSearchScope scope,
        int take = 30, CancellationToken ct = default)
    {
        if (orderId == Guid.Empty || !Enum.IsDefined(scope) || take is < 1 or > 100)
            return OperationError.Invalid("Ордер, область поиска или размер выдачи недопустимы.");
        if (!await store.OrderExistsAsync(orderId, ct))
            return OperationError.NotFound("Приходный ордер не найден.");

        var term = searchText?.Trim() ?? string.Empty;
        if (term.Length < 2)
            return OperationResult<IReadOnlyList<ReceivingSkuCandidate>>.Success(
                Array.Empty<ReceivingSkuCandidate>());
        return OperationResult<IReadOnlyList<ReceivingSkuCandidate>>.Success(
            await store.SearchAsync(orderId, term, scope, take, ct));
    }

    public async Task<OperationResult<ReceivingSkuCandidate>> ResolveBarcodeAsync(
        Guid orderId, string? barcode, CancellationToken ct = default)
    {
        if (orderId == Guid.Empty || string.IsNullOrWhiteSpace(barcode))
            return OperationError.Invalid("Ордер и штрихкод товара обязательны.");
        if (!await store.OrderExistsAsync(orderId, ct))
            return OperationError.NotFound("Приходный ордер не найден.");

        var matches = await store.FindByBarcodeAsync(orderId, barcode.Trim(), ct);
        return matches.Count switch
        {
            0 => OperationError.NotFound("Товар с таким штрихкодом не найден."),
            1 => matches[0],
            _ => OperationError.Conflict("Штрихкод соответствует нескольким товарам.")
        };
    }
}
