using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RioCommerce.Infrastructure.Data;

public class RioCommerceDbContext : DbContext, IDataProtectionKeyContext
{
    public RioCommerceDbContext(DbContextOptions<RioCommerceDbContext> options) : base(options) { }

    // Stores the ASP.NET Core Data Protection key ring in the DB so encrypted secrets (SMTP/SMS/payment
    // credentials in AppSettings) stay decryptable across restarts and machines — everything lives in Postgres.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<User> Users => Set<User>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductVideo> ProductVideos => Set<ProductVideo>();
    public DbSet<ProductMode> ProductModes => Set<ProductMode>();
    public DbSet<ProductOptionGroup> ProductOptionGroups => Set<ProductOptionGroup>();
    public DbSet<ProductOptionGroupItem> ProductOptionGroupItems => Set<ProductOptionGroupItem>();
    public DbSet<ProductInclusion> ProductInclusions => Set<ProductInclusion>();
    public DbSet<ProductFaculty> ProductFaculty => Set<ProductFaculty>();
    public DbSet<ProductSubject> ProductSubjects => Set<ProductSubject>();
    public DbSet<ProductBookPreview> ProductBookPreviews => Set<ProductBookPreview>();
    public DbSet<BookPreviewPage> BookPreviewPages => Set<BookPreviewPage>();

    // ── Serial-key integration (RioPlay / Valence / …) ──
    public DbSet<SerialKeyRecord> SerialKeyRecords => Set<SerialKeyRecord>();
    public DbSet<RioPlayTenant> RioPlayTenants => Set<RioPlayTenant>();
    public DbSet<ProductSerialKeyConfig> ProductSerialKeyConfigs => Set<ProductSerialKeyConfig>();
    public DbSet<ValencePack> ValencePacks => Set<ValencePack>();
    public DbSet<SuperclassSettings> SuperclassSettings => Set<SuperclassSettings>();
    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();

    // ── Application logs (every feature funnels structured events here) ──
    public DbSet<AppLog> AppLogs => Set<AppLog>();

    // ── Invoicing — DbSet<Invoice> already declared below (pre-existing). We only need to
    //    add the new line-items table. ──
    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();
    public DbSet<ProductTestimonial> ProductTestimonials => Set<ProductTestimonial>();
    public DbSet<UrlRedirect> UrlRedirects => Set<UrlRedirect>();
    public DbSet<Faculty> Faculty => Set<Faculty>();
    public DbSet<FacultySharingRule> FacultySharingRules => Set<FacultySharingRule>();
    // ── Product faculty share: global settings + the earned-share ledger (see 0027_faculty_share.sql) ──
    public DbSet<FacultySettings> FacultySettings => Set<FacultySettings>();
    public DbSet<FacultyShareEntry> FacultyShareEntries => Set<FacultyShareEntry>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderNote> OrderNotes => Set<OrderNote>();
    public DbSet<NoteAttachment> NoteAttachments => Set<NoteAttachment>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<RefundItem> RefundItems => Set<RefundItem>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();
    public DbSet<ApprovalComment> ApprovalComments => Set<ApprovalComment>();
    public DbSet<Payout> Payouts => Set<Payout>();
    public DbSet<PayoutItem> PayoutItems => Set<PayoutItem>();
    public DbSet<SettlementBatch> SettlementBatches => Set<SettlementBatch>();
    public DbSet<SettlementAdjustment> SettlementAdjustments => Set<SettlementAdjustment>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OrderInstallmentPlan> OrderInstallmentPlans => Set<OrderInstallmentPlan>();
    public DbSet<OrderInstallment> OrderInstallments => Set<OrderInstallment>();
    public DbSet<InstallmentSettings> InstallmentSettings => Set<InstallmentSettings>();
    public DbSet<LegacyProductMap> LegacyProductMaps => Set<LegacyProductMap>();
    public DbSet<LegacyUserMap> LegacyUserMaps => Set<LegacyUserMap>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<Affiliate> Affiliates => Set<Affiliate>();
    public DbSet<AffiliateReferral> AffiliateReferrals => Set<AffiliateReferral>();
    public DbSet<NewsletterSubscriber> NewsletterSubscribers => Set<NewsletterSubscriber>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Franchise> Franchises => Set<Franchise>();
    public DbSet<FranchiseLedgerEntry> FranchiseLedger => Set<FranchiseLedgerEntry>();
    public DbSet<FranchiseCommission> FranchiseCommissions => Set<FranchiseCommission>();
    public DbSet<FranchiseSettings> FranchiseSettings => Set<FranchiseSettings>();
    public DbSet<FranchiseCommissionEntry> FranchiseCommissionEntries => Set<FranchiseCommissionEntry>();
    public DbSet<WalletRecharge> WalletRecharges => Set<WalletRecharge>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<BlogSummaryPoint> BlogSummaryPoints => Set<BlogSummaryPoint>();
    public DbSet<CmsPage> CmsPages => Set<CmsPage>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<SeoUrlRecord> SeoUrlRecords => Set<SeoUrlRecord>();
    public DbSet<ReferralSource> ReferralSources => Set<ReferralSource>();
    public DbSet<ProductRecommendation> ProductRecommendations => Set<ProductRecommendation>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentItem> ShipmentItems => Set<ShipmentItem>();
    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();

