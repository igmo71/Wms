namespace Wms.WebApp.Integration.OneS.Models;

public class Catalog_УпаковкиЕдиницыИзмерения
{
    public required string Ref_Key { get; set; }
    public bool DeletionMark { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }
    public string? НаименованиеПолное { get; set; }
    public string? МеждународноеСокращение { get; set; }
    public int Числитель { get; set; }
    public int Знаменатель { get; set; }

    private static readonly string select = "Ref_Key,DeletionMark,Code,Description,НаименованиеПолное,МеждународноеСокращение,Числитель,Знаменатель";

    public static string GetListUri => $"Catalog_УпаковкиЕдиницыИзмерения?$format=json&$select={select}";

    public static string GetUri(string refKey) => $"Catalog_УпаковкиЕдиницыИзмерения?$format=json&$select={select}&$filter=Ref_Key eq guid'{refKey}'";
}
