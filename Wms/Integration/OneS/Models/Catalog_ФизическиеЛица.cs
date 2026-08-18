namespace Wms.Integration.OneS.Models;

public class Catalog_ФизическиеЛица
{
    public Guid Ref_Key { get; set; }
    public bool DeletionMark { get; set; }
    public Guid? Parent_Key { get; set; }
    public bool IsFolder { get; set; }
    public string? Description { get; set; }

    public static string GetListUri => "Catalog_ФизическиеЛица" +
        "?$format=json" +
        $"&$select={select}";

    public static string GetUri(string refKey) => "Catalog_ФизическиеЛица" +
        "?$format=json" +
        $"&$select={select}" +
        $"&$filter=Ref_Key eq guid'{refKey}'";

    private static readonly string select =
        "Ref_Key,DeletionMark,Parent_Key,IsFolder,Description";
}
