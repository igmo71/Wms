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
    AssemblyReceipt = 5,


    [EnumMember(Value = "ОтгрузкаКлиенту")]
    [Display(Name = "Отгрузка клиенту")]
    CustomerShipment = 6,

    [EnumMember(Value = "ОтгрузкаКомплектовДляРазборки")]
    [Display(Name = "Отгрузка комплектов для разборки")]
    ShipmentOfKitsForDisassembly = 7,

    [EnumMember(Value = "ОтгрузкаКомплектующихДляСборки")]
    [Display(Name = "Отгрузка комплектующих для сборки")]
    ShipmentOfComponentsForAssembly = 8,

    [EnumMember(Value = "ОтгрузкаНаВнутренниеНужды")]
    [Display(Name = "Отгрузка на внутренние нужды")]
    InternalShipment = 9,

    [EnumMember(Value = "ОтгрузкаПоВозвратуПоставщику")]
    [Display(Name = "Отгрузка по возврату поставщику")]
    VendorReturnShipment = 10,

    [EnumMember(Value = "ОтгрузкаПоПеремещению")]
    [Display(Name = "Отгрузка по перемещению")]
    TransferShipment = 11
}
