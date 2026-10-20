using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Receiving;

public static class ReceivingCurrentComposition
{
    public static HashSet<Guid> SourceSkuIds(IEnumerable<ReceivingSourceState> sources) =>
        sources.Where(source => source.Version is not null)
            .SelectMany(source => source.Version!.Lines)
            .Where(line => line.StockKeepingUnitId is not null)
            .Select(line => line.StockKeepingUnitId!.Value)
            .ToHashSet();

    public static HashSet<Guid> PostedSkuIds(ReceivingOrder order) =>
        order.LicensePlateNumbers
            .Where(lpn => lpn.Status == LicensePlateNumberStatus.Closed)
            .SelectMany(lpn => lpn.Items)
            .Where(item => !item.IsDeleted)
            .Select(item => item.StockKeepingUnitId)
            .ToHashSet();
}
