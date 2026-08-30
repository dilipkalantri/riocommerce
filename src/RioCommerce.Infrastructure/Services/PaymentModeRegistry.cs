using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Builds the allowed-payment-mode list per request from the active gateway settings. Order:
/// Razorpay, Easebuzz, BankTransfer — matches what the admin sees in the settings page top-down.
///
/// <para>Two lists, deliberately: <see cref="GetAvailableAsync"/> is the general one (franchise
/// order form, Orders filter), and <see cref="GetCounterAsync"/> adds the modes that only make
/// sense when staff are taking payment in person.</para>
/// </summary>
public sealed class PaymentModeRegistry : IPaymentModeRegistry
{
    private readonly IIntegrationSettingsService _settings;
    public PaymentModeRegistry(IIntegrationSettingsService settings) { _settings = settings; }

    public async Task<List<PaymentMode>> GetAvailableAsync()
    {
        var list = new List<PaymentMode>();

        // Razorpay — needs Enabled + at least a KeyId, otherwise the user picks a mode the
        // gateway can't actually process.
        var rp = await _settings.GetRazorpayAsync();
        if (rp != null && rp.Enabled && !string.IsNullOrWhiteSpace(rp.KeyId))
            list.Add(PaymentMode.Razorpay);

        // Easebuzz — same gate.
        var eb = await _settings.GetEasebuzzAsync();
        if (eb != null && eb.Enabled && !string.IsNullOrWhiteSpace(eb.MerchantKey))
            list.Add(PaymentMode.Easebuzz);

        // BankTransfer — always offered. It's a manual-recording mode (admin marks the order
        // paid after the bank statement reconciles), no gateway configuration needed.
        list.Add(PaymentMode.BankTransfer);

        return list;
    }

    public async Task<ResolvedGateway?> ResolveOnlineGatewayAsync()
    {
        // GetAvailableAsync already applies the Enabled + credentials gate per gateway, so reusing it
        // keeps one definition of "this gateway is usable" instead of a second copy that can drift.
        // Its order is the settings page's top-down order, which is the priority when both are on.
        foreach (var mode in await GetAvailableAsync())
        {
            var name = GatewayNameFor(mode);
            if (name != null) return new ResolvedGateway(mode, name);
        }
        return null;   // nothing enabled — the caller must refuse, never substitute a default
    }

    public string? GatewayNameFor(PaymentMode mode) => mode switch
    {
        PaymentMode.Razorpay => "Razorpay",
        PaymentMode.Easebuzz => "Easebuzz",
        // Cash / Upi / BankTransfer / Cheque / … are recorded manually; they have no gateway.
        _ => null,
    };

    public async Task<List<PaymentMode>> GetCounterAsync()
    {
        // Counter orders are taken face-to-face by staff, so the money can arrive as cash in hand
        // or a UPI transfer to the counter — neither of which any gateway setting describes.
        // These are added ONLY here, not to GetAvailableAsync: that list also drives the franchise
        // order form and the Orders filter, where "Cash" would be wrong or misleading.
        var list = await GetAvailableAsync();
        list.Insert(0, PaymentMode.Cash);
        list.Insert(1, PaymentMode.Upi);
        return list;
    }
}
