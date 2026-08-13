using System.ComponentModel.DataAnnotations;

namespace Wms.Domain.Enums;

public enum InventoryTransferStatus
{
    [Display(Name = "Черновик")]
    Draft = 0,

    [Display(Name = "В работе")]
    InProgress = 1,

    [Display(Name = "Завершено")]
    Completed = 2
}
