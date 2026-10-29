using DDDToolkit.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Examples.Tenancy.Projects.Infrastructure.Persistence;

/// <summary>
/// The application project's <see cref="IProjectStore"/>, over the request's context: the unit of work a command
/// loads into and saves.
/// </summary>
/// <remarks>
/// It takes the scope's context, and only commands use it. A request's commands run one at a time, so one context
/// serves them all, and what one loaded and changed is what <see cref="SaveAsync"/> writes, with the domain events
/// it raised, in one transaction. Nothing here reads for a query: those take a context of their own, through
/// <see cref="EfProjectReads"/>.
/// <para>
/// Internal: the module's registration, in this project, is the only code that names it. Everything else asks for
/// the port.
/// </para>
/// </remarks>
/// <param name="db">The module's context, per request.</param>
internal sealed class EfProjectStore(ProjectsContext db) : IProjectStore
{
    /// <inheritdoc />
    /// <remarks>
    /// A plain tracked load by id, under the tenant filter and the caller's row rules, of the whole aggregate in
    /// one statement: the project, its crew and each member's roles, as they were at one moment. A project the
    /// caller no longer sees is not there. The access rules are not part of it: they were asked by the check.
    /// Composed into this query, Tenancy's untracked rows would make the project untracked too, and a change to it
    /// would save nothing.
    /// </remarks>
    public async Task<Project?> LoadAsync(ProjectId id, long? expectedVersion, CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsTracking().AsSingleQuery().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (project is null)
        {
            return null;
        }

        // At another version than the caller read: what it decided was decided about another project. The same
        // answer as a save that came second, which is what this would have become; and from here the save compares
        // against the version loaded. No version named, nothing to compare: the caller changes it as it is now.
        db.ExpectVersion(project, expectedVersion);
        return project;
    }

    /// <inheritdoc />
    public Task<bool> NumberTakenAsync(string number, CancellationToken cancellationToken)
        => db.Projects.AnyAsync(existing => existing.Number == number, cancellationToken);

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