    // ── Order Management & Reporting module (see 0028_order_reporting_module.sql) ──
    public DbSet<FranchiseReInvoice> FranchiseReInvoices => Set<FranchiseReInvoice>();
    public DbSet<FranchiseReInvoiceItem> FranchiseReInvoiceItems => Set<FranchiseReInvoiceItem>();
    public DbSet<TeacherSettlement> TeacherSettlements => Set<TeacherSettlement>();
    public DbSet<TeacherSettlementItem> TeacherSettlementItems => Set<TeacherSettlementItem>();
    public DbSet<TeacherSettlementPayment> TeacherSettlementPayments => Set<TeacherSettlementPayment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<BannerSliderSettings> BannerSliderSettings => Set<BannerSliderSettings>();
    public DbSet<Testimonial> Testimonials => Set<Testimonial>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadNote> LeadNotes => Set<LeadNote>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<SettingHistory> SettingHistory => Set<SettingHistory>();
    public DbSet<AdminNotification> AdminNotifications => Set<AdminNotification>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    // ── 🔒 RBAC — role grants + per-user overrides (see Permissions / PermissionCatalog). ──
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<PermissionAuditLog> PermissionAuditLogs => Set<PermissionAuditLog>();

    // ── Catalog attributes (nopCommerce-style) ──
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<PredefinedProductAttributeValue> PredefinedProductAttributeValues => Set<PredefinedProductAttributeValue>();
    public DbSet<ProductAttributeMapping> ProductAttributeMappings => Set<ProductAttributeMapping>();
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();
    public DbSet<SpecificationAttributeGroup> SpecificationAttributeGroups => Set<SpecificationAttributeGroup>();
    public DbSet<SpecificationAttribute> SpecificationAttributes => Set<SpecificationAttribute>();
    public DbSet<SpecificationAttributeOption> SpecificationAttributeOptions => Set<SpecificationAttributeOption>();
    public DbSet<ProductSpecificationAttribute> ProductSpecificationAttributes => Set<ProductSpecificationAttribute>();
    public DbSet<CheckoutAttribute> CheckoutAttributes => Set<CheckoutAttribute>();
    public DbSet<CheckoutAttributeValue> CheckoutAttributeValues => Set<CheckoutAttributeValue>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<SpecialPriceAudit> SpecialPriceAudits => Set<SpecialPriceAudit>();
    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();
    public DbSet<ScheduledTaskRun> ScheduledTaskRuns => Set<ScheduledTaskRun>();

