namespace Examples.Tenancy.Projects.Application.StoredProjects;

/// <summary>
/// Where Projects' commands load, add and save projects and project roles: one unit of work per request. The
/// application declares it; the infrastructure project implements it over the module's context and registers it,
/// scoped.
/// </summary>
/// <remarks>
/// The write side only. A command loads the project it changes, calls the domain, and saves; nothing here lists
/// projects or answers who may see one, which is <see cref="IProjectReads"/>'s. The two never share storage: a
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

    /// <summary>Adds a new project to the unit of work.</summary>
    /// <param name="project">The project, just opened.</param>
    void Add(Project project);

    /// <summary>
    /// A project role of the caller's tenant, for a command to change and then save with <see cref="SaveAsync"/>;
    /// or <see langword="null"/> when the tenant has no such role.
    /// </summary>
    /// <param name="id">The role.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<ProjectRole?> LoadRoleAsync(ProjectRoleId id, CancellationToken cancellationToken);

    /// <summary>
    /// Every project role of the caller's tenant, archived ones included: what the tenant's starter roles are made
    /// against, so none is made twice.
    /// </summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<ProjectRole>> RolesOfTheTenantAsync(CancellationToken cancellationToken);

    /// <summary>Adds a new project role to the unit of work.</summary>
    /// <param name="role">The role, just made.</param>
    void Add(ProjectRole role);

    /// <summary>
    /// Saves every project loaded or added in this unit of work, with the domain events they raised, in one
    /// transaction.
    /// </summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <exception cref="Exceptions.ConcurrencyConflictException">Somebody else saved the same project first.</exception>
    Task SaveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves <paramref name="project"/>, with the domain events it raised, and refuses to when the unit of work
    /// holds a change to anything else: for a save that is not judged as its caller's.
    /// </summary>
    /// <remarks>
    /// A save writes everything the unit of work holds, so one that runs as the application's own work would write
    /// whatever an earlier command of the same scope left changed there, past the rules its caller is held to.
    /// What such a save may write is therefore said here, by the project, and held where the changes are known.
    /// </remarks>
    /// <param name="project">The one project the save may write, with its crew.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <exception cref="InvalidOperationException">The unit of work holds a change to something that is not <paramref name="project"/>'s. Nothing was saved.</exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">Somebody else saved the same project first.</exception>
    Task SaveOnlyAsync(Project project, CancellationToken cancellationToken);
}
