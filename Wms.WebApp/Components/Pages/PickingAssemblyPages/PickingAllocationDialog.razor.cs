using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Picking;

namespace Wms.WebApp.Components.Pages.PickingAssemblyPages;

public partial class PickingAllocationDialog
{
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;
    [Parameter, EditorRequired] public PickingAssemblyView Assembly { get; set; } = null!;
    [Parameter, EditorRequired] public PickingWorkView Work { get; set; } = null!;
    [Parameter, EditorRequired] public IReadOnlyList<Guid> UnresolvedSkuIds { get; set; } = [];

    private sealed class AllocationRow
    {
        public Guid SkuId { get; init; }
        public string SkuName { get; init; } = "";
        public Guid SourceId { get; init; }
        public string SourceName { get; init; } = "";
        public decimal Plan { get; init; }
        public decimal Fact { get; init; }
        public decimal Quantity { get; set; }
    }

    private List<AllocationRow> _rows = [];
    private string? _error;

    protected override void OnInitialized()
    {
        var needed = UnresolvedSkuIds.ToHashSet();
        _rows = Assembly.PlanLines.Where(x => needed.Contains(x.SkuId))
            .Select(x => new AllocationRow
            {
                SkuId = x.SkuId,
                SkuName = x.SkuName ?? x.SkuCode ?? x.SkuId.ToString(),
                SourceId = x.SourceRefKey,
                SourceName = Assembly.Sources.First(s => s.SourceRefKey == x.SourceRefKey).Number
                    ?? x.SourceRefKey.ToString(),
                Plan = x.Quantity,
                Fact = Work.Skus.FirstOrDefault(s => s.SkuId == x.SkuId)?.ConfirmedQuantity ?? 0
            }).OrderBy(x => x.SkuName).ThenBy(x => x.SourceName).ToList();
    }

    private void Close() => Dialog.Cancel();

    private void Accept()
    {
        foreach (var group in _rows.GroupBy(x => x.SkuId))
        {
            if (group.Any(x => x.Quantity < 0 || x.Quantity > x.Plan)
                || group.Sum(x => x.Quantity) != group.First().Fact)
            {
                _error = $"Проверьте распределение SKU {group.First().SkuName}: итог должен совпадать с фактом и не превышать план каждого документа.";
                return;
            }
        }
        Dialog.Close(DialogResult.Ok(_rows.Select(x => new PickingAllocationInput(
            x.SourceId, x.SkuId, x.Quantity)).ToArray()));
    }
}
