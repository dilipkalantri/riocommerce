namespace RioCommerce.Core.Interfaces;

// Provider-agnostic SMS dispatch. Reads admin-configured SmsSettings (MSG91 / Twilio / TextLocal / …)
// and either sends a real SMS or — when SMS is disabled / not configured — logs and returns ok.
public interface ISmsSender
{
    Task<(bool ok, string? error)> SendAsync(string toPhone, string body);
}
