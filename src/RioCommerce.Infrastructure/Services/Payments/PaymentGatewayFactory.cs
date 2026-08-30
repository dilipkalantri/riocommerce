using RioCommerce.Core.Interfaces;

namespace RioCommerce.Infrastructure.Services.Payments;

/// <summary>
/// Resolves a payment gateway by name. All <see cref="IPaymentGateway"/> implementations
/// registered with DI are injected via <c>IEnumerable&lt;IPaymentGateway&gt;</c>; the factory
/// keys them by their <c>Name</c> property (case-insensitive) so CheckoutService can pick
/// "Razorpay" or "Easebuzz" based on the customer's selection.
/// </summary>
public sealed class PaymentGatewayFactory : IPaymentGatewayFactory
{
    private readonly Dictionary<string, IPaymentGateway> _byName;
    private readonly IReadOnlyList<IPaymentGateway> _ordered;

    public PaymentGatewayFactory(IEnumerable<IPaymentGateway> gateways)
    {
        _ordered = gateways.ToList();
        _byName = _ordered.ToDictionary(g => g.Name, g => g, StringComparer.OrdinalIgnoreCase);
    }

    public IPaymentGateway Get(string name)
        => TryGet(name) ?? throw new InvalidOperationException($"No payment gateway registered with name '{name}'. Registered: {string.Join(", ", _byName.Keys)}.");

    public IPaymentGateway? TryGet(string name)
        => string.IsNullOrWhiteSpace(name) ? null
         : _byName.TryGetValue(name, out var g) ? g
         : null;

    public IEnumerable<IPaymentGateway> All => _ordered;
}
