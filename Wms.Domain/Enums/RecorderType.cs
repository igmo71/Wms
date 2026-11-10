using System.ComponentModel.DataAnnotations;

namespace Wms.Domain.Enums;

public enum RecorderType
{
    [Display(Name = "Неопределено")]
    Unknown = 0,

    [Display(Name = "Приходный ордер")]
    ReceivingOrder = 1,

    [Display(Name = "Расходный ордер")]
    ShippingOrder = 2,

    [Display(Name = "Инвентаризация")]
    InventoryCount = 3,

    [Display(Name = "Перемещение")]
    InventoryTransfer = 4,

    [Display(Name = "Начальные остатки")]
    OpeningBalance = 5,

    [Display(Name = "Сборка WMS")]
    PickingAssembly = 6,

    [Display(Name = "Подпитка")]
    ReplenishmentTask = 7,

    [Display(Name = "Корректировка остатка")]
    StockCorrection = 8,
}
