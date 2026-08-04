using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Wms.Domain.Enums;

public enum ShippingOrderQueue
{
    [EnumMember(Value = "")]
    [Display(Name = "Очередность не указана")]
    Unknown = 0,

    [EnumMember(Value = "ЖиваяОчередь")]
    [Display(Name = "Живая Очередь")]
    LiveQueue = 1,

    [EnumMember(Value = "СобратьКДате")]
    [Display(Name = "Собрать К Дате")]
    CollectByDate = 2,

    [EnumMember(Value = "СобственнаяДоставка")]
    [Display(Name = "Собственная Доставка")]
    OwnDelivery = 3
}
