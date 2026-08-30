using RioCommerce.Core.DTOs.Faculty;
namespace RioCommerce.Core.Interfaces;

public interface IFacultyService
{
    /// <summary>Active faculty who teach at least one active course, with course count + aggregate rating.</summary>
    /// <param name="homeOnly">When true, only faculty marked <c>ShowOnHomePage</c> are returned (used by the homepage carousel).</param>
    Task<List<FacultyCard>> ListAsync(bool homeOnly = false);

    /// <summary>Public profile (bio, subjects, their courses) looked up by ShortCode; null if not found.</summary>
    Task<FacultyProfile?> GetByCodeAsync(string code);
}
