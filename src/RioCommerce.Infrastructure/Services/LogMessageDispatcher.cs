using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services;

/// <summary>Default dispatcher — logs the message. Swap for SMTP/WhatsApp/SMS providers once keys are configured.</summary>
public class LogMessageDispatcher : IMessageDispatcher
{
    private readonly ILogger<LogMessageDispatcher> _log;
    public LogMessageDispatcher(ILogger<LogMessageDispatcher> log) => _log = log;

    public Task<(bool ok, string? error)> DispatchAsync(string channel, string recipient, string subject, string body)
    {
        _log.LogInformation("✉️  [{Channel}] → {Recipient} | {Subject}", channel, recipient, subject);
        return Task.FromResult<(bool, string?)>((true, null));
    }
}
