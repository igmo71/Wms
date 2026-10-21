using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Receiving;
using Wms.Common;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class ReceivingSourceCompositionDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;
    [Parameter, EditorRequired] public Guid OrderId { get; set; }
    [Parameter, EditorRequired] public Guid WarehouseId { get; set; }
    [Parameter, EditorRequired] public string UserId { get; set; } = null!;

    [Inject] private GetReceivingSources Sources { get; set; } = null!;
    [Inject] private IReceivingWorkQueryStore Orders { get; set; } = null!;
    [Inject] private PreviewReceivingOrderSourceChange PreviewChange { get; set; } = null!;
    [Inject] private ChangeReceivingOrderSources ChangeSources { get; set; } = null!;

    private IReadOnlyList<ReceivingSourceView> _freeSources = [];
    private IReadOnlyList<ReceivingSourceView> _linkedSources = [];
    private ReceivingSourceCompositionPreview? _preview;
    private Guid? _pendingRequestId;
    private string? _attachSearch;
    private string? _detachSearch;
    private string? _error;
    private bool _busy;

    private IEnumerable<ReceivingSourceView> MatchingFreeSources
    {
        get
        {
            var term = _attachSearch?.Trim();
            return _freeSources.Where(source => source.WarehouseId == WarehouseId
                && (string.IsNullOrEmpty(term)
                    || source.Number?.Contains(term, StringComparison.OrdinalIgnoreCase) == true))
                .Take(20);
        }
    }

    private IEnumerable<ReceivingSourceView> MatchingLinkedSources
    {
        get
        {
            var term = _detachSearch?.Trim();
            return _linkedSources.Where(source => string.IsNullOrEmpty(term)
                || source.Number?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
                .Take(20);
        }
    }

    protected override async Task OnInitializedAsync()
    {
        _busy = true;
        try { await ReloadSourcesAsync(); }
        finally { _busy = false; }
    }

    private async Task ReloadSourcesAsync()
    {
        try
        {
            var order = await Orders.GetOrderAsync(OrderId, UserId, CancellationToken.None);
            if (order is null)
            {
                _error = "Приходный ордер не найден.";
                _linkedSources = [];
                _freeSources = [];
                return;
            }
            _linkedSources = await Sources.GetByRefKeysAsync(order.SourceRefKeys);
            _freeSources = await Sources.ListUnlinkedAsync();
        }
        catch (Exception exception)
        {
            _error = exception.Message;
        }
    }

    private async Task PreviewAsync(ReceivingSourceChangeKind kind, Guid refKey)
    {
        if (_busy || refKey == Guid.Empty)
            return;
        _busy = true;
        _error = null;
        _preview = null;
        _pendingRequestId = null;
        try
        {
            var result = await PreviewChange.ExecuteAsync(
                new PreviewReceivingOrderSourceChangeRequest(OrderId, refKey, kind, UserId));
            if (!result.IsSuccess)
            {
                _error = result.Error!.Message;
                return;
            }
            _preview = result.Value;
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private async Task ConfirmAsync()
    {
        if (_busy || _preview is not { } preview)
            return;
        _busy = true;
        _error = null;
        try
        {
            _pendingRequestId ??= Guid.NewGuid();
            var result = await ChangeSources.ExecuteAsync(new ChangeReceivingOrderSourcesRequest(
                _pendingRequestId.Value, OrderId, preview.SourceRefKey, UserId,
                preview.Kind, ReceivingSourceChangeChannel.Web, preview.StateToken));
            if (!result.IsSuccess)
            {
                _pendingRequestId = null;
                _preview = null;
                _error = result.Error!.Type == OperationErrorType.Conflict
                    ? $"Состав изменился. Выберите документ заново. {result.Error.Message}"
                    : result.Error.Message;
                await ReloadSourcesAsync();
                return;
            }
            MudDialog.Close(DialogResult.Ok(preview.Kind));
        }
        catch (Exception exception)
        {
            _error = $"Не удалось получить результат. Повторите подтверждение: {exception.Message}";
        }
        finally { _busy = false; }
    }

    private void BackToSelection()
    {
        _preview = null;
        _pendingRequestId = null;
        _error = null;
    }

    private void Cancel() => MudDialog.Cancel();

    private static string PlanText(bool present, decimal? quantity) =>
        !present ? "0 (нет в текущем списке)" : quantity?.ToString("0.###") ?? "Неизвестно";
}
