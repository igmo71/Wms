using System.Runtime.Serialization;

namespace Wms.Domain.Enums;

public enum PartyType
{
    [EnumMember(Value = "StandardODATA.СтруктураПредприятия")]
    OrganizationalUnit = 1,

    [EnumMember(Value = "StandardODATA.Catalog_Партнеры")]
    Partner = 2,

    [EnumMember(Value = "StandardODATA.ФизическиеЛица")]
    Individual = 3,

    [EnumMember(Value = "StandardODATA.Склады")]
    Warehouse = 4
}
