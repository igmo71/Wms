using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Inventory;
using Wms.Application.Picking;

namespace Wms.WebApp.Components.Pages.PickingAssemblyPages;

public partial class ShortageResolutionDialog
{
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;
    [Parameter, EditorRequired] public PickingShortageView Shortage { get; set; } = null!;
    [Parameter, EditorRequired] public string UserId { get; set; } = null!;
    [Inject] private ResolveStockShortage Resolver { get; set; } = null!;

    private ResolveStockShortageRequest? _pendingRequest;
    private string? _reason;
    private string? _error;
    private bool _busy;

    private void Close() => Dialog.Cancel();

    private async Task ResolveAsync(bool confirmed)
    {
        if (_pendingRequest is null && string.IsNullOrWhiteSpace(_reason))
        {
            _error = "Укажите причину решения.";
            return;
        }
        _busy = true;
        _error = null;
        try
        {
            _pendingRequest ??= new ResolveStockShortageRequest(Guid.NewGuid(),
                Shortage.Id, confirmed, _reason!.Trim(), UserId);
            var result = await Resolver.ExecuteAsync(_pendingRequest);
            if (!result.IsSuccess)
            {
                _pendingRequest = null;
                _error = result.Error!.Message;
                return;
            }
            _pendingRequest = null;
            Dialog.Close(DialogResult.Ok(true));
        }
        catch (Exception exception)
        {
            _error = $"Ответ на решение не получен: {exception.Message} Повторите ту же операцию.";
        }
        finally { _busy = false; }
    }
}
