using System.Globalization;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;

namespace RioCommerce.Infrastructure.Services;

// Typed finance/payout configuration over ISettingService. Centralises the finance.* / payout.* keys so
// domain services and the admin UI never touch raw setting strings.
public class FinanceSettingsService : IFinanceSettingsService
{
    public const string FinanceCategory = "finance";
    public const string PayoutCategory = "payout";

    // Keys (single source of truth for the whole finance subsystem).
    public const string FranchiseCommissionPct = "finance.franchise_commission_pct";
    public const string TdsPct = "finance.tds_pct";
    public const string SettlementApprovalThreshold = "finance.settlement_approval_threshold";
    public const string RefundApprovalThreshold = "finance.refund_approval_threshold";
    public const string DefaultGstPct = "finance.default_gst_pct";
    public const string TaxModeKey = "finance.tax_mode";
    public const string RoundingModeKey = "finance.rounding_mode";

    public const string AutoSettlement = "payout.auto_settlement";
    public const string PayoutFrequencyKey = "payout.frequency";
    public const string MinimumPayoutAmount = "payout.minimum_amount";
    public const string MaxRetries = "payout.max_retries";
    public const string RetryIntervalHours = "payout.retry_interval_hours";

    private readonly ISettingService _settings;
    public FinanceSettingsService(ISettingService settings) => _settings = settings;

    public async Task<FinanceSettings> GetFinanceAsync() => new()
    {
        FranchiseCommissionPct = await _settings.GetDecimalAsync(FranchiseCommissionPct, 0m),
        TdsPct = await _settings.GetDecimalAsync(TdsPct, 0m),
        SettlementApprovalThreshold = await _settings.GetDecimalAsync(SettlementApprovalThreshold, 50000m),
        RefundApprovalThreshold = await _settings.GetDecimalAsync(RefundApprovalThreshold, 5000m),
        DefaultGstPct = await _settings.GetDecimalAsync(DefaultGstPct, 18m),
        TaxMode = await _settings.GetEnumAsync(TaxModeKey, TaxMode.Exclusive),
        RoundingMode = await _settings.GetEnumAsync(RoundingModeKey, RoundingMode.None),
    };

    public Task SaveFinanceAsync(FinanceSettings s, Guid? actorId, string actorName) =>
        _settings.SetManyAsync(new[]
        {
            new SettingWrite(FranchiseCommissionPct, Num(s.FranchiseCommissionPct)),
            new SettingWrite(TdsPct, Num(s.TdsPct)),
            new SettingWrite(SettlementApprovalThreshold, Num(s.SettlementApprovalThreshold)),
            new SettingWrite(RefundApprovalThreshold, Num(s.RefundApprovalThreshold)),
            new SettingWrite(DefaultGstPct, Num(s.DefaultGstPct)),
            new SettingWrite(TaxModeKey, s.TaxMode.ToString()),
            new SettingWrite(RoundingModeKey, s.RoundingMode.ToString()),
        }, FinanceCategory, actorId, actorName);

    public async Task<PayoutSettings> GetPayoutAsync() => new()
    {
        AutoSettlementEnabled = await _settings.GetBoolAsync(AutoSettlement, false),
        Frequency = await _settings.GetEnumAsync(PayoutFrequencyKey, PayoutFrequency.Monthly),
        MinimumPayoutAmount = await _settings.GetDecimalAsync(MinimumPayoutAmount, 0m),
        MaxRetries = await _settings.GetIntAsync(MaxRetries, 3),
        RetryIntervalHours = await _settings.GetIntAsync(RetryIntervalHours, 24),
    };

    public Task SavePayoutAsync(PayoutSettings s, Guid? actorId, string actorName) =>
        _settings.SetManyAsync(new[]
        {
            new SettingWrite(AutoSettlement, s.AutoSettlementEnabled ? "true" : "false"),
            new SettingWrite(PayoutFrequencyKey, s.Frequency.ToString()),
            new SettingWrite(MinimumPayoutAmount, Num(s.MinimumPayoutAmount)),
            new SettingWrite(MaxRetries, s.MaxRetries.ToString(CultureInfo.InvariantCulture)),
            new SettingWrite(RetryIntervalHours, s.RetryIntervalHours.ToString(CultureInfo.InvariantCulture)),
        }, PayoutCategory, actorId, actorName);

    public async Task<List<SettingHistoryDto>> GetHistoryAsync(int take = 30)
    {
        var fin = await _settings.HistoryByCategoryAsync(FinanceCategory, take);
        var pay = await _settings.HistoryByCategoryAsync(PayoutCategory, take);
        return fin.Concat(pay).OrderByDescending(h => h.CreatedAt).Take(take).ToList();
    }

    private static string Num(decimal d) => d.ToString(CultureInfo.InvariantCulture);
}
