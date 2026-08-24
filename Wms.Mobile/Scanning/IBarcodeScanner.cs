namespace Wms.Mobile.Scanning;

public interface IBarcodeScanner
{
    BarcodeScanSource Source { get; }

    bool IsAvailable { get; }

    event EventHandler<BarcodeScanEvent>? ScanReceived;
}
