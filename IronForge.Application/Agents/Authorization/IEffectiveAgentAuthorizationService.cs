namespace IronForge.Application.Agents.Authorization;

public interface IEffectiveAgentAuthorizationService
{
    Task<EffectivePermissionResult> AuthorizeAsync(
        string permission,
        CancellationToken cancellationToken = default);
}