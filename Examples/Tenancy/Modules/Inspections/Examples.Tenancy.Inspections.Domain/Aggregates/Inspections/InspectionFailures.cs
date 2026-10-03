namespace Examples.Tenancy.Inspections.Domain.Aggregates.Inspections;

/// <summary>
/// The marker type <c>InspectionFailures.resx</c> and <c>InspectionFailures.nl.resx</c> are named after: the
/// English and Dutch text of Inspections' own codes in <see cref="InspectionRefusals"/>. The module's own
/// registration adds it to the toolkit's localizer.
/// </summary>
/// <remarks>
/// The three codes Inspections passes on from Projects are not here. A code has one text, in the files of the
/// module that owns it, so a project that is closed reads the same whichever module refused.
/// </remarks>
public sealed class InspectionFailures;
