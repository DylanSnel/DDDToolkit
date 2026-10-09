namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// What a reading returns of one project for deciding what its caller may do to it: plain data, read with the
/// same facts of every other project asked about, in one statement.
/// </summary>
/// <param name="State">Open or closed.</param>
/// <param name="Held">The keys asked about that the caller holds on it, through its crew or through the organization.</param>
/// <param name="HoldsAtAUnit">Whether the caller holds the key it was asked with for units at any unit at all.</param>
public sealed record ProjectStanding(ProjectState State, IReadOnlySet<string> Held, bool HoldsAtAUnit);
