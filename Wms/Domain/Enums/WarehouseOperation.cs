using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Wms.Domain.Enums;

public enum WarehouseOperation
{
    [Display(Name = "Неизвестная операция")]
    Unknown = 0,

    [EnumMember(Value = "ПриемкаКомплектующихПослеРазборки")]
    [Display(Name = "Приемка комплектующих после разборки")]
    DisassemblyReceipt = 1,

    [EnumMember(Value = "ПриемкаОтПоставщика")]
    [Display(Name = "Приемка от поставщика")]
    VendorReceipt = 2,

    [EnumMember(Value = "ПриемкаПоВозвратуОтКлиента")]
    [Display(Name = "Приемка по возврату от клиента")]
    CustomerReturnReceipt = 3,

    [EnumMember(Value = "ПриемкаПоПеремещению")]
    [Display(Name = "Приемка по перемещению")]
    TransferReceipt = 4,

    [EnumMember(Value = "ПриемкаСобранныхКомплектов")]
    [Display(Name = "Приемка собранных комплектов")]
    AssemblyReceipt = 5
}
