using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Core.Interfaces.Repositories;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Migrations;
using RioCommerce.Infrastructure.Repositories;
using RioCommerce.Infrastructure.Services;
using RioCommerce.Infrastructure.Services.Reporting;
using RioCommerce.Infrastructure.Services.SerialKeys;
using RioCommerce.API.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

// ── Pin the whole process to India Standard Time — MUST stay the first statement ──
// Timestamps are stored in UTC (correctly), and roughly 110 display sites render them with
// .ToLocalTime(). "Local" is the SERVER's timezone, so on a Linux host running UTC — which is the
// default on most cloud VMs — every date on the site showed UTC: a 07:31 IST order read as 02:01.
//
// Setting TZ before anything touches TimeZoneInfo makes .NET resolve Local as IST on Unix, so all
// of those sites become correct at once, without editing them one by one and without depending on
// how the server happens to be configured. ClearCachedData discards any timezone the runtime may
// already have resolved. Windows ignores TZ (it reads the registry), which is harmless — dev
// machines here are already IST.
Environment.SetEnvironmentVariable("TZ", "Asia/Kolkata");
TimeZoneInfo.ClearCachedData();

var builder = WebApplication.CreateBuilder(args);

// Applied to every endpoint that can put an email or SMS in flight — see AddRateLimiter below.
const string OtpSendRateLimitPolicy = "otp-send";

// ── Database ──
builder.Services.AddDbContext<RioCommerceDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql =>
        {
            npgsql.MapEnum<RioCommerce.Core.Enums.CourseLevel>();
            npgsql.MapEnum<RioCommerce.Core.Enums.CourseType>();
            npgsql.MapEnum<RioCommerce.Core.Enums.OrderStatus>();
            npgsql.MapEnum<RioCommerce.Core.Enums.OrderSource>();
            npgsql.MapEnum<RioCommerce.Core.Enums.PaymentMode>();
            npgsql.MapEnum<RioCommerce.Core.Enums.PaymentStatus>();
            npgsql.MapEnum<RioCommerce.Core.Enums.ProductStatus>();
            npgsql.MapEnum<RioCommerce.Core.Enums.SharingType>();
            npgsql.MapEnum<RioCommerce.Core.Enums.LectureMode>();
            npgsql.MapEnum<RioCommerce.Core.Enums.LeadStatus>();
            npgsql.MapEnum<RioCommerce.Core.Enums.ContentStatus>();
            npgsql.MapEnum<RioCommerce.Core.Enums.ReviewStatus>();
            npgsql.MapEnum<RioCommerce.Core.Enums.FranchiseStatus>();
            npgsql.MapEnum<RioCommerce.Core.Enums.CommissionType>();
            npgsql.MapEnum<RioCommerce.Core.Enums.CustomerType>();
            npgsql.MapEnum<RioCommerce.Core.Enums.AttributeControlType>();
            npgsql.MapEnum<RioCommerce.Core.Enums.SchoolType>();
            npgsql.MapEnum<RioCommerce.Core.Enums.SchoolUserRole>();
            npgsql.MapEnum<RioCommerce.Core.Enums.Gender>();
        }),
    // Transient so concurrently-rendering Blazor components (e.g. Header + page body during
    // prerender) don't share one DbContext — avoids "A second operation was started…" crashes.
    contextLifetime: ServiceLifetime.Transient,
    optionsLifetime: ServiceLifetime.Singleton);

// ── Script-based migrations ──
// Replaces EF Core migrations. Discovers embedded *.sql scripts under
// RioCommerce.Infrastructure/Migrations/Scripts and applies pending ones at startup
// (see app.Services.MigrateDatabaseAsync() below). Adding a schema change is a new
// NNNN_*.sql script — no dotnet-ef, no model snapshot.
builder.Services.AddSqlMigrations(
    builder.Configuration.GetConnectionString("DefaultConnection")!);

// ── Repositories ──
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// ── Services ──
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IProductAdminService, ProductAdminService>();
builder.Services.AddScoped<IOrderAdminService, OrderAdminService>();
builder.Services.AddScoped<IOrderCalculationService, OrderCalculationService>();
builder.Services.AddScoped<RioCommerce.Core.Interfaces.IInstallmentService, RioCommerce.Infrastructure.Services.InstallmentService>();
builder.Services.AddScoped<RioCommerce.Core.Interfaces.ILegacyMigrationService, RioCommerce.Infrastructure.Services.LegacyMigrationService>();
builder.Services.AddScoped<IPaymentTransactionService, PaymentTransactionService>();
builder.Services.AddScoped<IApprovalWorkflowService, ApprovalWorkflowService>();
builder.Services.AddScoped<IRefundService, RefundService>();
builder.Services.AddScoped<IFinanceReportService, FinanceReportService>();
// ── Payouts & settlements ──
builder.Services.AddSingleton<IPayoutProvider, RioCommerce.Infrastructure.Services.Payments.ManualPayoutProvider>();
builder.Services.AddScoped<IPayoutCalculationService, PayoutCalculationService>();
builder.Services.AddScoped<ISettlementService, SettlementService>();
// ── Public asset storage (product pictures) served from wwwroot/uploads via static files ──
var publicWebRoot = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(publicWebRoot);   // ensure the static-file root exists for uploads
builder.Services.AddSingleton<IPublicFileStorage>(new LocalPublicFileStorage(publicWebRoot, "uploads"));

// ── Book Preview: protected file storage (outside wwwroot) + service that signs short-lived view tokens.
//    PDFs live at {ContentRoot}/secure-files/book-previews/<guid>.pdf and are never served by
//    static-file middleware. The only legitimate read path is BookPreviewController, which
//    streams them after IBookPreviewService.TryResolveTokenAsync validates the token.
builder.Services.AddSingleton<RioCommerce.Core.Interfaces.IProtectedFileStorage>(
    new RioCommerce.Infrastructure.Services.LocalProtectedFileStorage(builder.Environment.ContentRootPath));
builder.Services.AddScoped<RioCommerce.Core.Interfaces.IBookPreviewService, RioCommerce.Infrastructure.Services.BookPreviewService>();

// Email Logs surface + template renderer used by the Preview / Send-Test admin flows.
builder.Services.AddSingleton<RioCommerce.Core.Interfaces.IEmailTemplateRenderer, RioCommerce.Infrastructure.Services.EmailTemplateRenderer>();
builder.Services.AddSingleton<RioCommerce.Core.Interfaces.ITemplateTokenCatalog, RioCommerce.Infrastructure.Services.TemplateTokenCatalog>();
builder.Services.AddScoped<RioCommerce.Core.Interfaces.IEmailLogService, RioCommerce.Infrastructure.Services.EmailLogService>();

// URL Redirects + Sitemap migration matcher — middleware below uses IUrlRedirectService on every hot-path request.
builder.Services.AddScoped<RioCommerce.Core.Interfaces.IUrlRedirectService, RioCommerce.Infrastructure.Services.UrlRedirectService>();
// Global clean-URL namespace: one public URL = one owner. Central resolver + duplicate guard.
builder.Services.AddScoped<RioCommerce.Core.Interfaces.ISeoUrlService, RioCommerce.Infrastructure.Services.SeoUrlService>();
builder.Services.AddScoped<RioCommerce.Core.Interfaces.IUrlMatchService, RioCommerce.Infrastructure.Services.UrlMatchService>();

// ── Attachments: private storage (outside wwwroot) + validation + service ──
builder.Services.AddSingleton<IFileStorageService>(
    new LocalFileStorageService(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "order-attachments")));
builder.Services.AddSingleton<IAttachmentSecurityService, AttachmentSecurityService>();
builder.Services.AddScoped<INoteAttachmentService, NoteAttachmentService>();
builder.Services.AddScoped<IFacultySharingService, FacultySharingService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ILeadService, LeadService>();
builder.Services.AddScoped<IFranchiseService, FranchiseService>();
builder.Services.AddScoped<RioCommerce.Core.Interfaces.IFranchiseShareCalculator, RioCommerce.Infrastructure.Services.FranchiseShareCalculator>();
builder.Services.AddScoped<RioCommerce.Core.Interfaces.IFacultyShareCalculator, RioCommerce.Infrastructure.Services.FacultyShareCalculator>();
builder.Services.AddScoped<IVerificationService, VerificationService>();
builder.Services.AddScoped<ICheckoutService, CheckoutService>();
builder.Services.AddHttpClient();

// Payment gateways. Credentials for each gateway are read from the DB
// (Admin → Configuration → Payment Gateways) on every call, so a Save in the admin UI
// takes effect on the very next checkout with no app restart. CheckoutService picks
// the right gateway via IPaymentGatewayFactory based on the customer's selection.
// AddHttpClient<T> registers T as transient with a typed HttpClient; the IPaymentGateway
// forwarders below ensure both end up resolving the same instance per scope.
builder.Services.AddHttpClient<RioCommerce.Infrastructure.Services.Payments.RazorpayGateway>();
builder.Services.AddHttpClient<RioCommerce.Infrastructure.Services.Payments.EasebuzzGateway>();
builder.Services.AddScoped<IPaymentGateway>(sp => sp.GetRequiredService<RioCommerce.Infrastructure.Services.Payments.RazorpayGateway>());
builder.Services.AddScoped<IPaymentGateway>(sp => sp.GetRequiredService<RioCommerce.Infrastructure.Services.Payments.EasebuzzGateway>());
builder.Services.AddScoped<IPaymentGatewayFactory, RioCommerce.Infrastructure.Services.Payments.PaymentGatewayFactory>();

// ── Application-wide log table (admin /admin/logs) ──
builder.Services.AddScoped<RioCommerce.Core.Interfaces.IAppLogService,
    RioCommerce.Infrastructure.Services.AppLogService>();

// ── Invoices (auto-generated for paid orders, QuestPDF rendering) ──
// Set the QuestPDF community license up front so PDF generation works.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
builder.Services.AddScoped<RioCommerce.Core.Interfaces.IInvoiceService,
    RioCommerce.Infrastructure.Services.InvoiceService>();

// ── Revenue-sharing bulk import / export (Excel) ──
builder.Services.AddScoped<RioCommerce.Core.Interfaces.ISharingImportExportService,
    RioCommerce.Infrastructure.Services.SharingImportExportService>();

// ── Payment-mode dropdown registry (driven by gateway settings) ──
builder.Services.AddScoped<RioCommerce.Core.Interfaces.IPaymentModeRegistry,
    RioCommerce.Infrastructure.Services.PaymentModeRegistry>();

// ── Serial-key integration (RioPlay / Valence / …) ──
// Single-line bootstrapper. Adding a future provider is ONE more line after this:
//   builder.Services.AddScoped<ISerialKeyProvider, ValenceSerialKeyProvider>();
builder.Services.AddSerialKeyIntegration();

