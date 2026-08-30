using System.Text.RegularExpressions;

namespace RioCommerce.Core.Security;

/// <summary>
/// Turns an editor-supplied YouTube link into a safe embed URL.
///
/// The stored value is always the plain URL the editor typed — never iframe HTML —
/// and the src an iframe finally receives is BUILT here from a validated 11-character
/// video id, not copied from input. That is what makes arbitrary iframe injection
/// impossible: no matter what is pasted, the only thing that can ever reach the page
/// is https://www.youtube.com/embed/{id} where id matched [A-Za-z0-9_-]{11}.
///
/// An allowlist of hosts is used rather than a "contains youtube" check, so
/// lookalikes such as youtube.com.evil.tld or a javascript: URL cannot pass.
/// </summary>
public static class YouTubeEmbed
{
    private static readonly TimeSpan RxTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Exactly the 11 characters YouTube uses for a video id.</summary>
    private static readonly Regex IdShape =
        new(@"^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant, RxTimeout);

    private static readonly string[] AllowedHosts =
    {
        "youtube.com", "www.youtube.com", "m.youtube.com",
        "youtube-nocookie.com", "www.youtube-nocookie.com",
        "youtu.be", "www.youtu.be",
    };

    /// <summary>
    /// Extracts the video id from watch?v=, youtu.be/, /embed/, /shorts/ and /live/ forms.
    /// Returns null when the input is not a YouTube link carrying a well-formed id.
    /// </summary>
    public static string? TryGetVideoId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var raw = url.Trim();
        // Tolerate a pasted address with no scheme; anything else must parse as absolute.
        if (!raw.Contains("://", StringComparison.Ordinal)) raw = "https://" + raw;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (!AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) return null;

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        // youtu.be/<id>
        if (uri.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase))
            return Valid(segments.FirstOrDefault());

        // /embed/<id>, /shorts/<id>, /live/<id>, /v/<id>
        if (segments.Length >= 2 &&
            (segments[0].Equals("embed", StringComparison.OrdinalIgnoreCase) ||
             segments[0].Equals("shorts", StringComparison.OrdinalIgnoreCase) ||
             segments[0].Equals("live", StringComparison.OrdinalIgnoreCase) ||
             segments[0].Equals("v", StringComparison.OrdinalIgnoreCase)))
            return Valid(segments[1]);

        // /watch?v=<id>
        if (segments.Length >= 1 && segments[0].Equals("watch", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("v", StringComparison.OrdinalIgnoreCase))
                    return Valid(Uri.UnescapeDataString(kv[1]));
            }
        }

        return null;
    }

    /// <summary>The embed URL for a link, or null when the link is not a usable YouTube video.</summary>
    public static string? TryGetEmbedUrl(string? url)
    {
        var id = TryGetVideoId(url);
        return id == null ? null : $"https://www.youtube.com/embed/{id}";
    }

    /// <summary>True when the link yields a valid video id.</summary>
    public static bool IsValid(string? url) => TryGetVideoId(url) != null;

    private static string? Valid(string? candidate) =>
        candidate != null && IdShape.IsMatch(candidate) ? candidate : null;
}
