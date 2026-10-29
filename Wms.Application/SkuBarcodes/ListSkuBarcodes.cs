using Wms.Domain.Enums;

namespace Wms.Application.SkuBarcodes;

public sealed record SkuBarcodeListQuery(
    string? Search, bool IncludeInactive, bool OnlyDuplicates,
    string? SortBy, bool SortDescending, int Skip, int Take);

public sealed record SkuBarcodeListRow(
    Guid SkuId, string Value, string? SkuName, string? SkuCode,
    SkuBarcodeSource Source, bool IsDuplicate);

public sealed record SkuBarcodeListPage(
    IReadOnlyList<SkuBarcodeListRow> Items, int TotalItems);

public interface ISkuBarcodeListStore
{
    Task<SkuBarcodeListPage> ListAsync(SkuBarcodeListQuery query,
        CancellationToken ct);
}

public sealed class ListSkuBarcodes(ISkuBarcodeListStore store)
{
    public Task<SkuBarcodeListPage> ExecuteAsync(
        SkuBarcodeListQuery query, CancellationToken ct = default) =>
        store.ListAsync(query with
        {
            Search = query.Search?.Trim(),
            Skip = Math.Max(0, query.Skip),
            Take = Math.Clamp(query.Take, 1, 100)
        }, ct);
}
