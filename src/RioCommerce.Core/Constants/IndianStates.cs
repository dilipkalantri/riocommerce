namespace RioCommerce.Core.Constants;

/// <summary>
/// The 28 states and 8 union territories of India, as their official names.
///
/// <para><b>These strings ARE the persisted value.</b> A form binds a selection straight to whatever
/// <c>State</c> column it is filling — there is no code, no id and no lookup table — so the spelling
/// here is what ends up on the order, the invoice and the GST place-of-supply comparison. Renaming
/// an entry would silently orphan every row already carrying the old spelling.</para>
///
/// <para>States first in alphabetical order, then the union territories, which is how a customer
/// expects to scan the list. <c>Maharashtra</c> deliberately sits in its alphabetical place rather
/// than being hoisted to the top: the seller's own state leading the list is a habit that makes
/// out-of-state orders easy to mis-key.</para>
///
/// <para>Three admin/checkout screens still carry their own older, shorter arrays of their own. They
/// are deliberately untouched — this list exists for them to adopt when someone is working in that
/// code, not as a reason to change forms nobody asked about.</para>
/// </summary>
public static class IndianStates
{
    /// <summary>The 28 states.</summary>
    public static readonly IReadOnlyList<string> States = new[]
    {
        "Andhra Pradesh",
        "Arunachal Pradesh",
        "Assam",
        "Bihar",
        "Chhattisgarh",
        "Goa",
        "Gujarat",
        "Haryana",
        "Himachal Pradesh",
        "Jharkhand",
        "Karnataka",
        "Kerala",
        "Madhya Pradesh",
        "Maharashtra",
        "Manipur",
        "Meghalaya",
        "Mizoram",
        "Nagaland",
        "Odisha",
        "Punjab",
        "Rajasthan",
        "Sikkim",
        "Tamil Nadu",
        "Telangana",
        "Tripura",
        "Uttar Pradesh",
        "Uttarakhand",
        "West Bengal",
    };

    /// <summary>The 8 union territories.</summary>
    public static readonly IReadOnlyList<string> UnionTerritories = new[]
    {
        "Andaman and Nicobar Islands",
        "Chandigarh",
        "Dadra and Nagar Haveli and Daman and Diu",
        "Delhi",
        "Jammu and Kashmir",
        "Ladakh",
        "Lakshadweep",
        "Puducherry",
    };

    /// <summary>All 36, states then union territories — the order a dropdown should render.</summary>
    public static readonly IReadOnlyList<string> All =
        States.Concat(UnionTerritories).ToArray();
}
