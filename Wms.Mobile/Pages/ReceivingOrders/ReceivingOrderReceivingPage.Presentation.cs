using System.Collections.ObjectModel;
using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderReceivingPage
{
    private void ApplyDetails(MobileReceivingOrderDetailsResponse details)
    {
        _details = details;
        _synchronization = OrderSynchronizationPresentation.MergeOpeningAssessment(
            _synchronization,
            details.Order.Synchronization);
        NumberLabel.Text = $"Приёмка ордера {details.Order.Number}";
        StatusLabel.Text = details.Order.Status switch
        {
            MobileReceivingOrderStatus.ReadyForReceiving => "Готов к приёмке",
            MobileReceivingOrderStatus.InReceiving => "В приёмке",
            MobileReceivingOrderStatus.ProcessingRequired => "Требуется обработка",
            _ => "Приёмка завершена"
        };
        WarehouseLabel.Text = $"Склад: {details.Order.WarehouseName}";
        ShipperLabel.Text = $"Отправитель: {details.Order.ShipperName}";
        LocationLabel.Text = details.Order.ReceivingLocation is null
            ? "Позиция приёмки не выбрана"
            : $"Позиция приёмки: {details.Order.ReceivingLocation.Address}";
        ProgressLabel.Text = $"Факт: {details.Order.Progress.FactQuantity:g} из "
            + $"{details.Order.Progress.PlanQuantity:g} · Проверено строк: "
            + $"{details.Order.Progress.ConfirmedLineCount} из "
            + $"{details.Order.Progress.TotalLineCount}";
        SynchronizationPanel.IsVisible = OrderSynchronizationPresentation.HasIssue(
            _synchronization);
        SynchronizationTitleLabel.Text = OrderSynchronizationPresentation.BuildTitle(
            _synchronization);
        SynchronizationDetailsLabel.Text = OrderSynchronizationPresentation.BuildDetails(
            _synchronization);
        if (details.Order.RequiresManagerCompletion)
        {
            SynchronizationPanel.IsVisible = true;
            SynchronizationTitleLabel.Text = "Требуется решение заведующего";
            SynchronizationDetailsLabel.Text += Environment.NewLine
                + "Количества отличаются от плана WMS или текущего плана 1С. Завершить приемку может заведующий в Web.";
        }
        else if (_synchronization.ChangedFields.Count > 0 && !OrderSynchronizationPresentation.HasIssue(_synchronization))
        {
            SynchronizationPanel.IsVisible = true;
            SynchronizationTitleLabel.Text = "Изменения в 1С (информация)";
            SynchronizationDetailsLabel.Text = string.Join(", ", _synchronization.ChangedFields);
        }
        SynchronizeLineStates(details.Lines);
        RefreshActionAvailability();
    }

    private void SynchronizeLineStates(IReadOnlyList<MobileReceivingOrderLineResponse> lines)
    {
        var lineNumbers = lines.Select(x => x.LineNumber).ToHashSet();
        for (var index = LineStates.Count - 1; index >= 0; index--)
        {
            if (!lineNumbers.Contains(LineStates[index].LineNumber))
            {
                LineStates.RemoveAt(index);
            }
        }

        foreach (var line in lines)
        {
            var state = LineStates.SingleOrDefault(x => x.LineNumber == line.LineNumber);
            if (state is null)
            {
                LineStates.Add(ReceivingOrderLineViewState.From(line, IsActiveReceiving));
            }
            else
            {
                state.Update(line, IsActiveReceiving);
            }
        }
    }

    private static bool TryReadQuantity(
        ReceivingOrderLineViewState line,
        out decimal quantity)
    {
        var value = line.QuantityText.Trim().Replace(',', '.');
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out quantity)
            && WarehouseQuantityInput.IsSupported(quantity)
            && quantity >= 0)
        {
            return true;
        }

        line.MarkQuantityInvalid();
        return false;
    }

    private void AccentLine(int lineNumber, string? text)
    {
        foreach (var line in LineStates)
        {
            line.SetAccent(line.LineNumber == lineNumber, text);
        }
    }

    private async Task ClearSearchAsync()
    {
        _searchVersion++;
        SetSearchBusy(false);
        LineSearchEntry.Unfocus();
        await LineSearchEntry.HideSoftInputAsync(CancellationToken.None);
        LineSearchEntry.Text = string.Empty;
        SearchCandidates = [];
        OnPropertyChanged(nameof(SearchCandidates));
    }

    private void ReturnToScanning()
    {
        SetMode(ReceivingPageMode.Scanning);
        ErrorLabel.Text = string.Empty;
    }

    private void SetMode(ReceivingPageMode mode)
    {
        _mode = mode;
        StartReceivingButton.IsVisible = mode == ReceivingPageMode.Ready;
        LocationConfirmationPanel.IsVisible = mode == ReceivingPageMode.LocationConfirmation;
        LineSearchPrompt.IsVisible = mode == ReceivingPageMode.Scanning && IsActiveReceiving;
        LineSearchPanel.IsVisible = mode == ReceivingPageMode.Searching;
        LineCandidatesPanel.IsVisible = mode == ReceivingPageMode.CandidateSelection;

        (StepLabel.Text, InstructionLabel.Text) = mode switch
        {
            ReceivingPageMode.Ready => (
                "Начало приёмки",
                "Проверьте ордер и начните приёмку."),
            ReceivingPageMode.LocationScanning => (
                "Позиция приёмки",
                "Отсканируйте позицию зоны приёмки этого склада."),
            ReceivingPageMode.LocationConfirmation => (
                "Подтверждение позиции",
                "Проверьте адрес и подтвердите начало приёмки."),
            ReceivingPageMode.Searching => (
                "Ручной выбор строки",
                "Выбор строки открывает итоговое количество и не добавляет единицу."),
            ReceivingPageMode.CandidateSelection => (
                "Выбор строки",
                "Одинаковый товар есть в нескольких строках."),
            ReceivingPageMode.Editing => (
                "Итоговое количество",
                "Введите абсолютное фактическое количество."),
            _ => (
                "Приёмка товара",
                "Отсканируйте товар. Каждый принятый скан добавляет одну единицу.")
        };
        RefreshActionAvailability();
    }

    private async Task UpdateCameraAsync()
    {
        if (_isVisible
            && _scanner.ActiveSource == BarcodeScanSource.Camera
            && IsScanExpected)
        {
            await CameraScannerView.StartAsync();
        }
        else
        {
            CameraScannerView.Stop();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CommandProgressIndicator.Opacity = busy ? 1 : 0;
        RefreshActionAvailability();
    }

    private void RefreshActionAvailability()
    {
        if (_details is null)
        {
            return;
        }

        StartReceivingButton.IsEnabled = !_busy
            && !HasPendingCommand
            && (IsSynchronizationResolved || (Details.Order.CanStartWithSourceDifferences
                && _synchronization is { IsFresh: true } && string.IsNullOrWhiteSpace(_synchronization.VerificationError)));
        ConfirmLocationButton.IsEnabled = !_busy
            && (!HasPendingCommand || _process.IsStartPending);
        CancelLocationButton.IsEnabled = !_busy && !_process.IsStartPending;
        CancelCandidateButton.IsEnabled = !_busy && !_process.IsScanPending;
        LineSearchPrompt.IsEnabled = CanStartNewAction;
        CompleteReceivingButton.IsVisible = IsActiveReceiving;
        CompleteReceivingButton.IsEnabled = !_busy
            && _mode == ReceivingPageMode.Scanning
            && IsSynchronizationResolved
            && (!HasPendingCommand || _process.IsCompletionPending)
            && (_process.IsCompletionPending || !Details.Order.RequiresManagerCompletion)
            && Details.Lines.All(x => x.FactQuantity.HasValue);
        foreach (var line in LineStates)
        {
            line.SetActionsEnabled(CanStartNewAction);
        }
    }

    private void SetSearchBusy(bool busy) =>
        LineSearchIndicator.Opacity = busy ? 1 : 0;

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
        {
            AndroidFocus.Suppress(element);
        }
    }

    private enum ReceivingPageMode
    {
        Ready,
        LocationScanning,
        LocationConfirmation,
        Scanning,
        CandidateSelection,
        Searching,
        Editing
    }
}

