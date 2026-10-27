namespace Examples.Tenancy.Projects.Application.StoredProjects;

/// <summary>
/// Where Projects' commands load and save a project they change themselves: its name, its plan, its unit, whether it is
/// closed. One unit of work per request. The application declares it; the infrastructure project implements it over the
/// module's context and registers it, scoped.
/// </summary>
/// <remarks>
/// The write side only. A command loads the project it changes, calls the domain, and saves; nothing here lists
/// projects or answers who may see one, which is <see cref="IProjectReads"/>'s. A project's crew, its opening and the
/// tenant's project roles are the Membership package's managers' to load and save, <c>CrewMemberManager</c> and
/// <c>ProjectRoleManager</c>, over the same context. The two never share storage: a
/// request's commands run one at a time on this unit of work, while its queries may run side by side, each on a
/// context of its own.
/// <para>
/// The projects keep to the current caller's tenant, as the storage's tenant filter does: a project of another
/// tenant is simply not there, to <see cref="LoadAsync"/> and to <see cref="NumberTakenAsync"/>.
/// </para>
/// <para>
/// Public, because the infrastructure project implements it and a handler's constructor names it. No route
/// names it: a route only sends a request.
/// </para>
/// </remarks>
public interface IProjectStore
{
    /// <summary>
    /// The project, with its crew, for a command to change and then save with <see cref="SaveAsync"/>; or
    /// <see langword="null"/> when the caller does not see it, or it is gone from the caller's tenant.
    /// </summary>
    /// <remarks>
    /// The command's requirement was checked before its handler, on the project its request names, which is the
    /// one loaded here. The check and the load are two statements, and between them the version the caller named
    /// is what ties them: a project that is no longer at that version was changed since the caller read it, by a
    /// crew change as much as by a rename, and the command has lost the race like one whose save came second. A
    /// command that names no version changes the project as it is when it is loaded. From the load on, the save
    /// compares the version loaded; and the database, which checks every row, checks the write once more as the
    /// caller.
    /// </remarks>
    /// <param name="id">The project, from the command.</param>
    /// <param name="expectedVersion">
    /// The version the caller last read, from <c>If-Match</c> or the mutation's input, or <see langword="null"/>
    /// when it named none.
    /// </param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="Exceptions.ConcurrencyConflictException">The project is not at <paramref name="expectedVersion"/>.</exception>
    Task<Project?> LoadAsync(ProjectId id, long? expectedVersion, CancellationToken cancellationToken);

    /// <summary>Whether a project of the caller's tenant already has <paramref name="number"/>.</summary>
    /// <param name="number">The number, trimmed.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<bool> NumberTakenAsync(string number, CancellationToken cancellationToken);

    /// <summary>
    /// Saves every project loaded in this unit of work, with the domain events they raised, in one transaction.
    /// </summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <exception cref="Exceptions.ConcurrencyConflictException">Somebody else saved the same project first.</exception>
    Task SaveAsync(CancellationToken cancellationToken);
}
