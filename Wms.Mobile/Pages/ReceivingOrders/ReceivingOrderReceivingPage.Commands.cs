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
            if (_lpnTabActive && CanUseLicensePlateNumbers)
            {
                if (_lpnQuantityEditing)
                    return;
                if (SelectedLicensePlateNumber?.CanEdit == true)
                    await ResolveLpnSkuAsync(barcode);
                else
                    await OpenLicensePlateNumberAsync(barcode);
            }
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

    private async Task OpenLicensePlateNumberAsync(string code)
    {
        if (_busy || (HasPendingCommand && !_process.IsOpenLpnPending))
            return;

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _process.OpenLicensePlateNumberAsync(Details.Order.Id, code);
            ApplyDetails(response.Details);
            SelectedLicensePlateNumber = response.SelectedLicensePlateNumberId is Guid id
                ? LicensePlateNumbers.SingleOrDefault(x => x.Id == id)
                : null;
            OnPropertyChanged(nameof(SelectedLicensePlateNumber));
            SelectedLpnPanel.IsVisible = SelectedLicensePlateNumber is not null;
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Повторно отсканируйте тот же LPN.";
        }
        catch (InvalidOperationException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private void OnLicensePlateNumberTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not MobileReceivingOrderLicensePlateNumberResponse lpn)
            return;
        SelectedLicensePlateNumber = lpn;
        OnPropertyChanged(nameof(SelectedLicensePlateNumber));
        SelectedLpnPanel.IsVisible = true;
        CloseLpnQuantityEditor();
        LpnScanInstructionLabel.Text = lpn.CanEdit
            ? "Отсканируйте товар для ввода абсолютного количества."
            : "LPN доступен только для просмотра.";
        _ = UpdateCameraAsync();
    }

    private async Task ResolveLpnSkuAsync(string barcode)
    {
        if (_busy || SelectedLicensePlateNumber?.CanEdit != true || HasPendingCommand)
            return;

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var candidates = await _orderClient.ResolveSkuAsync(Details.Order.Id, barcode);
            if (candidates.Count != 1)
            {
                ErrorLabel.Text = candidates.Count == 0
                    ? "Товар отсутствует в плане приходного ордера."
                    : "Штрихкод соответствует нескольким строкам плана.";
                return;
            }
            OpenLpnQuantityEditor(candidates[0]);
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Не удалось определить товар. Повторите сканирование.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private void OpenLpnQuantityEditor(MobileReceivingOrderLineCandidateResponse candidate)
    {
        _selectedLpnSkuId = candidate.StockKeepingUnitId;
        _lpnQuantityEditing = true;
        LpnQuantitySkuLabel.Text = candidate.SkuName;
        LpnQuantityUnitLabel.Text = string.IsNullOrWhiteSpace(candidate.UnitOfMeasure)
            ? candidate.SkuCode
            : $"{candidate.SkuCode} · {candidate.UnitOfMeasure}";
        var existing = SelectedLicensePlateNumber?.Items
            .SingleOrDefault(x => x.StockKeepingUnitId == candidate.StockKeepingUnitId);
        LpnQuantityEntry.Text = existing?.Quantity.ToString("0.###") ?? string.Empty;
        RemoveLpnItemButton.IsVisible = existing is not null;
        LpnQuantityEditor.IsVisible = true;
        LpnScanInstructionLabel.Text = "Введите абсолютное количество, включая ноль, и подтвердите.";
    }

    private void OnLpnItemTapped(object? sender, TappedEventArgs e)
    {
        if (SelectedLicensePlateNumber?.CanEdit != true
            || e.Parameter is not MobileReceivingOrderLicensePlateNumberItemResponse item)
            return;
        var line = Details.Lines.Single(x => x.StockKeepingUnitId == item.StockKeepingUnitId);
        OpenLpnQuantityEditor(new MobileReceivingOrderLineCandidateResponse(
            line.LineNumber, line.StockKeepingUnitId, line.SkuCode, line.SkuName,
            line.UnitOfMeasure, line.PlanQuantity, line.FactQuantity,
            line.AllocatedQuantity, line.RemainingPutawayQuantity, true));
        _ = UpdateCameraAsync();
    }

    private async void OnSaveLpnQuantityClicked(object? sender, EventArgs e)
    {
        if (_busy || SelectedLicensePlateNumber?.CanEdit != true
            || _selectedLpnSkuId is not Guid skuId
            || (HasPendingCommand && !_process.IsLpnMutationPending))
            return;
        var text = LpnQuantityEntry.Text?.Trim().Replace(',', '.');
        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var quantity)
            || !WarehouseQuantityInput.IsSupported(quantity)
            || quantity < 0)
        {
            ErrorLabel.Text = "Введите неотрицательное количество не более трёх знаков после запятой.";
            return;
        }

        await ExecuteLpnMutationAsync(
            () => _process.SetLicensePlateNumberItemQuantityAsync(
                Details.Order.Id, SelectedLicensePlateNumber.Id, skuId, quantity),
            "Ответ сервера не получен. Повторите сохранение того же количества.");
    }

    private async void OnRemoveLpnItemClicked(object? sender, EventArgs e)
    {
        if (_busy || SelectedLicensePlateNumber?.CanEdit != true
            || _selectedLpnSkuId is not Guid skuId
            || (HasPendingCommand && !_process.IsLpnMutationPending))
            return;
        if (!_process.IsLpnMutationPending
            && !await DisplayAlertAsync("Удалить строку", "Удалить товар из состава LPN?", "Удалить", "Назад"))
            return;
        await ExecuteLpnMutationAsync(
            () => _process.RemoveLicensePlateNumberItemAsync(
                Details.Order.Id, SelectedLicensePlateNumber.Id, skuId),
            "Ответ сервера не получен. Повторите удаление этой же строки.");
    }

    private async void OnCloseLicensePlateNumberClicked(object? sender, EventArgs e)
    {
        if (_busy || SelectedLicensePlateNumber?.CanClose != true
            || (HasPendingCommand && !_process.IsLpnMutationPending))
            return;
        if (!_process.IsLpnMutationPending
            && !await DisplayAlertAsync("Закрыть LPN", "Закрыть пересчёт этого LPN?", "Закрыть", "Назад"))
            return;
        await ExecuteLpnMutationAsync(
            () => _process.CloseLicensePlateNumberAsync(
                Details.Order.Id, SelectedLicensePlateNumber.Id),
            "Ответ сервера не получен. Повторите закрытие этого же LPN.");
    }

    private async void OnReopenLicensePlateNumberClicked(object? sender, EventArgs e)
    {
        if (_busy || SelectedLicensePlateNumber?.CanReopen != true
            || (HasPendingCommand && !_process.IsLpnMutationPending))
            return;
        await ExecuteLpnMutationAsync(
            () => _process.ReopenLicensePlateNumberAsync(
                Details.Order.Id, SelectedLicensePlateNumber.Id),
            "Ответ сервера не получен. Повторите возобновление этого же LPN.");
    }

    private async Task ExecuteLpnMutationAsync(
        Func<Task<MobileReceivingOrderCommandResponse>> execute,
        string uncertainMessage)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await execute();
            ApplyDetails(response.Details);
            if (response.SelectedLicensePlateNumberId is Guid id)
            {
                SelectedLicensePlateNumber = LicensePlateNumbers.SingleOrDefault(x => x.Id == id);
                OnPropertyChanged(nameof(SelectedLicensePlateNumber));
            }
            CloseLpnQuantityEditor();
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = uncertainMessage;
        }
        catch (InvalidOperationException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnCancelLpnQuantityClicked(object? sender, EventArgs e)
    {
        if (_process.IsLpnMutationPending)
        {
            ErrorLabel.Text = "Сначала повторите предыдущее изменение LPN.";
            return;
        }
        CloseLpnQuantityEditor();
        await UpdateCameraAsync();
    }

    private void CloseLpnQuantityEditor()
    {
        _selectedLpnSkuId = null;
        _lpnQuantityEditing = false;
        LpnQuantityEditor.IsVisible = false;
        LpnQuantityEntry.Text = string.Empty;
        if (SelectedLicensePlateNumber?.CanEdit == true)
            LpnScanInstructionLabel.Text = "Отсканируйте товар для ввода абсолютного количества.";
    }

    private async void OnCancelLicensePlateNumberClicked(object? sender, EventArgs e)
    {
        if (_busy
            || sender is not Button { CommandParameter: MobileReceivingOrderLicensePlateNumberResponse lpn }
            || !lpn.CanCancel
            || (HasPendingCommand && !_process.IsCancelLpnPending))
            return;

        if (!_process.IsCancelLpnPending
            && !await DisplayAlertAsync(
                "Отменить LPN",
                $"Отменить пустой LPN {lpn.Code}? Код больше нельзя будет использовать.",
                "Отменить LPN",
                "Назад"))
            return;

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _process.CancelLicensePlateNumberAsync(Details.Order.Id, lpn.Id);
            ApplyDetails(response.Details);
            SelectedLicensePlateNumber = LicensePlateNumbers.SingleOrDefault(x => x.Id == lpn.Id);
            OnPropertyChanged(nameof(SelectedLicensePlateNumber));
            SelectedLpnPanel.IsVisible = SelectedLicensePlateNumber is not null;
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Повторите отмену этого же LPN.";
        }
        catch (InvalidOperationException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
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
