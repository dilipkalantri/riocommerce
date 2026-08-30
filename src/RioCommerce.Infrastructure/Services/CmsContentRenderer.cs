using System.Text;
using System.Text.RegularExpressions;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Renders stored CMS <see cref="Core.Entities.CmsPage.Body"/> HTML for the public storefront in a
/// strictly page-scoped, script-free way.
///
/// <para>CMS pages store their custom design as a single combined HTML blob that may contain inline
/// <c>&lt;style&gt;</c> blocks (the established authoring model). Rendering that blob verbatim would
/// leak its CSS site-wide (a bare <c>&lt;style&gt;</c> element applies to the whole document) and could
/// run arbitrary scripts. This renderer:</para>
/// <list type="number">
///   <item>Extracts every <c>&lt;style&gt;</c> block out of the body.</item>
///   <item>Sanitizes the remaining HTML — drops <c>&lt;script&gt;</c>, inline <c>on*</c> handlers and
///         <c>javascript:</c> URLs (safe structural/formatting HTML is preserved).</item>
///   <item>Prefixes every CSS selector with the page's unique wrapper attribute
///         <c>[data-cms-page-id="{id}"]</c> so the styles can only ever match elements inside that
///         one CMS page. <c>body</c>/<c>html</c>/<c>:root</c> are mapped to the wrapper itself, so a
///         rule like <c>body{background:black}</c> colours only the CMS container, never the real page.</item>
/// </list>
///
/// <para>The scoper is a small brace-aware CSS parser (not blind string replacement). It correctly
/// handles multi-selector lists, pseudo-classes/elements, <c>@media</c>/<c>@supports</c> (inner
/// selectors are scoped, the query is preserved) and leaves <c>@keyframes</c>/<c>@font-face</c>
/// bodies untouched so animations and fonts keep working.</para>
/// </summary>
public static class CmsContentRenderer
{
    private static readonly RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled;

