using System.ComponentModel.DataAnnotations;

namespace Wms.Domain.Enums;

public enum PutawayStatus
{
    [Display(Name = "Неактивно")]
    Inactive = 0,

    [Display(Name = "Ожидает размещения")]
    Pending = 1,

    [Display(Name = "В размещении")]
    InProgress = 2,

    [Display(Name = "Размещен")]
    Completed = 3
}
