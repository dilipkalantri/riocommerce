namespace RioCommerce.Core.Enums;

/// <summary>
/// Where a lead was captured. A typed companion to <see cref="Entities.Lead.Source"/>, which is a
/// free-text tag ("website_popup", "contact_page", "blog_counselling") kept for backward
/// compatibility — this enum is what the admin filters and reports on.
///
/// Values are explicit because they are persisted as integers; never renumber them.
/// </summary>
public enum LeadSource
{
    /// <summary>Homepage enquiry popup (Source tag "website_popup").</summary>
    HomePage = 0,

    /// <summary>Counselling form on a blog detail page (Source tag "blog_counselling").
    /// These carry a <see cref="Entities.Lead.BlogPostId"/>.</summary>
    Blog = 1,

    /// <summary>Contact page form (Source tag "contact_page").</summary>
    ContactPage = 2,

    /// <summary>Anything not recognised — including leads imported or captured before this enum
    /// existed. Never assigned by guesswork; see migration 0030.</summary>
    Other = 3,
}
