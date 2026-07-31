namespace Wms.Common;

public class DocumentListQuery : ListQuery
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}
