using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Wms.Domain.Enums;

public enum ShippingOrderStatus
{
    [Display(Name = "Неизвестный статус")]
    Unknown = 0,

    [EnumMember(Value = "Подготовлен")]
    [Display(Name = "Подготовлен")]
    Pending = 1,

    [EnumMember(Value = "ВРаботе")]
    [Display(Name = "В работе")]
    InProcess = 2,

    [EnumMember(Value = "КПроверке")]
    [Display(Name = "К проверке")]
    ForVerification = 3,

    [EnumMember(Value = "ВПроцессеПроверки")]
    [Display(Name = "В процессе проверки")]
    InVerification = 4,

    [EnumMember(Value = "Проверен")]
    [Display(Name = "Проверен")]
    Verified = 5,

    [EnumMember(Value = "КОтгрузке")]
    [Display(Name = "К отгрузке")]
    ForShipment = 6,

    [EnumMember(Value = "Отгружен")]
    [Display(Name = "Отгружен")]
    Completed = 7
}
