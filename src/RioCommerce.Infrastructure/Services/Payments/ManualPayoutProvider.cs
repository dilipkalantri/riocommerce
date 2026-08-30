using RioCommerce.Core.Interfaces;

namespace RioCommerce.Infrastructure.Services.Payments;

// Default payout rail: the settlement is executed out-of-band (bank transfer / UPI / cash) and the
// operator records the reference. This is a real settlement path, not a simulated gateway — it simply
// trusts the human-entered UTR/reference. Razorpay/UPI API providers implement IPayoutProvider later.
public class ManualPayoutProvider : IPayoutProvider
{
    public string Name => "Manual";
    public bool SupportsBatch => true;

    public Task<PayoutProviderResult> SendAsync(PayoutInstruction instruction)
    {
        var reference = string.IsNullOrWhiteSpace(instruction.Reference)
            ? $"MANUAL-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : instruction.Reference.Trim();
        return Task.FromResult(new PayoutProviderResult(true, reference, "Recorded as manually settled", PayoutDispatchStatus.Succeeded));
    }
}
