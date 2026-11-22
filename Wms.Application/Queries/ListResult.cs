namespace Wms.Common;

public class ListResult<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalItems { get; set; }
}
