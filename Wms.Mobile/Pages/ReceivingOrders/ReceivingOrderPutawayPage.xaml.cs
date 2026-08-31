using System.Collections.ObjectModel;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderPutawayPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly IServiceProvider _services;
    private MobileReceivingOrderDetailsResponse? _details;
    private bool _busy;
    private bool _isVisible;
    private Guid? _pendingStartRequestId;
    private Guid? _pendingDeleteRequestId;
    private Guid? _pendingDeleteMovementId;
    private Guid? _pendingCompletionRequestId;
    private Guid? _accentedMovementId;

    public ReceivingOrderPutawayPage(
        MobileApiClient apiClient,
        IServiceProvider services)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _services = services;
    }

    public ObservableCollection<ReceivingOrderPutawayLineViewState> LineStates { get; } = [];

    private MobileReceivingOrderDetailsResponse Details =>
        _details ?? throw new InvalidOperationException("Приходный ордер не загружен.");

    private bool IsInProgress =>
        Details.Order.PutawayStatus == MobilePutawayStatus.InProgress;

    private bool HasPendingCommand => _pendingStartRequestId is not null
        || _pendingDeleteRequestId is not null
        || _pendingCompletionRequestId is not null;

    public void Show(MobileReceivingOrderDetailsResponse details)
    {
        _accentedMovementId = null;
        ApplyDetails(details);
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

    private async void OnStartPutawayClicked(object? sender, EventArgs e)
    {
        if (_busy
            || Details.Order.PutawayStatus != MobilePutawayStatus.Pending
            || (HasPendingCommand && _pendingStartRequestId is null))
        {
            return;
        }

        if (_pendingStartRequestId is null)
        {
            SetBusy(true);
            var confirmed = await DisplayAlertAsync(
                "Начать размещение",
                $"Принято товара: {Details.Order.Progress.FactQuantity:g}. Начать размещение?",
                "Начать",
                "Отмена");
            if (!confirmed)
            {
                SetBusy(false);
                return;
            }
        }
        else
        {
            SetBusy(true);
        }

        _pendingStartRequestId ??= Guid.NewGuid();
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _apiClient.StartReceivingOrderPutawayAsync(
                Details.Order.Id,
                _pendingStartRequestId.Value);
            _pendingStartRequestId = null;
            StartPutawayButton.Text = "Начать размещение";
            ApplyDetails(response.Details);
        }
        catch (MobileApiException exception)
        {
            _pendingStartRequestId = null;
            StartPutawayButton.Text = "Начать размещение";
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            StartPutawayButton.Text = "Повторить начало";
            ErrorLabel.Text = "Ответ сервера не получен. Повторите начало размещения.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnNewMovementClicked(object? sender, EventArgs e)
    {
        if (!CanStartMovement())
        {
            return;
        }

        await OpenMovementPageAsync(null);
    }

    private async void OnAddLineMovementTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is ReceivingOrderPutawayLineViewState line
            && CanStartMovement()
            && line.RemainingQuantity > 0)
        {
            await OpenMovementPageAsync(line.LineNumber);
        }
    }

    private bool CanStartMovement() =>
        !_busy && IsInProgress && !HasPendingCommand;

    private async Task OpenMovementPageAsync(int? lineNumber)
    {
        var line = lineNumber is int number
            ? Details.Lines.Single(x => x.LineNumber == number)
            : null;
        var page = _services.GetRequiredService<ReceivingOrderPutawayMovementPage>();
        page.Show(Details, line, ApplyMovementResult);
        await Navigation.PushAsync(page);
    }

    private void ApplyMovementResult(MobileReceivingOrderCommandResponse response)
    {
        _accentedMovementId = response.ChangedMovementId;
        ApplyDetails(response.Details);
    }

    private async void OnDeleteMovementTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ReceivingOrderPutawayMovementViewState movement || _busy)
        {
            return;
        }

        if (_pendingDeleteMovementId is Guid pendingId && pendingId != movement.Id)
        {
            ErrorLabel.Text = "Сначала повторите удаление предыдущего размещения.";
            return;
        }

        if (HasPendingCommand && _pendingDeleteRequestId is null)
        {
            ErrorLabel.Text = "Сначала повторите незавершённую операцию.";
            return;
        }

        if (_pendingDeleteRequestId is null)
        {
            SetBusy(true);
            var confirmed = await DisplayAlertAsync(
                "Удалить размещение",
                $"Удалить черновик «{movement.DestinationText}», {movement.QuantityText.ToLowerInvariant()}?",
                "Удалить",
                "Отмена");
            if (!confirmed)
            {
                SetBusy(false);
                return;
            }
        }
        else
        {
            SetBusy(true);
        }

        _pendingDeleteMovementId = movement.Id;
        _pendingDeleteRequestId ??= Guid.NewGuid();
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _apiClient.DeleteReceivingOrderPutawayMovementAsync(
                Details.Order.Id,
                movement.Id,
                _pendingDeleteRequestId.Value);
            _pendingDeleteRequestId = null;
            _pendingDeleteMovementId = null;
            _accentedMovementId = null;
            ApplyDetails(response.Details);
        }
        catch (MobileApiException exception)
        {
            _pendingDeleteRequestId = null;
            _pendingDeleteMovementId = null;
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Повторите удаление этого же размещения.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnCompletePutawayClicked(object? sender, EventArgs e)
    {
        if (_busy
            || !IsInProgress
            || !IsFullyAllocated()
            || (HasPendingCommand && _pendingCompletionRequestId is null))
        {
            return;
        }

        if (_pendingCompletionRequestId is null)
        {
            SetBusy(true);
            var confirmed = await DisplayAlertAsync(
                "Завершить размещение",
                BuildCompletionSummary(),
                "Завершить",
                "Отмена");
            if (!confirmed)
            {
                SetBusy(false);
                return;
            }
        }
        else
        {
            SetBusy(true);
        }

        _pendingCompletionRequestId ??= Guid.NewGuid();
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _apiClient.CompleteReceivingOrderPutawayAsync(
                Details.Order.Id,
                _pendingCompletionRequestId.Value);
            _pendingCompletionRequestId = null;
            CompletePutawayButton.Text = "Завершить размещение";
            ApplyDetails(response.Details);
            if (_isVisible)
            {
                await DisplayAlertAsync("Готово", "Размещение завершено.", "ОК");
                if (_isVisible)
                {
                    await Navigation.PopAsync();
                }
            }
        }
        catch (MobileApiException exception)
        {
            _pendingCompletionRequestId = null;
            CompletePutawayButton.Text = "Завершить размещение";
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            CompletePutawayButton.Text = "Повторить завершение";
            ErrorLabel.Text = "Ответ сервера не получен. Повторите завершение размещения.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool IsFullyAllocated() =>
        Details.Lines
            .Where(x => x.FactQuantity > 0)
            .All(x => x.RemainingPutawayQuantity == 0);

    private string BuildCompletionSummary()
    {
        var positiveLines = Details.Lines.Count(x => x.FactQuantity > 0);
        var destinationCount = Details.Movements
            .Select(x => x.Destination.Id)
            .Distinct()
            .Count();
        return $"Строк: {positiveLines}\n"
            + $"Позиций назначения: {destinationCount}\n"
            + $"Количество: {Details.Order.Progress.AllocatedQuantity:g}";
    }

    private void ApplyDetails(MobileReceivingOrderDetailsResponse details)
    {
        _details = details;
        NumberLabel.Text = $"Размещение ордера {details.Order.Number}";
        StatusLabel.Text = details.Order.PutawayStatus switch
        {
            MobilePutawayStatus.Pending => "Ожидает размещения",
            MobilePutawayStatus.InProgress => "В размещении",
            MobilePutawayStatus.Completed => "Размещён",
            _ => "Размещение неактивно"
        };
        WarehouseLabel.Text = $"Склад: {details.Order.WarehouseName}";
        LocationLabel.Text = details.Order.ReceivingLocation is null
            ? "Позиция приёмки не указана"
            : $"Позиция приёмки: {details.Order.ReceivingLocation.Address}";
        ProgressLabel.Text = $"Размещено: {details.Order.Progress.AllocatedQuantity:g} из "
            + $"{details.Order.Progress.FactQuantity:g} · Строк: "
            + $"{details.Order.Progress.FullyAllocatedLineCount} из "
            + $"{details.Order.Progress.PositiveLineCount}";
        InstructionLabel.Text = details.Order.PutawayStatus == MobilePutawayStatus.Pending
            ? "Проверьте принятое количество и начните размещение."
            : "Создавайте черновые размещения до полного распределения.";
        SynchronizeLines(details);
        RefreshActionAvailability();
    }

    private void SynchronizeLines(MobileReceivingOrderDetailsResponse details)
    {
        var positiveLines = details.Lines.Where(x => x.FactQuantity > 0).ToList();
        var numbers = positiveLines.Select(x => x.LineNumber).ToHashSet();
        for (var index = LineStates.Count - 1; index >= 0; index--)
        {
            if (!numbers.Contains(LineStates[index].LineNumber))
            {
                LineStates.RemoveAt(index);
            }
        }

        foreach (var line in positiveLines)
        {
            var movements = details.Movements.Where(x => x.LineNumber == line.LineNumber);
            var state = LineStates.SingleOrDefault(x => x.LineNumber == line.LineNumber);
            if (state is null)
            {
                LineStates.Add(ReceivingOrderPutawayLineViewState.From(
                    line,
                    movements,
                    IsInProgress && !_busy && !HasPendingCommand,
                    _accentedMovementId));
            }
            else
            {
                state.Update(
                    line,
                    movements,
                    IsInProgress && !_busy && !HasPendingCommand,
                    _accentedMovementId);
            }
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.Opacity = busy ? 1 : 0;
        RefreshActionAvailability();
    }

    private void RefreshActionAvailability()
    {
        if (_details is null)
        {
            return;
        }

        StartPutawayButton.IsVisible = Details.Order.PutawayStatus == MobilePutawayStatus.Pending;
        StartPutawayButton.IsEnabled = !_busy
            && (!HasPendingCommand || _pendingStartRequestId is not null);
        NewMovementButton.IsVisible = IsInProgress;
        NewMovementButton.IsEnabled = CanStartMovement()
            && Details.Lines.Any(x => x.RemainingPutawayQuantity > 0);
        CompletePutawayButton.IsVisible = IsInProgress;
        CompletePutawayButton.IsEnabled = !_busy
            && (!HasPendingCommand || _pendingCompletionRequestId is not null)
            && IsFullyAllocated();
        foreach (var line in LineStates)
        {
            line.SetActionAvailability(
                CanStartMovement(),
                _pendingDeleteRequestId is null ? null : _pendingDeleteMovementId);
        }
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
}
