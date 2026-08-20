namespace Wms.Integration.OneS.Models;

internal class Catalog_ЗоныДоставки
{
    public Guid Ref_Key { get; set; }
    public bool DeletionMark { get; set; }
    public Guid? Parent_Key { get; set; }
    public bool IsFolder { get; set; }
    public string? Description { get; set; }
    public string? Описание { get; set; }


    public static string GetListUri => $"Catalog_ЗоныДоставки" +
        $"?$format=json" +
        $"&$select={select}";

    public static string GetUri(string refKey) => $"Catalog_ЗоныДоставки" +
        $"?$format=json" +
        $"&$select={select}" +
        $"&$filter=Ref_Key eq guid'{refKey}'";

    private static readonly string select = "Ref_Key,DeletionMark,Parent_Key,IsFolder,Description,Описание";
}

