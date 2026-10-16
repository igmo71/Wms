using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
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
    [Inject] private ResynchronizeReceivingOrder RefreshSources { get; set; } = null!;
    [Inject] private CompleteReceivingOrder CompleteOrder { get; set; } = null!;
    [Inject] private ReopenReceivingOrder ReopenOrder { get; set; } = null!;
    [Inject] private SearchReceivingSku SearchSku { get; set; } = null!;
    [Inject] private AddReceivingOrderSku AddSku { get; set; } = null!;
    [Inject] private CloseReceivingLpn CloseLpn { get; set; } = null!;
    [Inject] private CorrectReceivingLpnQuantity CorrectLpn { get; set; } = null!;
    [Inject] private PreviewReceivingLpnDeletion PreviewDeletion { get; set; } = null!;
    [Inject] private DeleteReceivingLpn DeleteLpn { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;

    private ReceivingWorkOrderView? _order;
    private IReadOnlyList<ReceivingSourceView> _sources = [];
    private IReadOnlyList<ReceivingSkuCandidate> _candidates = [];
    private IReadOnlyDictionary<Guid, IReadOnlyList<ReceivingLpnReversalHistoryView>>
        _deletionHistory = new Dictionary<Guid, IReadOnlyList<ReceivingLpnReversalHistoryView>>();
    private string? _userId;
    private string? _error;
    private string? _lifecycleMessage;
    private string? _skuSearch;
    private string? _managerReason;
    private CloseReceivingLpnResult? _closeResult;
    private ReceivingOrderRefreshResult? _refreshResult;
    private Guid? _pendingCompleteRequestId;
    private Guid? _pendingReopenRequestId;
    private (Guid SkuId, Guid RequestId)? _pendingAdd;
    private (Guid LpnId, Guid RequestId, bool ConfirmExpired, string? Reason)? _pendingClose;
    private (Guid LpnId, decimal Quantity, Guid RequestId)? _pendingCorrection;
    private (Guid LpnId, string Token, bool ConfirmNegative, Guid RequestId)? _pendingDelete;
    private Guid? _closeLpnId;
    private Guid? _correctionLpnId;
    private decimal? _correctionQuantity;
    private Guid? _deletionLpnId;
    private ReceivingLpnDeletionPreview? _deletionPreview;
    private bool _confirmExpired;
    private bool _confirmNegative;
    private bool _canManage;
    private bool _loading = true;
    private bool _busy;

    protected override async Task OnParametersSetAsync()
    {
        _pendingCompleteRequestId = null;
        _pendingReopenRequestId = null;
        _lifecycleMessage = null;
        _refreshResult = null;
        _closeLpnId = null;
        _pendingClose = null;
        _pendingAdd = null;
        _correctionLpnId = null;
        _pendingCorrection = null;
        _deletionLpnId = null;
        _deletionPreview = null;
        _pendingDelete = null;
        await ReloadAsync();
    }

    private async Task CompleteOrderAsync()
    {
        if (_busy || _userId is null || _order is null
            || _order.Status != ReceivingOrderStatus.InReceiving
            || !_order.IsParticipant && !_canManage
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
            _sources = _order is null ? [] : await Sources.GetByRefKeysAsync(
                _order.SourceRefKeys);
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

    private async Task SearchSkuAsync()
    {
        if (!_canManage || _busy)
            return;
        _busy = true;
        _error = null;
        try
        {
            var result = await SearchSku.SearchAsync(
                Id, _skuSearch, ReceivingSkuSearchScope.Catalog);
            if (result.IsSuccess)
                _candidates = result.Value!;
            else
                _error = result.Error!.Message;
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private async Task AddSkuAsync(Guid skuId)
    {
        if (!_canManage || _busy || _userId is null)
            return;
        _busy = true;
        _error = null;
        try
        {
            if (_pendingAdd?.SkuId != skuId)
                _pendingAdd = (skuId, Guid.NewGuid());
            var result = await AddSku.ExecuteAsync(new AddReceivingOrderSkuRequest(
                _pendingAdd.Value.RequestId, Id, skuId, _userId));
            if (result.IsSuccess)
            {
                _pendingAdd = null;
                await ReloadAsync();
                _candidates = [];
            }
            else
            {
                _pendingAdd = null;
                _error = result.Error!.Message;
            }
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private void PrepareClose(Guid lpnId)
    {
        _closeLpnId = lpnId;
        _confirmExpired = false;
        _managerReason = null;
        _pendingClose = null;
        _closeResult = null;
        _correctionLpnId = null;
        _pendingCorrection = null;
        _deletionLpnId = null;
        _deletionPreview = null;
        _pendingDelete = null;
    }

    private async Task CloseLpnAsync()
    {
        if (!_canManage || _busy || _userId is null || _closeLpnId is not Guid lpnId)
            return;
        _busy = true;
        _error = null;
        try
        {
            var reason = string.IsNullOrWhiteSpace(_managerReason)
                ? null : _managerReason.Trim();
            if (_pendingClose is not { } pending || pending.LpnId != lpnId
                || pending.ConfirmExpired != _confirmExpired || pending.Reason != reason)
                _pendingClose = (lpnId, Guid.NewGuid(), _confirmExpired, reason);
            var command = new CloseReceivingLpnCommand(
                _pendingClose.Value.RequestId, Id, lpnId, _userId,
                _pendingClose.Value.ConfirmExpired, _pendingClose.Value.Reason);
            var result = await CloseLpn.ExecuteAsync(command);
            if (result.IsSuccess)
            {
                _pendingClose = null;
                _closeResult = result.Value;
                _closeLpnId = null;
                await ReloadAsync();
            }
            else
            {
                _pendingClose = null;
                _error = result.Error!.Message;
            }
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private void PrepareCorrection(ReceivingWorkLpnView lpn)
    {
        _closeLpnId = null;
        _pendingClose = null;
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
        _closeLpnId = null;
        _pendingClose = null;
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
