using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Wms.Application.Receiving;
using Wms.Application.Users;
using Wms.Common;
using Wms.Data;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class Details
{
    [Parameter] public Guid Id { get; set; }

    [Inject] private IReceivingWorkQueryStore Orders { get; set; } = null!;
    [Inject] private GetReceivingSources Sources { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ResynchronizeReceivingOrder RefreshSources { get; set; } = null!;
    [Inject] private CompleteReceivingOrder CompleteOrder { get; set; } = null!;
    [Inject] private CloseReceivingLpn CloseLpn { get; set; } = null!;
    [Inject] private ReopenReceivingOrder ReopenOrder { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;
    [Inject] private ApplicationUserQueryService UserNames { get; set; } = null!;

    private ReceivingWorkOrderView? _order;
    private IReadOnlyList<ReceivingSourceView> _sources = [];
    private IReadOnlyList<ReceivingSourceChangeHistoryView> _sourceHistory = [];
    private IReadOnlyDictionary<Guid, string> _sourceNames = new Dictionary<Guid, string>();
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();
    private bool _showLpns = true;
    private bool _showStages;
    private IReadOnlyDictionary<Guid, IReadOnlyList<ReceivingLpnReversalHistoryView>>
        _deletionHistory = new Dictionary<Guid, IReadOnlyList<ReceivingLpnReversalHistoryView>>();
    private string? _userId;
    private string? _error;
    private string? _lifecycleMessage;
    private string? _sourceChangeMessage;
    private ReceivingOrderRefreshResult? _refreshResult;
    private Guid? _pendingCompleteRequestId;
    private Guid? _pendingReopenRequestId;
    private (Guid LpnId, long Revision, bool ConfirmExpiredDate, Guid RequestId)? _pendingClose;
    private Guid? _expandedLpnId;
    private bool _canManage;
    private bool _loading = true;
    private bool _busy;

    protected override async Task OnParametersSetAsync()
    {
        _sourceChangeMessage = null;
        _pendingCompleteRequestId = null;
        _pendingReopenRequestId = null;
        _lifecycleMessage = null;
        _refreshResult = null;
        _expandedLpnId = null;
        await ReloadAsync();
    }

    private async Task OpenSourceCompositionAsync()
    {
        if (!_canManage || _busy || _userId is null || _order is null
            || _order.Status is not (ReceivingOrderStatus.ReadyForReceiving
                or ReceivingOrderStatus.InReceiving or ReceivingOrderStatus.Received))
            return;
        _busy = true;
        _error = null;
        _sourceChangeMessage = null;
        try
        {
            var parameters = new DialogParameters<ReceivingSourceCompositionDialog>
            {
                { x => x.OrderId, Id },
                { x => x.WarehouseId, _order.WarehouseId },
                { x => x.UserId, _userId }
            };
            var dialog = await DialogService.ShowAsync<ReceivingSourceCompositionDialog>(
                "Изменить состав ордера WMS", parameters,
                new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true });
            var result = await dialog.Result;
            if (result is null || result.Canceled
                || result.Data is not ReceivingSourceChangeKind kind)
                return;
            await ReloadAsync();
            if (_error is null)
                _sourceChangeMessage = kind == ReceivingSourceChangeKind.Attach
                    ? "Документ 1С добавлен. Текущий план пересчитан; факт сохранён."
                    : "Документ 1С откреплён. Текущий план пересчитан; факт сохранён.";
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private async Task CompleteOrderAsync()
    {
        if (_busy || _userId is null || _order is null
            || _order.Status != ReceivingOrderStatus.InReceiving
            || !_canManage
            || _order.Lpns.Any(x => x.Status == LicensePlateNumberStatus.Open))
            return;

        _busy = true;
        _error = null;
        _lifecycleMessage = null;
        try
        {
            _pendingCompleteRequestId ??= Guid.NewGuid();
            var result = await CompleteOrder.ExecuteAsync(new CompleteReceivingOrderRequest(
                _pendingCompleteRequestId.Value, Id, _userId));
            if (!result.IsSuccess)
            {
                _pendingCompleteRequestId = null;
                _error = result.Error!.Message;
                return;
            }
            _pendingCompleteRequestId = null;
            await ReloadAsync();
            if (_error is null)
                _lifecycleMessage = "Ордер завершён. План, факт и версии документов 1С сохранены для этой приёмки.";
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private async Task ReopenOrderAsync()
    {
        if (_busy || !_canManage || _userId is null || _order?.Status != ReceivingOrderStatus.Received)
            return;

        _busy = true;
        _error = null;
        _lifecycleMessage = null;
        try
        {
            _pendingReopenRequestId ??= Guid.NewGuid();
            var result = await ReopenOrder.ExecuteAsync(new ReopenReceivingOrderRequest(
                _pendingReopenRequestId.Value, Id, _userId, FromWeb: true));
            if (!result.IsSuccess)
            {
                _pendingReopenRequestId = null;
                _error = result.Error!.Message;
                return;
            }
            _pendingReopenRequestId = null;
            await ReloadAsync();
            if (_error is null)
                _lifecycleMessage = "Начата новая приёмка. Первый участник заново выбирает место приёмки.";
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private async Task RefreshSourcesAsync()
    {
        if (!_canManage || _busy || _userId is null)
            return;
        _busy = true;
        _error = null;
        _refreshResult = null;
        try
        {
            var result = await RefreshSources.ExecuteAsync(Id, _userId);
            if (!result.IsSuccess)
            {
                _error = result.Error!.Message;
                return;
            }
            await ReloadAsync();
            _refreshResult = result.Value;
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private async Task ReloadAsync()
    {
        _loading = true;
        _error = null;
        _lifecycleMessage = null;
        _sourceChangeMessage = null;
        _refreshResult = null;
        try
        {
            var user = (await Authentication.GetAuthenticationStateAsync()).User;
            _userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            _canManage = user.IsInRole(ApplicationRoles.Manager);
            if (_userId is null)
            {
                _order = null;
                _error = "Не удалось определить пользователя.";
                return;
            }
            _order = await Orders.GetOrderAsync(Id, _userId, CancellationToken.None);
            _sourceHistory = !_canManage || _order is null ? [] : await Orders.GetSourceChangeHistoryAsync(
                Id, CancellationToken.None);
            IReadOnlyList<ReceivingSourceView> sourceViews = _order is null ? [] : await Sources.GetByRefKeysAsync(
                _order.SourceRefKeys
                    .Concat(_sourceHistory.Select(x => x.SourceRefKey))
                    .Distinct().ToArray());
            var activeRefKeys = _order?.SourceRefKeys.ToHashSet() ?? new HashSet<Guid>();
            _sources = sourceViews.Where(x => activeRefKeys.Contains(x.RefKey)).ToArray();
            _sourceNames = sourceViews.ToDictionary(x => x.RefKey,
                x => x.Number ?? "Документ 1С без номера");
            _deletionHistory = _canManage && _order?.Lpns.Any(x =>
                x.Status == LicensePlateNumberStatus.Deleted) == true
                ? await Orders.GetDeletionHistoryAsync(Id, CancellationToken.None)
                : new Dictionary<Guid, IReadOnlyList<ReceivingLpnReversalHistoryView>>();
            _userNames = _order is null
                ? new Dictionary<string, string>()
                : await UserNames.GetDisplayNamesAsync(_order.Cycles.Select(x => x.CompletedBy)
                    .Concat(_order.Lpns.SelectMany(lpn => lpn.Putaways.Select(x => x.CreatedBy)))
                    .Concat(_deletionHistory.Values.SelectMany(history =>
                        history.Select(x => x.ConfirmedBy))));
        }
        catch (Exception exception)
        {
            _error = exception.Message;
        }
        finally { _loading = false; }
    }

    private void ToggleLpnDetails(Guid lpnId) =>
        _expandedLpnId = _expandedLpnId == lpnId ? null : lpnId;

    private IReadOnlyList<ReceivingWorkLpnView> SortedLpns =>
        _order?.Lpns.OrderBy(lpn => lpn.Items.Count == 0)
            .ThenBy(lpn => lpn.Items.FirstOrDefault() is { } item
                ? item.SkuName ?? item.SkuId.ToString() : string.Empty,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(lpn => lpn.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    private async Task OpenCorrectionAsync(ReceivingWorkLpnView lpn)
    {
        if (!_canManage || _busy || _userId is null || lpn.Status != LicensePlateNumberStatus.Closed
            || lpn.Putaways.Count != 0 || lpn.Items.Count != 1)
            return;
        _busy = true;
        _error = null;
        try
        {
            var parameters = new DialogParameters<ReceivingLpnCorrectionDialog>
            {
                { x => x.OrderId, Id },
                { x => x.LpnId, lpn.Id },
                { x => x.CurrentQuantity, lpn.Items.Single().Quantity },
                { x => x.UserId, _userId }
            };
            var dialog = await DialogService.ShowAsync<ReceivingLpnCorrectionDialog>(
                $"Исправить LPN {lpn.Code}", parameters,
                new DialogOptions { BackdropClick = false, CloseOnEscapeKey = false });
            var result = await dialog.Result;
            if (result is { Canceled: false })
                await ReloadAsync();
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private async Task CloseLpnAsync(ReceivingWorkLpnView lpn)
    {
        if (!_canManage || _busy || _userId is null
            || _order is not { Status: ReceivingOrderStatus.InReceiving } order
            || lpn.Status != LicensePlateNumberStatus.Open || lpn.Items.Count != 1
            || lpn.Items[0].Quantity <= 0) return;

        var item = lpn.Items[0];
        var orderItem = order.Items.FirstOrDefault(x => x.SkuId == item.SkuId);
        if (orderItem?.TracksExpirationDate == true && item.ExpirationDate is null)
        {
            _error = $"Для закрытия {lpn.Code} укажите срок годности в мобильном приложении.";
            return;
        }

        var expired = item.ExpirationDate is DateOnly date
            && date < DateOnly.FromDateTime(DateTime.UtcNow);
        var name = item.SkuName ?? item.SkuCode ?? item.SkuId.ToString();
        var message = $"LPN {lpn.Code}. SKU: {name}. Количество: {Format(item.Quantity)} {item.UnitOfMeasure ?? "ед."}. "
            + $"План по ордеру: {Format(orderItem?.PlanQuantity)}. "
            + $"Срок годности: {item.ExpirationDate?.ToString("dd.MM.yyyy") ?? "не указан"}. "
            + "Закрытие проведёт приход. Менеджер подтверждает приёмку сверх плана или без известного плана."
            + (expired ? " Срок годности прошёл; подтвердите закрытие с просроченным товаром." : string.Empty);

        _busy = true;
        _error = null;
        try
        {
            var confirmed = await DialogService.ShowMessageBoxAsync("Закрыть LPN", message,
                yesText: "Закрыть и провести", cancelText: "Назад");
            if (confirmed != true) return;

            if (_pendingClose is null || _pendingClose.Value.LpnId != lpn.Id
                || _pendingClose.Value.Revision != lpn.Revision
                || _pendingClose.Value.ConfirmExpiredDate != expired)
                _pendingClose = (lpn.Id, lpn.Revision, expired, Guid.NewGuid());
            var result = await CloseLpn.ExecuteAsync(new CloseReceivingLpnCommand(
                _pendingClose.Value.RequestId, Id, lpn.Id, _userId,
                ConfirmExpiredDate: expired, ExpectedLpnRevision: lpn.Revision));
            if (!result.IsSuccess)
            {
                _pendingClose = null;
                if (result.Error!.Type == OperationErrorType.Conflict)
                    await ReloadAsync();
                _error = result.Error!.Message;
                return;
            }

            _pendingClose = null;
            await ReloadAsync();
            if (_error is null)
                _lifecycleMessage = result.Value!.SourceCheckSucceeded
                    ? $"LPN {lpn.Code} закрыт, приход проведён."
                    : $"LPN {lpn.Code} закрыт по последнему плану; свежая сверка 1С не удалась.";
        }
        catch (Exception exception)
        {
            _error = $"Ответ не подтверждён. Повторите закрытие с тем же LPN. {exception.Message}";
        }
        finally { _busy = false; }
    }

    private async Task OpenDeletionAsync(ReceivingWorkLpnView lpn)
    {
        if (!_canManage || _busy || _userId is null || _order is null
            || lpn.Status != LicensePlateNumberStatus.Closed)
            return;
        _busy = true;
        _error = null;
        try
        {
            var parameters = new DialogParameters<ReceivingLpnDeletionDialog>
            {
                { x => x.OrderId, Id },
                { x => x.LpnId, lpn.Id },
                { x => x.LpnCode, lpn.Code },
                { x => x.UserId, _userId },
                { x => x.OrderItems, _order.Items }
            };
            var dialog = await DialogService.ShowAsync<ReceivingLpnDeletionDialog>(
                $"Удалить LPN {lpn.Code}", parameters,
                new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true,
                    BackdropClick = false, CloseOnEscapeKey = false });
            var result = await dialog.Result;
            if (result is { Canceled: false })
                await ReloadAsync();
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private static string Format(decimal? quantity) =>
        quantity?.ToString("0.###") ?? "неизвестно";

    private string SkuName(Guid skuId) =>
        _order?.Items.FirstOrDefault(x => x.SkuId == skuId)?.SkuName ?? skuId.ToString();

    private string UserName(string? userId) => string.IsNullOrWhiteSpace(userId)
        ? "—" : _userNames.TryGetValue(userId, out var name) && name != userId
            ? name : "Пользователь не найден";

    private static string StageTitle(ReceivingWorkCycleView cycle) =>
        $"Приёмка {cycle.Number} · " + (cycle.CompletedAtUtc is null
            ? "Не завершена" : $"Завершена {cycle.CompletedAtUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm}");

    private IReadOnlyList<ReceivingWorkCycleItemView> SortedCycleItems(ReceivingWorkCycleView cycle) =>
        cycle.Items.OrderBy(item => SkuName(item.SkuId), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.SkuId).ToArray();

}
