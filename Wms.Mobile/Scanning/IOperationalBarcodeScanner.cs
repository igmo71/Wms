namespace Wms.Mobile.Scanning;

public interface IOperationalBarcodeScanner
{
    BarcodeScanSource? ActiveSource { get; }

    event EventHandler<BarcodeScanEvent>? ScanReceived;

    bool TryAcceptCameraScan(string? value, string? symbology);
}