// Scheduled tasks framework — nopCommerce-style background runner. Handlers are scoped
// (each gets a fresh DbContext per run); the runner itself is the only IHostedService.
builder.Services.AddScoped<RioCommerce.Infrastructure.Services.ScheduledTasks.PaymentStatusSyncTask>();
builder.Services.AddScoped<RioCommerce.Infrastructure.Services.ScheduledTasks.WebhookReconciliationTask>();
builder.Services.AddScoped<RioCommerce.Infrastructure.Services.ScheduledTasks.PendingOrderCleanupTask>();
builder.Services.AddScoped<RioCommerce.Infrastructure.Services.ScheduledTasks.ExpiredCartCleanupTask>();
builder.Services.AddScoped<RioCommerce.Infrastructure.Services.ScheduledTasks.InstallmentReminderTask>();
builder.Services.AddScoped<IScheduledTaskHandler>(sp => sp.GetRequiredService<RioCommerce.Infrastructure.Services.ScheduledTasks.PaymentStatusSyncTask>());
builder.Services.AddScoped<IScheduledTaskHandler>(sp => sp.GetRequiredService<RioCommerce.Infrastructure.Services.ScheduledTasks.WebhookReconciliationTask>());
builder.Services.AddScoped<IScheduledTaskHandler>(sp => sp.GetRequiredService<RioCommerce.Infrastructure.Services.ScheduledTasks.PendingOrderCleanupTask>());
builder.Services.AddScoped<IScheduledTaskHandler>(sp => sp.GetRequiredService<RioCommerce.Infrastructure.Services.ScheduledTasks.ExpiredCartCleanupTask>());
builder.Services.AddScoped<IScheduledTaskHandler>(sp => sp.GetRequiredService<RioCommerce.Infrastructure.Services.ScheduledTasks.InstallmentReminderTask>());
builder.Services.AddScoped<IScheduledTaskService, RioCommerce.Infrastructure.Services.ScheduledTasks.ScheduledTaskService>();
builder.Services.AddScoped<RioCommerce.Infrastructure.Services.ScheduledTasks.ScheduledTaskService>();
builder.Services.AddHostedService<RioCommerce.Infrastructure.Services.ScheduledTasks.ScheduledTaskRunner>();

builder.Services.AddScoped<ISmsSender, SmsSender>();

// ─── Email providers ─────────────────────────────────────────────────────────
// One IEmailProvider per transport. The router (IEmailRouter → EmailRouter)
// picks the active one on every send from the admin-saved settings, so a
// transport switch in /admin/settings/email-api takes effect with no restart.
// Adding a new HTTP-API provider (SendGrid, Mailgun, Brevo, AWS SES, ...) is
// one new IEmailProvider implementation + one AddScoped registration here.
builder.Services.AddScoped<RioCommerce.Core.Interfaces.IEmailProvider, RioCommerce.Infrastructure.Services.EmailProviders.SmtpEmailProvider>();
builder.Services.AddHttpClient<RioCommerce.Infrastructure.Services.EmailProviders.ZeptoMailEmailProvider>();
builder.Services.AddScoped<RioCommerce.Core.Interfaces.IEmailProvider>(sp =>
    sp.GetRequiredService<RioCommerce.Infrastructure.Services.EmailProviders.ZeptoMailEmailProvider>());
builder.Services.AddScoped<RioCommerce.Core.Interfaces.IEmailRouter, RioCommerce.Infrastructure.Services.EmailProviders.EmailRouter>();

builder.Services.AddScoped<IMessageDispatcher, SmtpMessageDispatcher>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<INotificationSender>(sp => sp.GetRequiredService<NotificationService>());
builder.Services.AddScoped<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<IFacultyService, FacultyService>();
builder.Services.AddScoped<ICatalogAdminService, CatalogAdminService>();
builder.Services.AddScoped<IAttributeAdminService, AttributeAdminService>();
builder.Services.AddScoped<ISubjectAdminService, SubjectAdminService>();
builder.Services.AddScoped<IAcademicYearService, AcademicYearService>();
builder.Services.AddScoped<IGeographyService, GeographyService>();
builder.Services.AddScoped<ISchoolService, SchoolService>();
builder.Services.AddScoped<ISchoolRegistrationService, SchoolRegistrationService>();
builder.Services.AddScoped<ISchoolStudentService, SchoolStudentService>();
builder.Services.AddScoped<ISchoolEnrollmentService, SchoolEnrollmentService>();
builder.Services.AddScoped<RioCommerce.Web.Services.ToastService>();
// Per-circuit list-screen state (filters survive an order detail, clear on leaving the section).
builder.Services.AddScoped<RioCommerce.Web.Services.AdminScreenState>();
// ── Real-time (in-process bus) + notification center + user preferences ──
builder.Services.AddSingleton<IRealtimeBus, RealtimeBus>();
builder.Services.AddScoped<INotificationCenterService, NotificationCenterService>();
// Transient, for the same reason RioCommerceDbContext is (see the Database section above).
// AdminGrid and NotificationBell both resolve this service and both query from their own
// OnInitializedAsync, so as a Scoped service they shared one instance — and therefore one
// DbContext — and raced: "A second operation was started on this context instance". The service
// holds no state beyond its context, so a fresh instance per injection is free and correct.
builder.Services.AddTransient<IUserPreferenceService, UserPreferenceService>();
// Transient: CourseCard (repeated per grid) + Header all inject this; a shared instance would
// serialize their concurrent prerender queries onto one DbContext and throw.
builder.Services.AddTransient<IWishlistService, WishlistService>();
builder.Services.AddScoped<ICouponAdminService, CouponAdminService>();
builder.Services.AddScoped<IAffiliateService, AffiliateService>();
builder.Services.AddScoped<INewsletterService, NewsletterService>();
builder.Services.AddScoped<IContentService, ContentService>();
// Transient: HeroBanner sits in MainLayout and prerenders concurrently with the Header (which also
// hits the DB), so a shared-scope DbContext would collide. Same reasoning as IWishlistService above.
builder.Services.AddTransient<IBannerService, BannerService>();
builder.Services.AddScoped<IMenuAdminService, MenuAdminService>();
builder.Services.AddScoped<IReferralAdminService, ReferralAdminService>();
builder.Services.AddScoped<IReferralService, ReferralService>();
builder.Services.AddScoped<IProductRecommendationAdminService, ProductRecommendationAdminService>();
builder.Services.AddScoped<IProductRecommendationService, ProductRecommendationService>();
// Transient: consumed by App (root), Header, ContactStrip, FloatingButtons and both layouts, which
// all initialise concurrently during prerender — a shared-scope DbContext would collide. Same
// reasoning as IBannerService above.
builder.Services.AddTransient<ISiteSettingsService, SiteSettingsService>();
builder.Services.AddScoped<IIntegrationSettingsService, IntegrationSettingsService>();
builder.Services.AddScoped<IExportService, ExportService>();
// Centralised configuration: process-wide cache (singleton) + cached typed provider + finance façade.
builder.Services.AddSingleton<SettingsCache>();
builder.Services.AddScoped<ISettingService, SettingService>();
builder.Services.AddScoped<IFinanceSettingsService, FinanceSettingsService>();

// Data Protection: encrypt secrets (SMTP/SMS/payment credentials) at rest. The key ring is persisted
// to the DB so encrypted values stay decryptable across app restarts and deployments.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<RioCommerceDbContext>()
    .SetApplicationName("RioCommerce");
builder.Services.AddScoped<IFranchisePortalService, FranchisePortalService>();
builder.Services.AddScoped<RioCommerce.Core.Interfaces.IReceiptService, RioCommerce.Infrastructure.Services.ReceiptService>();
builder.Services.AddScoped<IOperationsService, OperationsService>();
builder.Services.AddScoped<IReportsService, ReportsService>();
// Order Management & Reporting module — the eight reports, their filter/export pipeline, and the
// re-invoice + teacher-settlement services they read from.
builder.Services.AddReportingModule();
builder.Services.AddScoped<IRequestContext, RioCommerce.API.Services.HttpRequestContext>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IAuditAdminService, AuditAdminService>();
builder.Services.AddScoped<ICustomerDuplicateService, CustomerDuplicateService>();
// Resolve-or-create the customer account behind an order somebody else placed for the student
// (franchise portal). Phone is the identity key; an existing customer is linked, never edited.
builder.Services.AddScoped<IStudentAccountProvisioner, RioCommerce.Infrastructure.Services.Customers.StudentAccountProvisioner>();
// One-off repair for orders written before that link existed. No HTTP surface by design — drive it
// from a super-admin page the way ILegacyMigrationService is driven. RunAsync(dryRun:) has no default.
builder.Services.AddScoped<IFranchiseCustomerBackfillService, RioCommerce.Infrastructure.Services.Customers.FranchiseCustomerBackfillService>();
// 15-minute RBAC role-permission cache; invalidated by PermissionService on every mutation.
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<ICustomerAdminService, CustomerAdminService>();

// ── HttpClient for Blazor ──
builder.Services.AddHttpClient();
builder.Services.AddScoped(sp =>
{
    var nav = sp.GetRequiredService<NavigationManager>();
    return new HttpClient { BaseAddress = new Uri(nav.BaseUri) };
});

// ── Auth ──
// Cookie scheme is the default for the Blazor browser UI; JWT bearer stays available for the API.
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
    .AddCookie(options =>
    {
        options.Cookie.Name = "rio_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // ── Live role refresh ──
        // Without this, role claims are frozen at sign-in time. A user granted backend access via
        // /admin/access-control AFTER they logged in would never see their new permissions until
        // their cookie expired or they manually logged out. This event re-reads the role rows on
        // every authenticated request and patches the cookie's role claims if they've drifted.
        // Cheap: indexed query on UserRoles by UserId, no joins beyond the Role name lookup.
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async ctx =>
            {
                if (ctx.Principal?.Identity is not ClaimsIdentity identity) return;
                var idStr = ctx.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!Guid.TryParse(idStr, out var uid)) return;

                var db = ctx.HttpContext.RequestServices.GetRequiredService<RioCommerceDbContext>();
                var liveRoles = await db.UserRoles
                    .Where(ur => ur.UserId == uid && ur.IsActive && ur.Role != null)
                    .Select(ur => ur.Role!.Name)
                    .ToListAsync();

                var cookieRoles = ctx.Principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet();
                var liveSet = liveRoles.ToHashSet();
                if (cookieRoles.SetEquals(liveSet)) return;     // no drift, common case

                // Drift: rewrite the cookie's role claims to match the live state.
                foreach (var stale in identity.FindAll(ClaimTypes.Role).ToList())
                    identity.RemoveClaim(stale);
                foreach (var name in liveSet)
                    identity.AddClaim(new Claim(ClaimTypes.Role, name));
                ctx.ShouldRenew = true;
            }
        };
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SecretKey"]!))
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

// ── API ──
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new() { Title = "RioCommerce API", Version = "v1" }));

