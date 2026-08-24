namespace Wms.Mobile.Scanning;

public interface IBarcodeScanner
{
    BarcodeScanSource Source { get; }

    event EventHandler<BarcodeScanEvent>? ScanReceived;
}
