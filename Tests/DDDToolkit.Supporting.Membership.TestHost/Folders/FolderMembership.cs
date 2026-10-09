using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Membership.TestHost.Folders;

/// <summary>The keys a folder is asked about.</summary>
public static class FolderKeys
{
    /// <summary>See a folder.</summary>
    public const string Read = "folders.read";

    /// <summary>Put something in a folder.</summary>
    public const string File = "folders.file";

    /// <summary>Decide who is on a folder's staff: the key this host's commands that change the staff require.</summary>
    public const string Staff = "folders.staff";

    /// <summary>Destroy what is in a folder: a key the roles list and no member's role gives, so the keeper's alone.</summary>
    public const string Shred = "folders.shred";

    /// <summary>Hand a folder to another keeper: a key no role lists, so the keeper's alone.</summary>
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
/// The rules of access through a folder's staff, with every entry said: members known by a claim, a declared
/// owner's role, two keys only the keeper holds, one that no role lists and one that no role gives, names
/// of its own in the database, the two keys the database holds a caller to, and the scope whose work is the
/// host's own on a folder.
/// </summary>
public static class FolderMembership
{
    /// <summary>The scope the host's own work on its folders runs in: the module's name.</summary>
    public const string Scope = "filing";

    /// <summary>The rules.</summary>
    public static MembershipRules Rules { get; } = new(
        "folders",
        keys: [FolderKeys.Read, FolderKeys.File, FolderKeys.Staff, FolderKeys.Shred, FolderKeys.HandOver],
        roles:
        [
            new("keeper", [FolderKeys.Read, FolderKeys.File, FolderKeys.Staff, FolderKeys.Shred]),
            new("clerk", [FolderKeys.Read, FolderKeys.File, FolderKeys.Shred]),
            new("visitor", [FolderKeys.Read]),
        ],
        members: MemberSource.Claim("app_metadata.staff"),
        ownerRole: "keeper",
        memberKeys: MemberKeys.AllBut(FolderKeys.Shred),
        codes: FolderRefusals.Membership,
        functions: new("folder_ids_staffed", "folder_ids_staffed_with", "folder_ids_seen", "folder_ids_held"),
        grantTo: [RowAccessRoles.User, RowAccessRoles.Token("archivist")],
        changeMembersKey: FolderKeys.Staff,
        changeOwnerKey: FolderKeys.HandOver,
        systemScopes: [Scope]);

    /// <summary>The keeper's role: the owner's.</summary>
    public static NamedRole Keeper { get; } = new("keeper");

    /// <summary>The role that files.</summary>
    public static NamedRole Clerk { get; } = new("clerk");

    /// <summary>The role that only reads.</summary>
    public static NamedRole Visitor { get; } = new("visitor");
}
