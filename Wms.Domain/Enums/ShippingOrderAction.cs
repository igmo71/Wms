using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Wms.Domain.Enums;

public enum ShippingOrderAction
{
    [Display(Name = "Неизвестное действие")]
    Unknown = 0,

    [EnumMember(Value = "Отобрать")]
    [Display(Name = "Отобрать")]
    PickUp = 1,

    [EnumMember(Value = "Отгрузить")]
    [Display(Name = "Отгрузить")]
    Ship = 2,

    [EnumMember(Value = "НеОтгружать")]
    [Display(Name = "Не отгружать")]
    DoNotShip = 3
}
