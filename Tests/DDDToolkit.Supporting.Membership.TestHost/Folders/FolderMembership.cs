using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.TestHost.Folders;

/// <summary>
/// The permissions a folder is asked about, every setting said somewhere: the keeper, the folder's owner, holds all
/// of them by keeping it; two no role can give; one the database holds the staff to and one the keeper column.
/// </summary>
[Permissions]
public static class FolderPermissions
{
    /// <summary>See a folder.</summary>
    [On<Folder>]
    public const string Read = "folders.read";

    /// <summary>Put something in a folder.</summary>
    [On<Folder>]
    public const string File = "folders.file";

    /// <summary>Decide who is on a folder's staff: the key this host's commands that change the staff require.</summary>
    [On<Folder>(LocksMembers = true)]
    public const string Staff = "folders.staff";

    /// <summary>Destroy what is in a folder: no role gives it, so the keeper's alone.</summary>
    [On<Folder>(InRoles = false)]
    public const string Shred = "folders.shred";

    /// <summary>Hand a folder to another keeper: no role gives it, so the keeper's alone.</summary>
    [On<Folder>(InRoles = false, LocksOwner = true)]
    public const string HandOver = "folders.hand-over";
}

/// <summary>
/// What a folder refuses with: its own codes, and the codes of its members, two of them in the host's own words,
/// and with the member carried under the host's own word for it.
/// </summary>
public static class FolderRefusals
{
    /// <summary>The folder is locked, and a locked folder changes no more.</summary>
    public const string Locked = "folders.locked";

    /// <summary>
    /// Somebody would be put on a folder for longer than whoever asks holds the key that lets them: the host's
    /// own rule about who gives what, which the package knows nothing of.
    /// </summary>
    public const string LongerThanHeld = "folders.longer-than-held";

    /// <summary>The codes of the rules about a folder's staff.</summary>
    public static MembershipCodes Membership { get; } = MembershipCodes.Under("folders")
        .With(MembershipRefusals.AlreadyMember, "already-on-folder")
        .With(MembershipRefusals.MemberNotFound, "not-on-folder")
        .WithMemberArgument("Staff");
}

/// <summary>
/// The rules of access through a folder's staff: members known by a claim, and roles in code. The host's own work
/// on a folder runs in the scope of the host's module, <see cref="Scope"/>, which the rules let through without
/// saying it.
/// </summary>
public static class FolderMembership
{
    /// <summary>The scope the host's own work on its folders runs in: the module's name, <c>Filing</c>, as a scope is written.</summary>
    public const string Scope = "filing";

    /// <summary>The rules.</summary>
    public static MembershipRules Rules { get; } = MembershipRules.For<Folder>(
        Roles.Fixed(
        [
            new("keeper", [FolderPermissions.Read, FolderPermissions.File, FolderPermissions.Staff]),
            new("clerk", [FolderPermissions.Read, FolderPermissions.File]),
            new("visitor", [FolderPermissions.Read]),
        ]),
        members: MemberSource.Claim("app_metadata.staff"));

    /// <summary>The keeper's role: what a member of staff holds who keeps the folder beside its keeper.</summary>
    public static NamedRole Keeper { get; } = new("keeper");

    /// <summary>The role that files.</summary>
    public static NamedRole Clerk { get; } = new("clerk");

    /// <summary>The role that only reads.</summary>
    public static NamedRole Visitor { get; } = new("visitor");
}
