using System.ComponentModel.DataAnnotations;

namespace Wms.Domain.Enums;

public enum InventoryCountStatus
{
    [Display(Name = "Черновик")]
    Draft = 0,

    [Display(Name = "Проведен")]
    Posted = 1
}
