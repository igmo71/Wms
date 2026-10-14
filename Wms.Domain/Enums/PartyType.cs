using System.Runtime.Serialization;

namespace Wms.Domain.Enums;

public enum PartyType
{
    [EnumMember(Value = "StandardODATA.Catalog_СтруктураПредприятия")]
    OrganizationalUnit = 1,

    [EnumMember(Value = "StandardODATA.Catalog_Партнеры")]
    Partner = 2,

    [EnumMember(Value = "StandardODATA.Catalog_ФизическиеЛица")]
    Individual = 3,

    [EnumMember(Value = "StandardODATA.Catalog_Склады")]
    Warehouse = 4
}
