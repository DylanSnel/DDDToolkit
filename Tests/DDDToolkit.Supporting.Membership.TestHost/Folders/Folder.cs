using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Membership.TestHost.Folders;

/// <summary>
/// A member of staff on a folder: the package's member, with a note the host adds to it. Declared next to
/// <c>DocumentShare</c>, in the same project: one member class for each kind of resource.
/// </summary>
[Member<FolderMemberId, StaffCode, NamedRole, Folder>]
public sealed partial class FolderMember
{
    /// <summary>The longest note.</summary>
    public const int MaxNoteLength = 40;

    /// <summary>Why this member of staff is on the folder, or <see langword="null"/>.</summary>
    public string? Note { get; private set; }

    /// <summary>Sets or clears the note.</summary>
    public void Annotate(string? note) => Note = note;

    /// <summary>A note is at most <see cref="MaxNoteLength"/> characters: a rule of the host's own, next to the package's.</summary>
    public sealed class NoteLength : IInvariant<FolderMember>
    {
        /// <inheritdoc />
        public string Code => "folders.member-note";

        /// <inheritdoc />
        public InvariantFailure? Check(FolderMember entity)
            => entity.Note is not { Length: > MaxNoteLength } ? null : $"A note is at most {MaxNoteLength} characters.";
    }
}

/// <summary>
/// A folder, kept by a member of staff: the host's second kind of resource with members, with other members,
/// other roles, other codes and another guard than a document has.
/// </summary>
[AggregateRoot<FolderId>]
public sealed partial class Folder
{
    /// <summary>Opens a folder, with <paramref name="keeper"/> on it from <paramref name="now"/> on, for good, in the keeper's role.</summary>
    public Folder(FolderId id, StaffCode keeper, NamedRole keeperRole, DateTimeOffset now) : base(id)
    {
        Keeper = keeper;
        Members.Open(keeperRole, now);
    }

    /// <summary>The member of staff that keeps the folder: its owner.</summary>
    public StaffCode Keeper { get; private set; }

    /// <summary>Whether the folder is locked: a locked folder changes no more.</summary>
    public bool Locked { get; private set; }

    /// <summary>The staff on the folder.</summary>
    public partial IReadOnlyList<FolderMember> Staff { get; }

    /// <summary>
    /// The codes the rules about a folder's staff refuse under. With the collection above and the owner, it is what the
    /// toolkit writes the member list from: <c>Members</c>, which the methods below change the members through.
    /// </summary>
    private static MembershipCodes Codes => FolderRefusals.Membership;

    /// <summary>Puts a member of staff on the folder, in a role.</summary>
    public FolderMember Admit(StaffCode staff, NamedRole role, MemberPeriod period, DateTimeOffset now, StaffCode? by)
    {
        RequireUnlocked();
        return Members.Add(staff, role, period, now, by);
    }

    /// <summary>Gives a member of staff on the folder a role.</summary>
    public void GiveRole(StaffCode to, NamedRole role, MemberPeriod period, DateTimeOffset now, StaffCode? by)
    {
        RequireUnlocked();
        Members.GiveRole(to, role, period, now, by);
    }

    /// <summary>Takes a role from a member of staff on the folder.</summary>
    public void TakeRole(StaffCode from, NamedRole role, NamedRole? keeperRole)
    {
        RequireUnlocked();
        Members.TakeRole(from, role, keeperRole);
    }

    /// <summary>Takes a member of staff off the folder.</summary>
    public void Dismiss(StaffCode staff)
    {
        RequireUnlocked();
        Members.Remove(staff);
    }

    /// <summary>Hands the folder to another keeper.</summary>
    public void HandOver(StaffCode to, NamedRole keeperRole, DateTimeOffset now)
    {
        RequireUnlocked();
        Keeper = Members.NameOwner(to, keeperRole, now).Owner;
    }

    /// <summary>Locks the folder.</summary>
    public void Lock() => Locked = true;

    private void RequireUnlocked()
    {
        if (Locked)
        {
            throw new RefusalException(FolderRefusals.Locked, RefusalKind.Conflict, "The folder is locked.");
        }
    }

    /// <summary>A member of staff is on a folder once.</summary>
    public sealed class OneMemberPerStaffCode : IInvariant<Folder>
    {
        /// <inheritdoc />
        public string Code => FolderRefusals.Membership[MembershipRefusals.AlreadyMember];

        /// <inheritdoc />
        public InvariantFailure? Check(Folder entity) => entity.Members.OneMembershipPerMember();
    }

    /// <summary>The keeper stays on the folder, with a role that does not run out.</summary>
    public sealed class KeeperKeepsAPlace : IInvariant<Folder>
    {
        /// <inheritdoc />
        public string Code => FolderRefusals.Membership[MembershipRefusals.OwnerProtected];

        /// <inheritdoc />
        public InvariantFailure? Check(Folder entity) => entity.Members.OwnerStays();
    }
}
