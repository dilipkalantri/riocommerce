using RioCommerce.Core.DTOs.Products;
namespace RioCommerce.Core.DTOs.Faculty;

public record FacultyCard(
    Guid Id, string Code, string DisplayName, string? Designation, string? Qualifications,
    string? PhotoUrl, string[]? Subjects, string? YearsOfExperience, string? StudentsTaught,
    string? ShortDescription, string? Bio,
    int CourseCount, decimal AvgRating, int RatingCount);

public record FacultyProfile(
    Guid Id, string Code, string DisplayName, string? Designation, string? Qualifications,
    string? ShortDescription, string? Bio,
    string? PhotoUrl, string? YoutubeUrl, string? WhatsappNumber, string? CallNumber, string[]? Subjects,
    string? YearsOfExperience, string? StudentsTaught, string? HoursOfTeaching, string? StudentSatisfaction, string? AirHoldersNote,
    int CourseCount, decimal AvgRating, int RatingCount, List<ProductListItem> Courses);

// Shared helper used by both admin lists (trimmed display) and the public Course-detail
// faculty card (fallback when ShortDescription wasn't authored yet). Returns null when
// neither field has any usable content.
public static class FacultyShortDescription
{
    public const int BioFallbackLength = 250;

    public static string? Resolve(string? shortDescription, string? bio)
    {
        if (!string.IsNullOrWhiteSpace(shortDescription)) return shortDescription.Trim();
        if (string.IsNullOrWhiteSpace(bio)) return null;
        var b = bio.Trim();
        return b.Length <= BioFallbackLength ? b : b[..BioFallbackLength].TrimEnd() + "…";
    }
}
