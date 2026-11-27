using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

public sealed record PickingReplenishmentAction(PickingReplenishmentState State, bool HasProposal = false)
{
    public static PickingReplenishmentAction Hidden { get; } = new(PickingReplenishmentState.Hidden);
    public bool IsVisible => State != PickingReplenishmentState.Hidden;
    public bool IsUnavailable => State == PickingReplenishmentState.Unavailable;

    public string Hint => State switch
    {
        PickingReplenishmentState.CanOrder => "Заказать подпитку",
        PickingReplenishmentState.Ordered => "Подпитка заказана",
        PickingReplenishmentState.InProgress => "Подпитка выполняется",
        PickingReplenishmentState.Ready => "Готово: можно отобрать оставшийся план",
        PickingReplenishmentState.Unavailable => "Сейчас подпитать нельзя",
        _ => string.Empty
    };

    public Color BackgroundColor => State switch
    {
        PickingReplenishmentState.CanOrder => Color.FromArgb("#1976D2"),
        PickingReplenishmentState.Ordered => Color.FromArgb("#E9B949"),
        PickingReplenishmentState.InProgress => Color.FromArgb("#E87924"),
        PickingReplenishmentState.Ready => Color.FromArgb("#358A4B"),
        _ => Color.FromArgb("#9E9E9E")
    };

    public static PickingReplenishmentAction ForSku(MobilePickingReplenishmentsResponse? list,
        Guid skuId, decimal remaining)
    {
        if (list is null || remaining <= 0) return Hidden;
        var reserved = list.PickableBySku.GetValueOrDefault(skuId);
        var tasks = list.Tasks.Where(x => x.SkuId == skuId).ToArray();
        if (reserved >= remaining)
            return tasks.Any(x => x.Status != MobileReplenishmentTaskStatus.Cancelled)
                ? new(PickingReplenishmentState.Ready) : Hidden;

        var hasProposal = list.Proposals.Any(x => x.SkuId == skuId);
        if (tasks.Any(x => x.Status == MobileReplenishmentTaskStatus.InProgress
            && x.TransferredQuantity < x.OrderedQuantity))
            return new(PickingReplenishmentState.InProgress, hasProposal);
        if (tasks.Any(x => x.Status == MobileReplenishmentTaskStatus.Ordered
            && x.TransferredQuantity < x.OrderedQuantity))
            return new(PickingReplenishmentState.Ordered, hasProposal);
        if (hasProposal) return new(PickingReplenishmentState.CanOrder);

        var freeStorage = list.StockBySku.GetValueOrDefault(skuId)?.FreeStorageQuantity ?? 0;
        return freeStorage >= remaining - reserved ? Hidden : new(PickingReplenishmentState.Unavailable);
    }

    public async Task<bool> ConfirmOrderAsync(Page page)
    {
        if (State == PickingReplenishmentState.CanOrder) return true;
        if (HasProposal && State is (PickingReplenishmentState.Ordered or PickingReplenishmentState.InProgress))
            return await page.DisplayAlertAsync("Подпитка", Hint, "Заказать ещё", "Закрыть");
        if (IsVisible)
            await page.DisplayAlertAsync("Подпитка", Hint, "Понятно");
        return false;
    }
}

public enum PickingReplenishmentState
{
    Hidden, CanOrder, Ordered, InProgress, Ready, Unavailable
}
