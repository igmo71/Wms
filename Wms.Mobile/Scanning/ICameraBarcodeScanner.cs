namespace Wms.Mobile.Scanning;

public interface ICameraBarcodeScanner : IBarcodeScanner
{
    bool IsAvailable { get; }

    bool TryAccept(string? value, string? symbology);
}
