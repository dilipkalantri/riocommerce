using RioCommerce.Core.Interfaces;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Curated per-trigger token registry. Centralised so the admin UI, the renderer,
/// and the Send-Test modal all show the same vocabulary.
///
/// Adding a new trigger: drop a new key into <see cref="_catalog"/> with its
/// TokenDef list. The Notifications editor's right-side panel will pick it up
/// automatically when the admin selects that trigger key.
/// </summary>
public sealed class TemplateTokenCatalog : ITemplateTokenCatalog
{
    // Cross-trigger commons — every catalog can append these to its own list.
    private static readonly TokenDef CommonWebsite = new("website_url", "Public storefront URL", "https://example.com");
    private static readonly TokenDef CommonSupportEmail = new("support_email", "Support inbox", "support@example.com");
    private static readonly TokenDef CommonSupportMobile = new("support_mobile", "Support phone number", "+91 98765 43210");

    private static readonly Dictionary<string, TokenDef[]> _catalog = new(StringComparer.OrdinalIgnoreCase)
    {
        ["welcome_email"] = new[]
        {
            new TokenDef("name",          "Student's full name",                 "Lokesh Patil"),
            new TokenDef("email",         "Student's email address",             "lokesh.patil@example.com"),
            new TokenDef("login_url",     "Direct login page URL",               "https://example.com/account/login"),
            new TokenDef("current_year",  "Current calendar year (auto)",        DateTime.Now.Year.ToString()),
            CommonWebsite, CommonSupportEmail, CommonSupportMobile,
        },

        ["order_placed"] = new[]
        {
            new TokenDef("student_name",     "Buyer's full name",                "Lokesh Patil"),
            new TokenDef("student_email",    "Buyer's email",                    "lokesh.patil@example.com"),
            new TokenDef("student_mobile",   "Buyer's mobile",                   "9876543210"),
            new TokenDef("order_number",     "Order # (e.g. RIO-2026-0457)",     "RIO-2026-0457"),
            new TokenDef("order_date",       "Order placed-on date",             DateTime.Now.ToString("dd-MMM-yyyy")),
            new TokenDef("product_name",     "Primary product title",            "Sample Course — Regular Batch"),
            new TokenDef("product_sku",      "Internal SKU",                     "RIO-CAFND-ECO-REG"),
            new TokenDef("batch_name",       "Batch label",                      "Sept 2026 · Live Streaming"),
            new TokenDef("faculty_name",     "Primary faculty",                  "Prof. Sample Faculty"),
            new TokenDef("quantity",         "Quantity ordered",                 "1"),
            new TokenDef("subtotal",         "Subtotal (pre-GST)",               "₹10,593"),
            new TokenDef("gst_amount",       "GST (18%)",                        "₹1,907"),
            new TokenDef("discount_amount",  "Discount applied",                 "₹0"),
            new TokenDef("total_amount",     "Grand total paid",                 "₹12,500"),
            new TokenDef("payment_method",   "How paid",                         "Razorpay · UPI"),
            new TokenDef("payment_status",   "Pay status",                       "Paid"),
            new TokenDef("invoice_number",   "Invoice number",                   "INV/2026/0457"),
            CommonWebsite, CommonSupportEmail, CommonSupportMobile,
        },

        ["order_confirmation"] = new[]
        {
            new TokenDef("student_name",    "Buyer's full name",              "Lokesh Patil"),
            new TokenDef("order_number",    "Order #",                        "RIO-2026-0457"),
            new TokenDef("product_name",    "Primary product title",          "Sample Course — Regular Batch"),
            new TokenDef("batch_name",      "Batch label",                    "Sept 2026 · Live Streaming"),
            new TokenDef("faculty_name",    "Primary faculty",                "Prof. Sample Faculty"),
            new TokenDef("invoice_number",  "Invoice number",                 "INV/2026/0457"),
            new TokenDef("total_amount",    "Grand total paid",               "₹12,500"),
            new TokenDef("payment_method",  "How paid",                       "Razorpay · UPI"),
            CommonWebsite,
        },

        ["order_status_updated"] = new[]
        {
            new TokenDef("student_name",     "Buyer's full name",       "Lokesh Patil"),
            new TokenDef("order_number",     "Order #",                 "RIO-2026-0457"),
            new TokenDef("old_status",       "Previous status",         "Processing"),
            new TokenDef("new_status",       "New status",              "Shipped"),
            new TokenDef("updated_date",     "When the change happened", DateTime.Now.ToString("dd-MMM-yyyy HH:mm")),
            new TokenDef("product_name",     "Primary product title",   "Sample Course — Books"),
            new TokenDef("tracking_number",  "Courier tracking #",      "DTDC123456789"),
            new TokenDef("tracking_url",     "Tracking page URL",       "https://dtdc.com/track/DTDC123456789"),
            CommonWebsite,
        },

        ["order_dispatched"] = new[]
        {
            new TokenDef("name",            "Buyer's full name",              "Lokesh Patil"),
            new TokenDef("order_number",     "Order #",                        "RIO-2026-0457"),
            new TokenDef("product_title",    "Items in the consignment",       "CA Inter Costing — Printed Notes"),
            new TokenDef("courier",          "Chosen courier",                 "Trackon"),
            new TokenDef("tracking_number",  "Courier tracking #",             "ABC123456"),
            new TokenDef("tracking_url",     "Courier's tracking page",        "https://www.trackon.in/courier-tracking"),
            // Pre-composed in code because the renderer has no conditionals — empty for a courier
            // with no tracking (PCMC), which makes the whole section disappear.
            new TokenDef("tracking_block",   "Ready-made tracking lines (empty when the courier has no tracking)",
                                             "Tracking Number: ABC123456\nTrack your shipment: https://www.trackon.in/courier-tracking"),
            CommonWebsite, CommonSupportEmail, CommonSupportMobile,
        },

        ["serial_key_generated"] = new[]
        {
            new TokenDef("student_name",     "Buyer's full name",        "Lokesh Patil"),
            new TokenDef("product_name",     "Product the key unlocks",  "Sample Course — Player"),
            new TokenDef("serial_key",       "The generated key",        "RIO-XK4F-2A8C-9LM2"),
            new TokenDef("activation_date",  "Date key was issued",      DateTime.Now.ToString("dd-MMM-yyyy")),
            new TokenDef("expiry_date",      "Date key expires",         DateTime.Now.AddYears(1).ToString("dd-MMM-yyyy")),
            new TokenDef("download_url",     "Where to download",        "https://example.com/downloads"),
            CommonWebsite,
        },

        ["franchise_registered"] = new[]
        {
            new TokenDef("franchise_name",     "Franchise display name",  "Sample Centre"),
            new TokenDef("owner_name",         "Owner / contact",         "Riya Sharma"),
            new TokenDef("mobile",             "Contact mobile",          "9876543210"),
            new TokenDef("email",              "Contact email",           "riya.sharma@example.com"),
            new TokenDef("city",               "Branch city",             "Pune"),
            new TokenDef("state",              "Branch state",            "Maharashtra"),
            new TokenDef("application_number", "Application #",           "FA-2026-0017"),
            CommonWebsite,
        },

        ["franchise_approved"] = new[]
        {
            new TokenDef("franchise_name",      "Franchise display name",  "Sample Centre"),
            new TokenDef("owner_name",          "Owner name",              "Riya Sharma"),
            new TokenDef("approval_date",       "Date approved",           DateTime.Now.ToString("dd-MMM-yyyy")),
            new TokenDef("login_url",           "Franchise portal login",  "https://example.com/franchise/login"),
            new TokenDef("temporary_password",  "One-time password",       "Temp@123!"),
            CommonWebsite,
        },

        ["franchise_rejected"] = new[]
        {
            new TokenDef("franchise_name",     "Franchise display name",   "Sample Centre"),
            new TokenDef("owner_name",         "Owner name",                "Aman Mehta"),
            new TokenDef("rejection_reason",   "Reason for rejection",     "Documents incomplete — PAN copy missing."),
            CommonWebsite,
        },

        ["student_enrollment_success"] = new[]
        {
            new TokenDef("name",              "Learner's full name",                             "Test Student"),
            new TokenDef("login_id",          "Learner's login ID — email if set, else mobile",  "test.student@example.com"),
            new TokenDef("password_line",     "Password / activation instructions (pre-composed — secure, no plaintext password)",
                                               "Not set yet — tap “Login to Vijaypath” below, then use “Forgot Password” with the Login ID above to create one."),
            new TokenDef("course_name",       "Actual purchased/enrolled course name",           "7th Scholarship Exam – Online Classes | Marathi Medium | Self Registration"),
            new TokenDef("enrollment_status", "Enrollment status",                                "Active"),
            new TokenDef("login_url",         "Direct login page URL",                            "https://vijaypath.org/login"),
            new TokenDef("support_mobile",    "Support phone number",                             "+91 84118 82618"),
            new TokenDef("support_email",     "Support email address",                            "contact@vijaypath.org"),
            new TokenDef("website_url",       "Public site URL",                                  "https://vijaypath.org/"),
        },

        ["wallet_recharged"] = new[]
        {
            new TokenDef("franchise_name",     "Franchise display name",   "Sample Centre"),
            new TokenDef("wallet_before",      "Balance before recharge",   "₹4,250"),
            new TokenDef("wallet_recharged",   "Amount credited",           "₹50,000"),
            new TokenDef("wallet_after",       "New balance",               "₹54,250"),
            new TokenDef("transaction_id",     "Recharge transaction id",   "TXN-RZP-2026-009812"),
            new TokenDef("transaction_date",   "When recharged",            DateTime.Now.ToString("dd-MMM-yyyy HH:mm")),
        },
    };

