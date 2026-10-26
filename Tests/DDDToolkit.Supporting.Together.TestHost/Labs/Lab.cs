using Campus.Tenants;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.Invariants;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Tenancy.Access;

namespace Campus.Labs;

/// <summary>A lab's id.</summary>
[EntityId<Guid>]
public readonly partial record struct LabId;

/// <summary>The id of a lab's member row.</summary>
[EntityId<Guid>]
public readonly partial record struct LabTechnicianId;

/// <summary>
/// Somebody who works in a lab: a seat of the college, holding roles of the college's own, each by the id
/// Tenancy knows the role by.
/// </summary>
[Member<LabTechnicianId, SeatId, RoleId, Lab>]
public sealed partial class LabTechnician;

/// <summary>
/// A lab, at a faculty or an institute of a college: the resource whose roles are the tenant's own. Nothing is
/// kept for a lab but who works in it: the roles a technician holds there are roles the college made in its
/// organization, and which of them give a permission is asked of Tenancy. A permission held in the organization where the lab
/// is, or above it, reaches the lab as it reaches a course.
/// </summary>
[AggregateRoot<LabId>]
public sealed partial class Lab
{
    /// <summary>Opens a lab at a unit, with <paramref name="owner"/> in it from <paramref name="now"/> on, for good, holding no role.</summary>
    public Lab(LabId id, TenantId tenant, OrganizationUnitId unit, string name, SeatId owner, DateTimeOffset now) : base(id)
    {
        TenantId = tenant;
        UnitId = unit;
        Name = name;
        OwnerSeatId = owner;
        Members.Open(now);
    }

    /// <summary>The college the lab is of.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The faculty or institute the lab is at: where it sits.</summary>
    [At]
    public OrganizationUnitId UnitId { get; private set; }

    /// <summary>What the lab is called.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The seat that owns the lab.</summary>
    [Owner]
    public SeatId OwnerSeatId { get; private set; }

    /// <summary>Those who work in the lab.</summary>
    public partial IReadOnlyList<LabTechnician> Technicians { get; }

    /// <summary>
    /// The codes the rules about a lab's technicians refuse under. With the collection above and the owner, it is what the
    /// toolkit writes the member list from: <c>Members</c>, which the methods below change the members through.
    /// </summary>
    private static MembershipCodes Codes => LabMembership.Codes;

    /// <summary>Lets a seat into the lab, with no role yet.</summary>
    public LabTechnician LetIn(SeatId seat, MemberPeriod period, DateTimeOffset now, SeatId? by) => Members.Add(seat, period, now, by);

    /// <summary>Lets a seat into the lab in a role of the college's, both for the same period.</summary>
    public LabTechnician LetIn(SeatId seat, RoleId role, MemberPeriod period, DateTimeOffset now, SeatId? by) => Members.Add(seat, role, period, now, by);

    /// <summary>A seat is in a lab once.</summary>
    public sealed class OnePlacePerSeat : IInvariant<Lab>
    {
        /// <inheritdoc />
        public string Code => LabMembership.Codes[MembershipRefusals.AlreadyMember];

        /// <inheritdoc />
        public InvariantFailure? Check(Lab entity) => entity.Members.OneMembershipPerMember();
    }

    /// <summary>The owner stays in the lab, with a role that does not run out.</summary>
    public sealed class OwnerKeepsAPlace : IInvariant<Lab>
    {
        /// <inheritdoc />
        public string Code => LabMembership.Codes[MembershipRefusals.OwnerProtected];

        /// <inheritdoc />
        public InvariantFailure? Check(Lab entity) => entity.Members.OwnerStays();
    }
}

/// <summary>The permissions asked on a lab: its owner holds all three, and a role of the organization gives them.</summary>
[Permissions]
public static class LabPermissions
{
    /// <summary>See a lab.</summary>
    [On<Lab>(EveryMember = true)]
    public const string See = "labs.see";

    /// <summary>Equip a lab.</summary>
    [On<Lab>]
    public const string Equip = "labs.equip";

    /// <summary>Calibrate a lab's instruments.</summary>
    [On<Lab>]
    public const string Calibrate = "labs.calibrate";
}

/// <summary>
/// The rules of access through a lab's technicians: the organization's own roles, kept by Tenancy, and reach from
/// above through its grants. A role of the organization holds permissions of many modules; on a lab it gives the lab's alone.
/// The college's own work in its module's scope holds everything on a course and nothing on a lab: the labs' rules
/// let no scope through, <c>systemScopes: []</c>.
/// </summary>
public static class LabMembership
{
    /// <summary>The codes a lab refuses under.</summary>
    public static MembershipCodes Codes { get; } = MembershipCodes.Under("labs");

    /// <summary>The rules.</summary>
    public static MembershipRules Rules { get; } = MembershipRules.For<Lab>(
        Roles.External(TenancyFunctions.RolesWithPermission),
        reachFromAbove: true,
        systemScopes: []);
}
