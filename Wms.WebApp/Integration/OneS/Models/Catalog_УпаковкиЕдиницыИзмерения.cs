namespace Wms.WebApp.Integration.OneS.Models;

public class Catalog_УпаковкиЕдиницыИзмерения
{
    public string? Ref_Key { get; set; }
    public bool DeletionMark { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }
    public string? НаименованиеПолное { get; set; }
    public string? МеждународноеСокращение { get; set; }
    public int Числитель { get; set; }
    public int Знаменатель { get; set; }

    private readonly string select = "Ref_Key,DeletionMark,Code,Description,НаименованиеПолное,МеждународноеСокращение,Числитель,Знаменатель";

    public string GetUri => $"Catalog_УпаковкиЕдиницыИзмерения?$format=json&$select={select}&$filter=Ref_Key eq guid'{Ref_Key}'";

    public string ListUri => $"Catalog_УпаковкиЕдиницыИзмерения?$format=json&$select={select}";
}
