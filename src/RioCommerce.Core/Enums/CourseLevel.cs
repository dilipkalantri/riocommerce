namespace RioCommerce.Core.Enums;

/// <summary>
/// APPEND-ONLY. New levels go on the END, never in the middle — the public course search
/// serialises the ORDINAL into its query string (<c>?level=2</c>, see Search.razor), so inserting
/// CaFinal between CaIntermediate and Books would renumber Books 2→3 and TestSeries 3→4 and
/// silently change what every existing bookmarked or indexed search URL means.
/// Where CaFinal should APPEAR is a matter for the dropdown markup, not this declaration order;
/// PostgreSQL's own label order is set by the migration ("AFTER 'ca_intermediate'").
/// </summary>
public enum CourseLevel { CaFoundation, CaIntermediate, Books, TestSeries, CaFinal }
