namespace Wms.Integration.OneS.Models;

public class Catalog_Партнеры
{
    public Guid Ref_Key { get; set; }
    public bool DeletionMark { get; set; }
    public Guid? Parent_Key { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }

    public const int BatchSize = 1000;

    public static string TotalUri => "Catalog_Партнеры/$count?$format=json";

    public static string GetListUri(int page) => $"Catalog_Партнеры" +
        $"?$format=json" +
        $"&$select={select}" +
        $"&$orderby=Ref_Key" +
        $"&$skip={page * BatchSize}" +
        $"&$top={BatchSize}";

    public static string GetUri(string refKey) => $"Catalog_Партнеры" +
        $"?$format=json" +
        $"&$select={select}" +
        $"&$filter=Ref_Key eq guid'{refKey}'";

    private static readonly string select = "Ref_Key,DeletionMark,Parent_Key,Code,Description";
}
