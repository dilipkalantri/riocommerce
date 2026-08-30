namespace RioCommerce.Core.Enums;

/// <summary>
/// APPEND-ONLY. New levels go on the END, never in the middle — the public course search
/// serialises the ORDINAL into its query string (<c>?level=2</c>, see Search.razor), so inserting
/// a value in the middle would renumber existing members and silently change what every existing
/// bookmarked or indexed search URL means.
/// </summary>
public enum CourseLevel { Beginner, Intermediate, Books, TestSeries, Advanced }
