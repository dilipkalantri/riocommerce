namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Trigger → tokens registry. Each trigger key (welcome_email, order_placed, …)
/// declares the tokens its template body can use, plus a short admin-facing
/// description and a realistic sample value used by Preview and Send-Test.
///
/// Templates whose trigger key isn't in the catalog still work — they fall back
/// to a default mixed sample set so unknown keys aren't broken.
/// </summary>
public interface ITemplateTokenCatalog
{
    /// <summary>Trigger keys we have curated catalogs for.</summary>
    IReadOnlyCollection<string> KnownTriggerKeys { get; }

    /// <summary>Returns the tokens the admin can use for this trigger key.</summary>
    IReadOnlyList<TokenDef> GetForTrigger(string? triggerKey);

    /// <summary>Sample data dictionary for Preview + Send Test, keyed by trigger.</summary>
    IDictionary<string, string?> BuildSampleData(string? triggerKey, IDictionary<string, string?>? overrides = null);
}

public sealed record TokenDef(string Name, string Description, string SampleValue);
