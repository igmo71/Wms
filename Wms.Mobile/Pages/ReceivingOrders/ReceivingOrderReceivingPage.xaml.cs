using System.Collections.ObjectModel;
using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderReceivingPage : ContentPage
{
    private readonly ReceivingOrderReceivingProcess _process;
    private readonly MobileReceivingOrderClient _orderClient;
    private readonly MobileReferenceDataClient _referenceDataClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private MobileReceivingOrderDetailsResponse? _details;
    private MobileOrderSynchronizationResponse? _synchronization;
    private ReceivingPageMode _mode = ReceivingPageMode.Ready;
    private ReceivingOrderLineViewState? _editingLine;
    private MobileStorageLocationResponse? _scannedLocation;
    private string? _scannedLocationBarcode;
    private int _searchVersion;
    private bool _isVisible;
    private bool _scannerSubscribed;
    private bool _busy;

    public ReceivingOrderReceivingPage(
        ReceivingOrderReceivingProcess process,
        MobileReceivingOrderClient orderClient,
        MobileReferenceDataClient referenceDataClient,
        IOperationalBarcodeScanner scanner)
    {
        InitializeComponent();
        _process = process;
        _orderClient = orderClient;
        _referenceDataClient = referenceDataClient;
        _scanner = scanner;
        CameraScannerView.Configure(scanner);
    }

    public ObservableCollection<ReceivingOrderLineViewState> LineStates { get; } = [];
    public IReadOnlyList<MobileReceivingOrderLineCandidateResponse> ScanCandidates { get; private set; } = [];
    public IReadOnlyList<MobileReceivingOrderLineCandidateResponse> SearchCandidates { get; private set; } = [];

    private MobileReceivingOrderDetailsResponse Details =>
        _details ?? throw new InvalidOperationException("Приходный ордер не загружен.");

    private bool IsActiveReceiving => false;

    private bool IsSynchronizationResolved =>
        _synchronization is not null
        && OrderSynchronizationPresentation.CanPerformCriticalTransition(_synchronization);

    private bool HasPendingCommand => _process.HasPendingCommand;

    private bool CanStartNewAction => IsActiveReceiving
        && !_busy
        && _mode == ReceivingPageMode.Scanning
        && !HasPendingCommand;

    private bool IsScanExpected => !_busy && _mode == ReceivingPageMode.LocationScanning;

    private void OnProductsTabClicked(object? sender, EventArgs e)
    {
        ProductLinesPanel.IsVisible = true;
        LpnPanel.IsVisible = false;
        ProductsTabButton.Opacity = 1;
        LpnTabButton.Opacity = 0.65;
    }

    private void OnLpnTabClicked(object? sender, EventArgs e)
    {
        ProductLinesPanel.IsVisible = false;
        LpnPanel.IsVisible = true;
        ProductsTabButton.Opacity = 0.65;
        LpnTabButton.Opacity = 1;
    }

    public void Show(MobileReceivingOrderDetailsResponse details)
    {
        _synchronization = null;
        ApplyDetails(details);
        SetMode(details.Order.Status == MobileReceivingOrderStatus.ReadyForReceiving
            ? ReceivingPageMode.Ready
            : ReceivingPageMode.Scanning);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isVisible = true;
        if (!_scannerSubscribed)
        {
            _scanner.ScanReceived += OnScanReceived;
            _scannerSubscribed = true;
        }

        await UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _isVisible = false;
        _searchVersion++;
        LineSearchEntry.Unfocus();
        CameraScannerView.Stop();
        if (_scannerSubscribed)
        {
            _scanner.ScanReceived -= OnScanReceived;
            _scannerSubscribed = false;
        }

        base.OnDisappearing();
    }

    private async void OnStartReceivingClicked(object? sender, EventArgs e)
    {
        if (_busy || HasPendingCommand || Details.Order.Status != MobileReceivingOrderStatus.ReadyForReceiving)
        {
            return;
        }

        ErrorLabel.Text = string.Empty;
        SetMode(ReceivingPageMode.LocationScanning);
        await UpdateCameraAsync();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent) =>
        MainThread.BeginInvokeOnMainThread(async () => await HandleScanAsync(scanEvent.Value));

}
