using IronForge.Application.Execution;

namespace IronForge.Application.Auth.Services;

public class UserAuthorizationService
    : IUserAuthorizationService
{
    private readonly IExecutionContextAccessor _executionContext;

    public UserAuthorizationService(
        IExecutionContextAccessor executionContext)
    {
        _executionContext = executionContext;
    }

    public bool HasPermission(string permission)
    {
        var context = _executionContext.Current;

        if (context == null)
            return false;

        return context.Actor.UserPermissions
            .Contains(permission);
    }
    public IReadOnlyCollection<string> GetPermissions()
    {
        var context = _executionContext.Current;

        if (context?.Actor?.UserPermissions == null)
        {
            return Array.Empty<string>();
        }

        return context.Actor.UserPermissions;
    }
}