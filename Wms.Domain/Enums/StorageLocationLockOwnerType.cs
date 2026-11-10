using System.ComponentModel.DataAnnotations;

namespace Wms.Domain.Enums;

public enum StorageLocationLockOwnerType
{
    [Display(Name = "Вручную")]
    Manual = 0,

    [Display(Name = "Инвентаризация")]
    InventoryCount = 1,

    [Display(Name = "Палетный отбор")]
    PickingAssembly = 2,

    [Display(Name = "Подпитка")]
    ReplenishmentTask = 3
}
