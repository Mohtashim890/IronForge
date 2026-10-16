using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Auditing.AgentTools;
using IronForge.Application.Entities;
using IronForge.Application.Products.AgentTools;
using IronForge.Shared.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace IronForge.Application.Tests.Tenants;

public class SubscriptionRulesTests
{
    private static readonly IAgentTool[] Catalogue =
    [
        new GetProductsAgentTool(null!),
        new GetProductAgentTool(null!),
        new CreateProductAgentTool(null!),
        new UpdateProductAgentTool(null!),
        new DeleteProductAgentTool(null!),
        new SearchAuditEventsAgentTool(null!, null!)
    ];

    [Fact]
    public void AgentTemplates_DeclareExactlyTheModulesOfTheirTools()
    {
        var moduleByTool = Catalogue.ToDictionary(
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
    public void EveryPlanAndToolUsesKnownModules()
    {
        Assert.All(
            SubscriptionPlans.All.SelectMany(x => x.Modules),
            module => Assert.NotNull(PlatformModules.Find(module)));

        Assert.All(
            Catalogue,
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
    public void ToolRegistry_RejectsToolWithUnknownModule()
    {
        var tool = new Mock<IAgentTool>();

        tool.Setup(x => x.Descriptor).Returns(new AgentToolDescriptor
        {
            Name = "typo_tool",
            Description = "A tool whose module was mistyped.",
            RequiredPermission = "x.read",
            Module = "prodcts"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AgentToolRegistry(
                [tool.Object],
                Mock.Of<IAgentGovernanceService>(),
                Mock.Of<IronForge.Application.Auth.Services.IUserAuthorizationService>(),
                NullLogger<AgentToolRegistry>.Instance));

        Assert.Contains("unknown module 'prodcts'", exception.Message);
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
            [PlatformModules.Audit, PlatformModules.Products],
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