    public IReadOnlyCollection<string> KnownTriggerKeys => _catalog.Keys.ToArray();

    public IReadOnlyList<TokenDef> GetForTrigger(string? triggerKey)
        => !string.IsNullOrWhiteSpace(triggerKey) && _catalog.TryGetValue(triggerKey, out var list)
            ? list
            : Array.Empty<TokenDef>();

    public IDictionary<string, string?> BuildSampleData(string? triggerKey, IDictionary<string, string?>? overrides = null)
    {
        var sample = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // 1. Start from the trigger-specific tokens.
        foreach (var t in GetForTrigger(triggerKey)) sample[t.Name] = t.SampleValue;

        // 2. Add the cross-trigger fallback set so older templates with legacy
        //    tokens like {{name}}, {{order_number}}, {{total}} still render.
        sample.TryAdd("name",           "Lokesh Patil");
        sample.TryAdd("email",          "lokesh.patil@example.com");
        sample.TryAdd("mobile",         "9876543210");
        sample.TryAdd("order_number",   "RIO-2026-0457");
        sample.TryAdd("total",          "₹12,500");
        sample.TryAdd("amount",         "₹2,500");
        sample.TryAdd("balance",        "₹0");
        sample.TryAdd("course_name",    "Sample Course");
        sample.TryAdd("invoice_number", "INV/2026/0457");
        sample.TryAdd("items",          "1");
        sample.TryAdd("status",         "Confirmed");
        sample.TryAdd("franchise",      "Sample Centre");

        // 3. Caller overrides win — used by the Send Test modal so the admin
        //    can paste their own recipient + key data fields.
        if (overrides != null)
            foreach (var kvp in overrides) sample[kvp.Key] = kvp.Value;

        return sample;
    }
}
