namespace Wms.Integration.OneS.Models;

public class Catalog_Склады
{
    public Guid Ref_Key { get; set; }
    public bool DeletionMark { get; set; }
    public string? Description { get; set; }

    public static string GetListUri => $"Catalog_Склады" +
        $"?$format=json" +
        $"&$select={select}";

    public static string GetUri(string refKey) => $"Catalog_Склады" +
        $"?$format=json" +
        $"&$select={select}" +
        $"&$filter=Ref_Key eq guid'{refKey}'";

    private static readonly string select = "Ref_Key,DeletionMark,Description";

}
