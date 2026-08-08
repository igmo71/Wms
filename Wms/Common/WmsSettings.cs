namespace Wms.Common;

public class WmsSettings
{
    public int ImportDelay { get; set; }

    public bool AllowExternalCreateReceived { get; set; }
    public bool AllowExternalUpdateReadyForReceiving { get; set; }
    public bool AllowExternalUpdateInReceiving { get; set; }
    public bool AllowExternalUpdateProcessingRequired { get; set; }
    public bool AllowExternalUpdateReceived { get; set; }

    public bool AllowExternalCreateShipped { get; set; }
    public bool AllowExternalUpdatePrepared { get; set; }
    public bool AllowExternalUpdateReadyForPicking { get; set; }
    public bool AllowExternalUpdateReadyForVerification { get; set; }
    public bool AllowExternalUpdateInVerification { get; set; }
    public bool AllowExternalUpdateVerified { get; set; }
    public bool AllowExternalUpdateReadyForShipment { get; set; }
    public bool AllowExternalUpdateShipped { get; set; }

    public int ReceivingRefreshLoop { get; set; }
}
