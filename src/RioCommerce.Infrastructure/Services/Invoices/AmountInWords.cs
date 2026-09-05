namespace RioCommerce.Infrastructure.Services.Invoices;

/// <summary>
/// Renders a rupee amount as words for the "Amount Chargeable (in words)" and
/// "Tax Amount (in words)" lines on the tax invoice.
///
/// Uses the INDIAN numbering system (crore / lakh / thousand), not the short scale —
/// 1,50,000 reads "One Lakh Fifty Thousand", never "One Hundred Fifty Thousand".
///
/// Presentation only. It never rounds or alters an amount: callers pass a value already
/// persisted on the invoice, and the paise shown here are the paise stored there.
/// </summary>
public static class AmountInWords
{
    private static readonly string[] Ones =
    {
        "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
        "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen",
        "Seventeen", "Eighteen", "Nineteen"
    };

    private static readonly string[] Tens =
    {
        "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
    };

    /// <summary>
    /// "Indian Rupees Two Hundred Only", or with a paise component
    /// "Indian Rupees Two Hundred and Fifty Paise Only".
    /// </summary>
    public static string Rupees(decimal amount)
    {
        var negative = amount < 0;
        amount = Math.Abs(amount);

        // Round to paise ONCE, then split, so 0.005 cases cannot produce
        // "Zero Rupees and One Hundred Paise".
        var totalPaise = (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        var whole = totalPaise / 100;
        var paise = (int)(totalPaise % 100);

        var text = whole == 0 && paise == 0
            ? "Zero"
            : whole == 0 ? "Zero" : Convert(whole);

        var s = $"Indian Rupees {text}";
        if (paise > 0) s += $" and {Convert(paise)} Paise";
        s += " Only";
        return negative ? "Minus " + s : s;
    }

    /// <summary>Whole-number words with no currency wrapper — used for sub-parts.</summary>
    public static string Convert(long n)
    {
        if (n == 0) return "Zero";

        var parts = new List<string>();

        // Indian grouping: crore (10^7), lakh (10^5), thousand (10^3), then the last three digits.
        var crore = n / 10_000_000; n %= 10_000_000;
        var lakh = n / 100_000; n %= 100_000;
        var thousand = n / 1_000; n %= 1_000;
        var hundred = n / 100; n %= 100;

        if (crore > 0) parts.Add($"{Convert(crore)} Crore");
        if (lakh > 0) parts.Add($"{TwoDigits((int)lakh)} Lakh");
        if (thousand > 0) parts.Add($"{TwoDigits((int)thousand)} Thousand");
        if (hundred > 0) parts.Add($"{Ones[hundred]} Hundred");
        if (n > 0) parts.Add(TwoDigits((int)n));

        return string.Join(" ", parts);
    }

    private static string TwoDigits(int n)
    {
        if (n < 20) return Ones[n];
        var t = Tens[n / 10];
        var o = n % 10;
        return o == 0 ? t : $"{t} {Ones[o]}";
    }
}