    // ── School module (vijaypath branch) ──
    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<State> States => Set<State>();
    public DbSet<District> Districts => Set<District>();
    public DbSet<Taluka> Talukas => Set<Taluka>();
    public DbSet<School> Schools => Set<School>();
    public DbSet<SchoolUser> SchoolUsers => Set<SchoolUser>();
    public DbSet<SchoolStudent> SchoolStudents => Set<SchoolStudent>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w
            .Ignore(RelationalEventId.PendingModelChangesWarning)
            // Order has a soft-delete query filter; its dependents (Payment/Shipment/Note/…) are only ever
            // loaded via the Order aggregate (which honours the filter), so this interaction is intentional.
            .Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PostgreSQL extensions
        modelBuilder.HasPostgresExtension("uuid-ossp");
        modelBuilder.HasPostgresExtension("citext");

        // PostgreSQL enums
        modelBuilder.HasPostgresEnum<CourseLevel>();
        modelBuilder.HasPostgresEnum<CourseType>();
        modelBuilder.HasPostgresEnum<OrderStatus>();
        modelBuilder.HasPostgresEnum<OrderSource>();
        modelBuilder.HasPostgresEnum<PaymentMode>();
        modelBuilder.HasPostgresEnum<PaymentStatus>();
        modelBuilder.HasPostgresEnum<ProductStatus>();
        modelBuilder.HasPostgresEnum<SharingType>();
        modelBuilder.HasPostgresEnum<LectureMode>();
        modelBuilder.HasPostgresEnum<LeadStatus>();
        modelBuilder.HasPostgresEnum<ContentStatus>();
        modelBuilder.HasPostgresEnum<ReviewStatus>();
        modelBuilder.HasPostgresEnum<FranchiseStatus>();
        modelBuilder.HasPostgresEnum<CommissionType>();
        modelBuilder.HasPostgresEnum<CustomerType>();
        modelBuilder.HasPostgresEnum<AttributeControlType>();
        modelBuilder.HasPostgresEnum<SchoolType>();
        modelBuilder.HasPostgresEnum<SchoolUserRole>();
        modelBuilder.HasPostgresEnum<Gender>();

