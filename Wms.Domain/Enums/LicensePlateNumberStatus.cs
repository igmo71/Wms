using System.ComponentModel.DataAnnotations;

namespace Wms.Domain.Enums;

public enum LicensePlateNumberStatus
{
    [Display(Name = "Свободен")]
    Available = 0,

    [Display(Name = "Открыт")]
    Open = 1,

    [Display(Name = "Закрыт")]
    Closed = 2,

    [Display(Name = "Отменён")]
    Cancelled = 3,

    [Display(Name = "Удалён")]
    Deleted = 4
}
