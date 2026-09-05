using RioCommerce.Core.DTOs.Invoices;
using RioCommerce.Core.Interfaces;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Typed company / invoice-issuer configuration over ISettingService. Single source of truth for
/// the company.* keys — InvoiceService reads through <see cref="ReadAsync"/> and the admin screen
/// writes through <see cref="SaveAsync"/>, so both sides always agree on the key names.
/// </summary>
public class CompanySettingsService : ICompanySettingsService
{
    public const string Category = "company";

    // Keys — the whole company.* surface, in one place.
    public const string NameKey = "company.name";
    public const string AddressKey = "company.address";
    public const string GstinKey = "company.gstin";
    public const string PhoneKey = "company.phone";
    public const string EmailKey = "company.email";
    public const string PanKey = "company.pan";
    public const string BankNameKey = "company.bank_name";
    public const string BankAccountNameKey = "company.bank_account_name";
    public const string BankAccountNumberKey = "company.bank_account_number";
    public const string BankIfscKey = "company.bank_ifsc";
    public const string BankBranchKey = "company.bank_branch";
    public const string InvoiceSeriesKey = "company.invoice_series";
    public const string OrderSeriesKey = "company.order_series";

    // Not owned by the company screen, but part of the same prefix — listed so it is obvious
    // they are deliberately NOT overwritten on save.
    public const string WebsiteKey = "company.website";
    public const string LogoUrlKey = "company.logo_url";

    private readonly ISettingService _settings;
    public CompanySettingsService(ISettingService settings) => _settings = settings;

    /// <summary>
    /// Shared reader. Static so InvoiceService can use it without taking a new constructor
    /// dependency, while still going through the one set of key constants.
    /// </summary>
    public static async Task<CompanyProfile> ReadAsync(ISettingService settings, string? fallbackName = null)
    {
        var name = await settings.GetStringAsync(NameKey);
        return new CompanyProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? (fallbackName ?? "RioCommerce") : name,
            Address = await settings.GetStringAsync(AddressKey),
            Gstin = await settings.GetStringAsync(GstinKey),
            Phone = await settings.GetStringAsync(PhoneKey),
            Email = await settings.GetStringAsync(EmailKey),
            Website = await settings.GetStringAsync(WebsiteKey),
            LogoUrl = await settings.GetStringAsync(LogoUrlKey),
            Pan = await settings.GetStringAsync(PanKey),
            BankName = await settings.GetStringAsync(BankNameKey),
            BankAccountName = await settings.GetStringAsync(BankAccountNameKey),
            BankAccountNumber = await settings.GetStringAsync(BankAccountNumberKey),
            BankIfsc = await settings.GetStringAsync(BankIfscKey),
            BankBranch = await settings.GetStringAsync(BankBranchKey),
            InvoiceSeries = await settings.GetStringAsync(InvoiceSeriesKey),
            OrderSeries = await settings.GetStringAsync(OrderSeriesKey),
        };
    }

    public Task<CompanyProfile> GetAsync() => ReadAsync(_settings);

    public Task SaveAsync(CompanyProfile p, Guid? actorId, string actorName) =>
        _settings.SetManyAsync(new[]
        {
            new SettingWrite(NameKey,              Trim(p.Name)),
            new SettingWrite(AddressKey,           Trim(p.Address)),
            // Statutory identifiers are stored upper-case: GSTIN, PAN and IFSC are defined as
            // upper-case, and normalising on write means the invoice never shows "27aaati…".
            new SettingWrite(GstinKey,             Upper(p.Gstin)),
            new SettingWrite(PhoneKey,             Trim(p.Phone)),
            new SettingWrite(EmailKey,             Trim(p.Email)),
            new SettingWrite(PanKey,               Upper(p.Pan)),
            new SettingWrite(BankNameKey,          Trim(p.BankName)),
            new SettingWrite(BankAccountNameKey,   Trim(p.BankAccountName)),
            new SettingWrite(BankAccountNumberKey, Trim(p.BankAccountNumber)),
            new SettingWrite(BankIfscKey,          Upper(p.BankIfsc)),
            new SettingWrite(BankBranchKey,        Trim(p.BankBranch)),
            // Prefix only. The running number is allocated by InvoiceService, never stored here.
            new SettingWrite(InvoiceSeriesKey,     Trim(p.InvoiceSeries)),
            // Prefix only; the running number is allocated by OrderNumberGenerator.
            new SettingWrite(OrderSeriesKey,       Trim(p.OrderSeries)),
            // Note: WebsiteKey / LogoUrlKey are intentionally absent — this screen does not own
            // them, and including them would blank whatever another screen has set.
        }, Category, actorId, actorName);

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string? Upper(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();
}
