using IronForge.Shared.Subscriptions;
using IronForge.Application.Entities;

namespace IronForge.Application.Agents.Runtime;

/// <summary>
/// An agent every new tenant starts with: a code definition plus the
/// display data stored with the tenant's copy.
/// </summary>
public sealed class AgentTemplate
{
    public required AgentDefinition Definition { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    /// <summary>
    /// Bump when the template changes (instructions, tools, limits). The next
    /// template rollout updates every managed copy with a lower version.
    /// </summary>
    public required int Version { get; init; }

    public string TemplateId => Definition.AgentId;

    /// <summary>
    /// The template's model profile when the tenant may use it, otherwise the
    /// tenant's default (first allowed) profile.
    /// </summary>
    public string ChooseModelProfile(IReadOnlyList<string> allowedProfiles) =>
        allowedProfiles.Count == 0 || allowedProfiles.Contains(Definition.ModelProfile)
            ? Definition.ModelProfile
            : allowedProfiles[0];

    /// <summary>
    /// Subscription modules of the template's tools. The template is only
    /// installed for tenants that have all of them (checked by a unit test
    /// against the tools' descriptors).
    /// </summary>
    public required IReadOnlyList<string> Modules { get; init; }

    /// <summary>
    /// Builds the tenant's managed copy of the agent with its tool bindings
    /// (which define its permissions), ready to be added in one save.
    /// </summary>
    public Agent CreateAgent(int tenantId, DateTime nowUtc)
    {
        var agent = new Agent
        {
            TenantId = tenantId,
            AgentId = Definition.AgentId,
            IsActive = true,
            CreatedAtUtc = nowUtc
        };

        ApplyTo(agent, nowUtc);

        return agent;
    }

    /// <summary>
    /// Brings a managed agent to this template version. Tenant choices are
    /// kept: active state and allowed roles. Tool bindings are replaced.
    /// </summary>
    public void ApplyTo(Agent agent, DateTime nowUtc)
    {
        agent.Name = Name;
        agent.Description = Description;
        agent.Version = Definition.Version;
        agent.Instructions = Definition.Instructions;
        agent.MaximumIterationsPerRequest = Definition.MaximumIterationsPerRequest;
        agent.AllowMultipleToolCalls = Definition.AllowMultipleToolCalls;
        agent.ApprovalLifetimeMinutes = (int)Definition.ApprovalLifetime.TotalMinutes;
        agent.ModelProfile = Definition.ModelProfile;
        agent.TemplateId = TemplateId;
        agent.TemplateVersion = Version;
        agent.UpdatedAtUtc = nowUtc;

        agent.ToolBindings.Clear();

        foreach (var tool in Definition.Tools)
        {
            agent.ToolBindings.Add(new AgentToolBindingEntity
            {
                ToolName = tool.ToolName,
                RequiresApproval = tool.RequiresApproval,
                RiskLevel = tool.RiskLevel?.ToString(),
                MaximumAmount = tool.MaximumAmount
            });
        }
    }
}

public static class AgentTemplates
{
    public static readonly AgentTemplate ProductAgent =
        new()
        {
            Definition = AgentDefinitions.ProductAgent,
            Name = "Product Agent",
            Description = "Agent for product management operations.",
            Version = 1,
            Modules = [PlatformModules.Products],
        };

    public static readonly AgentTemplate InsightsAgent =
        new()
        {
            Definition = AgentDefinitions.InsightsAgent,
            Name = "Insights Agent",
            Description = "Read-only analyst for the product catalogue and recent activity.",
            Version = 1,
            Modules = [PlatformModules.Products, PlatformModules.Audit],
        };

    /// <summary>Installed into every newly provisioned tenant.</summary>
    public static readonly IReadOnlyList<AgentTemplate> Default =
    [
        ProductAgent,
        InsightsAgent
    ];
}
