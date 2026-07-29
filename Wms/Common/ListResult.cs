namespace Wms.Common;

internal class ListResult<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalItems { get; set; }
}
