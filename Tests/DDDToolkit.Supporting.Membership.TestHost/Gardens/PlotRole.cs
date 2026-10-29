using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Membership.TestHost.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.TestHost.Gardens;

/// <summary>
/// A role of plots, as the package declares one: an aggregate of the host's own, beside the plot, named for
/// the plot it is a role of. The host adds whose it is, a garden's, and raises its own events in the hooks the
/// generator declares, which the role calls after each change that changed anything. Which garden's roles a request reads is the host's rule too, on its context.
/// </summary>
[CustomRole<PlotRoleId, Plot>]
public sealed partial class PlotRole
{
    /// <summary>Makes a role for a garden, from what a gardener entered or from a starter role.</summary>
    public PlotRole(PlotRoleId id, GardenId garden, CustomRoleDraft draft) : base(id, draft, PlotMembership.Rules)
    {
        GardenId = garden;
        RaiseDomainEvent(new PlotRoleMade(id, garden, MadeFrom));
    }

    /// <summary>The garden the role is of: the host's own column, which the package knows nothing of.</summary>
    public GardenId GardenId { get; private set; }

    /// <summary>Calls the role something else: what the role manager's RenameAsync does, for the tests to set a role up with.</summary>
    public void CallIt(string name, string? description) => Rename(name, description, PlotMembership.Rules);

    /// <summary>Has the role give these permissions from now on.</summary>
    public void HaveItGive(params string[] permissions) => SetPermissions(permissions, PlotMembership.Rules);

    /// <summary>Puts the role away: it is archived.</summary>
    public void PutAway() => Archive(PlotMembership.Rules);

    /// <summary>The host's event for a new name: the hook the role calls after a rename that changed anything.</summary>
    partial void OnRenamed() => RaiseDomainEvent(new PlotRoleRenamed(Id, Name));

    partial void OnPermissionsSet(RolePermissionsSet set) => RaiseDomainEvent(new PlotRolePermissionsChanged(Id, set.Added, set.Removed));

    partial void OnArchived() => RaiseDomainEvent(new PlotRolePutAway(Id));
}

/// <summary>A role of plots was made, by hand or from a starter role.</summary>
public sealed record PlotRoleMade(PlotRoleId Role, GardenId Garden, string? MadeFrom) : DomainEvent;

/// <summary>A role of plots is called something else.</summary>
public sealed record PlotRoleRenamed(PlotRoleId Role, string Name) : DomainEvent;

/// <summary>A role of plots gives other permissions.</summary>
public sealed record PlotRolePermissionsChanged(PlotRoleId Role, IReadOnlyList<string> Added, IReadOnlyList<string> Removed) : DomainEvent;

/// <summary>A role of plots was put away.</summary>
public sealed record PlotRolePutAway(PlotRoleId Role) : DomainEvent;

/// <summary>
/// The garden a request is in: the host's own rule about whose roles a request reads. A host knows it from
/// its caller; here whoever runs a piece of work says it for as long as the work runs. The package is told
/// nothing of it: it reads the roles through the host's context, which applies the rule.
/// </summary>
public static class GardenOfTheRequest
{
    private static readonly AsyncLocal<GardenId?> InGarden = new();

    /// <summary>The garden the work that is running is in, or <see langword="null"/> for work that is in none.</summary>
    public static GardenId? Current => InGarden.Value;

    /// <summary>Runs what follows in <paramref name="garden"/>, until what it answers is disposed.</summary>
    public static IDisposable Begin(GardenId garden)
    {
        var before = InGarden.Value;
        InGarden.Value = garden;
        return new Ended(before);
    }

    private sealed class Ended(GardenId? before) : IDisposable
    {
        public void Dispose() => InGarden.Value = before;
    }
}

/// <summary>
/// The host's use case that gives a garden its first roles: the starter roles the plots' rules declare, each
/// made once, told by what it was made from. It reads the roles of the garden it is told, whichever garden the request
/// is in, which the role manager does not: that reads the roles the request's context lets through. So it makes what is
/// missing itself, with its own constructor, which says whose the role is.
/// </summary>
/// <param name="context">The host's context.</param>
public sealed class GardenRoles(GardenContext context)
{
    /// <summary>Makes the starter roles <paramref name="garden"/> does not have yet, and answers the ones it made.</summary>
    public async Task<IReadOnlyList<PlotRole>> MakeFirstAsync(GardenId garden, CancellationToken cancellationToken)
    {
        // Every role of that garden, whichever garden this request is in: the scope is said here, not read from the air.
        var existing = await context.PlotRoles.IgnoreQueryFilters().Where(role => role.GardenId == garden).ToListAsync(cancellationToken);

        var rules = PlotMembership.Rules;
        var there = existing.Select(role => role.MadeFrom).ToHashSet(StringComparer.Ordinal);
        List<PlotRole> made = [.. rules.Roles.Declared
            .Where(starter => !there.Contains(starter.Name))
            .Select(starter => new PlotRole(PlotRoleId.CreateSequential(), garden, new CustomRoleDraft(starter.Name, null, rules.PermissionsOf(new NamedRole(starter.Name)), starter.Name)))];
        context.PlotRoles.AddRange(made);
        await context.SaveChangesAsync(cancellationToken);
        return made;
    }
}
