using System.Runtime.Serialization;

namespace Wms.Integration.OneS.Models;

internal enum ShippingOrderAction
{
    [EnumMember(Value = "Отобрать")]
    PickUp,

    [EnumMember(Value = "Отгрузить")]
    Ship,

    [EnumMember(Value = "НеОтгружать")]
    DoNotShip
}
