using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Interfaces;

// Outcome of a provider payout attempt. Status drives the payout/batch lifecycle.
public enum PayoutDispatchStatus { Succeeded, Pending, Failed }

public record PayoutProviderResult(bool Success, string? Reference, string? Message, PayoutDispatchStatus Status);

// A single transfer instruction handed to a provider. Bank/UPI fields are optional so the same shape
// works for manual settlement, bank-file export, and (future) Razorpay/UPI API payouts.
public record PayoutInstruction(
    Guid PayoutId, PayoutType BeneficiaryType, string BeneficiaryName, decimal Amount,
    string? BankAccount, string? BankIfsc, string? Upi, string? Reference);

// Pluggable payout rail. Real implementation today is ManualPayoutProvider (offline/bank settlement);
// Razorpay/UPI/bank-API providers slot in later behind this same contract — no caller changes.
public interface IPayoutProvider
{
    string Name { get; }
    bool SupportsBatch { get; }
    Task<PayoutProviderResult> SendAsync(PayoutInstruction instruction);
}
