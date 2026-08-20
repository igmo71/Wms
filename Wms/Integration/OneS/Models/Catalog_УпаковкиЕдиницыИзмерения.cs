namespace Wms.Integration.OneS.Models;

internal class Catalog_УпаковкиЕдиницыИзмерения
{
    public Guid Ref_Key { get; set; }
    public bool DeletionMark { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }
    public string? НаименованиеПолное { get; set; }
    public string? МеждународноеСокращение { get; set; }
    public double Числитель { get; set; }
    public double Знаменатель { get; set; }

    public static string GetListUri => $"Catalog_УпаковкиЕдиницыИзмерения" +
        $"?$format=json" +
        $"&$select={select}";

    public static string GetUri(string refKey) => $"Catalog_УпаковкиЕдиницыИзмерения" +
        $"?$format=json" +
        $"&$select={select}" +
        $"&$filter=Ref_Key eq guid'{refKey}'";

    private static readonly string select = "Ref_Key,DeletionMark,Code,Description,НаименованиеПолное,МеждународноеСокращение,Числитель,Знаменатель";
}
