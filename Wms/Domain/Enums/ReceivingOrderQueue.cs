using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Wms.Domain.Enums;

public enum ReceivingOrderQueue
{
    [EnumMember(Value = "")]
    [Display(Name = "Очередность не указана")]
    Unknown = 0,

    [EnumMember(Value = "ПодКлиента")]
    [Display(Name = "Под Клиента")]
    ForClient = 1,

    [EnumMember(Value = "СрочноВПродажу")]
    [Display(Name = "Срочно В Продажу")]
    UrgentlyOnSale = 2,

    [EnumMember(Value = "Просрочено")]
    [Display(Name = "Просрочено")]
    Expired = 3
}
