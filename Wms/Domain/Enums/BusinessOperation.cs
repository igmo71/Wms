using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Wms.Domain.Enums;

public enum BusinessOperation
{
    [Display(Name = "Неизвестная хоз операция")]
    Unknown = 0,

    [EnumMember(Value = "ЗакупкаУПоставщика")]
    [Display(Name = "Закупка у поставщика")]
    VendorPurchase = 1,

    [EnumMember(Value = "ЗакупкаПоИмпорту")]
    [Display(Name = "Закупка по импорту")]
    ImportPurchase = 2,

    [EnumMember(Value = "ВозвратТоваровОтКлиента")]
    [Display(Name = "Возврат от клиента")]
    CustomerReturn = 3,

    [EnumMember(Value = "ВозвратОтРозничногоПокупателя")]
    [Display(Name = "Возврат от розничного покупателя")]
    RetailCustomerReturn = 4,

    [EnumMember(Value = "ПеремещениеТоваров")]
    [Display(Name = "Перемещение товаров")]
    GoodsTransfer = 5
}
