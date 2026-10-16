namespace IronForge.Shared.Subscriptions
{
    /// <summary>Commercial state of a tenant's subscription.</summary>
    public enum SubscriptionStatus
    {
        /// <summary>Full access until TrialEndsAtUtc (if set).</summary>
        Trial,

        Active,

        /// <summary>Payment overdue: access continues as a grace period.</summary>
        PastDue,

        /// <summary>No access. Data is kept so the tenant can be reactivated.</summary>
        Canceled
    }

    /// <summary>
    /// A group of agent tools that is enabled or disabled for a tenant as a
    /// whole. Every catalogue tool belongs to exactly one module.
    /// </summary>
    public sealed record PlatformModule(
        string Id,
        string Name,
        string Description,
        bool IsCustom = false);

    public static class PlatformModules
    {
        public const string Products = "products";
        public const string Audit = "audit";

        /// <summary>
        /// Every module the platform knows. Core modules come with plans.
        /// Custom modules hold tools built for one client; they are never part
        /// of a plan and are enabled per tenant as add-ons, e.g.:
        /// new("client.acme-erp", "Acme ERP", "Orders and stock in Acme's ERP.", IsCustom: true)
        /// </summary>
        public static readonly IReadOnlyList<PlatformModule> All =
        [
            new(Products, "Products", "Read and manage the product catalogue."),
            new(Audit, "Activity insights", "Search the workspace's audit log.")
        ];

        public static PlatformModule? Find(string? id) =>
            All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
    }

    /// <param name="ModelProfiles">
    /// Model profile names (configured in the API under AISettings:ModelProfiles)
    /// the plan's agents may run on. The first one is the default for new agents
    /// and the fallback for agents on a profile the tenant no longer has.
    /// </param>
    public sealed record SubscriptionPlan(
        string Id,
        string Name,
        IReadOnlyList<string> Modules,
        long? MonthlyAgentTokenLimit,
        IReadOnlyList<string> ModelProfiles);

    /// <summary>Profile names used by the plans; they must exist in AISettings:ModelProfiles.</summary>
    public static class ModelProfileNames
    {
        public const string Standard = "standard";
        public const string Economy = "economy";
    }

    /// <summary>
    /// Plans and what they include. Placeholder values: adjust names, modules
    /// and limits to your pricing. A tenant's own token limit overrides the plan's.
    /// </summary>
    public static class SubscriptionPlans
    {
        public static readonly SubscriptionPlan Starter = new(
            "starter",
            "Starter",
            [PlatformModules.Products],
            MonthlyAgentTokenLimit: 500_000,
            ModelProfiles: [ModelProfileNames.Economy]);

        public static readonly SubscriptionPlan Professional = new(
            "professional",
            "Professional",
            [PlatformModules.Products, PlatformModules.Audit],
            MonthlyAgentTokenLimit: 2_000_000,
            ModelProfiles: [ModelProfileNames.Standard, ModelProfileNames.Economy]);

        public static readonly SubscriptionPlan Enterprise = new(
            "enterprise",
            "Enterprise",
            [PlatformModules.Products, PlatformModules.Audit],
            MonthlyAgentTokenLimit: 10_000_000,
            ModelProfiles: [ModelProfileNames.Standard, ModelProfileNames.Economy]);

        public static readonly IReadOnlyList<SubscriptionPlan> All =
            [Starter, Professional, Enterprise];

        /// <summary>Used for new tenants when no plan is given.</summary>
        public static readonly SubscriptionPlan Default = Professional;

        public static SubscriptionPlan? Find(string? id) =>
            All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
    }
}
