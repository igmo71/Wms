using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderReceivingPage
{
    private async Task HandleScanAsync(string barcode)
    {
        if (!IsScanExpected)
        {
            return;
        }

        if (_mode == ReceivingPageMode.LocationScanning)
        {
            await ResolveReceivingLocationAsync(barcode);
        }
        else
        {
            await ResolveOrIncrementSkuAsync(barcode);
        }
    }

    private async Task ResolveReceivingLocationAsync(string barcode)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var location = await _referenceDataClient.ResolveStorageLocationAsync(
                barcode,
                Details.Order.WarehouseId,
                MobileStorageLocationContext.Receiving);
            _scannedLocation = location;
            _scannedLocationBarcode = barcode;
            ScannedLocationLabel.Text = $"{location.Address} · {location.Name}";
            SetMode(ReceivingPageMode.LocationConfirmation);
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен. Повторите сканирование позиции.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnConfirmLocationClicked(object? sender, EventArgs e)
    {
        if (_busy || _scannedLocation is null || _scannedLocationBarcode is null)
        {
            return;
        }

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _process.StartAsync(
                Details.Order.Id,
                _scannedLocationBarcode);
            _scannedLocation = null;
            _scannedLocationBarcode = null;
            ApplyDetails(response.Details);
            SetMode(ReceivingPageMode.Scanning);
        }
        catch (MobileApiException exception)
        {
            ConfirmLocationButton.Text = "Начать";
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ConfirmLocationButton.Text = "Повторить начало";
            ErrorLabel.Text = "Ответ сервера не получен. Повторите начало с тем же адресом.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnCancelLocationClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_process.IsStartPending)
        {
            ErrorLabel.Text = "Сначала повторите начало приёмки с тем же адресом.";
            return;
        }

        _scannedLocation = null;
        _scannedLocationBarcode = null;
        ConfirmLocationButton.Text = "Начать";
        SetMode(ReceivingPageMode.LocationScanning);
        await UpdateCameraAsync();
    }

    private async Task ResolveOrIncrementSkuAsync(string barcode)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var result = await _process.ScanAsync(Details.Order.Id, barcode);
            ApplyScanResult(result);
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = _process.IsScanPending
                ? "Ответ сервера не получен. Повторно отсканируйте этот же товар."
                : "Сервер WMS недоступен. Повторите сканирование товара.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnScanCandidateTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || e.Parameter is not MobileReceivingOrderLineCandidateResponse candidate)
        {
            return;
        }

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var result = await _process.SelectCandidateAsync(
                Details.Order.Id,
                candidate.LineNumber);
            ApplyScanResult(result);
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Повторно выберите эту же строку.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private void ApplyScanResult(ReceivingOrderScanResult result)
    {
        if (result.ErrorMessage is not null)
        {
            ErrorLabel.Text = result.ErrorMessage;
            return;
        }

        if (result.Candidates is not null)
        {
            ScanCandidates = result.Candidates;
            OnPropertyChanged(nameof(ScanCandidates));
            SetMode(ReceivingPageMode.CandidateSelection);
            return;
        }

        var response = result.Response
            ?? throw new InvalidOperationException("Процесс приёмки не вернул результат команды.");
        var lineNumber = result.ChangedLineNumber
            ?? throw new InvalidOperationException("Процесс приёмки не вернул номер строки.");
        ScanCandidates = [];
        OnPropertyChanged(nameof(ScanCandidates));
        ApplyDetails(response.Details);
        AccentLine(lineNumber, "+1");
        SetMode(ReceivingPageMode.Scanning);
        InstructionLabel.Text = "Принято +1. Сканируйте следующий товар.";
    }

    private async void OnCancelCandidateClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_process.IsScanPending)
        {
            ErrorLabel.Text = "Сначала повторно выберите предыдущую строку.";
            return;
        }

        ScanCandidates = [];
        OnPropertyChanged(nameof(ScanCandidates));
        ReturnToScanning();
        await UpdateCameraAsync();
    }

    private async void OnCompleteReceivingClicked(object? sender, EventArgs e)
    {
        if (_busy
            || !IsActiveReceiving
            || !IsSynchronizationResolved
            || _mode != ReceivingPageMode.Scanning
            || (HasPendingCommand && !_process.IsCompletionPending))
        {
            return;
        }

        if (!_process.IsCompletionPending && Details.Order.RequiresManagerCompletion)
        {
            ErrorLabel.Text = "Завершить приемку с расхождениями может заведующий в Web.";
            return;
        }

        if (Details.Lines.Any(x => x.FactQuantity is null))
        {
            ErrorLabel.Text = "Сначала проверьте фактическое количество каждой строки.";
            return;
        }

        if (!_process.IsCompletionPending)
        {
            SetBusy(true);
            CameraScannerView.Stop();
            var confirmed = await DisplayAlertAsync(
                "Завершить приёмку",
                BuildCompletionSummary(),
                "Завершить",
                "Отмена");
            if (!confirmed)
            {
                SetBusy(false);
                await UpdateCameraAsync();
                return;
            }
        }
        else
        {
            SetBusy(true);
        }

        try
        {
            var response = await _process.CompleteAsync(Details.Order.Id);
            ApplyDetails(response.Details);
            if (_isVisible)
            {
                await DisplayAlertAsync("Готово", "Приёмка завершена.", "ОК");
                if (_isVisible)
                {
                    await Navigation.PopAsync();
                }
            }
        }
        catch (MobileApiException exception)
        {
            CompleteReceivingButton.Text = "Завершить приёмку";
            ErrorLabel.Text = exception.Message;
            if (exception.StatusCode == System.Net.HttpStatusCode.Conflict)
                await RefreshOrderAsync();
        }
        catch (HttpRequestException)
        {
            CompleteReceivingButton.Text = "Повторить завершение";
            ErrorLabel.Text = "Ответ сервера не получен. Повторите завершение приёмки.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private string BuildCompletionSummary()
    {
        var lines = Details.Lines;
        var exact = lines.Count(x => x.FactQuantity == x.PlanQuantity);
        var zero = lines.Count(x => x.FactQuantity == 0);
        var shortage = lines.Count(x => x.FactQuantity > 0 && x.FactQuantity < x.PlanQuantity);
        var overage = lines.Count(x => x.FactQuantity > x.PlanQuantity);
        var location = Details.Order.ReceivingLocation?.Address ?? "не указана";
        return $"План: {Details.Order.Progress.PlanQuantity:g}\n"
            + $"Факт: {Details.Order.Progress.FactQuantity:g}\n"
            + $"Совпало: {exact}; недоприёмка: {shortage}; переприёмка: {overage}; ноль: {zero}\n"
            + $"Позиция приёмки: {location}";
    }

}