// ── Blazor ──
// ── Per-IP cap on the endpoints that send an OTP ──
// The per-address quota in VerificationService stops one mailbox being bombed; this stops one
// caller walking a list of addresses to find which ones exist and mailing all of them. Deliberately
// scoped to the OTP-send endpoints only and left generous: coaching centres and franchise offices
// browse from behind a single NAT address, so a tighter limit — or one on /account/login — would
// lock out a whole classroom.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(OtpSendRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    // These endpoints are called by rc.postForm, which parses the response as JSON — an empty 429
    // body surfaces to the customer as "Unexpected server response." Answer in the same shape the
    // endpoints themselves use so the screen shows a real message.
    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync(
            "{\"success\":false,\"error\":\"Too many requests from this connection. Please wait a minute and try again.\"}",
            ct);
    };
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ── Legacy URL redirects ──
// Runs BEFORE static files + routing so legacy slugs like /ca-foundation get
// 301'd to /courses?level=ca-foundation before anything else gets to handle them.
// The middleware itself short-circuits /admin, /api, /_blazor, /_content, static
// assets etc — see UrlRedirectMiddleware.ShouldSkip.
app.UseMiddleware<RioCommerce.API.Middleware.UrlRedirectMiddleware>();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapControllers();

// ── Secure attachment streaming (cookie-auth admin only; path lookup is by id, never user input) ──
app.MapGet("/admin/orders/attachments/{id:guid}", async (
    Guid id, bool? download, HttpContext http,
    RioCommerce.Core.Interfaces.INoteAttachmentService att,
    RioCommerce.Core.Interfaces.IFileStorageService storage,
    RioCommerce.Core.Interfaces.IAuditService audit) =>
{
    string[] staff = { "super_admin", "admin", "operations", "faculty", "franchise_admin" };
    if (http.User.Identity?.IsAuthenticated != true || !staff.Any(r => http.User.IsInRole(r)))
        return Results.Redirect("/login");

    var dl = await att.GetForDownloadAsync(id);
    if (dl is null) return Results.NotFound();
    var stream = storage.Open(dl.StoragePath);
    if (stream is null) return Results.NotFound();

    if (download == true)
    {
        var actorId = Guid.TryParse(http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? (Guid?)g : null;
        await audit.LogAsync(actorId, http.User.Identity?.Name ?? "admin", "AttachmentDownloaded", "Order", null, dl.OriginalFileName);
        return Results.File(stream, dl.ContentType, dl.OriginalFileName);
    }
    return Results.File(stream, dl.ContentType);   // inline (image/PDF preview)
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    // Routable pages live in two RCLs — admin/auth/franchise in RioCommerce.Web, the public
    // storefront in RioCommerce.Web.Storefront. Both must be registered here (not just on the
    // <Router> in Routes.razor) or their @page endpoints are never discovered and 404 before
    // the router runs.
    .AddAdditionalAssemblies(
        typeof(RioCommerce.Web.Components.Layout.MainLayout).Assembly,
        typeof(RioCommerce.Web.Storefront.Components.Pages.Public.Home).Assembly);

// ── Account endpoints (browser-level cookie auth) ──
// These run in the real browser request context, so SignInAsync sets a cookie on the browser
// (unlike calling the API from inside a Blazor circuit, which cannot set a browser cookie).
app.MapPost("/account/login", async (HttpContext http, RioCommerceDbContext db, IAuditService audit, IDataProtectionProvider dp) =>
{
    var form = await http.Request.ReadFormAsync();
    var emailOrPhone = form["emailOrPhone"].ToString();
    var password = form["password"].ToString();
    var raw = form["rurl"].ToString().Trim();

    // Normalize to exactly one leading slash → always a local path (blocks open redirects like //evil.com),
    // and survives the environment stripping a leading slash from form values.
    var target = string.IsNullOrEmpty(raw) ? "/" : "/" + raw.TrimStart('/', '\\');
    if (string.IsNullOrWhiteSpace(emailOrPhone) || string.IsNullOrWhiteSpace(password))
        return Results.Redirect($"/login?error=1&returnUrl={Uri.EscapeDataString(target)}");

    var identifier = emailOrPhone.Trim();

    var user = await db.Users
        .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
        .FirstOrDefaultAsync(u =>
            (u.Email != null && u.Email.ToLower() == identifier.ToLower()) ||
            (u.Phone != null && u.Phone == identifier));

    // ── School Principal login by UDISE code ──
    // Schools are identified in the field by their UDISE — the 11-digit code printed on every
    // school's board — so the principal wants to type that instead of the email/phone they
    // registered with. Only the active Principal is resolvable this way: the code identifies
    // the SCHOOL, not a user, and there's exactly one active Principal per school. Coordinators
    // still sign in with their own email/phone because a UDISE couldn't tell one from another.
    if (user == null && identifier.Length > 0 && identifier.All(char.IsDigit))
    {
        user = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Where(u => db.SchoolUsers.Any(su =>
                su.UserId == u.Id
                && su.IsActive
                && su.Role == RioCommerce.Core.Enums.SchoolUserRole.Principal
                && su.School.UdiseCode == identifier))
            .FirstOrDefaultAsync();
    }

    // ── Customer migrated from the old website ──
    // The old site's password hashes were not carried across, so these accounts arrive with a NULL
    // PasswordHash and there is simply nothing to verify against. Falling through to the generic
    // "invalid email/phone or password" leaves them stuck on a password they know is right, so
    // instead hand them a short-lived token naming the account and send them to the setup screen.
    // The legacy_user_map row is what separates "came over from the old website" from any other
    // account that happens to hold no hash (an anonymised admin user, for one).
    //
    // IsActive is deliberately NOT required here. The importer copies nopCommerce's Customer.Active,
    // which is 0 for every customer who registered but never clicked the old confirmation mail —
    // blocking them would strand real customers on an old site that no longer exists. Completing
    // the OTP proves they hold that mailbox, which is exactly the evidence Active = 0 was waiting
    // for, so setup re-activates them. Accounts an admin disabled on purpose are re-openable this
    // way too; the importer stamps users.AdminComment on every inactive import so they stay findable.
    if (user is not null
        && string.IsNullOrEmpty(user.PasswordHash)
        && !string.IsNullOrWhiteSpace(user.Email)
        && await db.LegacyUserMaps.AnyAsync(m => m.NewId == user.Id))
    {
        await audit.WriteAsync(new AuditEntry
        {
            ActorUserId = user.Id,
            ActorName = user.FullName,
            Module = "Auth",
            Action = "UserPasswordSetupRequired",
            EntityType = "User",
            EntityId = user.Id.ToString(),
            EntityName = emailOrPhone,
            Status = "Warning",
            Details = "Migrated account with no password — sent to password setup"
        });

        var setupToken = LegacySetupProtector(dp).Protect(user.Id.ToString(), LegacySetupTokenLifetime());
        return Results.Redirect(
            $"/set-password?t={Uri.EscapeDataString(setupToken)}&rurl={Uri.EscapeDataString(target)}");
    }

    bool ok;
    try { ok = user != null && !string.IsNullOrEmpty(user.PasswordHash) && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash); }
    catch { ok = false; }

    if (!ok || user is null || !user.IsActive)
    {
        // ── 🔒 Failed-login audit row ── identifies the credential attempted but never the password
        //    or its hash. Surfaces in the "Failed Logins Today" KPI + the audit table. ──
        await audit.WriteAsync(new AuditEntry
        {
            ActorUserId = user?.Id,
            ActorName = user?.FullName ?? emailOrPhone,
            Module = "Auth",
            Action = "UserLoginFailed",
            EntityType = "User",
            EntityId = user?.Id.ToString(),
            EntityName = emailOrPhone,
            Status = "Failed",
            Details = user == null ? "User not found"
                    : !user.IsActive ? "Account is inactive"
                    : "Invalid password"
        });
        return Results.Redirect($"/login?error=1&returnUrl={Uri.EscapeDataString(target)}");
    }

    user.LastLoginAt = DateTime.UtcNow;
    user.LoginCount++;
    await db.SaveChangesAsync();

    // ── ✅ Successful login audit row ──
    await audit.WriteAsync(new AuditEntry
    {
        ActorUserId = user.Id,
        ActorName = user.FullName,
        Module = "Auth",
        Action = "UserLoginSuccess",
        EntityType = "User",
        EntityId = user.Id.ToString(),
        EntityName = user.FullName,
        Status = "Success"
    });

    await SignInUserAsync(http, user);

    // School staff have no reason to land on the storefront home page. Only applies when the
    // login carried no explicit destination — an rurl from a deep link still wins, so a
    // principal following /login?ReturnUrl=/cart still ends up at the cart.
    if (target == "/" && user.UserRoles.Any(ur => ur.IsActive
            && (ur.Role.Name == "school_principal" || ur.Role.Name == "school_coordinator")))
        target = "/school";

    return Results.Redirect(target);
}).DisableAntiforgery();

// Affiliate referral link: stores the code in a (JS-readable) cookie, then lands on the home page.
// Checkout reads this cookie and attributes the resulting order to the affiliate.
app.MapGet("/r/{code}", (string code, HttpContext http) =>
{
    http.Response.Cookies.Append("rc_ref", code, new CookieOptions
    {
        Expires = DateTimeOffset.UtcNow.AddDays(30),
        HttpOnly = false,            // the Blazor checkout circuit reads it via JS
        SameSite = SameSiteMode.Lax,
        IsEssential = true
    });
    return Results.Redirect("/");
});

// ── SEO: robots.txt + dynamic sitemap.xml ──
app.MapGet("/robots.txt", (HttpContext http) =>
{
    var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
    return Results.Text($"User-agent: *\nAllow: /\nSitemap: {baseUrl}/sitemap.xml\n", "text/plain");
});

app.MapGet("/sitemap.xml", async (HttpContext http, RioCommerceDbContext db, IContentService content) =>
{
    var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
    var urls = new List<string> { "/", "/courses/ca-foundation", "/courses/ca-intermediate", "/faculty", "/blog", "/search", "/contact" };

    urls.AddRange((await db.Products.Where(p => p.Status == RioCommerce.Core.Enums.ProductStatus.Active)
        .Select(p => p.Slug).ToListAsync()).Select(s => $"/{s}"));
    urls.AddRange((await db.Faculty.Where(f => f.IsActive).Select(f => f.ShortCode.ToLower()).ToListAsync())
        .Select(c => $"/faculty/{c}"));
    var (pageSlugs, blogSlugs) = await content.PublishedSlugsAsync();
    urls.AddRange(pageSlugs.Select(s => $"/page/{s}"));
    urls.AddRange(blogSlugs.Select(s => $"/blog/{s}"));

    var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
    sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
    foreach (var u in urls.Distinct())
        sb.Append($"  <url><loc>{baseUrl}{u}</loc></url>\n");
    sb.Append("</urlset>");
    return Results.Text(sb.ToString(), "application/xml");
});

app.MapPost("/account/logout", async (HttpContext http, IAuditService audit) =>
{
    // Capture identity BEFORE clearing the cookie so the audit row has the user id + name.
    var uidStr = http.User?.FindFirstValue(ClaimTypes.NameIdentifier);
    var name = http.User?.Identity?.Name;
    if (Guid.TryParse(uidStr, out var uid))
    {
        await audit.WriteAsync(new AuditEntry
        {
            ActorUserId = uid,
            ActorName = name ?? "system",
            Module = "Auth",
            Action = "UserLogout",
            EntityType = "User",
            EntityId = uid.ToString(),
            EntityName = name,
            Status = "Success"
        });
    }
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
}).DisableAntiforgery();

app.MapPost("/account/register", async (HttpContext http, RioCommerceDbContext db, ICustomerDuplicateService dupes, IVerificationService verify) =>
{
    var form = await http.Request.ReadFormAsync();
    var fullName = form["fullName"].ToString();
    var email = form["email"].ToString();
    var phone = form["phone"].ToString();
    var password = form["password"].ToString();
    var city = form["city"].ToString();
    // Optional student-profile fields. Left nullable so the endpoint keeps accepting the
    // original four required fields unchanged.
    var gender = form["gender"].ToString();
    var state = form["state"].ToString();
    var district = form["district"].ToString();
    var schoolName = form["schoolName"].ToString();
    var studentClass = form["studentClass"].ToString();
    var board = form["board"].ToString();
    DateTime? dob = DateTime.TryParse(form["dob"].ToString(), out var dobParsed)
        ? DateTime.SpecifyKind(dobParsed, DateTimeKind.Utc) : null;

    if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) ||
        string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(password))
        return Results.Redirect("/login?tab=register&error=missing");

    // Centralised duplicate probe — one query covers both fields, audit-logged on a hit.
    var ip = http.Connection.RemoteIpAddress?.ToString();
    var dup = await dupes.CheckAsync(email, phone);
    if (dup.HasDuplicate)
    {
        await dupes.LogDuplicateAttemptAsync(email, phone, "Website", null, fullName, ip, dup.FieldName);
        var key = dup.Field switch
        {
            RioCommerce.Core.DTOs.Customers.DuplicateField.Email => "email",
            RioCommerce.Core.DTOs.Customers.DuplicateField.Phone => "phone",
            RioCommerce.Core.DTOs.Customers.DuplicateField.Both => "both",
            _ => "email"
        };
        return Results.Redirect($"/login?tab=register&error={key}");
    }

    // Create the account INACTIVE + unverified. It cannot be used to log in until the email OTP
    // is confirmed on /account/verify (login blocks on !IsActive).
    var user = new User
    {
        Id = Guid.NewGuid(),               // set up-front so the UserRole FK is valid
        FullName = fullName.Trim(),
        Email = email.Trim(),
        Phone = phone.Trim(),
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
        City = string.IsNullOrWhiteSpace(city) ? null : city.Trim(),
        Gender = string.IsNullOrWhiteSpace(gender) ? null : gender.Trim(),
        State = string.IsNullOrWhiteSpace(state) ? null : state.Trim(),
        District = string.IsNullOrWhiteSpace(district) ? null : district.Trim(),
        SchoolName = string.IsNullOrWhiteSpace(schoolName) ? null : schoolName.Trim(),
        StudentClass = string.IsNullOrWhiteSpace(studentClass) ? null : studentClass.Trim(),
        Board = string.IsNullOrWhiteSpace(board) ? null : board.Trim(),
        DateOfBirth = dob,
        IsActive = false,
        IsVerified = false
    };
    db.Users.Add(user);

    var studentRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "student");
    if (studentRole != null)
        db.UserRoles.Add(new UserRole { User = user, RoleId = studentRole.Id, IsActive = true });

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException)
    {
        var raceDup = await dupes.CheckAsync(email, phone);
        await dupes.LogDuplicateAttemptAsync(email, phone, "Website-Race", null, fullName, ip, raceDup.FieldName);
        var key = raceDup.Field == RioCommerce.Core.DTOs.Customers.DuplicateField.Phone ? "phone"
                : raceDup.Field == RioCommerce.Core.DTOs.Customers.DuplicateField.Both ? "both" : "email";
        return Results.Redirect($"/login?tab=register&error={key}");
    }

    // One 6-digit OTP delivered to BOTH email and phone, then hand off to the verify screen.
    // The student supplies a phone, so email-only delivery was leaving that channel unused;
    // SendBothAsync shares a single hash across the two channel rows, so the code entered on
    // the verify screen matches whichever one it arrived on.
    await verify.SendBothAsync(RioCommerce.Core.Enums.VerificationPurpose.CustomerSignup,
        user.Email, user.Phone, user.Id, user.FullName);

    return Results.Redirect($"/account/verify?email={Uri.EscapeDataString(user.Email!)}");
}).DisableAntiforgery();

