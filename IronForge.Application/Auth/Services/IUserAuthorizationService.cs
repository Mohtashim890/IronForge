namespace IronForge.Application.Auth.Services;

public interface IUserAuthorizationService
{
    bool HasPermission(string permission);
    IReadOnlyCollection<string> GetPermissions();
}