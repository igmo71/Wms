namespace Wms.Integration.OneS.Models;

internal class InformationRegister_ШтрихкодыНоменклатуры
{
    public Guid Номенклатура_Key { get; set; }
    public string? Штрихкод { get; set; }

    private static readonly string select = "Номенклатура_Key,Штрихкод";

    public static string GetListUri => $"InformationRegister_ШтрихкодыНоменклатуры?$format=json&$select={select}";

    public static string GetUri(string refKey) =>
        $"InformationRegister_ШтрихкодыНоменклатуры?$format=json&$select={select}&$filter=Номенклатура_Key eq guid'{refKey}'";
}