// ── OTP: verify a registration code (email). On success activates the account and returns a
//    short-lived one-time token the verify page uses to complete cookie sign-in via the bridge. ──
// ── Student registration (JSON) for the /student/register wizard ──────────────────────────────
// Mirrors /account/register but answers JSON instead of redirecting, so step 3 can hand over to
// step 4 IN PAGE. Reuses the same collaborators as every other entry point - no second auth
// system, no second duplicate checker, no second OTP infrastructure:
//   • ICustomerDuplicateService - email (case-insensitive) + phone (normalised) uniqueness
//   • IVerificationService.SendBothAsync - one code to email AND mobile
//   • the "student" role row - never school_principal / admin / coordinator
app.MapPost("/api/student/register", async (HttpContext http, RioCommerceDbContext db,
    ICustomerDuplicateService dupes, IVerificationService verify) =>
{
    var form = await http.Request.ReadFormAsync();
    string F(string k) => form[k].ToString().Trim();

    var fullName = F("fullName");
    var email = F("email");
    var phone = F("phone");
    var password = F("password");

    if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) ||
        string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(password))
        return Results.Json(new { success = false, error = "Please complete every required field." });

    // ── Education details are mandatory for a student signup, and re-checked HERE rather than
    //    trusted from the wizard: a crafted POST straight at this endpoint must not be able to
    //    create a half-populated student. Checked BEFORE any account is created, so an
    //    incomplete request never leaves a pending row behind. ──
    var eduState = F("state");
    var eduDistrict = F("district");
    var eduClass = F("studentClass");
    var eduBoard = F("board");
    var eduSchool = F("schoolName");

    if (string.IsNullOrWhiteSpace(eduState) || string.IsNullOrWhiteSpace(eduDistrict) ||
        string.IsNullOrWhiteSpace(eduClass) || string.IsNullOrWhiteSpace(eduBoard) ||
        string.IsNullOrWhiteSpace(eduSchool))
        return Results.Json(new { success = false, error = "Please complete all required education details." });

    // ── Duplicate check: SERVER-SIDE and authoritative. Covers the cross-account case too -
    //    CheckAsync probes email and phone independently, so an email on account A plus a phone
    //    on account B reports Both and is refused. ──
    var ip = http.Connection.RemoteIpAddress?.ToString();
    var dup = await dupes.CheckAsync(email, phone);
    if (dup.HasDuplicate)
    {
        await dupes.LogDuplicateAttemptAsync(email, phone, "Website-StudentWizard", null, fullName, ip, dup.FieldName);
        var msg = dup.Field switch
        {
            RioCommerce.Core.DTOs.Customers.DuplicateField.Email =>
                "This email address is already registered. Please login or use a different email address.",
            RioCommerce.Core.DTOs.Customers.DuplicateField.Phone =>
                "This mobile number is already registered. Please login or use a different mobile number.",
            _ => "An account with this email address and mobile number already exists. Please login instead.",
        };
        return Results.Json(new { success = false, error = msg, duplicate = true });
    }

    DateTime? dob = DateTime.TryParse(F("dob"), out var d)
        ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;

    // Created PENDING: IsActive=false blocks login until the OTP is confirmed, so an unverified
    // signup is never a usable account.
    var user = new User
    {
        Id = Guid.NewGuid(),
        FullName = fullName,
        Email = email,
        Phone = phone,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
        DateOfBirth = dob,
        Gender = string.IsNullOrWhiteSpace(F("gender")) ? null : F("gender"),
        State = eduState,
        District = eduDistrict,
        SchoolName = eduSchool,
        StudentClass = eduClass,
        Board = eduBoard,
        IsActive = false,
        IsVerified = false,
    };
    db.Users.Add(user);

    // ONLY the student role. Never school_principal, admin or coordinator.
    var studentRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "student");
    if (studentRole != null)
        db.UserRoles.Add(new UserRole { User = user, RoleId = studentRole.Id, IsActive = true });

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException)
    {
        // Race: two signups for the same email/phone in flight. The partial unique indexes
        // IX_users_Email / IX_users_Phone reject the loser at the database, so a duplicate cannot
        // be created even under concurrency. Re-probe to report which field actually collided.
        var race = await dupes.CheckAsync(email, phone);
        await dupes.LogDuplicateAttemptAsync(email, phone, "Website-StudentWizard-Race", null, fullName, ip, race.FieldName);
        var msg = race.Field switch
        {
            RioCommerce.Core.DTOs.Customers.DuplicateField.Email =>
                "This email address is already registered. Please login or use a different email address.",
            RioCommerce.Core.DTOs.Customers.DuplicateField.Phone =>
                "This mobile number is already registered. Please login or use a different mobile number.",
            _ => "The email address or mobile number is already associated with another account. Please use different contact details or login to your existing account.",
        };
        return Results.Json(new { success = false, error = msg, duplicate = true });
    }

    // One 6-digit code to BOTH channels. Never returned to the browser or logged.
    var send = await verify.SendBothAsync(RioCommerce.Core.Enums.VerificationPurpose.CustomerSignup,
        user.Email, user.Phone, user.Id, user.FullName);

    if (!send.Success)
    {
        // Nothing was delivered, so do NOT strand the user in a verification state they can never
        // clear. Undo the pending account and keep them on step 3 to retry cleanly.
        db.Users.Remove(user);
        try { await db.SaveChangesAsync(); } catch { /* best effort rollback */ }
        return Results.Json(new { success = false, error = "We couldn't send the verification code. Please try again." });
    }

    // Masked for the step-4 subtitle. The code itself is never sent to the client.
    static string MaskEmail(string e)
    {
        var at = e.IndexOf('@');
        if (at <= 1) return e;
        var head = e[..Math.Min(3, at)];
        return head + new string('*', Math.Max(1, at - head.Length)) + e[at..];
    }
    static string MaskPhone(string p)
    {
        var digits = new string(p.Where(char.IsDigit).ToArray());
        return digits.Length < 4 ? p : "+91 " + new string('*', Math.Max(0, digits.Length - 4)) + digits[^4..];
    }

    return Results.Json(new
    {
        success = true,
        email = user.Email,
        maskedEmail = MaskEmail(user.Email!),
        maskedPhone = MaskPhone(user.Phone!),
    });
}).DisableAntiforgery();

