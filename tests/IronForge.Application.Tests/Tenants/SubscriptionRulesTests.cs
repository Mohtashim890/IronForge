using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Auditing.AgentTools;
using IronForge.Application.Entities;
using IronForge.Application.Gym.AgentTools;
using IronForge.Shared.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace IronForge.Application.Tests.Tenants;

public class SubscriptionRulesTests
{
    /// <summary>The real catalogue; only descriptors are read, so dependencies are null.</summary>
    public static IAgentTool[] Catalogue() =>
        GymAgentToolCatalog.Types
            .Select(type =>
            {
                var constructor = type.GetConstructors().Single();

                return (IAgentTool)constructor.Invoke(
                    constructor.GetParameters().Select(_ => (object?)null).ToArray());
            })
            .Append(new SearchAuditEventsAgentTool(null!, null!))
            .ToArray();

    [Fact]
    public void AgentTemplates_DeclareExactlyTheModulesOfTheirTools()
    {
        var moduleByTool = Catalogue().ToDictionary(
            x => x.Descriptor.Name,
            x => x.Descriptor.Module);

        foreach (var template in AgentTemplates.Default)
        {
            var toolModules = template.Definition.Tools
                .Select(x => moduleByTool[x.ToolName])
                .Distinct()
                .Order();

            Assert.Equal(toolModules, template.Modules.Order());
        }
    }

    [Fact]
    public void ToolNamesAreUnique()
    {
        var names = Catalogue().Select(x => x.Descriptor.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void EveryPlanAndToolUsesKnownModules()
    {
        Assert.All(
            SubscriptionPlans.All.SelectMany(x => x.Modules),
            module => Assert.NotNull(PlatformModules.Find(module)));

        Assert.All(
            Catalogue(),
            tool => Assert.NotNull(PlatformModules.Find(tool.Descriptor.Module)));
    }

    [Fact]
    public void PlansNeverIncludeCustomModules()
    {
        var custom = PlatformModules.All.Where(x => x.IsCustom).Select(x => x.Id);

        Assert.All(
            SubscriptionPlans.All,
            plan => Assert.Empty(plan.Modules.Intersect(custom)));
    }

    [Fact]
    public void EveryPlanRunsTheGymAndItsAdminAgent()
    {
        Assert.All(SubscriptionPlans.All, plan =>
        {
            Assert.Contains(PlatformModules.GymCore, plan.Modules);
            Assert.Contains(PlatformModules.GymBilling, plan.Modules);
            Assert.All(AgentTemplates.GymAdmin.Modules, module => Assert.Contains(module, plan.Modules));
        });
    }

    [Fact]
    public void Starter_HasNoCoaching_SoNoFitnessCoach()
    {
        Assert.DoesNotContain(PlatformModules.GymCoaching, SubscriptionPlans.Starter.Modules);
        Assert.Contains(PlatformModules.GymCoaching, SubscriptionPlans.Professional.Modules);
        Assert.Contains(PlatformModules.GymCoaching, AgentTemplates.FitnessCoach.Modules);
    }

    [Fact]
    public void ApprovalTools_AreNotLowRisk()
    {
        Assert.All(
            Catalogue().Where(x => x.Descriptor.RequiresApproval),
            tool => Assert.NotEqual(AgentRiskLevel.Low, tool.Descriptor.RiskLevel));
    }

    [Fact]
    public void ToolRegistry_RejectsToolWithUnknownModule()
    {
        var tool = new Mock<IAgentTool>();

        tool.Setup(x => x.Descriptor).Returns(new AgentToolDescriptor
        {
            Name = "typo_tool",
            Description = "A tool whose module was mistyped.",
            RequiredPermission = "x.read",
            Module = "gym.cor"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AgentToolRegistry(
                [tool.Object],
                Mock.Of<IAgentGovernanceService>(),
                Mock.Of<IronForge.Application.Auth.Services.IUserAuthorizationService>(),
                NullLogger<AgentToolRegistry>.Instance));

        Assert.Contains("unknown module 'gym.cor'", exception.Message);
    }

    [Fact]
    public void EnabledModules_ArePlanModulesPlusAddOns()
    {
        var tenant = new Tenant
        {
            Plan = SubscriptionPlans.Starter.Id,
            AddOnModules = [PlatformModules.Audit]
        };

        Assert.Equal(
            new[]
            {
                PlatformModules.Audit,
                PlatformModules.GymBilling,
                PlatformModules.GymCore,
                PlatformModules.GymEngagement
            }.Order(),
            tenant.GetEnabledModules().Order());
    }

    [Fact]
    public void EnabledModules_OfUnknownPlan_AreOnlyTheAddOns()
    {
        var tenant = new Tenant { Plan = "retired-plan", AddOnModules = [] };

        Assert.Empty(tenant.GetEnabledModules());
    }

    [Theory]
    [InlineData(true, SubscriptionStatus.Active, null, true)]
    [InlineData(true, SubscriptionStatus.PastDue, null, true)]
    [InlineData(true, SubscriptionStatus.Trial, null, true)]
    [InlineData(true, SubscriptionStatus.Trial, 1, true)]
    [InlineData(true, SubscriptionStatus.Trial, -1, false)]
    [InlineData(true, SubscriptionStatus.Canceled, null, false)]
    [InlineData(false, SubscriptionStatus.Active, null, false)]
    public void HasAccess_CombinesSuspensionAndSubscription(
        bool isActive,
        SubscriptionStatus status,
        int? trialEndsInDays,
        bool expected)
    {
        var now = DateTime.UtcNow;

        var tenant = new Tenant
        {
            IsActive = isActive,
            SubscriptionStatus = status,
            TrialEndsAtUtc = trialEndsInDays is { } days ? now.AddDays(days) : null
        };

        Assert.Equal(expected, tenant.HasAccess(now));
    }
}
