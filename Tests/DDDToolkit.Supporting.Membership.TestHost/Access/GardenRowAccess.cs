using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Membership.TestHost.Gardens;

namespace DDDToolkit.Supporting.Membership.TestHost.Access;

// What the database itself lets a caller do with the gardens' plots, on a database that checks every row: thin
// rules of the application's own, as for a document, which ask the set functions the package writes for the
// plots. A gardener reads the plots it is on, and whoever fences a plot changes it, its gardeners and their
// roles among it; the package's lock holds those rows to the same key. Those functions read the roles of plots
// straight from their table.
//
// The tables of the roles are the host's to guard: a role's row says what everybody who holds the role may
// do, so a caller that could write it would give itself, and everybody else, any key a role can give. Each
// role class has a rule that lets a signed-in user read roles and nobody write them, which turns row security
// on for its table. The roles are changed by the host's own work. Whose a role of plots is, is the host's as
// well, and the database knows it from the caller's token: a gardener reads the roles of its own garden, and
// so gives no other garden's, which the package's lock asks of the role table as the caller.

/// <summary>What the gardens know of a caller in the database, which a function of the host's own answers.</summary>
[AccessFunctions(Owner = "gardens")]
public static partial class GardenQuestions
{
    /// <summary>The garden the caller's requests are in, as its token says it, or null for a caller in none.</summary>
    [AccessScalar("caller_garden")]
    public static partial GardenId CallerGarden();
}

/// <summary>The questions about plots the database answers, under the names a plot's rules give its functions.</summary>
[AccessFunctions(Owner = "plots")]
public static partial class PlotQuestions
{
    /// <summary>The plots the caller sees: those it is a gardener on now, and those it owns.</summary>
    [AccessSet("plots_i_see")]
    public static partial AccessSet<PlotId> Seen();

    /// <summary>The plots on which <paramref name="key"/> is held by the caller: as its owner, or through a role of plots that gives it.</summary>
    /// <param name="key">A permission key.</param>
    [AccessSet("plots_where_i_hold")]
    public static partial AccessSet<PlotId> HeldOn(string key);
}

/// <summary>A signed-in user reads the roles of plots of its own garden, and changes none: no rule lets a caller write one.</summary>
[RowAccess<PlotRole>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class GardenersReadTheRolesOfTheirGarden
{
    /// <summary>Whether the caller reads <paramref name="role"/>: a role of the garden its requests are in.</summary>
    public static bool Allows(PlotRole role, Caller caller) => role.GardenId == GardenQuestions.CallerGarden();
}

/// <summary>A signed-in user reads the roles of sheds, and changes none.</summary>
[RowAccess<ShedRole>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class UsersReadTheRolesOfSheds
{
    /// <summary>Whether the caller reads <paramref name="role"/>.</summary>
    public static bool Allows(ShedRole role, Caller caller) => caller.IsSignedIn;
}

/// <summary>A gardener reads the plots it is on.</summary>
[RowAccess<Plot>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class GardenersReadThePlotsTheyAreOn
{
    /// <summary>Whether the caller sees <paramref name="plot"/>.</summary>
    public static bool Allows(Plot plot, Caller caller) => PlotQuestions.Seen().Contains(plot.Id);
}

/// <summary>Whoever fences a plot changes it, and writes its gardeners and their roles: the key the plots' rules name for the lock.</summary>
[RowAccess<Plot>(RowOperations.Change, To = [RowAccessRoles.User])]
public static partial class FencersChangeTheirPlots
{
    /// <summary>Whether the caller changes <paramref name="plot"/>.</summary>
    public static bool Allows(Plot plot, Caller caller) => PlotQuestions.HeldOn(PlotKeys.Fence).Contains(plot.Id);
}