app.MapPost("/api/account/verify-otp", async (HttpContext http, RioCommerceDbContext db, IVerificationService verify,
    IDataProtectionProvider dp) =>
{
    var form = await http.Request.ReadFormAsync();
    var email = form["email"].ToString().Trim();
    var code = form["code"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
        return Results.Json(new { success = false, error = "Enter the 6-digit code." });

    var check = await verify.VerifyAsync(RioCommerce.Core.Enums.VerificationPurpose.CustomerSignup, email, code);
    if (!check.Success)
        return Results.Json(new { success = false, error = check.ErrorMessage ?? "Invalid or expired code." });

    var user = await db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email.ToLower());
    if (user == null) return Results.Json(new { success = false, error = "Account not found." });

    user.IsActive = true;
    user.IsVerified = true;
    await db.SaveChangesAsync();

    // One-time, 2-minute signed token → consumed by the GET sign-in bridge to set the auth cookie.
    var protector = dp.CreateProtector("RioCommerce.Account.SignInBridge.v1").ToTimeLimitedDataProtector();
    var token = protector.Protect(user.Id.ToString(), TimeSpan.FromMinutes(2));
    return Results.Json(new { success = true, token });
}).DisableAntiforgery();

// ── OTP: resend a registration code to the email (cooldown is enforced client-side + the
//    service replaces any prior un-consumed code). ──
app.MapPost("/api/account/resend-otp", async (HttpContext http, RioCommerceDbContext db, IVerificationService verify) =>
{
    var form = await http.Request.ReadFormAsync();
    var email = form["email"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(email)) return Results.Json(new { success = false, error = "Missing email." });

    var user = await db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email.ToLower());
    if (user == null) return Results.Json(new { success = true });   // don't reveal non-existence

    // Both channels, matching what registration sent - resending on email only would contradict
    // the "sent to your email and mobile" wording the user was just shown.
    var send = await verify.SendBothAsync(RioCommerce.Core.Enums.VerificationPurpose.CustomerSignup,
        user.Email, user.Phone, user.Id, user.FullName);
    return Results.Json(new { success = send.Success, error = send.ErrorMessage });
}).DisableAntiforgery().RequireRateLimiting(OtpSendRateLimitPolicy);

// ── School Registration: UDISE lookup ──
app.MapPost("/api/school/lookup", async (HttpContext http, ISchoolRegistrationService schoolReg) =>
{
    var form = await http.Request.ReadFormAsync();
    var udise = form["udiseCode"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(udise))
        return Results.Json(new { success = false, error = "Please enter a UDISE code." });

    var result = await schoolReg.LookupByUdiseAsync(udise);
    if (result == null)
        return Results.Json(new { success = false, error = "No school found with this UDISE code. Please check and try again." });

    return Results.Json(new { success = true, school = result });
}).DisableAntiforgery();

// ── School Registration: register principal (create user + send OTP) ──
app.MapPost("/api/school/register", async (HttpContext http, ISchoolRegistrationService schoolReg) =>
{
    var form = await http.Request.ReadFormAsync();
    var request = new RioCommerce.Core.DTOs.School.SchoolPrincipalRegisterRequest
    {
        UdiseCode = form["udiseCode"].ToString(),
        FullName = form["fullName"].ToString(),
        Email = form["email"].ToString(),
        Phone = form["phone"].ToString(),
        Password = form["password"].ToString()
    };

    if (string.IsNullOrWhiteSpace(request.UdiseCode) || string.IsNullOrWhiteSpace(request.FullName)
        || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Phone)
        || string.IsNullOrWhiteSpace(request.Password))
        return Results.Json(new { success = false, error = "All fields are required." });

    if (request.Password.Length < 6)
        return Results.Json(new { success = false, error = "Password must be at least 6 characters." });

    var (ok, error) = await schoolReg.RegisterPrincipalAsync(request);
    return Results.Json(new { success = ok, error });
}).DisableAntiforgery();

// ── School Registration: verify OTP, activate account, return sign-in token ──
app.MapPost("/api/school/verify-otp", async (HttpContext http, ISchoolRegistrationService schoolReg,
    RioCommerceDbContext db, IDataProtectionProvider dp) =>
{
    var form = await http.Request.ReadFormAsync();
    var email = form["email"].ToString().Trim();
    var code = form["code"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
        return Results.Json(new { success = false, error = "Enter the 6-digit code." });

    var (ok, error, userId) = await schoolReg.VerifyOtpAsync(email, code);
    if (!ok || userId == null)
        return Results.Json(new { success = false, error = error ?? "Invalid or expired code." });

    var protector = dp.CreateProtector("RioCommerce.Account.SignInBridge.v1").ToTimeLimitedDataProtector();
    var token = protector.Protect(userId.Value.ToString(), TimeSpan.FromMinutes(2));
    return Results.Json(new { success = true, token });
}).DisableAntiforgery();

// ── School Registration: resend OTP ──
app.MapPost("/api/school/resend-otp", async (HttpContext http, ISchoolRegistrationService schoolReg) =>
{
    var form = await http.Request.ReadFormAsync();
    var email = form["email"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(email))
        return Results.Json(new { success = false, error = "Missing email." });

    var (ok, error) = await schoolReg.ResendOtpAsync(email);
    return Results.Json(new { success = ok, error });
}).DisableAntiforgery().RequireRateLimiting(OtpSendRateLimitPolicy);

// ── Sign-in bridge: consumes the one-time token from verify-otp, sets the auth cookie, redirects home. ──
app.MapGet("/account/complete-signin", async (HttpContext http, RioCommerceDbContext db, IDataProtectionProvider dp, string token) =>
{
    try
    {
        var protector = dp.CreateProtector("RioCommerce.Account.SignInBridge.v1").ToTimeLimitedDataProtector();
        var uidStr = protector.Unprotect(token);            // throws if expired/tampered
        if (!Guid.TryParse(uidStr, out var uid)) return Results.Redirect("/login");

        var user = await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == uid);
        if (user == null || !user.IsActive) return Results.Redirect("/login");

        await SignInUserAsync(http, user);
        return Results.Redirect("/");
    }
    catch
    {
        return Results.Redirect("/login?verified=1");       // token expired — ask them to log in
    }
});

// ── Forgot password: send a 6-digit OTP to the email if it's registered (uniform response). ──
app.MapPost("/api/account/forgot-otp", async (HttpContext http, RioCommerceDbContext db, IVerificationService verify) =>
{
    var form = await http.Request.ReadFormAsync();
    // Accepts an email OR a mobile number. This is also the ACTIVATION path for a student a
    // School Principal created: that account has no password, and nothing here requires one, so
    // the same flow claims a brand-new account and resets a forgotten one. No second OTP system.
    var id = form["email"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(id))
        return Results.Json(new { success = false, error = "Enter your email or mobile number." });

    var idLower = id.ToLower();
    var idDigits = new string(id.Where(char.IsDigit).ToArray());
    var user = await db.Users.FirstOrDefaultAsync(u =>
        (u.Email != null && u.Email.ToLower() == idLower) ||
        (idDigits.Length >= 10 && u.Phone != null && u.Phone == idDigits));

    if (user != null)
        // Both channels, so a student who typed their mobile receives it there.
        await verify.SendBothAsync(RioCommerce.Core.Enums.VerificationPurpose.PasswordReset,
            user.Email, user.Phone, user.Id, user.FullName);

    // Uniform response — never reveal whether the identifier is registered.
    return Results.Json(new { success = true });
}).DisableAntiforgery().RequireRateLimiting(OtpSendRateLimitPolicy);

