using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using Wms.Domain;

namespace Wms.Data;

public class ApplicationUser : IdentityUser
{
    public const int DisplayNameMaxLength = 200;

    [PersonalData]
    [MaxLength(DisplayNameMaxLength)]
    public string DisplayName { get; set; } = string.Empty;

    public Guid? DefaultWarehouseId { get; set; }
    public Warehouse? DefaultWarehouse { get; set; }

    public string GetDisplayName() => string.IsNullOrWhiteSpace(DisplayName)
        ? UserName ?? Id
        : DisplayName;
}

