namespace Wms.Mobile.Scanning;

public interface IKeyboardWedgeBarcodeScanner : IBarcodeScanner
{
    bool TryAccept(string? rawValue);
}
