using System.ComponentModel.DataAnnotations;

namespace Wms.Domain.Enums;

public enum ZoneType
{
    [Display(Name = "Хранение")]
    Storage = 1,

    [Display(Name = "Транзит")]
    Transit = 2,

    [Display(Name = "Приёмка")]
    Receiving = 3,

    [Display(Name = "Отгрузка")]
    Shipping = 4
}
