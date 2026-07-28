namespace Wms.WebApp.Integration.OneS.Models;

public class Catalog_Номенклатура
{
    public string? Ref_Key { get; set; }
    public bool DeletionMark { get; set; }
    public string? Parent_Key { get; set; }
    public bool IsFolder { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }
    public string? Артикул { get; set; }
    public string? ЕдиницаИзмерения_Key { get; set; }

    public bool ВесИспользовать { get; set; }
    public string? ВесЕдиницаИзмерения_Key { get; set; }
    public double ВесЧислитель { get; set; }
    public double ВесЗнаменатель { get; set; }

    public bool ОбъемИспользовать { get; set; }
    public string? ОбъемЕдиницаИзмерения_Key { get; set; }
    public double ОбъемЗнаменатель { get; set; }
    public double ОбъемЧислитель { get; set; }
}

