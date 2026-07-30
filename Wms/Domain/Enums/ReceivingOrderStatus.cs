using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Wms.Domain.Enums;

public enum ReceivingOrderStatus
{
    [Display(Name = "Неизвестный статус")]
    Unknown = 0,

    [EnumMember(Value = "КПоступлению")]
    [Display(Name = "К поступлению")]
    Pending = 1,

    [EnumMember(Value = "ВРаботе")]
    [Display(Name = "В работе")]
    InProcess = 2,

    [EnumMember(Value = "ТребуетсяОбработка")]
    [Display(Name = "Требуется обработка")]
    ProcessingRequired = 3,

    [EnumMember(Value = "Принят")]
    [Display(Name = "Принят")]
    Completed = 4
}
