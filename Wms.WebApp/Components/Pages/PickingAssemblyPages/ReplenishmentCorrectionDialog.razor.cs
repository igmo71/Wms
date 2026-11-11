using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Replenishment;

namespace Wms.WebApp.Components.Pages.PickingAssemblyPages;

public partial class ReplenishmentCorrectionDialog
{
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;
    [Parameter, EditorRequired] public ReplenishmentTransferView Transfer { get; set; } = null!;
    [Parameter, EditorRequired] public string UserId { get; set; } = null!;
    [Inject] private CorrectReplenishmentTransfer Corrector { get; set; } = null!;

    private CorrectReplenishmentTransferRequest? _pendingRequest;
    private decimal? _correctedQuantity;
    private string? _reason;
    private string? _error;
    private bool _busy;

    protected override void OnInitialized() => _correctedQuantity = Transfer.EffectiveQuantity;

    private void Close() => Dialog.Cancel();

    private async Task CorrectAsync()
    {
        if (_pendingRequest is null && (_correctedQuantity is null
            || string.IsNullOrWhiteSpace(_reason)))
        {
            _error = "Укажите фактически перенесённое количество и причину.";
            return;
        }
        _busy = true;
        _error = null;
        try
        {
            _pendingRequest ??= new CorrectReplenishmentTransferRequest(
                Guid.NewGuid(), Transfer.Id, Transfer.EffectiveQuantity,
                _correctedQuantity!.Value, _reason!.Trim(), UserId);
            var result = await Corrector.ExecuteAsync(_pendingRequest);
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
            _error = $"Ответ на корректировку не получен: {exception.Message} Повторите ту же операцию.";
        }
        finally { _busy = false; }
    }
}
