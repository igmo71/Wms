namespace Wms.Integration.OneS.Models;

public class Catalog_ФизическиеЛица
{
    public Guid Ref_Key { get; set; }
    public bool DeletionMark { get; set; }
    public Guid? Parent_Key { get; set; }
    public bool IsFolder { get; set; }
    public string? Description { get; set; }
}
