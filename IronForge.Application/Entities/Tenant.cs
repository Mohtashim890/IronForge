using IronForge.Shared.Subscriptions;

namespace IronForge.Application.Entities
{
    public class Tenant
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string Slug { get; set; } = "";

        /// <summary>
        /// Operator switch (Suspend/Reactivate), independent of billing.
        /// Access requires this and an access-granting subscription status.
        /// </summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; }

        /// <summary>
        /// Monthly agent token limit for this tenant. Null uses the plan's
        /// limit, then the platform default (AgentQuotas:DefaultMonthlyTokenLimit).
        /// Set by the platform operator, never by the tenant.
        /// </summary>
        public long? MonthlyAgentTokenLimit { get; set; }

        /// <summary>Id of a <see cref="SubscriptionPlans"/> plan.</summary>
        public string Plan { get; set; } = SubscriptionPlans.Default.Id;

        public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.Active;

        /// <summary>End of the trial; null means an open-ended trial.</summary>
        public DateTime? TrialEndsAtUtc { get; set; }

        /// <summary>
        /// Modules enabled on top of the plan, typically a client's custom
        /// tools (<see cref="PlatformModule.IsCustom"/>).
        /// </summary>
        public List<string> AddOnModules { get; set; } = [];

        /// <summary>Model profiles enabled on top of the plan's.</summary>
        public List<string> AddOnModelProfiles { get; set; } = [];

        /// <summary>Whether members can use the tenant right now.</summary>
        public bool HasAccess(DateTime nowUtc)
        {
            if (!IsActive)
                return false;

            return SubscriptionStatus switch
            {
                SubscriptionStatus.Active => true,
                SubscriptionStatus.PastDue => true,
                SubscriptionStatus.Trial => TrialEndsAtUtc is null || TrialEndsAtUtc > nowUtc,
                _ => false
            };
        }

        /// <summary>Plan modules plus add-ons. An unknown plan contributes nothing.</summary>
        public IReadOnlySet<string> GetEnabledModules()
        {
            var modules = new HashSet<string>(StringComparer.Ordinal);

            if (SubscriptionPlans.Find(Plan) is { } plan)
            {
                modules.UnionWith(plan.Modules);
            }

            modules.UnionWith(AddOnModules);

            return modules;
        }

        /// <summary>
        /// Plan profiles (in plan order, so the first is the default) followed
        /// by add-ons. An unknown plan contributes nothing.
        /// </summary>
        public IReadOnlyList<string> GetAllowedModelProfiles()
        {
            var plan = SubscriptionPlans.Find(Plan)?.ModelProfiles ?? [];

            return plan
                .Concat(AddOnModelProfiles)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Why <see cref="HasAccess"/> is false, for messages and audit.</summary>
        public string DescribeNoAccess(DateTime nowUtc)
        {
            if (!IsActive)
                return "The workspace is suspended.";

            return SubscriptionStatus switch
            {
                SubscriptionStatus.Trial when TrialEndsAtUtc <= nowUtc => "The workspace's trial has ended.",
                SubscriptionStatus.Canceled => "The workspace's subscription is canceled.",
                _ => "The workspace is not available."
            };
        }
    }
}
