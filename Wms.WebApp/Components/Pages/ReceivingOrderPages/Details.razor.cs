using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Wms.Application.Receiving;
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
    [Inject] private ReopenReceivingOrder ReopenOrder { get; set; } = null!;
    [Inject] private CorrectReceivingLpnQuantity CorrectLpn { get; set; } = null!;
    [Inject] private PreviewReceivingLpnDeletion PreviewDeletion { get; set; } = null!;
    [Inject] private DeleteReceivingLpn DeleteLpn { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;

    private ReceivingWorkOrderView? _order;
    private IReadOnlyList<ReceivingSourceView> _sources = [];
    private IReadOnlyList<ReceivingSourceChangeHistoryView> _sourceHistory = [];
    private IReadOnlyDictionary<Guid, string> _sourceNames = new Dictionary<Guid, string>();
    private IReadOnlyDictionary<Guid, IReadOnlyList<ReceivingLpnReversalHistoryView>>
        _deletionHistory = new Dictionary<Guid, IReadOnlyList<ReceivingLpnReversalHistoryView>>();
    private string? _userId;
    private string? _error;
    private string? _lifecycleMessage;
    private string? _sourceChangeMessage;
    private ReceivingOrderRefreshResult? _refreshResult;
    private Guid? _pendingCompleteRequestId;
    private Guid? _pendingReopenRequestId;
    private (Guid LpnId, decimal Quantity, Guid RequestId)? _pendingCorrection;
    private (Guid LpnId, string Token, bool ConfirmNegative, Guid RequestId)? _pendingDelete;
    private Guid? _correctionLpnId;
    private decimal? _correctionQuantity;
    private Guid? _deletionLpnId;
    private ReceivingLpnDeletionPreview? _deletionPreview;
    private bool _confirmNegative;
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
        _correctionLpnId = null;
        _pendingCorrection = null;
        _deletionLpnId = null;
        _deletionPreview = null;
        _pendingDelete = null;
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
                "Изменить состав источников", parameters,
                new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true });
            var result = await dialog.Result;
            if (result is null || result.Canceled
                || result.Data is not ReceivingSourceChangeKind kind)
                return;
            await ReloadAsync();
            if (_error is null)
                _sourceChangeMessage = kind == ReceivingSourceChangeKind.Attach
                    ? "Источник добавлен. Текущий план пересчитан; факт сохранён."
                    : "Источник откреплён. Текущий план пересчитан; факт сохранён.";
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
                _lifecycleMessage = "Ордер завершён. План, факт и версии источников сохранены в снимке цикла.";
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
                _lifecycleMessage = "Открыт новый цикл. Первый участник заново выбирает место приёмки.";
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
                _order.SourceRefKeys.Concat(_sourceHistory.Select(x => x.SourceRefKey))
                    .Distinct().ToArray());
            var activeRefKeys = _order?.SourceRefKeys.ToHashSet() ?? new HashSet<Guid>();
            _sources = sourceViews.Where(x => activeRefKeys.Contains(x.RefKey)).ToArray();
            _sourceNames = sourceViews.ToDictionary(x => x.RefKey,
                x => x.Number ?? x.RefKey.ToString());
            _deletionHistory = _canManage && _order?.Lpns.Any(x =>
                x.Status == LicensePlateNumberStatus.Deleted) == true
                ? await Orders.GetDeletionHistoryAsync(Id, CancellationToken.None)
                : new Dictionary<Guid, IReadOnlyList<ReceivingLpnReversalHistoryView>>();
        }
        catch (Exception exception)
        {
            _error = exception.Message;
        }
        finally { _loading = false; }
    }

    private void PrepareCorrection(ReceivingWorkLpnView lpn)
    {
        if (!_canManage || _busy || lpn.Status != LicensePlateNumberStatus.Closed
            || lpn.Putaways.Count != 0 || lpn.Items.Count != 1)
            return;

        _correctionLpnId = lpn.Id;
        _correctionQuantity = lpn.Items.Single().Quantity;
        _pendingCorrection = null;
        _deletionLpnId = null;
        _deletionPreview = null;
        _pendingDelete = null;
    }

    private async Task CorrectLpnAsync()
    {
        if (!_canManage || _busy || _userId is null
            || _correctionLpnId is not Guid lpnId || _correctionQuantity is not decimal quantity)
            return;
        _busy = true;
        _error = null;
        try
        {
            if (_pendingCorrection is not { } pending || pending.LpnId != lpnId
                || pending.Quantity != quantity)
                _pendingCorrection = (lpnId, quantity, Guid.NewGuid());
            var result = await CorrectLpn.ExecuteAsync(new CorrectReceivingLpnQuantityCommand(
                _pendingCorrection.Value.RequestId, Id, lpnId, quantity, _userId));
            if (result.IsSuccess)
            {
                _pendingCorrection = null;
                _correctionLpnId = null;
                await ReloadAsync();
            }
            else
            {
                _pendingCorrection = null;
                _error = result.Error!.Message;
            }
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private async Task PreviewDeletionAsync(Guid lpnId)
    {
        if (!_canManage || _busy || _userId is null)
            return;
        _busy = true;
        _error = null;
        _deletionLpnId = lpnId;
        _deletionPreview = null;
        _confirmNegative = false;
        _pendingDelete = null;
        _correctionLpnId = null;
        _pendingCorrection = null;
        try
        {
            var result = await PreviewDeletion.ExecuteAsync(Id, lpnId, _userId);
            if (result.IsSuccess)
                _deletionPreview = result.Value;
            else
                _error = result.Error!.Message;
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private async Task DeleteLpnAsync()
    {
        if (!_canManage || _busy || _userId is null || _deletionPreview is not { } preview
            || _deletionLpnId != preview.LpnId
            || preview.RequiresNegativeConfirmation && !_confirmNegative)
            return;
        _busy = true;
        _error = null;
        try
        {
            if (_pendingDelete is not { } pending || pending.LpnId != preview.LpnId
                || pending.Token != preview.SnapshotToken
                || pending.ConfirmNegative != _confirmNegative)
                _pendingDelete = (preview.LpnId, preview.SnapshotToken,
                    _confirmNegative, Guid.NewGuid());
            var result = await DeleteLpn.ExecuteAsync(new DeleteReceivingLpnCommand(
                _pendingDelete.Value.RequestId, Id, preview.LpnId,
                preview.SnapshotToken, _confirmNegative, _userId));
            if (result.IsSuccess)
            {
                _pendingDelete = null;
                _deletionLpnId = null;
                _deletionPreview = null;
                await ReloadAsync();
            }
            else
            {
                _pendingDelete = null;
                _error = result.Error!.Message;
                if (result.Error.Type == OperationErrorType.Conflict)
                {
                    var refreshed = await PreviewDeletion.ExecuteAsync(Id, preview.LpnId, _userId);
                    _deletionPreview = refreshed.IsSuccess ? refreshed.Value : null;
                    _confirmNegative = false;
                }
            }
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private static string Format(decimal? quantity) =>
        quantity?.ToString("0.###") ?? "неизвестно";

    private string SkuName(Guid skuId) =>
        _order?.Items.FirstOrDefault(x => x.SkuId == skuId)?.SkuName ?? skuId.ToString();
}
