using IronForge.Application.Agents.Authorization;
using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Auth.Models;
using IronForge.Application.Auth.Services;
using IronForge.Application.Configurations;
using IronForge.Application.Execution;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace IronForge.Application.Agents.Delegations;

public class AgentDelegationCredentialService
    : IAgentDelegationCredentialService
{
    private readonly IAgentDelegationService _delegationService;
    private readonly JwtSettings _jwtSettings;
    private readonly AgentCredentialSettings _credentialSettings;
    private readonly IUserAuthorizationService _userAuthorization;
    private readonly IAuditService _auditService;
    private readonly IExecutionContextAccessor _executionContextAccessor;
    private readonly IAgentMcpClientAuthorizationService _agentMcpClientAuthorizationService;

    public AgentDelegationCredentialService(
        IAgentDelegationService delegationService,
        IOptions<JwtSettings> jwtOptions,
        IOptions<AgentCredentialSettings> credentialOptions,
        IUserAuthorizationService userAuthorization,
        IAuditService auditService,
        IExecutionContextAccessor executionContextAccessor,
        IAgentMcpClientAuthorizationService agentMcpClientAuthorizationService)
    {
        _delegationService = delegationService;
        _jwtSettings = jwtOptions.Value;
        _credentialSettings = credentialOptions.Value;
        _userAuthorization = userAuthorization;
        _auditService = auditService;
        _executionContextAccessor = executionContextAccessor;
        _agentMcpClientAuthorizationService = agentMcpClientAuthorizationService;
    }

    public async Task<string> CreateCredentialAsync(
        Guid delegationId,
        CancellationToken cancellationToken = default)
    {
        var context = _executionContextAccessor.Current;
        var delegation =
            await _delegationService.GetForCurrentUserAsync(
                delegationId,
                cancellationToken);

        if (delegation.Revoked)
        {
            throw new UnauthorizedAccessException(
                "Delegation has been revoked.");
        }

        if (delegation.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new UnauthorizedAccessException(
                "Delegation has expired.");
        }
       
        var clientAuthorized =
           await _agentMcpClientAuthorizationService.IsAuthorizedAsync(
               delegation.AgentId,
               delegation.ClientId,
               cancellationToken);

        if (!clientAuthorized)
        {
            throw new UnauthorizedAccessException(
                "The MCP client is no longer authorized for this agent.");
        }

        var userPermissions =
            _userAuthorization
                .GetPermissions()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var scope in delegation.Scopes)
        {
            if (!userPermissions.Contains(scope))
            {
                throw new UnauthorizedAccessException(
                    $"User no longer has permission '{scope}'.");
            }
        }

        if (delegation.Scopes.Count == 0)
        {
            throw new UnauthorizedAccessException(
                "Delegation contains no scopes.");
        }

        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(
            _credentialSettings.ExpirationMinutes);

        // The credential can never outlive its delegation.
        if (expiresAt > delegation.ExpiresAtUtc)
        {
            expiresAt = delegation.ExpiresAtUtc;
        }

        if (expiresAt <= now)
        {
            throw new UnauthorizedAccessException(
                "Delegation does not have enough remaining lifetime to issue a credential.");
        }

        var claims = new List<Claim>
        {
            new(
                JwtRegisteredClaimNames.Sub,
                delegation.UserId.ToString()),

            new(
                CustomClaimTypes.AgentId,
                delegation.AgentId),

            new(
                CustomClaimTypes.ClientId,
                delegation.ClientId),

            new(
                CustomClaimTypes.DelegationId,
                delegation.DelegationId.ToString()),

            new(CustomClaimTypes.TenantId,
                context?.TenantId?.ToString() ?? ""),

            new(
                CustomClaimTypes.Scope,
                string.Join(" ", delegation.Scopes)),

            new(
                JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString("N"))
        };

        // IMPORTANT: only delegated scopes become permission claims.
        // Never copy the user's complete permission set into the credential.
        foreach (var scope in delegation.Scopes)
        {
            claims.Add(new Claim(
                CustomClaimTypes.Permission,
                scope));
        }

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_jwtSettings.Key));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _credentialSettings.Issuer,
            audience: _credentialSettings.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler()
            .WriteToken(token);

        await _auditService.RecordAsync(
            AuditCategory.Delegation,
            "DelegationCredentialIssued",
            AuditOutcome.Succeeded,
            actorType: AuditActorType.User,
            resourceType: "Delegation",
            resourceId: delegation.DelegationId.ToString());

        return accessToken;
    }
}