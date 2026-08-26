namespace Wms.Mobile.Scanning;

public interface ILifecycleBarcodeScanner : IBarcodeScanner
{
    bool IsAvailable { get; }

    void Start();

    void Stop();
}
