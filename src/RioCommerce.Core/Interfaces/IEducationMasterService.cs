using RioCommerce.Core.DTOs.Admin;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Education master data (boards) and the authoritative validation of a student's
/// State → District → School → Board → Class selection.
///
/// The validation lives here rather than in the page because the UI dropdowns are only a
/// convenience: a crafted POST straight at /api/student/register must be rejected by the
/// same rules. Both the wizard and the endpoint call this.
/// </summary>
public interface IEducationMasterService
{
    /// <summary>Active boards, alphabetical. Replaces the hard-coded wizard list.</summary>
    Task<List<BoardItem>> ListActiveBoardsAsync();

    /// <summary>
    /// Verifies the whole chain and returns the database's own names for what was selected:
    /// the district belongs to the state, the school is active and belongs to that district,
    /// the board exists and is active, and the class sits inside the school's own range.
    /// </summary>
    Task<EducationValidation> ValidateSelectionAsync(
        Guid? stateId, Guid? districtId, Guid? schoolId, Guid? boardId, string? studentClass,
        Guid? talukaId = null, string? otherSchoolName = null);
}
