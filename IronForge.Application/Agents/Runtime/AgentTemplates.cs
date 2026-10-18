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
    /// Modules of extra tools the template also binds. They do not block the
    /// install: a tenant without one of them gets the agent, and the provider
    /// drops those tools until the module is enabled.
    /// </summary>
    public IReadOnlyList<string> OptionalModules { get; init; } = [];

    /// <summary>True when the modules include every required module of the template.</summary>
    public bool IsCoveredBy(IEnumerable<string> enabledModules)
    {
        var enabled = enabledModules.ToHashSet(StringComparer.Ordinal);
        return Modules.All(enabled.Contains);
    }

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
            CreatedAtUtc = nowUtc,
            // The template's audience; the tenant may change it later.
            AllowedRoles = Definition.AllowedRoles.ToList()
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
    public static readonly AgentTemplate FitnessCoach =
        new()
        {
            Definition = AgentDefinitions.FitnessCoach,
            Name = "Fitness Coach",
            Description = "Members' personal AI coach: routines, workout logging, progress and goals.",
            Version = 2,
            Modules = [PlatformModules.GymCoaching, PlatformModules.GymBilling],
        };

    public static readonly AgentTemplate GymAdmin =
        new()
        {
            Definition = AgentDefinitions.GymAdmin,
            Name = "Gym Admin Agent",
            Description = "Staff assistant for members, attendance, billing, announcements and the exercise library.",
            Version = 2,
            Modules = [PlatformModules.GymCore, PlatformModules.GymBilling, PlatformModules.GymEngagement],
            // list_exercises and get_member_routines need coaching; Starter gyms get the agent without them.
            OptionalModules = [PlatformModules.GymCoaching],
        };

    public static readonly AgentTemplate InsightsAgent =
        new()
        {
            Definition = AgentDefinitions.InsightsAgent,
            Name = "Insights Agent",
            Description = "Read-only analyst for the gym's activity log and key numbers.",
            Version = 3,
            Modules = [PlatformModules.GymCore, PlatformModules.Audit],
        };

    /// <summary>Installed into every newly provisioned tenant whose plan covers their modules.</summary>
    public static readonly IReadOnlyList<AgentTemplate> Default =
    [
        GymAdmin,
        FitnessCoach,
        InsightsAgent
    ];

    /// <summary>The default template with this id, or null for a tenant-built agent.</summary>
    public static AgentTemplate? Find(string? templateId) =>
        templateId is null
            ? null
            : Default.FirstOrDefault(x => x.TemplateId == templateId);
}
