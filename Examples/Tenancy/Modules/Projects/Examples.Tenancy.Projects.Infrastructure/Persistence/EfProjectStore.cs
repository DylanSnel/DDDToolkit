using DDDToolkit.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
    /// A plain tracked load by id, under the tenant filter, of the whole aggregate in one statement: the project,
    /// its crew and each member's roles, as they were at one moment. The access rules are not part of it: they
    /// were asked by the check, whose version this compares with. Composed into this query, Tenancy's untracked
    /// rows would make the project untracked too, and a change to it would save nothing.
    /// </remarks>
    public async Task<Project?> LoadAsync(ProjectId id, long version, CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsTracking().AsSingleQuery().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (project is null)
        {
            return null;
        }

        // Changed between the check and now: what the check decided was decided about another version, and so was
        // what the caller decided, when its command named the version it had read. The same answer as a save that
        // came second, which is what this would have become; and from here the save compares against that version.
        db.ExpectVersion(project, version);
        return project;
    }

    /// <inheritdoc />
    public Task<bool> NumberTakenAsync(string number, CancellationToken cancellationToken)
        => db.Projects.AnyAsync(existing => existing.Number == number, cancellationToken);

    /// <inheritdoc />
    public void Add(Project project) => db.Projects.Add(project);

    /// <inheritdoc />
    /// <remarks>A tracked load by id, under the tenant filter: a role of another tenant is not there.</remarks>
    public Task<ProjectRole?> LoadRoleAsync(ProjectRoleId id, CancellationToken cancellationToken)
        => db.ProjectRoles.AsTracking().FirstOrDefaultAsync(role => role.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectRole>> RolesOfTheTenantAsync(CancellationToken cancellationToken)
        => await db.ProjectRoles.AsTracking().ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(ProjectRole role) => db.ProjectRoles.Add(role);

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Asked of the change tracker before anything is written: every row it would write is the project's own, or
    /// a crew member's or a role's held on that crew. The outbox rows of the project's events are added by the
    /// save itself, after this.
    /// </remarks>
    public Task SaveOnlyAsync(Project project, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);

        var others = db.ChangeTracker.Entries()
            .Where(entry => entry.State != EntityState.Unchanged && !Equals(ProjectOf(entry), project.Id))
            .Select(entry => entry.Metadata.ClrType.Name)
            .Distinct()
            .ToList();

        return others.Count == 0
            ? db.SaveChangesAsync(cancellationToken)
            : throw new InvalidOperationException(
                $"This save may write project {project.Id} and nothing else, and the unit of work also holds a change to: {string.Join(", ", others)}. " +
                "An earlier command of the same scope left it there. Send each command in a scope of its own, or save what the earlier one changed first.");
    }

    /// <summary>
    /// The project a tracked row belongs to: a project's own id, and for a crew member or a crew role the first
    /// part of its owner's key, which is the project's id at either depth. <see langword="null"/> for anything else.
    /// </summary>
    private static object? ProjectOf(EntityEntry entry)
        => entry.Entity is Project project ? project.Id
            : entry.Metadata.FindOwnership() is { } ownership ? entry.Property(ownership.Properties[0].Name).CurrentValue
            : null;
}
