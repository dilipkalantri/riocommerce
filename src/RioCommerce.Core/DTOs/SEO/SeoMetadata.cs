namespace RioCommerce.Core.DTOs.SEO;
public class SeoMetadata
{
    public string Title { get; set; } = "RioCommerce — CA Coaching in Pune";
    public string Description { get; set; } = "Best CA Foundation & Intermediate coaching by CA Harshad Jaju.";
    public string? CanonicalUrl { get; set; }
    public string? OgImage { get; set; }
    public string OgType { get; set; } = "website";
    public string? Keywords { get; set; }
    public string? JsonLd { get; set; }
}
