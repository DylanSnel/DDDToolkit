namespace Examples.Tenancy.Inspections.Application.Access;

/// <summary>
/// The project a request passed Projects' gate for, with the planned range the gate answered, kept for that
/// request's handler: the tie between what was asked about and what is read or recorded on.
/// </summary>
/// <remarks>
/// The check runs before the handler, in <see cref="InspectionsAccessCheck"/>, and asks Projects whether the
/// caller may do what the request is for. A handler then takes from the toolkit's <see cref="Checked{T}"/> which
/// project that was, <c>gated.TakeFor(command)</c>, and acts on that one. So what an inspection is recorded on,
/// and whose inspections are listed, is the project the gate answered for, whichever field of its request names
/// it.
/// <para>
/// It also closes the way round the check. A handler reached without its request having passed the check, called
/// directly rather than sent, has no project to act on, and <c>TakeFor</c> throws. What was kept is kept per
/// request, found by reference, and the handler is handed what the latest pass kept, once.
/// </para>
/// </remarks>
/// <param name="Project">The project the gate let the request through for.</param>
/// <param name="Planned">The days that project is planned for as the gate answered them then, or <see langword="null"/> for a project with no planned range.</param>
public sealed record GatedProject(ProjectId Project, DateRange? Planned);
