namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// An inspection, with the days it covers as two dates. The API gives the recording seat's id only; the screen asks
/// the directory for its name.
/// </summary>
public sealed record InspectionInfo(Guid Id, string Title, DateOnly From, DateOnly Until, Guid RecordedBy, DateTimeOffset RecordedAt);
