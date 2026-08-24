namespace Wms.Mobile.Scanning;

public interface ILifecycleBarcodeScanner : IBarcodeScanner
{
    void Start();

    void Stop();
}
