using IronForge.Shared.Subscriptions;

namespace IronForge.Shared.Models.Tenants
{
    /// <summary>The platform operator's home screen.</summary>
    public sealed class PlatformOverviewDto
    {
        public int TenantCount { get; set; }

        public int ActiveTenants { get; set; }

        public int TrialTenants { get; set; }

        public int PastDueTenants { get; set; }

        public int CanceledTenants { get; set; }

        public int SuspendedTenants { get; set; }

        /// <summary>Gym members with an active membership, across all gyms.</summary>
        public int ActiveGymMembers { get; set; }

        public int StaffAccounts { get; set; }

        public long TokensThisMonth { get; set; }

        public List<DailyTokensDto> TokensByDay { get; set; } = [];

        public List<PlanCountDto> TenantsByPlan { get; set; } = [];

        /// <summary>Trials that end within 7 days.</summary>
        public List<PlatformTenantDto> TrialsEndingSoon { get; set; } = [];

        public List<PlatformTenantDto> PastDue { get; set; } = [];
    }

    public sealed class DailyTokensDto
    {
        public DateOnly Day { get; set; }

        public long Tokens { get; set; }
    }

    public sealed class PlanCountDto
    {
        public string Plan { get; set; } = "";

        public int Tenants { get; set; }
    }

    public sealed class PlatformTenantDetailDto
    {
        public PlatformTenantDto Tenant { get; set; } = new();

        public string? Currency { get; set; }

        public string? TimeZoneId { get; set; }

        public string? JoinCode { get; set; }

        public Dictionary<string, int> MembersByStatus { get; set; } = [];

        public int StaffCount { get; set; }

        /// <summary>Member payments collected by the gym in the last 30 days, in its currency.</summary>
        public decimal GymRevenueLast30Days { get; set; }

        public long TokensThisMonth { get; set; }

        public long? EffectiveMonthlyTokenLimit { get; set; }

        public List<DailyTokensDto> TokensByDay { get; set; } = [];

        public List<PlatformTenantAgentDto> Agents { get; set; } = [];

        public List<PlatformAuditEventDto> RecentActivity { get; set; } = [];
    }

    public sealed class PlatformTenantAgentDto
    {
        public string AgentId { get; set; } = "";

        public string Name { get; set; } = "";

        public bool IsActive { get; set; }

        public bool IsManaged { get; set; }

        public int? TemplateVersion { get; set; }

        public int? LatestTemplateVersion { get; set; }

        public string ModelProfile { get; set; } = "";
    }

    /// <summary>Plans, modules and model profiles as defined in code.</summary>
    public sealed class PlatformCatalogDto
    {
        public List<PlatformPlanDto> Plans { get; set; } = [];

        public List<PlatformModuleDto> Modules { get; set; } = [];

        public List<string> ModelProfiles { get; set; } = [];

        public List<PlatformTemplateDto> AgentTemplates { get; set; } = [];
    }

    public sealed class PlatformPlanDto
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public bool IsDefault { get; set; }

        public List<string> Modules { get; set; } = [];

        public long? MonthlyAgentTokenLimit { get; set; }

        public List<string> ModelProfiles { get; set; } = [];

        public int TenantCount { get; set; }
    }

    public sealed class PlatformModuleDto
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string Description { get; set; } = "";

        public bool IsCustom { get; set; }
    }

    public sealed class PlatformTemplateDto
    {
        public string TemplateId { get; set; } = "";

        public string Name { get; set; } = "";

        public string Description { get; set; } = "";

        public int Version { get; set; }

        public List<string> Modules { get; set; } = [];

        public List<string> Tools { get; set; } = [];

        public List<string> AllowedRoles { get; set; } = [];

        /// <summary>Tenants whose copy is older than this version.</summary>
        public int OutdatedTenants { get; set; }

        public int InstalledTenants { get; set; }
    }

    public sealed class PlatformAuditEventDto
    {
        public DateTime OccurredAtUtc { get; set; }

        public int? TenantId { get; set; }

        public string? TenantName { get; set; }

        public string Category { get; set; } = "";

        public string Action { get; set; } = "";

        public string Outcome { get; set; } = "";

        public int? UserId { get; set; }

        public string? AgentId { get; set; }

        public string? ResourceType { get; set; }

        public string? ResourceId { get; set; }

        public string? Reason { get; set; }
    }

    public sealed class PlatformAuditQuery
    {
        public int? TenantId { get; set; }

        public string? Category { get; set; }

        public string? Outcome { get; set; }

        public int Days { get; set; } = 7;

        public int Take { get; set; } = 100;
    }

    /// <summary>Platform plans as a quick lookup for UI labels.</summary>
    public static class PlanLabels
    {
        public static string NameOf(string? planId) =>
            SubscriptionPlans.Find(planId)?.Name ?? planId ?? "";

        public static string ModuleName(string moduleId) =>
            PlatformModules.Find(moduleId)?.Name ?? moduleId;
    }
}
