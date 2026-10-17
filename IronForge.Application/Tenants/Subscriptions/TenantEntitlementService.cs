using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Persistence;

namespace IronForge.Application.Tenants.Subscriptions;

/// <summary>
/// What the current tenant's subscription enables. The single place that
/// decides whether a module, and so its tools, may be used.
/// </summary>
public interface ITenantEntitlementService
{
    /// <summary>Enabled modules of the current tenant; empty without a tenant.</summary>
    Task<IReadOnlySet<string>> GetEnabledModulesAsync(
        CancellationToken cancellationToken = default);

    Task<bool> IsModuleEnabledAsync(
        string module,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Model profiles the current tenant's agents may use, default first;
    /// empty without a tenant or for an unknown plan without add-ons.
    /// </summary>
    Task<IReadOnlyList<string>> GetAllowedModelProfilesAsync(
        CancellationToken cancellationToken = default);
}

public sealed class TenantEntitlementService : ITenantEntitlementService
{
    private static readonly IReadOnlySet<string> None =
        new HashSet<string>(StringComparer.Ordinal);

    private readonly IRepository<Tenant> _tenants;
    private readonly IExecutionContextAccessor _executionContext;

    // Scoped service: one lookup per request.
    private Tenant? _tenant;
    private bool _loaded;

    public TenantEntitlementService(
        IRepository<Tenant> tenants,
        IExecutionContextAccessor executionContext)
    {
        _tenants = tenants;
        _executionContext = executionContext;
    }

    public async Task<IReadOnlySet<string>> GetEnabledModulesAsync(
        CancellationToken cancellationToken = default)
    {
        var tenant = await GetTenantAsync(cancellationToken);

        return tenant?.GetEnabledModules() ?? None;
    }

    public async Task<IReadOnlyList<string>> GetAllowedModelProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        var tenant = await GetTenantAsync(cancellationToken);

        return tenant?.GetAllowedModelProfiles() ?? [];
    }

    private async Task<Tenant?> GetTenantAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
            return _tenant;

        var tenantId = _executionContext.Current?.TenantId;

        if (tenantId is not null)
        {
            _tenant = await _tenants.FirstOrDefaultAsync(
                x => x.Id == tenantId.Value,
                cancellationToken);
        }

        _loaded = true;

        return _tenant;
    }

    public async Task<bool> IsModuleEnabledAsync(
        string module,
        CancellationToken cancellationToken = default)
    {
        var modules = await GetEnabledModulesAsync(cancellationToken);

        return modules.Contains(module);
    }
}
