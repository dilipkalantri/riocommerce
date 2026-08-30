namespace RioCommerce.Infrastructure.Services.SerialKeys.Providers;

/// <summary>
/// The one place that turns an admin-entered Valence Base URL into the prefix every Valence path is
/// appended to.
///
/// <para><b>Why this exists.</b> Every Valence path constant already begins with "/index.php/"
/// (<c>register_student_with_course</c>, <c>get_packs</c>, <c>save_product_pack</c>), while admins
/// routinely paste the Base URL WITH a trailing "/index.php" — the config hint even says it will be
/// stripped. Concatenating the two verbatim yields ".../index.php/index.php/..." and a flat 404.</para>
///
/// <para>The registration provider normalised this inline; the pack service did not. The result was a
/// setup that could register a student against a pack but could never CREATE the product↔pack
/// mapping, so every newly configured product failed with "No pack found for this course" while
/// older, already-mapped products kept working — a split-brain that looked like an outage. Both
/// callers now share this method so the two cannot drift again.</para>
/// </summary>
internal static class ValenceUrl
{
    private const string IndexPhp = "/index.php";

    /// <summary>Trims trailing slashes and a trailing "/index.php" so the caller's own
    /// "/index.php/{segment}/{action}" is the single, canonical one.</summary>
    public static string Base(string baseUrl)
    {
        var s = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (s.EndsWith(IndexPhp, StringComparison.OrdinalIgnoreCase))
            s = s[..^IndexPhp.Length].TrimEnd('/');
        return s;
    }
}
