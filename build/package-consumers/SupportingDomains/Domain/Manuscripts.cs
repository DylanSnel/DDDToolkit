using Acme.Press.Tenants;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.Access;

namespace Acme.Press.Manuscripts;

/// <summary>A manuscript's id.</summary>
[EntityId<Guid>]
public readonly partial record struct ManuscriptId;

/// <summary>The id of a manuscript's member row.</summary>
[EntityId<Guid>]
public readonly partial record struct ManuscriptEditorId;

/// <summary>The id of a role of manuscripts: what a member holds a role by.</summary>
[EntityId<Guid>]
public readonly partial record struct ManuscriptRoleId;

/// <summary>Somebody who works on a manuscript: a seat of the house, holding roles the house keeps for its manuscripts.</summary>
[Member<ManuscriptEditorId, SeatId, ManuscriptRoleId, Manuscript>]
public sealed partial class ManuscriptEditor;

/// <summary>A role of manuscripts, as Membership declares one, with the house it is of beside it.</summary>
[KeptRole<ManuscriptRoleId, Manuscript>]
public sealed partial class ManuscriptRole
{
    /// <summary>Makes a role of manuscripts for a house.</summary>
    public ManuscriptRole(ManuscriptRoleId id, TenantId tenant, KeptRoleDraft draft) : base(id, draft, ManuscriptMembership.Rules) => TenantId = tenant;

    /// <summary>The house the role is of: the application's own column.</summary>
    public TenantId TenantId { get; private set; }
}

/// <summary>A manuscript, at an imprint of a house: a resource with members, whose roles the house keeps.</summary>
[AggregateRoot<ManuscriptId>]
public sealed partial class Manuscript
{
    /// <summary>Starts a manuscript at a unit, with <paramref name="owner"/> on it for good, in the house's role for owners.</summary>
    public Manuscript(ManuscriptId id, TenantId tenant, OrganizationUnitId unit, string title, SeatId owner, ManuscriptRoleId ownerRole, DateTimeOffset now) : base(id)
    {
        TenantId = tenant;
        UnitId = unit;
        Title = title;
        OwnerSeatId = owner;

        // Members is not declared here: Membership's generator writes it, from Editors, the owner and the codes.
        Members.Open(ownerRole, now);
    }

    /// <summary>The house the manuscript is of.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The imprint the manuscript is at: where it sits.</summary>
    public OrganizationUnitId UnitId { get; private set; }

    /// <summary>What the manuscript is called.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The seat that owns the manuscript.</summary>
    public SeatId OwnerSeatId { get; private set; }

    /// <summary>Those who work on the manuscript.</summary>
    public partial IReadOnlyList<ManuscriptEditor> Editors { get; }

    /// <summary>The codes the rules about a manuscript's members refuse under.</summary>
    private static MembershipCodes Codes => ManuscriptMembership.Codes;

    /// <summary>Takes a seat on to the manuscript in a role.</summary>
    public ManuscriptEditor TakeOn(SeatId seat, ManuscriptRoleId role, MemberPeriod period, DateTimeOffset now, SeatId? by) => Members.Add(seat, role, period, now, by);
}

/// <summary>The keys a manuscript is asked about.</summary>
public static class ManuscriptKeys
{
    /// <summary>Read a manuscript.</summary>
    public const string Read = "manuscripts.read";

    /// <summary>Edit a manuscript.</summary>
    public const string Edit = "manuscripts.edit";

    /// <summary>Retract a manuscript: its owner's alone, since no role of manuscripts may give it.</summary>
    public const string Retract = "manuscripts.retract";
}

/// <summary>The rules of access through a manuscript's members: they are seats, roles are kept, and the organization reaches it from above.</summary>
public static class ManuscriptMembership
{
    /// <summary>The starter role that reads and edits.</summary>
    public const string CopyEditor = "copy-editor";

    /// <summary>The codes a manuscript refuses with.</summary>
    public static MembershipCodes Codes { get; } = MembershipCodes.Under("manuscripts");

    /// <summary>
    /// The rules, marked as those of a manuscript's editors: Membership on Postgres writes a manuscript's functions
    /// from them because the infrastructure project references it.
    /// </summary>
    [MembershipRules<ManuscriptEditor>]
    public static MembershipRules Rules { get; } = new(
        "manuscripts",
        keys: [ManuscriptKeys.Read, ManuscriptKeys.Edit, ManuscriptKeys.Retract],
        roles: [new(CopyEditor, [ManuscriptKeys.Read, ManuscriptKeys.Edit])],
        members: MemberSource.Resolved(TenancyFunctions.CallerSeat),
        seeKey: ManuscriptKeys.Read,
        memberKeys: MemberKeys.Only(ManuscriptKeys.Read, ManuscriptKeys.Edit),
        codes: Codes,
        above: new(TenancyFunctions.UnitsWhereIHold),
        rolesKept: true);
}
