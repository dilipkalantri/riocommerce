using RioCommerce.Core.Interfaces;

namespace RioCommerce.API.Services;

/// <summary>Implementation of <see cref="IRequestContext"/> backed by <see cref="IHttpContextAccessor"/>.</summary>
public class HttpRequestContext : IRequestContext
{
    private readonly IHttpContextAccessor _http;
    public HttpRequestContext(IHttpContextAccessor http) => _http = http;

    public string? IpAddress => _http.HttpContext?.Connection?.RemoteIpAddress?.ToString();
    public string? UserAgent
    {
        get
        {
            var ua = _http.HttpContext?.Request?.Headers["User-Agent"].ToString();
            return string.IsNullOrWhiteSpace(ua) ? null : ua;
        }
    }
}
