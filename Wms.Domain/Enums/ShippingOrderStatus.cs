using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Wms.Domain.Enums;

public enum ShippingOrderStatus
{
    [Display(Name = "Неизвестный статус")]
    Unknown = 0,

    [EnumMember(Value = "Подготовлен")]
    [Display(Name = "Подготовлен")]
    Prepared = 1,

    [EnumMember(Value = "КОтбору")]
    [Display(Name = "К отбору")]
    ReadyForPicking = 2,

    [EnumMember(Value = "КПроверке")]
    [Display(Name = "К проверке")]
    ReadyForVerification = 3,

    [EnumMember(Value = "ВПроцессеПроверки")]
    [Display(Name = "В процессе проверки")]
    InVerification = 4,

    [EnumMember(Value = "Проверен")]
    [Display(Name = "Проверен")]
    Verified = 5,

    [EnumMember(Value = "КОтгрузке")]
    [Display(Name = "К отгрузке")]
    ReadyForShipment = 6,

    [EnumMember(Value = "Отгружен")]
    [Display(Name = "Отгружен")]
    Shipped = 7
}
