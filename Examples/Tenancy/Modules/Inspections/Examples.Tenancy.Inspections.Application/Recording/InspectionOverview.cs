namespace Examples.Tenancy.Inspections.Application.Recording;

/// <summary>An inspection as a client sees it: what every query of this feature answers with, and what the read port reads.</summary>
/// <remarks>
/// One record from the statement to the client. The read port answers it, the queries pass it on, the route
/// writes it out and the GraphQL type is declared over it, so nothing copies an inspection from one shape into
/// another on its way out.
/// </remarks>
/// <param name="Id">The inspection.</param>
/// <param name="Project">The project it is of, by id: the rest is Projects' to give.</param>
/// <param name="Title">What was found.</param>
/// <param name="Days">The days it covers.</param>
/// <param name="RecordedBy">The seat that recorded it. The module keeps no names; a client that shows one looks the seat up.</param>
/// <param name="RecordedAt">When it was recorded.</param>
/// <param name="ChangedBy">Who the save that wrote the row ran as: the recording seat, or the application's own work for it.</param>
public sealed record InspectionOverview(InspectionId Id, ProjectId Project, string Title, DateRange Days, SeatId RecordedBy, DateTimeOffset RecordedAt, ChangedBy ChangedBy);
