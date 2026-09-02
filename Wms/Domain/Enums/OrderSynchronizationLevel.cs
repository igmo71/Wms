using System.ComponentModel.DataAnnotations;

namespace Wms.Domain.Enums;

public enum OrderSynchronizationLevel
{
    [Display(Name = "Синхронизирован")]
    Synchronized = 0,

    [Display(Name = "Требует решения оператора")]
    RequiresOperatorDecision = 1,

    [Display(Name = "Работа заблокирована")]
    Blocking = 2
}