    private static readonly Regex StyleBlock = new(@"<style\b[^>]*>(.*?)</style>", Opt);
    private static readonly Regex ScriptBlock = new(@"<script\b[^>]*>.*?</script>", Opt);
    private static readonly Regex ScriptOpenOrClose = new(@"</?script\b[^>]*>", Opt);
    private static readonly Regex OnHandler = new(@"\s+on[a-z][a-z0-9_-]*\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", Opt);
    private static readonly Regex JsScheme = new(@"javascript\s*:", Opt);
    private static readonly Regex CssComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LeadingRoot = new(@"^(?:html|body|:root)(?![\w-])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// CMS pages: splits the combined body into safe, style-free HTML plus CSS scoped to
    /// <c>[data-cms-page-id="{pageId}"]</c>. Custom CSS is authored inline as <c>&lt;style&gt;</c> blocks.
    /// </summary>
    public static (string Html, string Css) Render(string? body, Guid pageId)
        => RenderScoped(body, null, "data-cms-page-id", pageId);

    /// <summary>
    /// Shared render pipeline used by every module that renders admin-authored HTML/CSS on the
    /// storefront (CMS pages, blog posts, …). Any <c>&lt;style&gt;</c> blocks inside <paramref name="bodyHtml"/>
    /// plus the separate <paramref name="customCss"/> field are collected, then every selector is
    /// prefixed with <c>[{scopeAttribute}="{id}"]</c> so the styles can only ever match elements inside
    /// that one wrapper. The returned HTML is sanitized (scripts / on* handlers / javascript: removed).
    /// </summary>
    /// <param name="bodyHtml">The content HTML (may contain inline &lt;style&gt; blocks).</param>
    /// <param name="customCss">Optional separate CSS field (e.g. BlogPost.CustomCss).</param>
    /// <param name="scopeAttribute">The wrapper's data attribute, e.g. "data-cms-page-id" / "data-blog-id".</param>
    /// <param name="id">The unique entity id that keys the wrapper.</param>
    public static (string Html, string Css) RenderScoped(string? bodyHtml, string? customCss, string scopeAttribute, Guid id)
    {
        var scope = $"[{scopeAttribute}=\"{id}\"]";

        // 1) Pull out every inline <style> block from the body, collecting its raw CSS.
        var rawCss = new StringBuilder();
        var htmlNoStyles = string.IsNullOrEmpty(bodyHtml)
            ? ""
            : StyleBlock.Replace(bodyHtml, m => { rawCss.Append(m.Groups[1].Value).Append('\n'); return ""; });

        // 2) Append the separately-stored custom CSS (the preferred field for the blog module).
        if (!string.IsNullOrWhiteSpace(customCss)) rawCss.Append('\n').Append(customCss);

        // 3) Sanitize the remaining HTML, and scope all collected CSS to this entity only.
        var safeHtml = SanitizeHtml(htmlNoStyles);
        var scopedCss = ScopeCss(rawCss.ToString(), scope);

        return (safeHtml, scopedCss);
    }

    // ── HTML sanitization ────────────────────────────────────────────────────────────────────
    // Keeps safe structural/formatting HTML; removes script execution vectors only. (CMS pages are
    // authored by authenticated admins, so this is defence-in-depth, not the primary trust boundary.)
    private static string SanitizeHtml(string html)
    {
        html = ScriptBlock.Replace(html, "");        // <script>…</script>
        html = ScriptOpenOrClose.Replace(html, "");  // stray <script …> / </script>
        html = OnHandler.Replace(html, "");          // inline onclick/onerror/onload/…
        html = JsScheme.Replace(html, "blocked:");   // javascript: URLs
        return html.Trim();
    }

    // ── CSS scoping (brace-aware) ──────────────────────────────────────────────────────────────
    private static string ScopeCss(string css, string scope)
    {
        css = CssComment.Replace(css ?? "", "");
        if (string.IsNullOrWhiteSpace(css)) return "";

        var sb = new StringBuilder();
        int i = 0, n = css.Length;
        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(css[i])) i++;
            if (i >= n) break;

            if (css[i] == '@')
            {
                int start = i;
                while (i < n && css[i] != '{' && css[i] != ';') i++;
                var prelude = css.Substring(start, i - start).Trim();
                var name = AtRuleName(prelude);

                if (i < n && css[i] == ';')      // statement at-rule (@import/@charset/@namespace)
                {
                    i++;
                    if (name != "import")        // drop @import (would pull unscoped external CSS)
                        sb.Append(prelude).Append(";\n");
                }
                else if (i < n && css[i] == '{') // block at-rule
                {
                    var inner = ReadBlock(css, ref i);
                    if (name is "media" or "supports" or "container" or "document")
                    {
                        // Preserve the query; scope the rules inside it.
                        sb.Append(prelude).Append(" {\n").Append(ScopeCss(inner, scope)).Append("}\n");
                    }
                    else
                    {
                        // @keyframes / @font-face / @page / @property / @counter-style … — never
                        // prefix their internals (keyframe steps & descriptors aren't selectors).
                        sb.Append(prelude).Append(" {").Append(inner).Append("}\n");
                    }
                }
                else break; // malformed tail
            }
            else
            {
                int start = i;
                while (i < n && css[i] != '{') i++;
                if (i >= n) break; // no block → malformed tail, drop
                var selector = css.Substring(start, i - start).Trim();
                var block = ReadBlock(css, ref i);
                if (selector.Length == 0) continue;
                sb.Append(ScopeSelectorList(selector, scope)).Append(" {").Append(block).Append("}\n");
            }
        }
        return sb.ToString();
    }

    // Reads a balanced { … } block assuming css[i]=='{'; returns the inner text and advances i past '}'.
    private static string ReadBlock(string css, ref int i)
    {
        int n = css.Length, depth = 1;
        i++;                       // skip '{'
        int start = i;
        while (i < n && depth > 0)
        {
            char c = css[i];
            if (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) break; }
            i++;
        }
        var inner = css.Substring(start, Math.Max(0, i - start));
        if (i < n && css[i] == '}') i++;   // consume closing '}'
        return inner;
    }

    private static string ScopeSelectorList(string selectorList, string scope)
    {
        var parts = SplitTopLevel(selectorList, ',');
        var scoped = new List<string>(parts.Count);
        foreach (var raw in parts)
        {
            var sel = raw.Trim();
            if (sel.Length == 0) continue;
            scoped.Add(ScopeOneSelector(sel, scope));
        }
        return string.Join(", ", scoped);
    }

    private static string ScopeOneSelector(string sel, string scope)
    {
        // body / html / :root anchor to the wrapper element itself (so document-level rules can't escape).
        var m = LeadingRoot.Match(sel);
        if (m.Success)
        {
            var rest = sel[m.Length..];
            return (scope + rest).Trim();
        }
        // Universal or any other selector → descendant of the wrapper.
        return scope + " " + sel;
    }

    // Splits on `sep` at the top level only — ignores separators inside (), [] and quotes so
    // :nth-child(2n+1), :is(.a, .b) and [data-x="a,b"] stay intact.
    private static List<string> SplitTopLevel(string s, char sep)
    {
        var list = new List<string>();
        int paren = 0, bracket = 0, start = 0;
        char quote = '\0';
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            switch (c)
            {
                case '"' or '\'': quote = c; break;
                case '(': paren++; break;
                case ')': if (paren > 0) paren--; break;
                case '[': bracket++; break;
                case ']': if (bracket > 0) bracket--; break;
                default:
                    if (c == sep && paren == 0 && bracket == 0)
                    {
                        list.Add(s[start..i]);
                        start = i + 1;
                    }
                    break;
            }
        }
        list.Add(s[start..]);
        return list;
    }

    // "@media (max-width:768px)" → "media"; "@-webkit-keyframes x" → "-webkit-keyframes".
    private static string AtRuleName(string prelude)
    {
        int i = 1; // skip '@'
        while (i < prelude.Length && (char.IsLetterOrDigit(prelude[i]) || prelude[i] == '-')) i++;
        return prelude[1..i].ToLowerInvariant();
    }
}