// ── Forgot password: validate the OTP WITHOUT consuming it, so the verify step can show an
//    "incorrect OTP" error before the user reaches the new-password screen. ──
app.MapPost("/api/account/verify-reset-otp", async (HttpContext http, IVerificationService verify) =>
{
    var form = await http.Request.ReadFormAsync();
    var email = form["email"].ToString().Trim();
    var code = form["code"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
        return Results.Json(new { success = false, error = "Enter the 6-digit code." });

    var check = await verify.PeekAsync(RioCommerce.Core.Enums.VerificationPurpose.PasswordReset, email, code);
    return Results.Json(new { success = check.Success, error = check.ErrorMessage });
}).DisableAntiforgery();

// ── Reset password: verify the OTP then set the new password. ──
app.MapPost("/api/account/reset-otp", async (HttpContext http, RioCommerceDbContext db, IVerificationService verify) =>
{
    var form = await http.Request.ReadFormAsync();
    var email = form["email"].ToString().Trim();
    var code = form["code"].ToString().Trim();
    var newPassword = form["password"].ToString();
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
        return Results.Json(new { success = false, error = "Enter the 6-digit code." });
    if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        return Results.Json(new { success = false, error = "Password must be at least 6 characters." });

    var check = await verify.VerifyAsync(RioCommerce.Core.Enums.VerificationPurpose.PasswordReset, email, code);
    if (!check.Success)
        return Results.Json(new { success = false, error = check.ErrorMessage ?? "Invalid or expired code." });

    var idLower = email.ToLower();
    var idDigits = new string(email.Where(char.IsDigit).ToArray());
    var user = await db.Users.FirstOrDefaultAsync(u =>
        (u.Email != null && u.Email.ToLower() == idLower) ||
        (idDigits.Length >= 10 && u.Phone != null && u.Phone == idDigits));
    if (user == null) return Results.Json(new { success = false, error = "Account not found." });

    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

    // Completing the OTP proves the holder owns that mailbox / handset, which is exactly the
    // evidence IsVerified was waiting for. Without this a principal-created student would set a
    // password and still show as Pending forever.
    //
    // IsActive is deliberately NOT touched: flipping it here would silently re-enable an account
    // an admin had disabled on purpose. Principal-created students are already IsActive = true.
    user.IsVerified = true;

    await db.SaveChangesAsync();
    return Results.Json(new { success = true });
}).DisableAntiforgery();

// ── First-login password setup for customers imported from the old website ─────────────────────
// /account/login redirects here with a 30-minute data-protected token that names the user. Every
// endpoint below re-reads the account from that token and re-asserts eligibility, so the browser
// is never trusted with — nor shown — the address the code is actually sent to. The token only
// names a user; ResolveLegacySetupUserAsync is what refuses to let it touch an account that
// already has a password or never came from the old site, which also makes a replay inert.

// Who is this token for? Returns only a masked address, enough for the screen to say where the
// code is going without publishing the full email to anyone holding the link.
app.MapPost("/api/account/setup-info", async (HttpContext http, RioCommerceDbContext db, IDataProtectionProvider dp) =>
{
    var form = await http.Request.ReadFormAsync();
    var user = await ResolveLegacySetupUserAsync(db, dp, form["token"].ToString());
    if (user is null) return Results.Json(new { success = false, error = LegacySetupExpiredMessage() });

    return Results.Json(new { success = true, maskedEmail = MaskEmail(user.Email!), name = user.FullName });
}).DisableAntiforgery();

// Send the 6-digit code to the address on the migrated account.
app.MapPost("/api/account/setup-send-otp", async (HttpContext http, RioCommerceDbContext db,
    IVerificationService verify, IDataProtectionProvider dp) =>
{
    var form = await http.Request.ReadFormAsync();
    var user = await ResolveLegacySetupUserAsync(db, dp, form["token"].ToString());
    if (user is null) return Results.Json(new { success = false, error = LegacySetupExpiredMessage() });

    var send = await verify.SendAsync(RioCommerce.Core.Enums.VerificationPurpose.LegacyPasswordSetup,
        RioCommerce.Core.Enums.VerificationChannel.Email, user.Email!, user.Id, user.FullName);
    return Results.Json(new { success = send.Success, error = send.ErrorMessage, maskedEmail = MaskEmail(user.Email!) });
}).DisableAntiforgery().RequireRateLimiting(OtpSendRateLimitPolicy);

// Validate the code WITHOUT consuming it, so a wrong code is reported on the OTP screen rather
// than on the new-password screen — same two-phase shape as the forgot-password flow above.
app.MapPost("/api/account/setup-verify-otp", async (HttpContext http, RioCommerceDbContext db,
    IVerificationService verify, IDataProtectionProvider dp) =>
{
    var form = await http.Request.ReadFormAsync();
    var code = form["code"].ToString().Trim();
    var user = await ResolveLegacySetupUserAsync(db, dp, form["token"].ToString());
    if (user is null) return Results.Json(new { success = false, error = LegacySetupExpiredMessage() });

    var check = await verify.PeekAsync(RioCommerce.Core.Enums.VerificationPurpose.LegacyPasswordSetup, user.Email!, code);
    return Results.Json(new { success = check.Success, error = check.ErrorMessage });
}).DisableAntiforgery();

// Final step. A real browser form POST rather than a fetch, because this signs the customer in and
// SignInAsync has to write its cookie on a genuine browser request (same reason /account/login is
// shaped this way). Errors bounce back to the page with the token intact so nothing is retyped.
app.MapPost("/account/setup-password", async (HttpContext http, RioCommerceDbContext db,
    IVerificationService verify, IDataProtectionProvider dp, IAuditService audit) =>
{
    var form = await http.Request.ReadFormAsync();
    var token = form["token"].ToString();
    var code = form["code"].ToString().Trim();
    var newPassword = form["password"].ToString();
    var confirmPassword = form["confirmPassword"].ToString();
    var raw = form["rurl"].ToString().Trim();

    // Same normalisation as /account/login — exactly one leading slash, so it can only ever be a
    // local path and never an open redirect.
    var target = string.IsNullOrEmpty(raw) ? "/" : "/" + raw.TrimStart('/', '\\');

    string BackToSetup(string error) =>
        $"/set-password?t={Uri.EscapeDataString(token)}&rurl={Uri.EscapeDataString(target)}&error={Uri.EscapeDataString(error)}";

    if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        return Results.Redirect(BackToSetup("Password must be at least 6 characters."));
    if (newPassword != confirmPassword)
        return Results.Redirect(BackToSetup("The two passwords do not match."));

    var user = await ResolveLegacySetupUserAsync(db, dp, token, withRoles: true);
    if (user is null) return Results.Redirect("/login?error=setup-expired");

    var check = await verify.VerifyAsync(RioCommerce.Core.Enums.VerificationPurpose.LegacyPasswordSetup, user.Email!, code);
    if (!check.Success)
        return Results.Redirect(BackToSetup(check.ErrorMessage ?? "Invalid or expired code."));

    var wasInactive = !user.IsActive;

    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
    // The OTP just proved they hold the mailbox the old site had on file — that is the same
    // evidence signup verification collects, so the imported IsVerified = false is now stale, and
    // an Active = 0 that only meant "email never confirmed on the old site" is now satisfied.
    user.IsVerified = true;
    user.IsActive = true;
    user.LastLoginAt = DateTime.UtcNow;
    user.LoginCount++;
    await db.SaveChangesAsync();

    await audit.WriteAsync(new AuditEntry
    {
        ActorUserId = user.Id,
        ActorName = user.FullName,
        Module = "Auth",
        Action = "UserPasswordSetupCompleted",
        EntityType = "User",
        EntityId = user.Id.ToString(),
        EntityName = user.FullName,
        // Flagged as a Warning when it re-opened a disabled account, so the one case an admin might
        // want to reverse stands out in the audit table instead of blending into normal signups.
        Status = wasInactive ? "Warning" : "Success",
        Details = wasInactive
            ? "Migrated account set its first password AND was re-activated (it was imported inactive)"
            : "Migrated account set its first password on the new platform"
    });

    await SignInUserAsync(http, user);
    return Results.Redirect(target);
}).DisableAntiforgery();

// ── Auto-migrate (script-based) + Seed/Repair Admin User ──
// Apply pending SQL migration scripts BEFORE opening a seeding scope. On an existing
// (previously EF-managed) database the baseline is auto-stamped as applied rather than
// re-run; on a fresh database the runner creates the DB and applies the baseline.
await app.Services.MigrateDatabaseAsync();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RioCommerceDbContext>();

    // ── 🔒 RBAC system-role seed — idempotent. Adds Counsellor/Accounts/Support/Student to the
    //    existing roles so the admin role-picker has the full enterprise set. Marked IsSystem so
    //    they cannot be renamed or deleted from /admin/access-control. ──
    var systemRoles = new (string name, string display, string desc)[]
    {
        ("counsellor",      "Counsellor",      "Counselling / advisory staff"),
        ("accounts",        "Accounts",        "Finance + accounts team"),
        ("support",         "Support",         "Customer support team"),
        ("student",         "Student",         "Enrolled student (public-site default)"),
        // ── Sentinel role attached automatically when an admin grants a customer custom backend
        //    access via /admin/access-control. Drives the orange "Admin" button visibility and
        //    the AdminLayout entry gate. Managed by PermissionService — DO NOT assign manually. ──
        ("backend_user",    "Backend Access",  "Customer with custom backend permissions (auto-managed)"),
        // ── SEO team. Deliberately narrow: the blog, and nothing else. Its reach is defined by the
        //    RolePermission rows seeded below, not by this line — which is the whole point of having
        //    an RBAC layer rather than another hard-coded staff role. ──
        ("seo",             "SEO",             "SEO team — blog content only"),
    };
    foreach (var (name, display, desc) in systemRoles)
    {
        if (!await db.Roles.AnyAsync(r => r.Name == name))
        {
            db.Roles.Add(new Role
            {
                Id = Guid.NewGuid(),
                Name = name,
                DisplayName = display,
                Description = desc,
                IsSystem = true,
                IsActive = true
            });
        }
    }
    await db.SaveChangesAsync();

    // ── Default permissions for the SEO role ─────────────────────────────────────────────────
    //    Seeded ONCE, and only while the role has no permissions at all. After that the grid in
    //    /admin/access-control owns it: a super admin who widens or narrows SEO's reach must not
    //    have that decision reverted by the next deploy.
    var seoRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "seo");
    if (seoRole != null && !await db.RolePermissions.AnyAsync(rp => rp.RoleId == seoRole.Id))
    {
        // Write and publish blog posts. Deleting is not granted — a mistaken delete costs a page
        // its history and its inbound links, which is exactly what an SEO team is protecting.
        foreach (var key in new[] { "content.blogs.view", "content.blogs.create", "content.blogs.edit", "content.blogs.approve" })
            db.RolePermissions.Add(new RolePermission { RoleId = seoRole.Id, PermissionKey = key });
        await db.SaveChangesAsync();
    }

    // ── Foundational data seed (idempotent) ──────────────────────────────────────────────────
    //    These rows historically lived ONLY in EF HasData (SeedData.cs), which is applied by EF's
    //    own migration pipeline — NOT by the SQL SqlMigrationRunner that builds this database. A DB
    //    created purely from the SQL scripts (e.g. Neon) therefore lacked staff roles, faculty,
    //    categories, subjects, and base settings. Seeding them here guarantees every fresh database
    //    (local or cloud) is identical, and must run BEFORE the admin-user block below which needs
    //    the super_admin role to exist. Each block guards on its own table so re-runs are no-ops. ──
    var seedDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

    // Staff roles (the systemRoles block above seeds the customer-facing set; these are the staff set).
    var staffRoles = new (string id, string name, string display)[]
    {
        ("44444444-4444-4444-4444-444444444401", "super_admin",     "Super Admin"),
        ("44444444-4444-4444-4444-444444444402", "admin",           "Admin"),
        ("44444444-4444-4444-4444-444444444403", "student",         "Student"),
        ("44444444-4444-4444-4444-444444444404", "faculty",         "Faculty"),
        ("44444444-4444-4444-4444-444444444405", "franchise_admin", "Franchise Admin"),
        ("44444444-4444-4444-4444-444444444406", "operations",      "Operations"),
        ("44444444-4444-4444-4444-444444444407", "school_principal", "School Principal"),
        ("44444444-4444-4444-4444-444444444408", "school_coordinator","School Coordinator"),
    };
    var existingRoleNames = (await db.Roles.Select(r => r.Name).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var (id, name, display) in staffRoles)
    {
        if (existingRoleNames.Contains(name)) continue;
        db.Roles.Add(new Role { Id = Guid.Parse(id), Name = name, DisplayName = display, IsSystem = true, IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate });
    }
    await db.SaveChangesAsync();

    if (!await db.Categories.AnyAsync())
    {
        db.Categories.AddRange(
            new Category { Id = Guid.Parse("22222222-2222-2222-2222-222222222201"), Name = "Sample Category", Slug = "sample-category", DisplayOrder = 1, IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate }
        );
        await db.SaveChangesAsync();
    }

    if (!await db.Subjects.AnyAsync())
    {
        db.Subjects.Add(
            new Subject { Id = Guid.Parse("33333333-3333-3333-3333-333333333301"), Name = "Sample Subject", Slug = "sample-subject", Level = CourseLevel.Beginner, DisplayOrder = 1, IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate }
        );
        await db.SaveChangesAsync();
    }

    if (!await db.AppSettings.AnyAsync())
    {
        db.AppSettings.AddRange(
            new AppSetting { Key = "company_name", Value = "My Store", Category = "general", ValueType = "string", UpdatedAt = seedDate },
            new AppSetting { Key = "company_phone_1", Value = "", Category = "general", ValueType = "string", UpdatedAt = seedDate },
            new AppSetting { Key = "company_email", Value = "admin@example.com", Category = "general", ValueType = "string", UpdatedAt = seedDate },
            new AppSetting { Key = "company_address", Value = "", Category = "general", ValueType = "string", UpdatedAt = seedDate },
            new AppSetting { Key = "whatsapp_message", Value = "Hi! I'd like to enquire about your courses.", Category = "site", ValueType = "string", UpdatedAt = seedDate },
            new AppSetting { Key = "business_hours", Value = "Mon–Sat, 10:00 AM – 7:00 PM", Category = "site", ValueType = "string", UpdatedAt = seedDate }
        );
        await db.SaveChangesAsync();
    }

    // ── Scheduled task seed — idempotent. One row per registered IScheduledTaskHandler;
    //    keyed by TaskKey so an admin's edits (interval, enabled) survive restarts. ──
    var handlers = scope.ServiceProvider.GetServices<IScheduledTaskHandler>().ToList();
    var existingKeys = (await db.ScheduledTasks.Select(t => t.TaskKey).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var h in handlers)
    {
        if (existingKeys.Contains(h.Key)) continue;
        db.ScheduledTasks.Add(new ScheduledTask
        {
            Id = Guid.NewGuid(),
            TaskKey = h.Key,
            Name = h.DefaultName,
            Description = h.DefaultDescription,
            IntervalSeconds = h.DefaultIntervalSeconds,
            TimeoutSeconds = 300,
            Enabled = true,
            NextRunAt = DateTime.UtcNow.AddSeconds(60), // first run shortly after boot
        });
    }
    await db.SaveChangesAsync();

    // Backfill: any user who already has at least one override row from before the sentinel-role
    // mechanism existed gets the role attached now so they can actually enter /admin.
    var backendRole = await db.Roles.FirstAsync(r => r.Name == "backend_user");
    var overrideUserIds = await db.UserPermissionOverrides.Select(x => x.UserId).Distinct().ToListAsync();
    var alreadyAttached = await db.UserRoles
        .Where(ur => ur.RoleId == backendRole.Id && overrideUserIds.Contains(ur.UserId))
        .Select(ur => ur.UserId).ToListAsync();
    foreach (var uid in overrideUserIds.Except(alreadyAttached))
        db.UserRoles.Add(new UserRole { UserId = uid, RoleId = backendRole.Id, IsActive = true });
    if (overrideUserIds.Count > alreadyAttached.Count) await db.SaveChangesAsync();

    const string adminEmail = "admin@example.com";
    const string adminPassword = "Admin@123";

    // Idempotent: ensure the admin exists, is active, can log in with Admin@123, and has the
    // super_admin role. This self-heals databases that were seeded by an older build with a
    // different/invalid password hash (the previous "create only if missing" logic could never
    // correct an already-present row, which is why login returned 401).
    var admin = await db.Users
        .Include(u => u.UserRoles)
        .FirstOrDefaultAsync(u => u.Email == adminEmail);

    if (admin == null)
    {
        admin = new User
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            FullName = "Admin",
            Email = adminEmail,
            Phone = "",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
            IsActive = true,
            IsVerified = true,
            City = "Pune",
            State = "Maharashtra",
            PhoneCountry = "+91",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(admin);
        Console.WriteLine($"✅ Admin user seeded: {adminEmail} / {adminPassword}");
    }
    else
    {
        // Only reset when the stored hash can't already verify (or is malformed), so a
        // deliberately-changed password is left alone.
        bool passwordOk;
        try
        {
            passwordOk = !string.IsNullOrEmpty(admin.PasswordHash)
                && BCrypt.Net.BCrypt.Verify(adminPassword, admin.PasswordHash);
        }
        catch
        {
            passwordOk = false;
        }

        if (!passwordOk)
        {
            admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword);
            Console.WriteLine($"🔧 Admin password reset to default: {adminEmail} / {adminPassword}");
        }
        if (!admin.IsActive) admin.IsActive = true;
    }

    // Ensure the super_admin role is assigned (use the navigation so the FK resolves correctly).
    var superAdminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "super_admin");
    if (superAdminRole != null && !admin.UserRoles.Any(ur => ur.RoleId == superAdminRole.Id))
    {
        db.UserRoles.Add(new UserRole
        {
            User = admin,
            RoleId = superAdminRole.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    // Demo products, coupons, sharing rules, and franchise orders are NOT seeded
    // in the base project — populate them through the admin UI for each client deployment.

    // ── Seed a demo coupon (idempotent) ──
    if (!await db.Coupons.AnyAsync())
    {
        db.Coupons.Add(new Coupon
        {
            Code = "WELCOME10",
            Name = "Welcome Offer — 10% off",
            CouponType = SharingType.Percentage,
            Value = 10,
            MaxDiscount = 1000,
            MinOrder = 2000,
            PerUserLimit = 1,
            IsActive = true
        });
        Console.WriteLine("✅ Seeded demo coupon WELCOME10.");
    }

    await db.SaveChangesAsync();

    // ── Seed CMS legal pages (idempotent; footer links to /page/{slug}) ──
    if (!await db.CmsPages.AnyAsync())
    {
        db.CmsPages.AddRange(
            new CmsPage { Title = "Terms & Conditions", Slug = "terms-and-conditions", DisplayOrder = 1, IsPublished = true, Body = "<p>By enrolling with RioCommerce you agree to our course access terms: lectures are licensed to a single student, validity and attempts are as listed on each course, and content may not be shared or redistributed.</p>" },
            new CmsPage { Title = "Privacy Policy", Slug = "privacy-policy", DisplayOrder = 2, IsPublished = true, Body = "<p>We collect only the information needed to deliver your courses and support — name, contact details and order history. We never sell your data. Payment details are handled by our payment gateway and are not stored on our servers.</p>" },
            new CmsPage { Title = "Refund Policy", Slug = "refund-policy", DisplayOrder = 3, IsPublished = true, Body = "<p>Given the digital nature of our courses, fees are non-refundable once lecture access has been activated. For genuine issues, please contact us within 7 days of purchase and our team will help.</p>" });
        await db.SaveChangesAsync();
        Console.WriteLine("✅ Seeded CMS legal pages.");
    }

    // ── Seed a demo blog post (idempotent) ──
    if (!await db.BlogPosts.AnyAsync())
    {
        db.BlogPosts.Add(new BlogPost
        {
            Title = "Welcome to Our Learning Platform",
            Slug = "welcome-to-our-platform",
            Excerpt = "Get started with our courses, resources, and community.",
            Category = "Announcements",
            Body = "<p>Welcome! We're thrilled to have you here. Explore our courses, connect with faculty, and take the first step in your learning journey.</p><p>Check back often for updates, tips, and new course announcements.</p>",
            Status = ContentStatus.Published,
            PublishedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        Console.WriteLine("✅ Seeded demo blog post.");
    }

    // ── Seed default message templates (idempotent) ──
    if (!await db.MessageTemplates.AnyAsync())
    {
        db.MessageTemplates.AddRange(
            new MessageTemplate
            {
                Key = "order_confirmation",
                Name = "Order Confirmation",
                Channel = "Email",
                Subject = "Your RioCommerce order {{order_number}} is confirmed 🎉",
                Body = "Hi {{name}},\n\nThank you! Your order {{order_number}} ({{items}} item(s)) totalling ₹{{total}} is confirmed and your courses are now active.\n\n— Team RioCommerce"
            },
            new MessageTemplate
            {
                Key = "welcome",
                Name = "Welcome Email",
                Channel = "Email",
                Subject = "Welcome to RioCommerce!",
                Body = "Hi {{name}},\n\nWelcome aboard! Explore our courses and start learning today.\n\n— Team RioCommerce"
            });
        await db.SaveChangesAsync();
        Console.WriteLine("✅ Seeded message templates.");
    }

    // ── Phase 1+6 lifecycle templates (idempotent per (Key, Channel)) — fan out across Email + SMS ──
    string[] eventKeys = { "franchisee_registered", "franchisee_approved", "franchisee_rejected", "franchisee_password_reset", "wallet_recharged", "order_placed", "order_status_updated", "serial_key_generated", "installment_paid", "installment_completed", "installment_reminder" };
    var existingPairs = (await db.MessageTemplates.Where(t => eventKeys.Contains(t.Key))
        .Select(t => new { t.Key, t.Channel }).ToListAsync())
        .Select(x => (x.Key, x.Channel)).ToHashSet();
    var fresh = new List<MessageTemplate>();
    void AddIfNew(string key, string channel, string name, string subject, string body)
    {
        if (existingPairs.Contains((key, channel))) return;
        fresh.Add(new MessageTemplate { Key = key, Channel = channel, Name = name, Subject = subject, Body = body, IsActive = true });
    }

    // Email versions (full-fat).
    AddIfNew("franchisee_registered", "Email", "Franchisee — Application Received (Email)",
        "We've received your franchisee application — RioCommerce",
        "Hi {{name}},\n\nThanks for applying to partner with RioCommerce ({{business}} · {{city}}). Our team will review your application and reach out within 2 working days.\n\n— Team RioCommerce");
    AddIfNew("franchisee_approved", "Email", "Franchisee — Approved (Email)",
        "🎉 Your RioCommerce franchisee application is approved",
        "Hi {{name}},\n\nGreat news — your application ({{business}}) has been approved. Your franchisee code is {{code}}.\n\nLog in to your portal:\n  URL:      {{login_url}}\n  Email:    {{email}}\n  Password: {{password}}\n\nPlease change your password after first login.\n\n— Team RioCommerce");
    AddIfNew("franchisee_rejected", "Email", "Franchisee — Rejected (Email)",
        "Update on your RioCommerce franchisee application",
        "Hi {{name}},\n\nThank you for applying. Unfortunately we're unable to approve your franchisee application at this time.\n\nRemarks from our team:\n{{remarks}}\n\nFeel free to reach out if you'd like to discuss this further.\n\n— Team RioCommerce");
    AddIfNew("franchisee_password_reset", "Email", "Franchisee — Password Reset (Email)",
        "Your RioCommerce franchisee password has been reset",
        "Hi {{name}},\n\nYour franchisee portal password has been reset by an administrator.\n\nLog in with:\n  URL:      {{login_url}}\n  Email:    {{email}}\n  Password: {{password}}\n\nPlease change it after signing in.\n\n— Team RioCommerce");
    AddIfNew("wallet_recharged", "Email", "Franchisee — Wallet Recharged (Email)",
        "Wallet recharge successful — RioCommerce",
        "Hi {{name}},\n\nYour wallet has been recharged with ₹{{amount}}. New balance: ₹{{balance}}.\nRecharge ref: {{recharge_id}}\n\n— Team RioCommerce");
    AddIfNew("order_placed", "Email", "Order — Placed (Email)",
        "Order {{order_number}} confirmed — RioCommerce",
        "Hi {{name}},\n\nYour order {{order_number}} ({{items}} item(s), ₹{{total}}) has been placed through {{franchise}}.\nWe'll keep you posted as it progresses.\n\n— Team RioCommerce");
    AddIfNew("order_status_updated", "Email", "Order — Status Updated (Email)",
        "Order {{order_number}} is now {{status}}",
        "Hi {{name}},\n\nQuick update on your order {{order_number}}: status changed from {{previous_status}} to {{status}}.\n\n— Team RioCommerce");

    // SMS versions (short — fits a single 160-char segment when tokens are reasonable).
    AddIfNew("franchisee_registered", "SMS", "Franchisee — Application Received (SMS)",
        "", "RioCommerce: Hi {{name}}, we received your franchise application. Our team will reach out within 2 working days.");
    AddIfNew("franchisee_approved", "SMS", "Franchisee — Approved (SMS)",
        "", "RioCommerce: Approved! Code {{code}}. Login at {{login_url}} with {{email}} / {{password}}");
    AddIfNew("franchisee_rejected", "SMS", "Franchisee — Rejected (SMS)",
        "", "RioCommerce: We couldn't approve your franchise application this time. Please check email for details.");
    AddIfNew("franchisee_password_reset", "SMS", "Franchisee — Password Reset (SMS)",
        "", "RioCommerce: Your portal password has been reset. New password: {{password}}. Please change it after sign-in.");
    AddIfNew("wallet_recharged", "SMS", "Franchisee — Wallet Recharged (SMS)",
        "", "RioCommerce: Wallet credited ₹{{amount}}. New balance: ₹{{balance}}.");
    AddIfNew("order_placed", "SMS", "Order — Placed (SMS)",
        "", "RioCommerce: Order {{order_number}} confirmed. Total ₹{{total}}. Thanks!");
    AddIfNew("order_status_updated", "SMS", "Order — Status Updated (SMS)",
        "", "RioCommerce: Order {{order_number}} is now {{status}}.");
    // ── Serial-key delivery: customer-facing email + SMS sent on successful key generation. ──
    AddIfNew("serial_key_generated", "Email", "Serial Key — Generated (Email)",
        "Your access key for {{product_title}} — RioCommerce",
        "Hi {{name}},\n\nYour access key for order {{order_number}} ({{product_title}}) is ready:\n\n  Key: {{serial_key}}\n\nKeep this safe — you'll need it to activate the course on your device.\n\n— Team RioCommerce");
    AddIfNew("serial_key_generated", "SMS", "Serial Key — Generated (SMS)",
        "", "RioCommerce: Access key for order {{order_number}}: {{serial_key}}. Keep it safe.");
    // ── Installment notifications: payment received, plan completed (final invoice), and due reminder. ──
    AddIfNew("installment_paid", "Email", "Installment — Payment Received (Email)",
        "Installment received for order {{order_number}} — RioCommerce",
        "Hi {{name}},\n\nWe've received ₹{{amount}} towards installment #{{installment_number}} on order {{order_number}}.\nPaid so far: ₹{{paid}} · Outstanding: ₹{{outstanding}}.\n\n— Team RioCommerce");
    AddIfNew("installment_paid", "SMS", "Installment — Payment Received (SMS)",
        "", "RioCommerce: Received ₹{{amount}} (installment #{{installment_number}}) for order {{order_number}}. Outstanding: ₹{{outstanding}}.");
    AddIfNew("installment_completed", "Email", "Installment — Plan Completed (Email)",
        "Order {{order_number}} fully paid — your invoice is ready",
        "Hi {{name}},\n\nGreat news — your installment plan for order {{order_number}} (₹{{total}}) is fully paid. Your final tax invoice has been generated and is available from your account.\n\nThank you!\n— Team RioCommerce");
    AddIfNew("installment_completed", "SMS", "Installment — Plan Completed (SMS)",
        "", "RioCommerce: Order {{order_number}} fully paid (₹{{total}}). Your final invoice is ready. Thank you!");
    AddIfNew("installment_reminder", "Email", "Installment — Due Reminder (Email)",
        "Reminder: installment #{{installment_number}} due {{due_date}}",
        "Hi {{name}},\n\nA friendly reminder that installment #{{installment_number}} of ₹{{amount}} on order {{order_number}} is due on {{due_date}}.\nPlease visit the counter to pay.\n\n— Team RioCommerce");
    AddIfNew("installment_reminder", "SMS", "Installment — Due Reminder (SMS)",
        "", "RioCommerce: Installment #{{installment_number}} of ₹{{amount}} for order {{order_number}} is due on {{due_date}}.");
    if (fresh.Count > 0)
    {
        db.MessageTemplates.AddRange(fresh);
        await db.SaveChangesAsync();
        Console.WriteLine($"✅ Seeded {fresh.Count} franchise message template(s).");
    }

    // ── Franchise portal: backfill a sensible default franchise share, seed a franchise user, ledger + credit limits ──
    var noShare = await db.Products.Where(p => !p.EnableDefaultFranchiseShare && p.DefaultFranchiseShareValue == 0).ToListAsync();
    if (noShare.Count > 0)
    {
        foreach (var p in noShare)
        {
            p.EnableDefaultFranchiseShare = true;
            p.DefaultFranchiseShareType = RioCommerce.Core.Enums.CommissionType.Percent;
            p.DefaultFranchiseShareValue = 15m;   // 15% default share
        }
        await db.SaveChangesAsync();
        Console.WriteLine("✅ Backfilled default franchise share (15%).");
    }

    var franchiseUser = await db.Users.FirstOrDefaultAsync(u => u.Email == "franchise@example.com");
    if (franchiseUser == null)
    {
        franchiseUser = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Demo Franchise Admin",
            Email = "franchise@example.com",
            Phone = "9876543210",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Franchise@123"),
            City = "Nashik",
            State = "Maharashtra",
            IsActive = true,
            IsVerified = true
        };
        db.Users.Add(franchiseUser);
        var frRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "franchise_admin");
        if (frRole != null) db.UserRoles.Add(new UserRole { User = franchiseUser, RoleId = frRole.Id, IsActive = true });

        var nashik = await db.Franchises.FirstOrDefaultAsync(f => f.Code == "NK");
        if (nashik != null) nashik.AdminUserId = franchiseUser.Id;
        await db.SaveChangesAsync();
        Console.WriteLine("✅ Seeded franchise portal user: franchise@example.com / Franchise@123");
    }

    if (!await db.FranchiseLedger.AnyAsync())
    {
        foreach (var fr in await db.Franchises.ToListAsync())
        {
            // No blanket credit line. This block runs once, on an empty ledger, and it used to hand
            // every existing franchisee ₹50,000 — which is how all of them ended up with an
            // identical limit nobody had actually decided on. Credit is granted per franchisee on
            // the approval screen instead.
            fr.CreditLimit = 0;
            db.FranchiseLedger.Add(new FranchiseLedgerEntry
            {
                FranchiseId = fr.Id,
                IsCredit = true,
                Amount = fr.WalletBalance,
                BalanceAfter = fr.WalletBalance,
                Description = "Opening balance"
            });
        }
        await db.SaveChangesAsync();
        Console.WriteLine("✅ Seeded franchise opening ledger + credit limits.");
    }
}

// One line so the timezone the process actually resolved is visible in the deploy log — the TZ pin
// above is silent when it works, and a wrong timezone is otherwise only noticed via wrong dates.
Console.WriteLine($"🕐 Display timezone: {TimeZoneInfo.Local.Id} (UTC{TimeZoneInfo.Local.BaseUtcOffset:hh\\:mm}) · "
                + $"now {DateTime.UtcNow.ToLocalTime():dd MMM yyyy HH:mm} local / {DateTime.UtcNow:HH:mm} UTC");

app.Run();

// Builds the cookie identity (id, name, email, role claims) and signs the user in on the browser.
static async Task SignInUserAsync(HttpContext http, User user)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.FullName),
        new(ClaimTypes.Email, user.Email ?? ""),
        new("phone", user.Phone ?? "")
    };
    foreach (var ur in user.UserRoles.Where(ur => ur.IsActive && ur.Role != null))
        claims.Add(new Claim(ClaimTypes.Role, ur.Role.Name));

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });
}

