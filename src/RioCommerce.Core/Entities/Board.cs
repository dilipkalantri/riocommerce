namespace RioCommerce.Core.Entities;

/// <summary>
/// Education board master (CBSE, ICSE, State Board, …). Backed by 0047_student_school_link_and_boards.sql.
///
/// Replaces the hard-coded list the student wizard used to carry. users."Board" keeps its
/// historical TEXT value for existing rows; users."BoardId" is the link to this table.
/// </summary>
public class Board : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
