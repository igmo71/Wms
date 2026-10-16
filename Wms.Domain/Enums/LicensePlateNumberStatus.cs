using System.ComponentModel.DataAnnotations;

namespace Wms.Domain.Enums;

public enum LicensePlateNumberStatus
{
    [Display(Name = "Свободна")]
    Available = 0,

    [Display(Name = "Открыта")]
    Open = 1,

    [Display(Name = "Закрыта")]
    Closed = 2,

    [Display(Name = "Отменена")]
    Cancelled = 3,

    [Display(Name = "Удалена")]
    Deleted = 4
}
