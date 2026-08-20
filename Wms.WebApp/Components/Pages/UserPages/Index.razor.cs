using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Users;
using Wms.Data;

namespace Wms.WebApp.Components.Pages.UserPages;

public partial class Index
{
    [Inject]
    private ApplicationUserManagementService UserManagementService { get; set; } = null!;

    [Inject]
    private IDialogService DialogService { get; set; } = null!;

    private MudDataGrid<ApplicationUserListItem> _dataGrid = null!;
    private string? _searchString;
    private string? _errorMessage;

    private async Task<GridData<ApplicationUserListItem>> LoadServerDataAsync(
        GridState<ApplicationUserListItem> state,
        CancellationToken ct)
    {
        var sortDefinition = state.SortDefinitions.FirstOrDefault();
        var result = await UserManagementService.ListAsync(new ApplicationUserListQuery
        {
            SearchString = _searchString,
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? false,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        }, ct);

        return new GridData<ApplicationUserListItem>
        {
            Items = result.Items,
            TotalItems = result.TotalItems
        };
    }

    private Task OnSearchChangedAsync(string? value)
    {
        _searchString = value;
        return _dataGrid.ReloadServerData();
    }

    private Task CreateUserAsync() => ShowUserDialogAsync(null);

    private Task EditUserAsync(ApplicationUserListItem user) => ShowUserDialogAsync(user);

    private async Task ShowUserDialogAsync(ApplicationUserListItem? user)
    {
        _errorMessage = null;
        var parameters = new DialogParameters<UserDialog>
        {
            { x => x.User, user }
        };

        var dialog = await DialogService.ShowAsync<UserDialog>(
            user is null ? "Создать пользователя" : "Редактировать пользователя",
            parameters,
            new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
        var result = await dialog.Result;

        if (result is not null && !result.Canceled)
            await _dataGrid.ReloadServerData();
    }

    private static string GetRoleName(string role) => role switch
    {
        ApplicationRoles.Administrator => "Администратор",
        ApplicationRoles.Operator => "Оператор",
        _ => role
    };
}
