using Wms.Common;

namespace Wms.Application.Users;

public sealed class ApplicationUserListQuery : ListQuery
{
}

public sealed class ApplicationUserListItem
{
    public required string Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Role { get; init; }
    public bool IsBlocked { get; init; }
}

public sealed class CreateApplicationUserCommand
{
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Password { get; init; }
    public required string Role { get; init; }
}

public sealed class UpdateApplicationUserCommand
{
    public required string UserId { get; init; }
    public required string CurrentUserId { get; init; }
    public required string DisplayName { get; init; }
    public required string Role { get; init; }
    public bool IsBlocked { get; init; }
}
