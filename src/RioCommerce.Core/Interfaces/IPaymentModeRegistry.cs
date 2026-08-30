using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Returns the subset of <see cref="PaymentMode"/> values the admin is currently allowed to
/// pick from, driven by the gateway configuration in /admin/payment-settings:
///
///   • <c>Razorpay</c>     — exposed only when Razorpay.Enabled is true
///   • <c>Easebuzz</c>     — exposed only when Easebuzz.Enabled is true
///   • <c>BankTransfer</c> — always available (no gateway dependency)
///
/// Counter orders get two more — see <see cref="GetCounterAsync"/>. The remaining PaymentMode
/// values (Cheque, Ccavenue, RazorpayLink, Emi) stay in the enum for historical compatibility but
/// are NOT offered in any dropdown.
/// </summary>
public interface IPaymentModeRegistry
{
    /// <summary>List of allowed modes in display order. Re-evaluated on each call so a
    /// gateway being switched on in settings takes effect immediately.</summary>
    Task<List<PaymentMode>> GetAvailableAsync();

    /// <summary>
    /// <see cref="GetAvailableAsync"/> plus <c>Cash</c> and <c>Upi</c> — for the admin
    /// counter-order form only, where staff take the money in person.
    ///
    /// <para>Deliberately NOT part of <see cref="GetAvailableAsync"/>: that list also feeds the
    /// franchise order form and the Orders filter, and offering "Cash" there would be wrong.</para>
    /// </summary>
    Task<List<PaymentMode>> GetCounterAsync();

    /// <summary>
    /// The ONLINE gateway a new payment should be started on, resolved from the admin gateway
    /// settings — the single source of truth. Returns null when no gateway is enabled/configured,
    /// so callers can refuse instead of guessing.
    ///
    /// <para><b>Never falls back to a default gateway.</b> Business flows used to hardcode Razorpay,
    /// which meant an admin who enabled only Easebuzz still got Razorpay orders that could not be
    /// paid. Use this for STARTING a payment; to VERIFY an existing one, resolve the gateway from
    /// the gateway recorded on that order instead — see <see cref="GatewayNameFor"/>.</para>
    ///
    /// <para>When more than one gateway is enabled the first in <see cref="GetAvailableAsync"/>
    /// order wins, which is the settings page's own top-down order.</para>
    /// </summary>
    Task<ResolvedGateway?> ResolveOnlineGatewayAsync();

    /// <summary>
    /// The registered <see cref="IPaymentGateway"/> name for a payment mode, or null when the mode is
    /// not an online gateway (Cash, BankTransfer, …). Deliberately null rather than a default: a
    /// silent fallback is what let disabled-gateway orders be created in the first place.
    /// </summary>
    string? GatewayNameFor(PaymentMode mode);
}

/// <summary>An online gateway resolved from configuration: the domain payment mode plus the
/// <see cref="IPaymentGateway.Name"/> to look it up with. Keeps mode↔name mapping in one place.</summary>
public record ResolvedGateway(PaymentMode Mode, string Name);
