using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Membership.TestHost.Crates;
using DDDToolkit.Supporting.Membership.TestHost.Pallets;

namespace DDDToolkit.Supporting.Membership.TestHost.Access;

// What the database itself lets a caller read of the depot's resources, on a database that checks every row:
// thin rules of the application's own, as for a document, which ask the set functions the package writes for
// each resource. Those functions ask the depot's own in turn, by the names the resources' rules give them.

/// <summary>The questions about pallets the database answers, under the names a pallet's rules give its functions.</summary>
[AccessFunctions(Owner = "pallets")]
public static partial class PalletQuestions
{
    /// <summary>The pallets the caller sees.</summary>
    [AccessSet("pallets_i_see")]
    public static partial AccessSet<PalletId> Seen();
}

/// <summary>The questions about crates the database answers, under the names a crate's rules give its functions.</summary>
[AccessFunctions(Owner = "crates")]
public static partial class CrateQuestions
{
    /// <summary>The crates the caller sees: those it is on, and those that stand where it holds the key that sees.</summary>
    [AccessSet("crates_i_see")]
    public static partial AccessSet<CrateId> Seen();

    /// <summary>The crates on which <paramref name="key"/> is held by the caller: as one of its porters, as its owner, or from a bay above.</summary>
    /// <param name="key">A permission key.</param>
    [AccessSet("crates_where_i_hold")]
    public static partial AccessSet<CrateId> HeldOn(string key);
}

/// <summary>A porter reads the pallets it is on.</summary>
[RowAccess<Pallet>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class PortersReadThePalletsTheyAreOn
{
    /// <summary>Whether the caller sees <paramref name="pallet"/>.</summary>
    public static bool Allows(Pallet pallet, Caller caller) => PalletQuestions.Seen().Contains(pallet.Id);
}

/// <summary>A porter reads the crates it sees: those it is on, and those in a bay where it holds the key that sees.</summary>
[RowAccess<Crate>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class PortersReadTheCratesTheySee
{
    /// <summary>Whether the caller sees <paramref name="crate"/>.</summary>
    public static bool Allows(Crate crate, Caller caller) => CrateQuestions.Seen().Contains(crate.Id);
}
