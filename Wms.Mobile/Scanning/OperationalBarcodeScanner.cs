namespace Wms.Mobile.Scanning;

public sealed class OperationalBarcodeScanner : IOperationalBarcodeScanner
{
    private readonly ILifecycleBarcodeScanner _embeddedScanner;
    private readonly ICameraBarcodeScanner _cameraScanner;

    public OperationalBarcodeScanner(
        ILifecycleBarcodeScanner embeddedScanner,
        ICameraBarcodeScanner cameraScanner)
    {
        _embeddedScanner = embeddedScanner;
        _cameraScanner = cameraScanner;
        _embeddedScanner.ScanReceived += OnEmbeddedScanReceived;
        _cameraScanner.ScanReceived += OnCameraScanReceived;
    }

    public BarcodeScanSource? ActiveSource => _embeddedScanner.IsAvailable
        ? BarcodeScanSource.EmbeddedScanner
        : _cameraScanner.IsAvailable
            ? BarcodeScanSource.Camera
            : null;

    public event EventHandler<BarcodeScanEvent>? ScanReceived;

    public bool TryAcceptCameraScan(string? value, string? symbology) =>
        ActiveSource == BarcodeScanSource.Camera
        && _cameraScanner.TryAccept(value, symbology);

    private void OnEmbeddedScanReceived(object? sender, BarcodeScanEvent scanEvent) =>
        ScanReceived?.Invoke(this, scanEvent);

    private void OnCameraScanReceived(object? sender, BarcodeScanEvent scanEvent)
    {
        if (ActiveSource == BarcodeScanSource.Camera)
        {
            ScanReceived?.Invoke(this, scanEvent);
        }
    }
}
