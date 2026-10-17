using System.ComponentModel.DataAnnotations;
using IronForge.Shared.Subscriptions;

namespace IronForge.Shared.Models.Tenants
{
    /// <summary>A tenant as seen by the platform operator.</summary>
    public sealed class PlatformTenantDto
    {
        public int TenantId { get; set; }

        public string Name { get; set; } = "";

        public string Slug { get; set; } = "";

        public bool IsActive { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public int ActiveMemberCount { get; set; }

        public long? MonthlyAgentTokenLimit { get; set; }

        public string Plan { get; set; } = "";

        public SubscriptionStatus SubscriptionStatus { get; set; }

        public DateTime? TrialEndsAtUtc { get; set; }

        public List<string> AddOnModules { get; set; } = [];

        /// <summary>Plan modules plus add-ons.</summary>
        public List<string> EnabledModules { get; set; } = [];

        public List<string> AddOnModelProfiles { get; set; } = [];

        /// <summary>Plan profiles plus add-ons; the first is the default.</summary>
        public List<string> AllowedModelProfiles { get; set; } = [];

        /// <summary>Suspension and subscription state combined: can members use it now?</summary>
        public bool HasAccess { get; set; }
    }

    public sealed class ProvisionTenantRequest
    {
        [Required]
        [StringLength(200, MinimumLength = 2)]
        public string Name { get; set; } = "";

        /// <summary>URL-friendly unique identifier, e.g. "acme-corp".</summary>
        [Required]
        [StringLength(100, MinimumLength = 2)]
        [RegularExpression(
            "^[a-z0-9]+(-[a-z0-9]+)*$",
            ErrorMessage = "Use lowercase letters, digits and single hyphens, e.g. acme-corp.")]
        public string Slug { get; set; } = "";

        /// <summary>Receives the invitation to become the tenant's first Admin.</summary>
        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string AdminEmail { get; set; } = "";

        /// <summary>Null uses the plan's limit.</summary>
        [Range(1, long.MaxValue)]
        public long? MonthlyAgentTokenLimit { get; set; }

        /// <summary>A <see cref="SubscriptionPlans"/> id.</summary>
        [Required]
        public string Plan { get; set; } = SubscriptionPlans.Default.Id;

        /// <summary>Starts a trial of this many days; null starts an active subscription.</summary>
        [Range(1, 90)]
        public int? TrialDays { get; set; }

        /// <summary>The gym's currency (ISO 4217); every price and invoice uses it.</summary>
        [StringLength(3, MinimumLength = 3)]
        public string? Currency { get; set; } = "USD";

        /// <summary>The gym's time zone (IANA id, e.g. Asia/Karachi).</summary>
        [StringLength(100)]
        public string? TimeZoneId { get; set; } = "UTC";
    }

    /// <summary>Platform operator changes what a tenant has paid for.</summary>
    public sealed class UpdateTenantSubscriptionRequest
    {
        [Required]
        public string Plan { get; set; } = "";

        public SubscriptionStatus SubscriptionStatus { get; set; }

        /// <summary>Only used with <see cref="SubscriptionStatus.Trial"/>; null is an open-ended trial.</summary>
        public DateTime? TrialEndsAtUtc { get; set; }

        /// <summary>Modules enabled on top of the plan (custom client modules).</summary>
        public List<string> AddOnModules { get; set; } = [];

        /// <summary>Model profiles allowed on top of the plan's.</summary>
        public List<string> AddOnModelProfiles { get; set; } = [];

        /// <summary>Null uses the plan's limit.</summary>
        [Range(1, long.MaxValue)]
        public long? MonthlyAgentTokenLimit { get; set; }
    }

    /// <summary>Outcome of rolling out the agent templates to every tenant.</summary>
    public sealed class TemplateRolloutResultDto
    {
        public int TenantsChanged { get; set; }

        public int AgentsInstalled { get; set; }

        public int AgentsUpdated { get; set; }

        /// <summary>Agents a tenant detached from their template; never changed.</summary>
        public int DetachedSkipped { get; set; }
    }

    public sealed class ProvisionTenantResult
    {
        public PlatformTenantDto Tenant { get; set; } = new();

        public TenantInvitationLinkDto AdminInvitation { get; set; } = new();
    }

    public sealed class UpdateTenantStatusRequest
    {
        public bool IsActive { get; set; }
    }

    /// <summary>Platform operator re-invites an Admin, e.g. after the first invitation expired.</summary>
    public sealed class CreateAdminInvitationRequest
    {
        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = "";
    }
}
