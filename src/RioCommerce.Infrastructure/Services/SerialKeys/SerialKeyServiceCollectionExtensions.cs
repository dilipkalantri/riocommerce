using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Services.ScheduledTasks;
using RioCommerce.Infrastructure.Services.SerialKeys;
using RioCommerce.Infrastructure.Services.SerialKeys.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace RioCommerce.Infrastructure.Services.SerialKeys;

/// <summary>
/// Single-line DI bootstrapper for the serial-key feature. Wires up every service the
/// orchestrator, retry task, controllers, and admin Razor pages need:
///   • Typed HttpClient for the RioPlay API
///   • RioPlay provider (registered as ISerialKeyProvider — adding Valence later is one more line)
///   • Provider factory (resolves IEnumerable&lt;ISerialKeyProvider&gt;)
///   • RioPlay tenant CRUD service (encrypt/decrypt secrets via Data Protection)
///   • Main orchestrator + scheduled retry task
///
/// Call this from <c>Program.cs</c> exactly once, after the existing
/// payment-gateway registrations:
///
/// <code>
/// builder.Services.AddSerialKeyIntegration();
/// </code>
///
/// To add a future provider (Valence, etc.): keep the call to this method as-is,
/// then append one line:
///
/// <code>
/// builder.Services.AddScoped&lt;ISerialKeyProvider, ValenceSerialKeyProvider&gt;();
/// </code>
/// </summary>
public static class SerialKeyServiceCollectionExtensions
{
    public static IServiceCollection AddSerialKeyIntegration(this IServiceCollection services)
    {
        // Diagnostic logger — full request/response capture (secrets redacted) so we can compare
        // byte-for-byte against Postman when debugging WAF / 404 issues.
        services.AddTransient<RioPlayLoggingHandler>();

        // Typed HttpClient — gives Rio its own connection pool, timeout, and lifetime
        // management without colliding with payment gateways or anything else.
        services.AddHttpClient<RioPlayApiClient>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddHttpMessageHandler<RioPlayLoggingHandler>()
        // Enable automatic gzip/deflate handling so we can send Accept-Encoding: gzip,deflate to
        // match Postman's defaults — required by some WAF setups (Antargyan's included) to avoid
        // silent 404s on requests that look "non-browser".
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
        });

        // Providers — registered as ISerialKeyProvider so the factory picks them up via IEnumerable.
        // Each provider's typed class is ALSO registered so AddHttpClient<T>'s registration resolves
        // the same instance per scope.
        services.AddScoped<ISerialKeyProvider, RioPlaySerialKeyProvider>();

        // ── Valence (Edubees) ──
        // Named HttpClient consumed via IHttpClientFactory.CreateClient("valence"). Mirrors Rio's
        // automatic gzip/deflate so requests look browser-like to any WAF in front of Edubees.
        services.AddHttpClient("valence", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
        });
        services.AddScoped<ISerialKeyProvider, ValenceSerialKeyProvider>();
        services.AddScoped<IValencePackService, ValencePackService>();

        // ── Superclass LMS (registration API, keyless) ──
        // Named HttpClient consumed via IHttpClientFactory.CreateClient("superclass"). Logging handler
        // redacts the Authorization / X-API-Key headers so the API key never reaches a log. Automatic
        // gzip/deflate mirrors the other providers so requests look browser-like to any WAF.
        services.AddTransient<SuperclassLoggingHandler>();
        services.AddHttpClient("superclass", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddHttpMessageHandler<SuperclassLoggingHandler>()
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
        });
        services.AddScoped<ISerialKeyProvider, SuperclassSerialKeyProvider>();
        services.AddScoped<ISuperclassSettingsService, SuperclassSettingsService>();

        // Factory + orchestrator + tenant CRUD.
        services.AddScoped<ISerialKeyProviderFactory, SerialKeyProviderFactory>();
        services.AddScoped<IRioPlayTenantService, RioPlayTenantService>();
        services.AddScoped<ISerialKeyService, SerialKeyService>();

        // Scheduled task — your existing scheduled-task seed loop in Program.cs picks this up by
        // its IScheduledTaskHandler.Key on the next app start. The runner gets a fresh scope per
        // run so the handler injects a clean DbContext.
        services.AddScoped<SerialKeyRetryTask>();
        services.AddScoped<IScheduledTaskHandler>(sp => sp.GetRequiredService<SerialKeyRetryTask>());

        return services;
    }
}
