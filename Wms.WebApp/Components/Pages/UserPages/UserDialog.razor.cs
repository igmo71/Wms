using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Wms.Application.Users;
using Wms.Application.Warehouses;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.UserPages;

public partial class UserDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter]
    public ApplicationUserListItem? User { get; set; }

    [Inject]
    private ApplicationUserManagementService UserManagementService { get; set; } = null!;

    [Inject]
    private WarehouseService WarehouseService { get; set; } = null!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    private string _displayName = string.Empty;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private IReadOnlyCollection<string> _roles = [ApplicationRoles.Operator];
    private IReadOnlyList<Warehouse> _warehouses = [];
    private Guid? _defaultWarehouseId;
    private bool _warehousesLoaded;
    private bool _isBlocked;
    private bool _isSaving;
    private string? _errorMessage;

    private bool IsSaveDisabled => _isSaving
        || !_warehousesLoaded
        || string.IsNullOrWhiteSpace(_displayName)
        || string.IsNullOrWhiteSpace(_email)
        || !_roles.Any()
        || (User is null && string.IsNullOrWhiteSpace(_password));

    protected override async Task OnInitializedAsync()
    {
        if (User is not null)
        {
            _displayName = User.DisplayName;
            _email = User.Email;
            _roles = User.Roles.ToArray();
            _isBlocked = User.IsBlocked;
            _defaultWarehouseId = User.DefaultWarehouseId;
        }

        try
        {
            var result = await WarehouseService.ListAsync(new ListQuery
            {
                SortBy = "Name",
                Take = int.MaxValue
            });
            _warehouses = result.Items;
            _warehousesLoaded = true;
        }
        catch
        {
            _errorMessage = "Не удалось загрузить склады.";
        }
    }

    private async Task SaveAsync()
    {
        if (IsSaveDisabled)
            return;

        _isSaving = true;
        _errorMessage = null;

        try
        {
            var result = User is null
                ? await UserManagementService.CreateAsync(new CreateApplicationUserCommand
                {
                    Email = _email,
                    DisplayName = _displayName,
                    DefaultWarehouseId = _defaultWarehouseId,
                    Password = _password,
                    Roles = _roles.ToArray()
                })
                : await UpdateAsync();

            if (!result.IsSuccess)
            {
                _errorMessage = result.Error?.Message ?? "Не удалось сохранить пользователя.";
                return;
            }

            MudDialog.Close(DialogResult.Ok(true));
        }
        catch
        {
            _errorMessage = "Не удалось сохранить пользователя.";
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task<Wms.Common.OperationResult> UpdateAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var currentUserId = authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(currentUserId))
            return Wms.Common.OperationError.Invalid("Не удалось определить текущего пользователя.");

        return await UserManagementService.UpdateAsync(new UpdateApplicationUserCommand
        {
            UserId = User!.Id,
            CurrentUserId = currentUserId,
            DisplayName = _displayName,
            DefaultWarehouseId = _defaultWarehouseId,
            Roles = _roles.ToArray(),
            IsBlocked = _isBlocked
        });
    }

    private void Cancel() => MudDialog.Cancel();
}
