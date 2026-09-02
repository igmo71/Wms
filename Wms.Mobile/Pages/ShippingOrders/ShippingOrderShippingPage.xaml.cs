using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ShippingOrderShippingPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private MobileShippingOrderDetailsResponse? _details;
    private MobileOrderSynchronizationResponse? _synchronization;
    private ShippingPageMode _mode = ShippingPageMode.Ready;
    private Guid? _pendingShippingRequestId;
    private bool _isVisible;
    private bool _busy;

    public ShippingOrderShippingPage(MobileApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
    }

    public IReadOnlyList<MobileShippingOrderLineResponse> Lines { get; private set; } = [];

    private MobileShippingOrderDetailsResponse Details =>
        _details ?? throw new InvalidOperationException("Расходный ордер не загружен.");

    private bool IsSynchronizationResolved =>
        _synchronization is not null
        && OrderSynchronizationPresentation.CanPerformCriticalTransition(_synchronization);

    public void Show(MobileShippingOrderDetailsResponse details)
    {
        _synchronization = null;
        _pendingShippingRequestId = null;
        ConfirmShippingButton.Text = "Отгрузить";
        ErrorLabel.Text = string.Empty;
        ApplyDetails(details);
        SetMode(ShippingPageMode.Ready);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isVisible = true;
    }

    protected override void OnDisappearing()
    {
        _isVisible = false;
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (_busy || _pendingShippingRequestId is not null)
        {
            ErrorLabel.Text = _pendingShippingRequestId is not null
                ? "Сначала повторите отгрузку."
                : "Дождитесь завершения операции.";
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private void OnStartShippingClicked(object? sender, EventArgs e)
    {
        if (_busy
            || _pendingShippingRequestId is not null
            || Details.Order.Status != MobileShippingOrderStatus.ReadyForShipment)
        {
            return;
        }

        ErrorLabel.Text = string.Empty;
        SetMode(ShippingPageMode.Confirmation);
    }

    private void OnCancelShippingClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_pendingShippingRequestId is not null)
        {
            ErrorLabel.Text = "Сначала повторите отгрузку.";
            return;
        }

        ConfirmShippingButton.Text = "Отгрузить";
        ErrorLabel.Text = string.Empty;
        SetMode(ShippingPageMode.Ready);
    }

    private async void OnConfirmShippingClicked(object? sender, EventArgs e)
    {
        if (_busy || Details.Order.Status != MobileShippingOrderStatus.ReadyForShipment)
        {
            return;
        }

        _pendingShippingRequestId ??= Guid.NewGuid();
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _apiClient.ShipShippingOrderAsync(
                Details.Order.Id,
                _pendingShippingRequestId.Value);
            _pendingShippingRequestId = null;
            ConfirmShippingButton.Text = "Отгрузить";
            ApplyDetails(response.Details);
            if (_isVisible)
            {
                await DisplayAlertAsync("Готово", "Ордер отгружен.", "ОК");
                if (_isVisible)
                {
                    await Navigation.PopAsync();
                }
            }
        }
        catch (MobileApiException exception)
        {
            _pendingShippingRequestId = null;
            ConfirmShippingButton.Text = "Отгрузить";
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ConfirmShippingButton.Text = "Повторить отгрузку";
            ErrorLabel.Text = "Ответ сервера не получен. Повторите отгрузку.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyDetails(MobileShippingOrderDetailsResponse details)
    {
        _details = details;
        _synchronization = OrderSynchronizationPresentation.MergeOpeningAssessment(
            _synchronization,
            details.Order.Synchronization);
        Lines = details.Lines;
        OnPropertyChanged(nameof(Lines));
        NumberLabel.Text = $"Ордер {details.Order.Number}";
        WarehouseLabel.Text = $"Склад: {details.Order.WarehouseName}";
        ReceiverLabel.Text = $"Получатель: {details.Order.ReceiverName}";
        LocationLabel.Text = details.Order.ShippingLocation is null
            ? "Позиция отгрузки не указана"
            : $"Позиция отгрузки: {details.Order.ShippingLocation.Address}";
        ProgressLabel.Text = $"К отгрузке: {details.Order.Progress.FactQuantity:g}";
        SynchronizationPanel.IsVisible = OrderSynchronizationPresentation.HasIssue(
            _synchronization);
        SynchronizationTitleLabel.Text = OrderSynchronizationPresentation.BuildTitle(
            _synchronization);
        SynchronizationDetailsLabel.Text = OrderSynchronizationPresentation.BuildDetails(
            _synchronization);
        ShippingSummaryLabel.Text = $"Строк: {details.Lines.Count}; "
            + $"фактическое количество: {details.Order.Progress.FactQuantity:g}.";
    }

    private void SetMode(ShippingPageMode mode)
    {
        _mode = mode;
        ConfirmationPanel.IsVisible = mode == ShippingPageMode.Confirmation;
        (StepLabel.Text, InstructionLabel.Text) = mode switch
        {
            ShippingPageMode.Confirmation => (
                "Подтверждение отгрузки",
                "Проверьте позицию и итоговое количество, затем подтвердите отгрузку."),
            _ => (
                "Финальная отгрузка",
                "Проверьте итог ордера и нажмите «Отгрузить».")
        };
        RefreshActionAvailability();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.Opacity = busy ? 1 : 0;
        RefreshActionAvailability();
    }

    private void RefreshActionAvailability()
    {
        StartShippingButton.IsVisible = _mode == ShippingPageMode.Ready;
        StartShippingButton.IsEnabled = !_busy
            && _pendingShippingRequestId is null
            && IsSynchronizationResolved;
        ConfirmShippingButton.IsEnabled = !_busy && IsSynchronizationResolved;
        CancelShippingButton.IsEnabled = !_busy && _pendingShippingRequestId is null;
    }

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
        {
            DisableAndroidFocus(element);
        }
    }

    private static void DisableAndroidFocus(VisualElement element)
    {
#if ANDROID
        if (element.Handler?.PlatformView is Android.Views.View view)
        {
            view.Focusable = false;
            view.FocusableInTouchMode = false;
        }
#endif
    }

    private enum ShippingPageMode
    {
        Ready,
        Confirmation
    }
}
