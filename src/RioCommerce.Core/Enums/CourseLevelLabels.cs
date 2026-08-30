namespace RioCommerce.Core.Enums;

public static class CourseLevelLabels
{
    public static string Label(this CourseLevel level) => level switch
    {
        CourseLevel.Beginner     => "Beginner",
        CourseLevel.Intermediate => "Intermediate",
        CourseLevel.Advanced     => "Advanced",
        CourseLevel.Books        => "Books",
        CourseLevel.TestSeries   => "Test Series",
        _                        => level.ToString()
    };

    public static string Slug(this CourseLevel level) => level switch
    {
        CourseLevel.Beginner     => "beginner",
        CourseLevel.Intermediate => "intermediate",
        CourseLevel.Advanced     => "advanced",
        CourseLevel.Books        => "books",
        CourseLevel.TestSeries   => "test-series",
        _                        => level.ToString().ToLowerInvariant()
    };

    public static string CssClass(this CourseLevel level) => level switch
    {
        CourseLevel.Beginner     => "beginner",
        CourseLevel.Intermediate => "intermediate",
        CourseLevel.Advanced     => "advanced",
        CourseLevel.Books        => "books",
        CourseLevel.TestSeries   => "test-series",
        _                        => "default"
    };

    public static CourseLevel? FromSlug(string? slug) => slug?.ToLowerInvariant() switch
    {
        "beginner"     => CourseLevel.Beginner,
        "intermediate" => CourseLevel.Intermediate,
        "advanced"     => CourseLevel.Advanced,
        "books"        => CourseLevel.Books,
        "test-series"  => CourseLevel.TestSeries,
        _              => null
    };

    public static string FormatString(string? levelString) => levelString switch
    {
        "Beginner"     => "Beginner",
        "Intermediate" => "Intermediate",
        "Advanced"     => "Advanced",
        "Books"        => "Books",
        "TestSeries"   => "Test Series",
        _ => levelString ?? ""
    };
}
