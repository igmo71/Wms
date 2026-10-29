using System.ComponentModel.DataAnnotations;

namespace Wms.Domain.Enums;

public enum ReceivingOrderStatus
{
    [Display(Name = "К поступлению")]
    ReadyForReceiving = 0,

    [Display(Name = "В работе")]
    InReceiving = 1,

    [Display(Name = "Принят")]
    Received = 2,

    [Display(Name = "Удалён")]
    Deleted = 3
}
