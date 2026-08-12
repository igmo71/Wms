using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.DeliveryDirectionPages;

public partial class DeliveryDirectionTreeItem
{
    [Parameter, EditorRequired] public DeliveryDirection Direction { get; set; } = null!;
    [Parameter, EditorRequired] public IReadOnlyCollection<DeliveryDirection> Directions { get; set; } = [];

    private IEnumerable<DeliveryDirection> Children => Directions
        .Where(x => x.ParentId == Direction.Id)
        .OrderBy(x => x.Description);

    private string Icon => Direction.IsFolder
        ? Icons.Material.Filled.Folder
        : Icons.Material.Filled.LocalShipping;

    private string Text
    {
        get
        {
            var text = Direction.Description ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(Direction.Comment))
                text += $" — {Direction.Comment}";
            if (Direction.DeletionMark)
                text += " (деактивировано)";

            return text;
        }
    }
}
