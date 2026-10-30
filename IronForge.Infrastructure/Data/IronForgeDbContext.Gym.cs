using IronForge.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IronForge.Infrastructure.Data
{
    /// <summary>Gym domain mapping: members, billing, fitness tracking and notifications.</summary>
    public partial class IronForgeDbContext
    {
        public DbSet<GymSettings> GymSettings => Set<GymSettings>();
        public DbSet<MembershipPlan> MembershipPlans => Set<MembershipPlan>();
        public DbSet<MemberProfile> MemberProfiles => Set<MemberProfile>();
        public DbSet<CheckIn> CheckIns => Set<CheckIn>();
        public DbSet<CheckInDenial> CheckInDenials => Set<CheckInDenial>();
        public DbSet<CheckInDevice> CheckInDevices => Set<CheckInDevice>();
        public DbSet<MemberPhoto> MemberPhotos => Set<MemberPhoto>();
        public DbSet<TerminalUser> TerminalUsers => Set<TerminalUser>();
        public DbSet<TerminalUserSync> TerminalUserSyncs => Set<TerminalUserSync>();
        public DbSet<TerminalCommand> TerminalCommands => Set<TerminalCommand>();
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<InvoiceReminder> InvoiceReminders => Set<InvoiceReminder>();
        public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents => Set<ProcessedWebhookEvent>();

        public DbSet<GymPaymentAccount> GymPaymentAccounts => Set<GymPaymentAccount>();
        public DbSet<Exercise> Exercises => Set<Exercise>();
        public DbSet<WorkoutRoutine> WorkoutRoutines => Set<WorkoutRoutine>();
        public DbSet<RoutineDay> RoutineDays => Set<RoutineDay>();
        public DbSet<RoutineExercise> RoutineExercises => Set<RoutineExercise>();
        public DbSet<WorkoutLog> WorkoutLogs => Set<WorkoutLog>();
        public DbSet<WorkoutSet> WorkoutSets => Set<WorkoutSet>();
        public DbSet<BodyMetric> BodyMetrics => Set<BodyMetric>();
        public DbSet<FitnessGoal> FitnessGoals => Set<FitnessGoal>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<Announcement> Announcements => Set<Announcement>();
        public DbSet<EmailOutboxMessage> EmailOutbox => Set<EmailOutboxMessage>();
        public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

        private void ConfigureGym(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GymSettings>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.HasIndex(x => x.TenantId).IsUnique();
                entity.HasIndex(x => x.JoinCode).IsUnique();

                entity.Property(x => x.JoinCode).IsRequired().HasMaxLength(20);
                entity.Property(x => x.Currency).IsRequired().HasMaxLength(3);
                entity.Property(x => x.TimeZoneId).IsRequired().HasMaxLength(100);
                entity.Property(x => x.Address).HasMaxLength(300);
                entity.Property(x => x.Phone).HasMaxLength(50);
                entity.Property(x => x.ContactEmail).HasMaxLength(256);
                entity.Property(x => x.PaymentInstructions).HasMaxLength(1000);
                entity.Property(x => x.ReminderDaysAfterDue).IsRequired();

                TenantOwned(entity);
                // No query filter: see GymSettings.
            });

            modelBuilder.Entity<MembershipPlan>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).IsRequired().HasMaxLength(100);
                entity.Property(x => x.Description).HasMaxLength(1000);
                Money(entity.Property(x => x.Price));
                entity.Property(x => x.BillingInterval).HasConversion<string>().HasMaxLength(20);
                entity.HasIndex(x => new { x.TenantId, x.Name });

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<MemberProfile>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.FullName).IsRequired().HasMaxLength(150);
                entity.Property(x => x.Email).IsRequired().HasMaxLength(256);
                entity.Property(x => x.Phone).HasMaxLength(50);
                entity.Property(x => x.Gender).HasMaxLength(20);
                entity.Property(x => x.HeightCm).HasPrecision(5, 1);
                entity.Property(x => x.EmergencyContact).HasMaxLength(200);
                entity.Property(x => x.Notes).HasMaxLength(2000);
                entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
                entity.Property(x => x.PassPhoneIdHash).HasMaxLength(100);
                entity.Ignore(x => x.IsApproved);
                entity.Ignore(x => x.IsDeclinedRequest);

                entity.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
                entity.HasIndex(x => new { x.TenantId, x.UserId })
                    .IsUnique()
                    .HasFilter("\"UserId\" IS NOT NULL");
                entity.HasIndex(x => new { x.TenantId, x.Status });
                entity.HasIndex(x => new { x.TenantId, x.NextBillingDate });

                entity.HasOne(x => x.MembershipPlan)
                    .WithMany()
                    .HasForeignKey(x => x.MembershipPlanId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.SetNull);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<CheckIn>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
                entity.Property(x => x.Warning).HasMaxLength(300);
                entity.HasIndex(x => new { x.TenantId, x.CheckedInAtUtc });
                entity.HasIndex(x => new { x.MemberProfileId, x.CheckedInAtUtc });

                entity.HasOne(x => x.MemberProfile)
                    .WithMany(x => x.CheckIns)
                    .HasForeignKey(x => x.MemberProfileId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.CheckInDevice)
                    .WithMany()
                    .HasForeignKey(x => x.CheckInDeviceId)
                    .OnDelete(DeleteBehavior.SetNull);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<CheckInDenial>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
                entity.Property(x => x.Reason).HasConversion<string>().HasMaxLength(30);
                entity.Property(x => x.Message).IsRequired().HasMaxLength(300);
                entity.HasIndex(x => new { x.TenantId, x.AttemptedAtUtc });
                entity.HasIndex(x => new { x.MemberProfileId, x.AttemptedAtUtc });

                entity.HasOne(x => x.MemberProfile)
                    .WithMany()
                    .HasForeignKey(x => x.MemberProfileId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.CheckInDevice)
                    .WithMany()
                    .HasForeignKey(x => x.CheckInDeviceId)
                    .OnDelete(DeleteBehavior.SetNull);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<CheckInDevice>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).IsRequired().HasMaxLength(100);
                entity.Property(x => x.KeyHash).HasMaxLength(100);
                entity.Property(x => x.PairingCodeHash).HasMaxLength(100);
                entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
                entity.Property(x => x.SerialNumber).HasMaxLength(40);
                entity.Property(x => x.AllowedIp).HasMaxLength(45);
                entity.HasIndex(x => x.TenantId);

                // A terminal names itself by serial number before any tenant is known.
                entity.HasIndex(x => x.SerialNumber)
                    .IsUnique()
                    .HasFilter("\"SerialNumber\" IS NOT NULL");

                // Looked up across tenants by the device itself (ICheckInDeviceDirectory).
                entity.HasIndex(x => x.KeyHash)
                    .IsUnique()
                    .HasFilter("\"KeyHash\" IS NOT NULL");
                entity.HasIndex(x => x.PairingCodeHash)
                    .IsUnique()
                    .HasFilter("\"PairingCodeHash\" IS NOT NULL");

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<TerminalUser>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Pin).IsRequired().HasMaxLength(24);
                entity.Property(x => x.TerminalName).HasMaxLength(100);
                entity.HasIndex(x => new { x.TenantId, x.Pin }).IsUnique();
                entity.HasIndex(x => new { x.TenantId, x.MemberProfileId })
                    .IsUnique()
                    .HasFilter("\"MemberProfileId\" IS NOT NULL");

                entity.HasOne(x => x.MemberProfile)
                    .WithMany()
                    .HasForeignKey(x => x.MemberProfileId)
                    .OnDelete(DeleteBehavior.SetNull);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<TerminalUserSync>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Pin).IsRequired().HasMaxLength(24);
                entity.Property(x => x.Name).IsRequired().HasMaxLength(100);
                entity.HasIndex(x => new { x.CheckInDeviceId, x.Pin }).IsUnique();

                entity.HasOne<CheckInDevice>()
                    .WithMany()
                    .HasForeignKey(x => x.CheckInDeviceId)
                    .OnDelete(DeleteBehavior.Cascade);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<TerminalCommand>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Text).IsRequired().HasMaxLength(2000);
                entity.Property(x => x.Pin).HasMaxLength(24);
                entity.HasIndex(x => new { x.CheckInDeviceId, x.CompletedAtUtc });

                entity.HasOne<CheckInDevice>()
                    .WithMany()
                    .HasForeignKey(x => x.CheckInDeviceId)
                    .OnDelete(DeleteBehavior.Cascade);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<MemberPhoto>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.ContentType).IsRequired().HasMaxLength(50);
                entity.Property(x => x.Data).IsRequired();
                entity.HasIndex(x => x.MemberProfileId).IsUnique();

                entity.HasOne<MemberProfile>()
                    .WithMany()
                    .HasForeignKey(x => x.MemberProfileId)
                    .OnDelete(DeleteBehavior.Cascade);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<Invoice>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Number).IsRequired().HasMaxLength(30);
                entity.Property(x => x.Description).IsRequired().HasMaxLength(300);
                entity.Property(x => x.Currency).IsRequired().HasMaxLength(3);
                entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
                entity.Property(x => x.CheckoutSessionId).HasMaxLength(200);
                entity.Property(x => x.CheckoutProvider).HasMaxLength(30);
                Money(entity.Property(x => x.Amount));
                entity.Property(x => x.Version).IsRowVersion();
                entity.Ignore(x => x.IsUnpaid);

                entity.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
                entity.HasIndex(x => new { x.TenantId, x.Status, x.DueDate });
                entity.HasIndex(x => new { x.MemberProfileId, x.PeriodStart });

                entity.HasOne(x => x.MemberProfile)
                    .WithMany(x => x.Invoices)
                    .HasForeignKey(x => x.MemberProfileId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<MembershipPlan>()
                    .WithMany()
                    .HasForeignKey(x => x.MembershipPlanId)
                    .OnDelete(DeleteBehavior.SetNull);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<Payment>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Currency).IsRequired().HasMaxLength(3);
                entity.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
                entity.Property(x => x.Provider).IsRequired().HasMaxLength(30);
                entity.Property(x => x.ProviderReference).HasMaxLength(200);
                Money(entity.Property(x => x.Amount));
                entity.HasIndex(x => new { x.TenantId, x.PaidAtUtc });

                entity.HasOne(x => x.Invoice)
                    .WithMany()
                    .HasForeignKey(x => x.InvoiceId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<MemberProfile>()
                    .WithMany()
                    .HasForeignKey(x => x.MemberProfileId)
                    .OnDelete(DeleteBehavior.Restrict);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<InvoiceReminder>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Kind).IsRequired().HasMaxLength(60);
                entity.HasIndex(x => new { x.InvoiceId, x.Kind }).IsUnique();

                entity.HasOne(x => x.Invoice)
                    .WithMany()
                    .HasForeignKey(x => x.InvoiceId)
                    .OnDelete(DeleteBehavior.Cascade);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            // One provider account per gym; keys are stored encrypted.
            modelBuilder.Entity<GymPaymentAccount>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Provider).IsRequired().HasMaxLength(30);
                entity.Property(x => x.ApiKeyEncrypted).IsRequired().HasMaxLength(2000);
                entity.Property(x => x.SecretEncrypted).IsRequired().HasMaxLength(2000);
                entity.Property(x => x.WebhookSecretEncrypted).HasMaxLength(2000);
                entity.Property(x => x.ApiKeyHint).IsRequired().HasMaxLength(20);
                entity.HasIndex(x => x.TenantId).IsUnique();

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<ProcessedWebhookEvent>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasMaxLength(200);
                entity.Property(x => x.Provider).IsRequired().HasMaxLength(30);
            });

            modelBuilder.Entity<Exercise>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).IsRequired().HasMaxLength(100);
                entity.Property(x => x.MuscleGroup).IsRequired().HasMaxLength(50);
                entity.Property(x => x.Equipment).HasMaxLength(100);
                entity.Property(x => x.Instructions).HasMaxLength(2000);
                entity.HasIndex(x => new { x.TenantId, x.Name });

                entity.HasOne<Tenant>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);

                // The shared library (TenantId null) is visible to every gym.
                entity.HasQueryFilter(x =>
                    x.TenantId == null ||
                    (CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value));
            });

            modelBuilder.Entity<WorkoutRoutine>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).IsRequired().HasMaxLength(100);
                entity.Property(x => x.Goal).HasMaxLength(200);
                entity.Property(x => x.Notes).HasMaxLength(2000);
                entity.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
                entity.HasIndex(x => new { x.MemberProfileId, x.IsActive });

                entity.HasMany(x => x.Days)
                    .WithOne()
                    .HasForeignKey(x => x.WorkoutRoutineId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<MemberProfile>()
                    .WithMany()
                    .HasForeignKey(x => x.MemberProfileId)
                    .OnDelete(DeleteBehavior.Cascade);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<RoutineDay>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).IsRequired().HasMaxLength(100);

                entity.HasMany(x => x.Exercises)
                    .WithOne()
                    .HasForeignKey(x => x.RoutineDayId)
                    .OnDelete(DeleteBehavior.Cascade);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<RoutineExercise>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Reps).IsRequired().HasMaxLength(20);
                entity.Property(x => x.Notes).HasMaxLength(300);
                entity.Property(x => x.TargetWeightKg).HasPrecision(6, 2);

                entity.HasOne(x => x.Exercise)
                    .WithMany()
                    .HasForeignKey(x => x.ExerciseId)
                    .OnDelete(DeleteBehavior.Restrict);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<WorkoutLog>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Notes).HasMaxLength(1000);
                entity.HasIndex(x => new { x.MemberProfileId, x.PerformedOn });

                entity.HasMany(x => x.Sets)
                    .WithOne()
                    .HasForeignKey(x => x.WorkoutLogId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<MemberProfile>()
                    .WithMany()
                    .HasForeignKey(x => x.MemberProfileId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<WorkoutRoutine>()
                    .WithMany()
                    .HasForeignKey(x => x.WorkoutRoutineId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne<RoutineDay>()
                    .WithMany()
                    .HasForeignKey(x => x.RoutineDayId)
                    .OnDelete(DeleteBehavior.SetNull);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<WorkoutSet>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.WeightKg).HasPrecision(6, 2);

                entity.HasOne(x => x.Exercise)
                    .WithMany()
                    .HasForeignKey(x => x.ExerciseId)
                    .OnDelete(DeleteBehavior.Restrict);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<BodyMetric>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.WeightKg).HasPrecision(5, 1);
                entity.Property(x => x.BodyFatPercent).HasPrecision(4, 1);
                entity.Property(x => x.Notes).HasMaxLength(500);
                entity.HasIndex(x => new { x.MemberProfileId, x.RecordedOn });

                entity.HasOne<MemberProfile>()
                    .WithMany()
                    .HasForeignKey(x => x.MemberProfileId)
                    .OnDelete(DeleteBehavior.Cascade);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<FitnessGoal>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Description).IsRequired().HasMaxLength(300);
                entity.Property(x => x.TargetUnit).HasMaxLength(20);
                entity.Property(x => x.TargetValue).HasPrecision(8, 2);
                entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
                entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
                entity.HasIndex(x => new { x.MemberProfileId, x.Status });

                entity.HasOne<MemberProfile>()
                    .WithMany()
                    .HasForeignKey(x => x.MemberProfileId)
                    .OnDelete(DeleteBehavior.Cascade);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<Notification>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Title).IsRequired().HasMaxLength(150);
                entity.Property(x => x.Body).IsRequired().HasMaxLength(4000);
                entity.Property(x => x.Link).HasMaxLength(300);
                entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
                entity.HasIndex(x => new { x.TenantId, x.UserId, x.ReadAtUtc });
                entity.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAtUtc });

                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<Announcement>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Title).IsRequired().HasMaxLength(150);
                entity.Property(x => x.Body).IsRequired().HasMaxLength(4000);
                entity.Property(x => x.SentByName).IsRequired().HasMaxLength(150);
                entity.Property(x => x.Audience).HasConversion<string>().HasMaxLength(20);
                entity.HasIndex(x => new { x.TenantId, x.SentAtUtc });

                TenantOwned(entity);
                entity.HasQueryFilter(x => CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value);
            });

            modelBuilder.Entity<EmailOutboxMessage>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.ToEmail).IsRequired().HasMaxLength(256);
                entity.Property(x => x.ToName).HasMaxLength(150);
                entity.Property(x => x.Subject).IsRequired().HasMaxLength(300);
                entity.Property(x => x.HtmlBody).IsRequired();
                entity.Property(x => x.LastError).HasMaxLength(2000);
                entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
                entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
            });

            modelBuilder.Entity<PasswordResetToken>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.TokenHash).IsRequired().HasMaxLength(100);
                entity.HasIndex(x => x.TokenHash).IsUnique();

                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        private static void TenantOwned<TEntity>(EntityTypeBuilder<TEntity> entity)
            where TEntity : class
        {
            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey("TenantId")
                .OnDelete(DeleteBehavior.Cascade);
        }

        /// <summary>Money is numeric(12,2): exact, never floating point.</summary>
        private static void Money(PropertyBuilder<decimal> property) =>
            property.HasPrecision(12, 2);
    }
}