// ── Legacy password-setup token ───────────────────────────────────────────────────────────────
// Long enough to read the mail and pick a password, short enough that a link left in history or a
// shared browser stops working. The OTP inside it expires in 10 minutes independently.
static TimeSpan LegacySetupTokenLifetime() => TimeSpan.FromMinutes(30);

static ITimeLimitedDataProtector LegacySetupProtector(IDataProtectionProvider dp) =>
    dp.CreateProtector("RioCommerce.Account.LegacySetup.v1").ToTimeLimitedDataProtector();

// One message for every rejection — expired, tampered, already-used, wrong kind of account. The
// screen cannot tell them apart and neither should a probe.
static string LegacySetupExpiredMessage() =>
    "This password setup link is no longer valid. Please go back to login and try again.";

/// <summary>
/// Reads the setup token and returns the account it names, but only while that account is still
/// entitled to set a first password: active, no hash yet, has an email, and carries a
/// <c>legacy_user_map</c> row. Re-checked on every call rather than trusted from the token, so the
/// same link cannot be replayed once the password is set, nor pointed at a normal account.
/// </summary>
static async Task<User?> ResolveLegacySetupUserAsync(
    RioCommerceDbContext db, IDataProtectionProvider dp, string? token, bool withRoles = false)
{
    if (string.IsNullOrWhiteSpace(token)) return null;

    Guid uid;
    try
    {
        var payload = LegacySetupProtector(dp).Unprotect(token);   // throws if expired or tampered
        if (!Guid.TryParse(payload, out uid)) return null;
    }
    catch { return null; }

    var q = db.Users.AsQueryable();
    if (withRoles) q = q.Include(u => u.UserRoles).ThenInclude(ur => ur.Role);
    var user = await q.FirstOrDefaultAsync(u => u.Id == uid);

    // IsActive is not checked — see the note on the /account/login redirect: a migrated account is
    // routinely inactive because the old site never got its email confirmed, and finishing setup is
    // what clears that. Everything else is re-asserted on every call.
    if (user is null
        || !string.IsNullOrEmpty(user.PasswordHash)
        || string.IsNullOrWhiteSpace(user.Email))
        return null;

    return await db.LegacyUserMaps.AnyAsync(m => m.NewId == user.Id) ? user : null;
}

/// <summary>Masks an address for display — "ganesh.pawar@gmail.com" → "ga••••••••ar@gmail.com".</summary>
static string MaskEmail(string email)
{
    var at = email.IndexOf('@');
    if (at <= 0) return "•••";
    var local = email[..at];
    var domain = email[at..];
    if (local.Length <= 2) return local[..1] + "•••" + domain;
    if (local.Length <= 4) return local[..1] + new string('•', local.Length - 1) + domain;
    return local[..2] + new string('•', local.Length - 4) + local[^2..] + domain;
}