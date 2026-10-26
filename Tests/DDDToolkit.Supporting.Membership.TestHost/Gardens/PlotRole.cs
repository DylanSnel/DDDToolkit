using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Membership.TestHost.Persistence;
using DDDToolkit.Supporting.Membership.UseCases;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.TestHost.Gardens;

/// <summary>
/// A role of plots, as the package declares one: an aggregate of the host's own, beside the plot, named for
/// the plot it is a role of. The host adds whose it is, a garden's, and raises its own events from what the
/// package's operations answer. Which garden's roles a request reads is the host's rule too, on its context.
/// </summary>
[KeptRole<PlotRoleId, Plot>]
public sealed partial class PlotRole
{
    /// <summary>Makes a role for a garden, from what a gardener entered or from a starter role.</summary>
    public PlotRole(PlotRoleId id, GardenId garden, KeptRoleDraft draft) : base(id, draft, PlotMembership.Rules)
    {
        GardenId = garden;
        RaiseDomainEvent(new PlotRoleMade(id, garden, MadeFrom));
    }

    /// <summary>The garden the role is of: the host's own column, which the package knows nothing of.</summary>
    public GardenId GardenId { get; private set; }

    /// <summary>Calls the role something else, and says so when that changed anything.</summary>
    public void CallIt(string name, string? description)
    {
        if (Rename(name, description, PlotMembership.Rules))
        {
            RaiseDomainEvent(new PlotRoleRenamed(Id, Name));
        }
    }

    /// <summary>Has the role give these keys, and says which came in and which went out.</summary>
    public void HaveItGive(params string[] keys)
    {
        var set = SetKeys(keys, PlotMembership.Rules);
        if (set.Changed)
        {
            RaiseDomainEvent(new PlotRoleKeysChanged(Id, set.Added, set.Removed));
        }
    }

    /// <summary>Puts the role away.</summary>
    public void PutAway()
    {
        Archive(PlotMembership.Rules);
        RaiseDomainEvent(new PlotRolePutAway(Id));
    }
}

/// <summary>A role of plots was made, by hand or from a starter role.</summary>
public sealed record PlotRoleMade(PlotRoleId Role, GardenId Garden, string? MadeFrom) : DomainEvent;

/// <summary>A role of plots is called something else.</summary>
public sealed record PlotRoleRenamed(PlotRoleId Role, string Name) : DomainEvent;

/// <summary>A role of plots gives other keys.</summary>
public sealed record PlotRoleKeysChanged(PlotRoleId Role, IReadOnlyList<string> Added, IReadOnlyList<string> Removed) : DomainEvent;

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
/// made once. The host says which garden it means by handing the package that garden's roles, and makes what
/// is missing with its own constructor, which says whose the role is.
/// </summary>
/// <param name="context">The host's context.</param>
public sealed class GardenRoles(GardenContext context)
{
    /// <summary>Makes the starter roles <paramref name="garden"/> does not have yet, and answers the ones it made.</summary>
    public async Task<IReadOnlyList<PlotRole>> MakeFirstAsync(GardenId garden, CancellationToken cancellationToken)
    {
        // Every role of that garden, whichever garden this request is in: the scope is said here, not read from the air.
        var existing = await context.PlotRoles.IgnoreQueryFilters().Where(role => role.GardenId == garden).ToListAsync(cancellationToken);

        List<PlotRole> made = [.. StarterRoles.Missing(PlotMembership.Rules, existing).Select(draft => new PlotRole(PlotRoleId.CreateSequential(), garden, draft))];
        context.PlotRoles.AddRange(made);
        await context.SaveChangesAsync(cancellationToken);
        return made;
    }
}
