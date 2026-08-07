namespace Wms.Common;

public class WmsSettings
{
    public int ImportDelay { get; set; }

    public bool AllowExternalCreateCompleted { get; set; }

    public bool AllowExternalUpdatePending { get; set; }
    public bool AllowExternalUpdateInProcess { get; set; }
    public bool AllowExternalUpdateCompleted { get; set; }
}
