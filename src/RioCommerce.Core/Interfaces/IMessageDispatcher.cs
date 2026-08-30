namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Actually delivers a rendered message over a channel. The default is a logging dispatcher;
/// real SMTP / WhatsApp / SMS providers drop in behind this interface (keys configurable later).
/// </summary>
public interface IMessageDispatcher
{
    Task<(bool ok, string? error)> DispatchAsync(string channel, string recipient, string subject, string body);
}
