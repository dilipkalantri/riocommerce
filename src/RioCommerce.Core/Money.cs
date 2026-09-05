using System.Globalization;

namespace RioCommerce.Core;

/// <summary>
/// Single place that renders a rupee amount for the UI.
///
/// <para>The symbol is written as the escape <c>"₹"</c> rather than a literal glyph. That is
/// deliberate: the source file then contains nothing but ASCII, so the value survives any tool that
/// mis-guesses a file's encoding. A literal glyph in a BOM-less UTF-8 file re-saved by a tool that
/// assumes Windows-1252 is re-encoded into three garbled characters — which is exactly how the
/// portal's prices broke. With the symbol defined once, here, that class of damage can no longer be
/// introduced page by page.</para>
///
/// <para>InvariantCulture keeps grouping as 1,000 / 12,345.67. It is NOT
/// <c>ToString("C")</c>: that would pick the server's current culture and could silently render
/// dollars, or move the symbol to the wrong side of the number.</para>
/// </summary>
public static class Money
{
    /// <summary>INDIAN RUPEE SIGN (U+20B9).</summary>
    public const string Rupee = "₹";

    /// <summary>Whole rupees, e.g. "₹200" / "₹1,000". For prices and totals on screen.</summary>
    public static string Inr0(decimal value) =>
        Rupee + value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Two decimal places, e.g. "₹200.00". For anything money-exact.</summary>
    public static string Inr(decimal value) =>
        Rupee + value.ToString("N2", CultureInfo.InvariantCulture);
}
