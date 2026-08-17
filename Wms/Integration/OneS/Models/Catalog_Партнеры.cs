namespace Wms.Integration.OneS.Models;

public class Catalog_Партнеры
{
    public Guid Ref_Key { get; set; }
    public bool DeletionMark { get; set; }
    public Guid? Parent_Key { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }
}