using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Membership.TestHost.Gardens;

/// <summary>A garden: the host's customer. Every plot, and every role of a plot, is one garden's.</summary>
[EntityId<Guid>]
public readonly partial record struct GardenId;

/// <summary>A plot's id.</summary>
[EntityId<Guid>]
public readonly partial record struct PlotId;

/// <summary>The id of a plot's member row.</summary>
[EntityId<Guid>]
public readonly partial record struct PlotGardenerId;

/// <summary>The id of a role of plots: what a gardener holds a role by.</summary>
[EntityId<Guid>]
public readonly partial record struct PlotRoleId;

/// <summary>
/// A gardener on a plot: a user, holding roles of plots that the garden made for itself, each by the role's
/// id.
/// </summary>
[Member<PlotGardenerId, UserId, PlotRoleId, Plot>]
public sealed partial class PlotGardener;

/// <summary>
/// A plot in a garden, worked by gardeners: the host's kind of resource whose roles are kept. Its members are
/// plain users, and what they hold on it comes from roles that are rows of <see cref="PlotRole"/>, which each
/// garden makes, renames, gives permissions and archives for itself.
/// </summary>
[AggregateRoot<PlotId>]
public sealed partial class Plot
{
    /// <summary>Lays out a plot in a garden, with <paramref name="owner"/> on it from <paramref name="now"/> on, for good, holding no role.</summary>
    public Plot(PlotId id, GardenId garden, string name, UserId owner, DateTimeOffset now) : base(id)
    {
        GardenId = garden;
        Name = name;
        OwnerId = owner;
        Members.Open(now);
    }

    /// <summary>The garden the plot is in.</summary>
    [At]
    public GardenId GardenId { get; private set; }

    /// <summary>What the plot is called.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The user that owns the plot.</summary>
    [Owner]
    public UserId OwnerId { get; private set; }

    /// <summary>The gardeners on the plot.</summary>
    public partial IReadOnlyList<PlotGardener> Gardeners { get; }

    /// <summary>
    /// The codes the rules about a plot's gardeners refuse under. With the collection above and the owner, it is what the
    /// toolkit writes the member list from: <c>Members</c>, which the methods below change the members through.
    /// </summary>
    private static MembershipCodes Codes => PlotMembership.Codes;

    /// <summary>Lets a gardener onto the plot, with no role yet.</summary>
    public PlotGardener LetOn(UserId gardener, MemberPeriod period, DateTimeOffset now, UserId? by) => Members.Add(gardener, period, now, by);

    /// <summary>Lets a gardener onto the plot in a role, both for the same period.</summary>
    public PlotGardener LetOn(UserId gardener, PlotRoleId role, MemberPeriod period, DateTimeOffset now, UserId? by) => Members.Add(gardener, role, period, now, by);

    /// <summary>Gives a gardener on the plot a role.</summary>
    public void GiveRole(UserId to, PlotRoleId role, MemberPeriod period, DateTimeOffset now, UserId? by) => Members.GiveRole(to, role, period, now, by);

    /// <summary>A gardener is on a plot once.</summary>
    public sealed class OnePlacePerGardener : IInvariant<Plot>
    {
        /// <inheritdoc />
        public string Code => PlotMembership.Codes[MembershipRefusals.AlreadyMember];

        /// <inheritdoc />
        public InvariantFailure? Check(Plot entity) => entity.Members.OneMembershipPerMember();
    }

    /// <summary>The owner stays on the plot, with a role that does not run out.</summary>
    public sealed class OwnerKeepsAPlace : IInvariant<Plot>
    {
        /// <inheritdoc />
        public string Code => PlotMembership.Codes[MembershipRefusals.OwnerProtected];

        /// <inheritdoc />
        public InvariantFailure? Check(Plot entity) => entity.Members.OwnerStays();
    }
}

/// <summary>
/// The permissions asked on a plot: its owner holds all of them; a role a garden makes gives all but the one that
/// sells it; who fences a plot says who is let on it, and who sells it hands it on.
/// </summary>
[Permissions]
public static class PlotPermissions
{
    /// <summary>See a plot.</summary>
    [On<Plot>(EveryMember = true)]
    public const string See = "plots.see";

    /// <summary>Plant on a plot.</summary>
    [On<Plot>]
    public const string Plant = "plots.plant";

    /// <summary>Water a plot.</summary>
    [On<Plot>]
    public const string Water = "plots.water";

    /// <summary>Fence a plot: who holds it says who is let on it.</summary>
    [On<Plot>(ManagesMembers = true)]
    public const string Fence = "plots.fence";

    /// <summary>Sell a plot: no role gives it, and who holds it hands the plot on.</summary>
    [On<Plot>(InRoles = false, NamesOwner = true)]
    public const string Sell = "plots.sell";
}

/// <summary>The rules of access through a plot's gardeners: roles a garden makes for itself, from two starter roles.</summary>
public static class PlotMembership
{
    /// <summary>The starter role that tends a plot.</summary>
    public const string Tender = "tender";

    /// <summary>The starter role that waters a plot.</summary>
    public const string Waterer = "waterer";

    /// <summary>A role of that name is a garden's already.</summary>
    public const string RoleNameTaken = "plots.role-name-taken";

    /// <summary>The codes a plot refuses under.</summary>
    public static MembershipCodes Codes { get; } = MembershipCodes.Under("plots");

    /// <summary>The rules.</summary>
    public static MembershipRules Rules { get; } = MembershipRules.For<Plot>(
        Roles.Custom(starters: [new(Tender, [PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Water]), new(Waterer, [PlotPermissions.See, PlotPermissions.Water])]));
}
