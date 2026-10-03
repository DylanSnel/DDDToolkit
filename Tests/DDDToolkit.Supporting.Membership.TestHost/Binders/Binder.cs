using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Membership.TestHost.Binders;

/// <summary>The shelf a binder stands on: what a binder is known by before its own id.</summary>
[EntityId<Guid>]
public readonly partial record struct ShelfId;

/// <summary>A binder's id.</summary>
[EntityId<Guid>]
public readonly partial record struct BinderId;

/// <summary>The id of a binder's member row.</summary>
[EntityId<Guid>]
public readonly partial record struct BinderBorrowerId;

/// <summary>Somebody a binder is lent to: the package's member, with nothing added.</summary>
[Member<BinderBorrowerId, UserId, NamedRole, Binder>]
public sealed partial class BinderBorrower;

/// <summary>
/// A binder, known by the shelf it stands on and its own id: a resource whose key has a part in front of the
/// id, which its member tables carry as well. Nothing else about it differs from a document.
/// </summary>
[AggregateRoot<BinderId>]
public sealed partial class Binder
{
    /// <summary>Puts a binder on a shelf, with <paramref name="owner"/> a member from <paramref name="now"/> on, for good, in the owner's role.</summary>
    public Binder(ShelfId shelf, BinderId id, UserId owner, NamedRole ownerRole, DateTimeOffset now) : base(id)
    {
        ShelfId = shelf;
        OwnerId = owner;
        Members.Open(ownerRole, now);
    }

    /// <summary>The shelf the binder stands on: the first part of its key.</summary>
    [KeyPart]
    public ShelfId ShelfId { get; }

    /// <summary>The user that owns the binder.</summary>
    public UserId OwnerId { get; private set; }

    /// <summary>Those the binder is lent to.</summary>
    public partial IReadOnlyList<BinderBorrower> Borrowers { get; }

    /// <summary>
    /// The member list, written by hand: the binder keeps no codes of its own and reads them from its rules, so
    /// there is nothing on the class for the toolkit to write the list from. A list a class declares itself is
    /// left alone.
    /// </summary>
    private MemberList<BinderBorrower, BinderBorrowerId, UserId, NamedRole> Members
        => new(_borrowers, OwnerId, BinderBorrowerId.CreateSequential, BinderMembership.Rules.Codes);

    /// <summary>Lends the binder to a user in a role, both for the same period.</summary>
    public BinderBorrower LendTo(UserId borrower, NamedRole role, MemberPeriod period, DateTimeOffset now, UserId? by) => Members.Add(borrower, role, period, now, by);

    /// <summary>Takes the binder back from a user.</summary>
    public void TakeBackFrom(UserId borrower) => Members.Remove(borrower);

    /// <summary>A binder is lent to a user once.</summary>
    public sealed class OneLoanPerUser : IInvariant<Binder>
    {
        /// <inheritdoc />
        public string Code => BinderMembership.Rules.Codes[MembershipRefusals.AlreadyMember];

        /// <inheritdoc />
        public InvariantFailure? Check(Binder entity) => entity.Members.OneMembershipPerMember();
    }
}

/// <summary>The rules of access through those a binder is lent to: as little as a resource can say.</summary>
public static class BinderMembership
{
    /// <summary>Read what is in a binder.</summary>
    public const string Read = "binders.read";

    /// <summary>The rules.</summary>
    public static MembershipRules Rules { get; } = new("binders", keys: [Read], roles: [new("reader", [Read])], seeKey: Read);

    /// <summary>The role that reads.</summary>
    public static NamedRole Borrower { get; } = new("reader");

    /// <summary>The owner's role, which the rules add.</summary>
    public static NamedRole Owner { get; } = new(MembershipRules.DefaultOwnerRole);
}
