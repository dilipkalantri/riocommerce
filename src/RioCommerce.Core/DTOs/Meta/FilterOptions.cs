using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Meta;

public record IdName(Guid Id, string Name);
public record SubjectOption(Guid Id, string Name, CourseLevel? Level);
public record SpecFilterGroup(Guid AttributeId, string Name, List<IdName> Options);
public record FilterOptions(List<IdName> Faculty, List<SubjectOption> Subjects, List<SpecFilterGroup> SpecFilters);
