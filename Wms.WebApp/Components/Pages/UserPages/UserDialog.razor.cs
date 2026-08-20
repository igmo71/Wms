using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Wms.Application.Users;
using Wms.Data;

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
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    private string _displayName = string.Empty;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _role = ApplicationRoles.Operator;
    private bool _isBlocked;
    private bool _isSaving;
    private string? _errorMessage;

    private bool IsSaveDisabled => _isSaving
        || string.IsNullOrWhiteSpace(_displayName)
        || string.IsNullOrWhiteSpace(_email)
        || string.IsNullOrWhiteSpace(_role)
        || (User is null && string.IsNullOrWhiteSpace(_password));

    protected override void OnInitialized()
    {
        if (User is null)
            return;

        _displayName = User.DisplayName;
        _email = User.Email;
        _role = User.Role;
        _isBlocked = User.IsBlocked;
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
                    Password = _password,
                    Role = _role
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
            Role = _role,
            IsBlocked = _isBlocked
        });
    }

    private void Cancel() => MudDialog.Cancel();
}
