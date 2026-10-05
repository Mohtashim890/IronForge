using IronForge.Application.Entities;
using IronForge.Application.Execution;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Infrastructure.Data
{
    public class IronForgeDbContext : DbContext
    {
        private readonly IExecutionContextAccessor _executionContext;
        public IronForgeDbContext(DbContextOptions<IronForgeDbContext> options, IExecutionContextAccessor executionContext) : base(options)
        {
            _executionContext = executionContext;
        }

        private int? CurrentTenantId => _executionContext.Current?.TenantId;
        public DbSet<Product> Products => Set<Product>();
        public DbSet<User> Users => Set<User>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<Agent> Agents => Set<Agent>();
        public DbSet<AgentPermission> AgentPermissions => Set<AgentPermission>();
        public DbSet<AgentDelegationEntity> AgentDelegations => Set<AgentDelegationEntity>();
        public DbSet<AgentDelegationScopeEntity> AgentDelegationScopes => Set<AgentDelegationScopeEntity>();
        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
        public DbSet<AgentMemory> AgentMemories => Set<AgentMemory>();
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

            modelBuilder.Entity<AgentPermission>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Permission)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.HasOne(x => x.Agent)
                    .WithMany(x => x.Permissions)
                    .HasForeignKey(x => x.AgentId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new
                {
                    x.AgentId,
                    x.Permission
                })
                .IsUnique();
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

            modelBuilder.Entity<AgentMemory>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Content)
                    .IsRequired()
                    .HasMaxLength(4000);

                entity.Property(x => x.AgentId)
                    .HasMaxLength(100);

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

            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Name)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(x => x.Price)
                    .HasPrecision(18, 2);

                entity.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(x => x.OwnerUserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasQueryFilter(p =>
                    CurrentTenantId.HasValue &&
                    p.TenantId == CurrentTenantId.Value);
            });

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

                entity.HasIndex(x => x.UserId);

                entity.HasIndex(x => x.AgentId);

                entity.HasIndex(x => x.ExpiresAtUtc);
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
                });
        }
    }
}
