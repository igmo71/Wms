using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Wms.Domain.Enums;

public enum ShippingOrderAction
{
    [EnumMember(Value = "Отобрать")]
    [Display(Name = "Отобрать")]
    PickUp,

    [EnumMember(Value = "Отгрузить")]
    [Display(Name = "Отгрузить")]
    Ship,

    [EnumMember(Value = "НеОтгружать")]
    [Display(Name = "Не отгружать")]
    DoNotShip
}
