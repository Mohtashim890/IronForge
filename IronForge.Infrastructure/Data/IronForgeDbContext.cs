using IronForge.Application.Entities;
using IronForge.Application.Execution;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Infrastructure.Data
{
    public partial class IronForgeDbContext : DbContext
    {
        private readonly IExecutionContextAccessor _executionContext;
        public IronForgeDbContext(DbContextOptions<IronForgeDbContext> options, IExecutionContextAccessor executionContext) : base(options)
        {
            _executionContext = executionContext;
        }

        private int? CurrentTenantId => _executionContext.Current?.TenantId;
        public DbSet<User> Users => Set<User>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<Agent> Agents => Set<Agent>();
        public DbSet<AgentToolBindingEntity> AgentToolBindings => Set<AgentToolBindingEntity>();
        public DbSet<AgentUsageRecord> AgentUsageRecords => Set<AgentUsageRecord>();
        public DbSet<AgentDelegationEntity> AgentDelegations => Set<AgentDelegationEntity>();
        public DbSet<AgentDelegationScopeEntity> AgentDelegationScopes => Set<AgentDelegationScopeEntity>();
        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
        public DbSet<TenantInvitation> TenantInvitations => Set<TenantInvitation>();
        public DbSet<AgentSession> AgentSessions => Set<AgentSession>();
        public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
        public DbSet<ConversationSummary> ConversationSummaries => Set<ConversationSummary>();
        public DbSet<AgentApproval> AgentApprovals => Set<AgentApproval>();
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
        public DbSet<McpClient> McpClients => Set<McpClient>();
        public DbSet<AgentMcpClientAuthorization>AgentMcpClientAuthorizations => Set<AgentMcpClientAuthorization>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigureGym(modelBuilder);

            modelBuilder.Entity<AgentMcpClientAuthorization>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.TenantId).IsRequired();
                entity.Property(x => x.IsActive).IsRequired();
                entity.Property(x => x.CreatedAtUtc).IsRequired();
                entity.Property(x => x.UpdatedAtUtc).IsRequired();

                entity.HasOne(x => x.Agent)
                    .WithMany()
                    .HasForeignKey(x => x.AgentEntityId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.McpClient)
                    .WithMany()
                    .HasForeignKey(x => x.McpClientEntityId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new
                {
                    x.TenantId,
                    x.AgentEntityId,
                    x.McpClientEntityId
                }).IsUnique();

                entity.HasIndex(x => new
                {
                    x.TenantId,
                    x.McpClientEntityId,
                    x.IsActive
                });

                entity.HasQueryFilter(x =>
                    CurrentTenantId.HasValue &&
                    x.TenantId == CurrentTenantId.Value);
            });


            modelBuilder.Entity<McpClient>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.ClientId)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(x => x.Name)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(x => x.Description)
                    .HasMaxLength(2000);

                entity.Property(x => x.ClientType)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(x => x.Version)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(x => x.IsActive)
                    .IsRequired();

                entity.Property(x => x.CreatedAtUtc)
                    .IsRequired();

                entity.Property(x => x.UpdatedAtUtc)
                    .IsRequired();

                entity.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasQueryFilter(x =>
                    CurrentTenantId.HasValue &&
                    x.TenantId == CurrentTenantId.Value);

                entity.HasIndex(x => new
                {
                    x.TenantId,
                    x.ClientId
                })
                .IsUnique();

                entity.HasIndex(x => new
                {
                    x.TenantId,
                    x.IsActive
                });
            });

            modelBuilder.Entity<Agent>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.AgentId)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(x => x.Name)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(x => x.Description)
                    .HasMaxLength(2000);

                entity.Property(x => x.Version)
                    .HasMaxLength(50);

                entity.Property(x => x.IsActive)
                    .IsRequired();

                entity.Property(x => x.CreatedAtUtc)
                    .IsRequired();

                entity.Property(x => x.UpdatedAtUtc)
                    .IsRequired();

                entity.Property(x => x.Instructions)
                    .IsRequired()
                    .HasColumnType("text")
                    .HasDefaultValue("");

                entity.Property(x => x.MaximumIterationsPerRequest)
                    .IsRequired()
                    .HasDefaultValue(5);

                entity.Property(x => x.AllowMultipleToolCalls)
                    .IsRequired()
                    .HasDefaultValue(false);

                entity.Property(x => x.ApprovalLifetimeMinutes)
                    .IsRequired()
                    .HasDefaultValue(10);

                entity.Property(x => x.ModelProfile)
                    .IsRequired()
                    .HasMaxLength(100)
                    .HasDefaultValue("standard");

                // PostgreSQL text[]; empty means every role.
                entity.Property(x => x.AllowedRoles)
                    .IsRequired();

                entity.Property(x => x.TemplateId)
                    .HasMaxLength(200);

                entity.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasQueryFilter(x =>
                    CurrentTenantId.HasValue &&
                    x.TenantId == CurrentTenantId.Value);

                entity.HasIndex(x => new
                {
                    x.TenantId,
                    x.AgentId
                })
                .IsUnique();
            });

            modelBuilder.Entity<AgentUsageRecord>(entity =>
            {
                entity.ToTable("AgentUsageRecords");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.AgentId)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(x => x.Operation)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(x => x.ModelProfile)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(x => x.Model)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasQueryFilter(x =>
                    CurrentTenantId.HasValue &&
                    x.TenantId == CurrentTenantId.Value);

                // Quota checks sum one tenant's rows for the current month.
                entity.HasIndex(x => new
                {
                    x.TenantId,
                    x.CreatedAtUtc
                });
            });

            modelBuilder.Entity<AgentToolBindingEntity>(entity =>
            {
                entity.ToTable("AgentToolBindings");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.ToolName)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(x => x.RiskLevel)
                    .HasMaxLength(20);

                entity.Property(x => x.MaximumAmount)
                    .HasPrecision(18, 2);

                entity.HasOne(x => x.Agent)
                    .WithMany(x => x.ToolBindings)
                    .HasForeignKey(x => x.AgentId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Tenant isolation through the owning agent.
                entity.HasQueryFilter(x =>
                    CurrentTenantId.HasValue &&
                    x.Agent.TenantId == CurrentTenantId.Value);

                entity.HasIndex(x => new
                {
                    x.AgentId,
                    x.ToolName
                })
                .IsUnique();
            });

            modelBuilder.Entity<AuditEvent>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.TenantId);

                entity.Property(x => x.OccurredAtUtc)
                    .IsRequired();

                entity.Property(x => x.Category)
                    .IsRequired();

                entity.Property(x => x.Action)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(x => x.Outcome)
                    .IsRequired();

                entity.Property(x => x.ActorType)
                    .IsRequired();

                entity.Property(x => x.AgentId)
                    .HasMaxLength(200);

                entity.Property(x => x.ClientId)
                    .HasMaxLength(200);

                entity.Property(x => x.ResourceType)
                    .HasMaxLength(200);

                entity.Property(x => x.ResourceId)
                    .HasMaxLength(200);

                entity.Property(x => x.CorrelationId)
                    .HasMaxLength(200);

                entity.Property(x => x.TraceId)
                    .HasMaxLength(100);

                entity.Property(x => x.Reason)
                    .HasMaxLength(2000);

                entity.Property(x => x.MetadataJson);

                entity.HasIndex(x => new
                {
                    x.TenantId,
                    x.OccurredAtUtc
                });

                entity.HasIndex(x => new
                {
                    x.TenantId,
                    x.Category,
                    x.OccurredAtUtc
                });

                entity.HasIndex(x => new
                {
                    x.TenantId,
                    x.ResourceType,
                    x.ResourceId
                });

                entity.HasIndex(x => x.CorrelationId);

                // Events recorded without a tenant (for example failed sign-ins)
                // are platform events and are not visible to any tenant.
                entity.HasQueryFilter(x =>
                    CurrentTenantId.HasValue &&
                    x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<AgentApproval>(
                entity =>
                {
                    entity.HasKey(x => x.ApprovalId);

                    entity.Property(x => x.ApprovalId)
                        .IsRequired()
                        .HasMaxLength(100);

                    entity.Property(x => x.RequestId)
                        .IsRequired()
                        .HasMaxLength(100);

                    entity.Property(x => x.AgentId)
                        .IsRequired()
                        .HasMaxLength(200);

                    entity.Property(x => x.ToolName)
                        .IsRequired()
                        .HasMaxLength(200);

                    entity.Property(x => x.ToolCallId)
                        .IsRequired()
                        .HasMaxLength(200);

                    entity.Property(x => x.ArgumentsJson)
                        .IsRequired();

                    entity.Property(x => x.RiskLevel)
                        .IsRequired();

                    entity.Property(x => x.Status)
                        .IsRequired();

                    entity.Property(x => x.CreatedAtUtc)
                        .IsRequired();

                    entity.Property(x => x.ExpiresAtUtc)
                        .IsRequired();

                    entity.HasIndex(x => new
                    {
                        x.TenantId,
                        x.UserId,
                        x.CreatedAtUtc
                    });

                    entity.HasIndex(x => new
                    {
                        x.TenantId,
                        x.SessionId
                    });

                    entity.HasIndex(x => new
                    {
                        x.TenantId,
                        x.Status
                    });

                    entity.HasQueryFilter(x =>
                        CurrentTenantId.HasValue &&
                        x.TenantId == CurrentTenantId.Value);
                });

            modelBuilder.Entity<ConversationSummary>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Content)
                    .IsRequired()
                    .HasMaxLength(32000);

                entity.Property(x => x.SummarizedThroughMessageId)
                    .IsRequired();

                entity.Property(x => x.CreatedAtUtc)
                    .IsRequired();

                entity.Property(x => x.UpdatedAtUtc)
                    .IsRequired();

                entity.HasOne<AgentSession>()
                    .WithMany()
                    .HasForeignKey(x => x.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x =>
                    new
                    {
                        x.TenantId,
                        x.SessionId
                    })
                    .IsUnique();

                entity.HasQueryFilter(x =>
                    CurrentTenantId.HasValue &&
                    x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<ConversationMessage>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Role)
                    .IsRequired();

                entity.Property(x => x.Content)
                    .IsRequired()
                    .HasMaxLength(16000);

                entity.Property(x => x.ToolCallId)
                    .HasMaxLength(200);

                entity.Property(x => x.ToolName)
                    .HasMaxLength(200);

                entity.Property(x => x.MetadataJson)
                    .HasMaxLength(16000);

                entity.Property(x => x.CreatedAtUtc)
                    .IsRequired();

                entity.HasOne<AgentSession>()
                    .WithMany()
                    .HasForeignKey(x => x.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x =>
                new
                {
                    x.TenantId,
                    x.SessionId,
                    x.CreatedAtUtc
                });

                entity.HasQueryFilter(x =>
                    CurrentTenantId.HasValue &&
                    x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<AgentSession>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.AgentId)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(x => x.Title)
                    .HasMaxLength(200);

                entity.Property(x => x.Status)
                    .IsRequired();

                entity.Property(x => x.CreatedAtUtc)
                    .IsRequired();

                entity.Property(x => x.UpdatedAtUtc)
                    .IsRequired();

                entity.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasQueryFilter(x =>
                    CurrentTenantId.HasValue &&
                    x.TenantId == CurrentTenantId.Value);

                entity.HasIndex(x =>
                new
                {
                    x.TenantId,
                    x.UserId,
                    x.AgentId,
                    x.LastMessageAtUtc
                });
                entity.HasIndex(x =>
                new
                {
                    x.TenantId,
                    x.UserId,
                    x.UpdatedAtUtc
                });
            });

            modelBuilder.Entity<RefreshToken>()
               .HasOne(rt => rt.User)
               .WithMany()
               .HasForeignKey(rt => rt.UserId)
               .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RefreshToken>()
                .HasOne(rt => rt.Tenant)
                .WithMany()
                .HasForeignKey(rt => rt.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TenantMembership>(entity =>
            {
                entity.HasKey(x => new
                {
                    x.TenantId,
                    x.UserId
                });

                entity.Property(x => x.Role)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(x => x.IsActive)
                    .IsRequired();

                entity.Property(x => x.CreatedAtUtc)
                    .IsRequired();

                entity.HasOne(x => x.Tenant)
                    .WithMany()
                    .HasForeignKey(x => x.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TenantInvitation>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Email)
                    .IsRequired()
                    .HasMaxLength(256);

                entity.Property(x => x.Role)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(x => x.TokenHash)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.HasIndex(x => x.TokenHash)
                    .IsUnique();

                entity.HasIndex(x => new
                {
                    x.TenantId,
                    x.Email
                });

                // Mapped to PostgreSQL's xmin system column; no table column.
                entity.Property(x => x.Version)
                    .IsRowVersion();

                entity.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(x => x.CreatedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(x => x.AcceptedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                // Defense in depth; TenantInvitationStore scopes explicitly.
                entity.HasQueryFilter(x =>
                    CurrentTenantId.HasValue &&
                    x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<Tenant>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Name)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(x => x.Slug)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.HasIndex(x => x.Slug)
                    .IsUnique();

                entity.Property(x => x.Plan)
                    .IsRequired()
                    .HasMaxLength(50);

                // Stored as text so the column stays readable and enum
                // reordering cannot change meaning.
                entity.Property(x => x.SubscriptionStatus)
                    .IsRequired()
                    .HasConversion<string>()
                    .HasMaxLength(20);

                // PostgreSQL text[].
                entity.Property(x => x.AddOnModules)
                    .IsRequired();

                entity.Property(x => x.AddOnModelProfiles)
                    .IsRequired();

                entity.Property(x => x.IsActive)
                    .IsRequired();

                entity.Property(x => x.CreatedAtUtc)
                    .IsRequired();
            });

            /*** db constraints ***/
            modelBuilder.Entity<RefreshToken>()
               .HasIndex(rt => rt.TokenHash)
               .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            // One account per email: invitations, joining and password reset find accounts by email.
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // Delegation constraints
            modelBuilder.Entity<AgentDelegationEntity>(
            entity =>
            {
                entity.HasKey(x => x.DelegationId);

                entity.Property(x => x.AgentId)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(x => x.ClientId)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(x => x.CreatedAtUtc)
                    .IsRequired();

                entity.Property(x => x.ExpiresAtUtc)
                    .IsRequired();

                entity.Property(x => x.Revoked)
                    .IsRequired();

                entity.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new
                {
                    x.TenantId,
                    x.UserId
                });

                entity.HasIndex(x => x.AgentId);

                entity.HasIndex(x => x.ExpiresAtUtc);

                entity.HasQueryFilter(x =>
                    CurrentTenantId.HasValue &&
                    x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<AgentDelegationScopeEntity>(
                entity =>
                {
                    entity.HasKey(x =>
                        new
                        {
                            x.DelegationId,
                            x.Scope
                        });

                    entity.Property(x => x.Scope)
                        .IsRequired()
                        .HasMaxLength(200);

                    entity.HasOne(x => x.Delegation)
                        .WithMany(x => x.Scopes)
                        .HasForeignKey(x => x.DelegationId)
                        .OnDelete(DeleteBehavior.Cascade);

                    // Tenant isolation through the owning delegation.
                    entity.HasQueryFilter(x =>
                        CurrentTenantId.HasValue &&
                        x.Delegation.TenantId == CurrentTenantId.Value);
                });
        }
    }
}
