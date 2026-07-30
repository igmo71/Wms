namespace Wms.Integration.OneS.Models;

public class Catalog_Номенклатура
{
    public Guid Ref_Key { get; set; }
    public bool DeletionMark { get; set; }
    public Guid? Parent_Key { get; set; }
    public bool IsFolder { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }
    public string? Артикул { get; set; }
    public Guid? ЕдиницаИзмерения_Key { get; set; }

    public bool? ВесИспользовать { get; set; }
    public Guid? ВесЕдиницаИзмерения_Key { get; set; }
    public double? ВесЧислитель { get; set; }
    public double? ВесЗнаменатель { get; set; }


    public const int BatchSize = 1000;

    public static string TotalUri => "Catalog_Номенклатура/$count?$format=json";

    public static string GetListUri(int page) => $"Catalog_Номенклатура" +
        $"?$format=json" +
        $"&$select={select}" +
        $"&$orderby=Ref_Key" +
        $"&$skip={page * BatchSize}" +
        $"&$top={BatchSize}";

    public static string GetUri(string refKey) => $"Catalog_Номенклатура" +
        $"?$format=json" +
        $"&$select={select}" +
        $"&$filter=Ref_Key eq guid'{refKey}'";

    private static readonly string select = "Ref_Key,DeletionMark,Parent_Key,IsFolder,Code,Description,Артикул,ЕдиницаИзмерения_Key,ВесИспользовать,ВесЕдиницаИзмерения_Key,ВесЧислитель,ВесЗнаменатель";
}
