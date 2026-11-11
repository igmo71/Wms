using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Picking;

namespace Wms.WebApp.Components.Pages.PickingAssemblyPages;

public partial class PickingPlanDialog
{
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;
    [Parameter, EditorRequired] public PickingAssemblyView Assembly { get; set; } = null!;
    [Parameter, EditorRequired] public PickingClosureSourceCheck Source { get; set; } = null!;
    [Parameter, EditorRequired] public string UserId { get; set; } = null!;
    [Inject] private ChangePickingPlan ChangePlan { get; set; } = null!;

    private sealed class PlanRow
    {
        public Guid SkuId { get; init; }
        public string Name { get; init; } = "";
        public decimal SourceQuantity { get; init; }
        public decimal Quantity { get; set; }
        public bool Included { get; set; }
    }

    private List<PlanRow> _rows = [];
    private string? _reason;
    private string? _error;
    private bool _busy;
    private ChangePickingPlanRequest? _pendingRequest;

    protected override void OnInitialized()
    {
        var sourceView = Assembly.Sources.Single(x => x.SourceRefKey == Source.ShippingOrderId);
        var names = Assembly.PlanLines.Where(x => x.SourceRefKey == Source.ShippingOrderId)
            .ToDictionary(x => x.SkuId, x => x.SkuName ?? x.SkuCode ?? x.SkuId.ToString());
        foreach (var line in sourceView.CurrentLines)
            names[line.SkuId] = line.SkuName ?? line.SkuCode ?? line.SkuId.ToString();
        _rows = Source.PlanBySku.Keys.Union(Source.CurrentBySku?.Keys ?? Array.Empty<Guid>())
            .Select(id => new PlanRow
            {
                SkuId = id, Name = names.GetValueOrDefault(id, id.ToString()),
                SourceQuantity = Source.CurrentBySku?.GetValueOrDefault(id) ?? 0,
                Quantity = Source.PlanBySku.GetValueOrDefault(id),
                Included = Source.PlanBySku.ContainsKey(id)
            }).OrderBy(x => x.Name).ToList();
    }

    private void Close() => Dialog.Cancel();

    private async Task SaveAsync()
    {
        if (_pendingRequest is null
            && (Source.CurrentSourceJson is null || string.IsNullOrWhiteSpace(_reason)))
        {
            _error = "Нужны актуальные данные 1С и причина изменения.";
            return;
        }
        _busy = true;
        _error = null;
        try
        {
            _pendingRequest ??= new ChangePickingPlanRequest(
                Guid.NewGuid(), Assembly.Id, Source.ShippingOrderId,
                Assembly.Revision, _rows.Where(x => x.Included)
                    .ToDictionary(x => x.SkuId, x => x.Quantity),
                Source.CurrentSourceJson!, _reason!.Trim(), UserId);
            var result = await ChangePlan.ExecuteAsync(_pendingRequest);
            if (!result.IsSuccess)
            {
                _pendingRequest = null;
                _error = result.Error!.Message;
                return;
            }
            _pendingRequest = null;
            Dialog.Close(DialogResult.Ok(true));
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }
}
