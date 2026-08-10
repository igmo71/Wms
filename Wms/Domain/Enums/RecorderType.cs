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
}