        // Set default values for ALL BaseEntity derived types via SQL
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).Property("Id").HasDefaultValueSql("gen_random_uuid()");
                modelBuilder.Entity(entityType.ClrType).Property("CreatedAt").HasDefaultValueSql("NOW()");
                modelBuilder.Entity(entityType.ClrType).Property("UpdatedAt").HasDefaultValueSql("NOW()");
            }
        }

        // AppSetting UpdatedAt default
        modelBuilder.Entity<AppSetting>().Property(a => a.UpdatedAt).HasDefaultValueSql("NOW()");

        // Franchise onboarding defaults — so existing rows backfill cleanly on the migration that adds these columns.
        modelBuilder.Entity<Franchise>().Property(f => f.Status).HasDefaultValue(FranchiseStatus.Approved);
        modelBuilder.Entity<Franchise>().Property(f => f.RegisteredAt).HasDefaultValueSql("NOW()");

        // Per-franchise per-product commission — only one active rule per (franchise, product).
        modelBuilder.Entity<FranchiseCommission>()
            .HasIndex(c => new { c.FranchiseId, c.ProductId })
            .IsUnique();
        modelBuilder.Entity<FranchiseCommissionEntry>()
            .HasIndex(c => new { c.FranchiseId, c.EarnedAt });

        modelBuilder.Entity<WalletRecharge>().HasIndex(r => new { r.FranchiseId, r.CreatedAt });

        // Lead CRM follow-up notes — always read newest-first for one lead, so index that access path.
        // Cascade delete keeps a lead's history from outliving the lead itself.
        modelBuilder.Entity<LeadNote>().HasIndex(n => new { n.LeadId, n.CreatedAt });
        modelBuilder.Entity<LeadNote>()
            .HasOne(n => n.Lead).WithMany(l => l.Notes)
            .HasForeignKey(n => n.LeadId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<WalletRecharge>().HasIndex(r => r.GatewayOrderId);

        // Order.CustomerType default — backfills existing rows as Individual on the migration that adds it.
        modelBuilder.Entity<Order>().Property(o => o.CustomerType).HasDefaultValue(CustomerType.Individual);

        // Gateway-reported payment instrument ("UPI", "Credit Card", …) on the per-attempt Payment
        // row. Payment has no IEntityTypeConfiguration of its own — every other column maps by
        // convention — so the widths (matching migration 0021) are declared here.
        modelBuilder.Entity<Payment>().Property(p => p.GatewayPaymentMode).HasMaxLength(40);
        modelBuilder.Entity<Payment>().Property(p => p.GatewayPaymentModeDetail).HasMaxLength(120);

        // Phase 6: notification templates are unique per (Key, Channel) so the same event can fan out to
        // distinct Email / SMS / WhatsApp templates.
        modelBuilder.Entity<MessageTemplate>().HasIndex(t => new { t.Key, t.Channel }).IsUnique();

        // Scheduled tasks — TaskKey is the discriminator that maps to a handler in DI,
        // so it must be globally unique. Runs index by StartedAt for the history grid.
        modelBuilder.Entity<ScheduledTask>().HasIndex(t => t.TaskKey).IsUnique();
        modelBuilder.Entity<ScheduledTask>().Property(t => t.TaskKey).HasMaxLength(80).IsRequired();
        modelBuilder.Entity<ScheduledTask>().Property(t => t.Name).HasMaxLength(160).IsRequired();
        modelBuilder.Entity<ScheduledTask>().Property(t => t.Description).HasMaxLength(500);
        modelBuilder.Entity<ScheduledTask>().Property(t => t.LastError).HasMaxLength(800);
        modelBuilder.Entity<ScheduledTaskRun>()
            .HasOne(r => r.Task).WithMany().HasForeignKey(r => r.ScheduledTaskId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ScheduledTaskRun>().HasIndex(r => r.StartedAt);
        modelBuilder.Entity<ScheduledTaskRun>().HasIndex(r => new { r.ScheduledTaskId, r.StartedAt });
        modelBuilder.Entity<ScheduledTaskRun>().Property(r => r.Trigger).HasMaxLength(20);
        modelBuilder.Entity<ScheduledTaskRun>().Property(r => r.Output).HasMaxLength(800);
        modelBuilder.Entity<ScheduledTaskRun>().Property(r => r.Error).HasMaxLength(800);

        // ── School module indexes & constraints ──
        modelBuilder.Entity<School>().HasIndex(s => s.UdiseCode).IsUnique();
        modelBuilder.Entity<School>().Property(s => s.UdiseCode).HasMaxLength(20).IsRequired();
        modelBuilder.Entity<School>().Property(s => s.Name).HasMaxLength(300).IsRequired();
        modelBuilder.Entity<SchoolUser>().HasIndex(su => new { su.SchoolId, su.UserId }).IsUnique();
        modelBuilder.Entity<AcademicYear>().HasIndex(ay => ay.Name).IsUnique();
        modelBuilder.Entity<State>().HasIndex(s => s.Code).IsUnique();
        modelBuilder.Entity<District>().HasIndex(d => new { d.StateId, d.Name }).IsUnique();
        modelBuilder.Entity<Taluka>().HasIndex(t => new { t.DistrictId, t.Name }).IsUnique();

        // Apply all IEntityTypeConfiguration from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RioCommerceDbContext).Assembly);

        // Seed data
        Seed.SeedData.Seed(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        foreach (var e in ChangeTracker.Entries<BaseEntity>())
        {
            if (e.State == EntityState.Added)
            {
                if (e.Entity.Id == Guid.Empty) e.Entity.Id = Guid.NewGuid();
                e.Entity.CreatedAt = now;
                e.Entity.UpdatedAt = now;
            }
            else if (e.State == EntityState.Modified)
            {
                e.Entity.UpdatedAt = now;
            }
        }
        return base.SaveChangesAsync(ct);
    }
}
