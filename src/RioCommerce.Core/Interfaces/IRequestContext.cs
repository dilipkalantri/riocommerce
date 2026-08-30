namespace RioCommerce.Core.Interfaces;

/// <summary>Thin abstraction over <c>HttpContext</c> so the Infrastructure layer can fingerprint
/// requests (IP / User-Agent) without taking a hard dependency on ASP.NET Core.</summary>
public interface IRequestContext
{
    string? IpAddress { get; }
    string? UserAgent { get; }
}
