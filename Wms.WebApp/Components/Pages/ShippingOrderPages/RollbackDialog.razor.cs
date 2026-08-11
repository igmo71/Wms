using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Wms.WebApp.Components.Pages.ShippingOrderPages;

public partial class RollbackDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    private string? _reason;

    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(_reason))
            return;

        MudDialog.Close(DialogResult.Ok(_reason.Trim()));
    }

    private void Cancel() => MudDialog.Cancel();
}
