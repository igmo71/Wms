namespace Wms.Common;

internal class ListQuery
{
    public string? SearchString { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}
