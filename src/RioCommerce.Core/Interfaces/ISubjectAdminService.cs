using RioCommerce.Core.DTOs.Admin;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// CRUD for the Subjects lookup behind the product editor's Subject dropdown.
/// </summary>
public interface ISubjectAdminService
{
    /// <summary>Every subject, active or not, with its course usage count.</summary>
    Task<List<SubjectAdminItem>> ListAsync();

    Task<SubjectEditModel?> GetAsync(Guid id);

    Task<(bool ok, string? error, Guid id)> SaveAsync(SubjectEditModel model);

    /// <summary>Flips IsActive. This is the normal way to retire a subject: it leaves it off the
    /// dropdown while the courses already pointing at it keep their link.</summary>
    Task<(bool ok, string? error)> ToggleAsync(Guid id);

    /// <summary>Hard delete. Refused while any course still references the subject.</summary>
    Task<(bool ok, string? error)> DeleteAsync(Guid id);
}
