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
        public const string Audit = "audit";

        /// <summary>Members, membership plans, check-ins and gym settings.</summary>
        public const string GymCore = "gym.core";

        /// <summary>Invoices, payments and payment reminders.</summary>
        public const string GymBilling = "gym.billing";

        /// <summary>Routines, workout logging, progress tracking and the AI fitness coach.</summary>
        public const string GymCoaching = "gym.coaching";

        /// <summary>Announcements and member notifications.</summary>
        public const string GymEngagement = "gym.engagement";

        /// <summary>Access-control hardware: ZKTeco fingerprint terminals synced with memberships.</summary>
        public const string GymAccess = "gym.access";

        /// <summary>
        /// Every module the platform knows. Core modules come with plans.
        /// Custom modules hold tools built for one client; they are never part
        /// of a plan and are enabled per tenant as add-ons, e.g.:
        /// new("client.acme-erp", "Acme ERP", "Orders and stock in Acme's ERP.", IsCustom: true)
        /// </summary>
        public static readonly IReadOnlyList<PlatformModule> All =
        [
            new(GymCore, "Gym core", "Members, membership plans, check-ins and gym settings."),
            new(GymBilling, "Billing", "Invoices, card and cash payments, and payment reminders."),
            new(GymCoaching, "Coaching", "Workout routines, progress tracking and the AI fitness coach."),
            new(GymEngagement, "Engagement", "Announcements and member notifications."),
            new(GymAccess, "Access control", "ZKTeco fingerprint terminals: members enter with their finger; IronForge keeps each terminal in step with memberships."),
            new(Audit, "Activity insights", "Search the gym's audit log.")
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
        /// <summary>Run the gym: members, billing and announcements, with the admin agent.</summary>
        public static readonly SubscriptionPlan Starter = new(
            "starter",
            "Starter",
            [PlatformModules.GymCore, PlatformModules.GymBilling, PlatformModules.GymEngagement],
            MonthlyAgentTokenLimit: 500_000,
            ModelProfiles: [ModelProfileNames.Economy]);

        /// <summary>Adds member coaching (routines, progress, AI coach) and activity insights.</summary>
        public static readonly SubscriptionPlan Professional = new(
            "professional",
            "Professional",
            [
                PlatformModules.GymCore,
                PlatformModules.GymBilling,
                PlatformModules.GymEngagement,
                PlatformModules.GymCoaching,
                PlatformModules.Audit
            ],
            MonthlyAgentTokenLimit: 2_000_000,
            ModelProfiles: [ModelProfileNames.Standard, ModelProfileNames.Economy]);

        /// <summary>Professional with access control and a larger AI budget, for big gyms and chains.</summary>
        public static readonly SubscriptionPlan Enterprise = new(
            "enterprise",
            "Enterprise",
            [
                PlatformModules.GymCore,
                PlatformModules.GymBilling,
                PlatformModules.GymEngagement,
                PlatformModules.GymCoaching,
                PlatformModules.Audit,
                PlatformModules.GymAccess
            ],
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
