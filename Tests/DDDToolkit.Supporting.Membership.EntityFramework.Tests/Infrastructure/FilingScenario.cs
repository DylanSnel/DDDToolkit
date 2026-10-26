namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests.Infrastructure;

/// <summary>
/// The data the questions are asked over: three documents and two folders, with a member for every way of
/// being one. Every period is days away from the moment it is made for, so a clock that is a little off, a
/// database's own among them, answers the same.
/// </summary>
/// <remarks>
/// <para>
/// The minutes, owned by Ada: Ben contributes now; Cy looks on, until next week; Dee is a member with no role;
/// Eve contributed until yesterday, her membership ended; Fay is a member whose contributor's role ended
/// yesterday and whose onlooker's role starts tomorrow; Gil contributes from tomorrow; Ian manages it without
/// being the owner. The budget, owned by Ben: Ada looks on. The outline, owned by Cy alone. Hal is a member of
/// nothing. An owner holds every permission of a document by owning it, and holds no role; the manager's role gives every
/// permission as well, to whoever holds it.
/// </para>
/// <para>
/// The cabinet, kept by K-001: C-014 is a clerk, V-020 a visitor, and T-030 is on it with no role. The
/// annex, kept by C-014 alone. X-999 is on no folder. A keeper holds every permission of a folder by keeping it, two
/// of them no role gives: the keeper's role is one the host declared, and lists less.
/// </para>
/// </remarks>
public sealed class FilingScenario
{
    /// <summary>The scenario, as it is at <paramref name="now"/>.</summary>
    public FilingScenario(DateTimeOffset now)
    {
        Now = now;
        var monthAgo = now.AddDays(-30);
        var before = now.AddDays(-10);
        var yesterday = now.AddDays(-1);
        var tomorrow = now.AddDays(1);

        var minutes = new Document(Minutes, "Minutes", Ada, monthAgo);
        minutes.ShareWith(Ben, DocumentMembership.Contributor, MemberPeriod.Open(before), now, by: Ada);
        minutes.ShareWith(Cy, DocumentMembership.Onlooker, MemberPeriod.Between(before, now.AddDays(7)), now, by: Ada);
        minutes.ShareWith(Dee, MemberPeriod.Open(before), now, by: Ada);
        minutes.ShareWith(Eve, DocumentMembership.Contributor, MemberPeriod.Between(before, yesterday), now, by: Ada);
        minutes.ShareWith(Fay, MemberPeriod.Open(before), now, by: Ada);
        minutes.GiveRole(Fay, DocumentMembership.Contributor, MemberPeriod.Between(before, yesterday), now, by: Ada);
        minutes.GiveRole(Fay, DocumentMembership.Onlooker, MemberPeriod.Open(tomorrow), now, by: Ada);
        minutes.ShareWith(Gil, DocumentMembership.Contributor, MemberPeriod.Open(tomorrow), now, by: Ada);
        minutes.ShareWith(Ian, DocumentMembership.Manager, MemberPeriod.Open(before), now, by: Ada);

        var budget = new Document(Budget, "Budget", Ben, monthAgo);
        budget.ShareWith(Ada, DocumentMembership.Onlooker, MemberPeriod.Open(before), now, by: Ben);

        var outline = new Document(Outline, "Outline", Cy, monthAgo);

        Documents = [minutes, budget, outline];

        var cabinet = new Folder(Cabinet, Keeper, monthAgo);
        cabinet.Admit(Clerk, FolderMembership.Clerk, MemberPeriod.Open(before), now, by: Keeper);
        cabinet.Admit(Visitor, FolderMembership.Visitor, MemberPeriod.Open(before), now, by: Keeper);
        cabinet.Admit(Temp, FolderMembership.Visitor, MemberPeriod.Open(before), now, by: Keeper);
        cabinet.TakeRole(Temp, FolderMembership.Visitor);

        var annex = new Folder(Annex, Clerk, monthAgo);

        Folders = [cabinet, annex];
    }

    /// <summary>The moment the scenario is made for.</summary>
    public DateTimeOffset Now { get; }

    /// <summary>The documents, as their aggregates: what the questions are compared with.</summary>
    public IReadOnlyList<Document> Documents { get; }

    /// <summary>The folders, as their aggregates.</summary>
    public IReadOnlyList<Folder> Folders { get; }

    public DocumentId Minutes { get; } = DocumentId.CreateSequential();

    public DocumentId Budget { get; } = DocumentId.CreateSequential();

    public DocumentId Outline { get; } = DocumentId.CreateSequential();

    public UserId Ada { get; } = UserId.CreateSequential();

    public UserId Ben { get; } = UserId.CreateSequential();

    public UserId Cy { get; } = UserId.CreateSequential();

    public UserId Dee { get; } = UserId.CreateSequential();

    public UserId Eve { get; } = UserId.CreateSequential();

    public UserId Fay { get; } = UserId.CreateSequential();

    public UserId Gil { get; } = UserId.CreateSequential();

    public UserId Ian { get; } = UserId.CreateSequential();

    public UserId Hal { get; } = UserId.CreateSequential();

    /// <summary>Everybody a question about documents is asked as.</summary>
    public IReadOnlyList<UserId> Users => [Ada, Ben, Cy, Dee, Eve, Fay, Gil, Ian, Hal];

    public FolderId Cabinet { get; } = new(7);

    public FolderId Annex { get; } = new(8);

    public StaffCode Keeper { get; } = new("K-001");

    public StaffCode Clerk { get; } = new("C-014");

    public StaffCode Visitor { get; } = new("V-020");

    public StaffCode Temp { get; } = new("T-030");

    public StaffCode Nobody { get; } = new("X-999");

    /// <summary>Everybody a question about folders is asked as.</summary>
    public IReadOnlyList<StaffCode> Staff => [Keeper, Clerk, Visitor, Temp, Nobody];

    /// <summary>Every permission a question about documents is asked with, and one nobody declared.</summary>
    public static IReadOnlyList<string> DocumentPermissionsAsked => [DocumentPermissions.View, DocumentPermissions.Edit, DocumentPermissions.Share, "documents.unheard-of"];

    /// <summary>Every permission a question about folders is asked with, and one nobody declared.</summary>
    public static IReadOnlyList<string> FolderPermissionsAsked => [FolderPermissions.Read, FolderPermissions.File, FolderPermissions.Staff, FolderPermissions.Shred, FolderPermissions.HandOver, "folders.unheard-of"];

    /// <summary>Saves the scenario as the application's own work.</summary>
    public async Task SaveAsync(FilingServices services)
        => await services.AsAsync(Caller.System, async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            context.Documents.AddRange(Documents);
            context.Folders.AddRange(Folders);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

    /// <summary>
    /// The documents a user holds <paramref name="permission"/> on, worked out from the aggregates in memory with the
    /// rules, the way a reader of the rules would: what every storage has to agree with.
    /// </summary>
    public IReadOnlyList<DocumentId> DocumentsHeldBy(UserId user, string permission)
    {
        var rules = DocumentMembership.Rules;
        return [.. Documents
            .Where(document => (rules.OwnerHolds(permission) && document.OwnerId == user)
                || document.Shares.Any(share => share.MemberId == user && share.AppliesAt(Now)
                    && (rules.MembershipGives(permission) || rules.RolesWith(permission).Any(role => share.HoldsAt(role, Now)))))
            .Select(document => document.Id)
            .Order()];
    }

    /// <summary>The documents a user sees: those it owns, and those it is a member of now.</summary>
    public IReadOnlyList<DocumentId> DocumentsSeenBy(UserId user)
        => [.. Documents.Where(document => document.OwnerId == user || document.Shares.Any(share => share.MemberId == user && share.AppliesAt(Now))).Select(document => document.Id).Order()];

    /// <summary>The documents where a user is a member now and holds a role now that gives <paramref name="permission"/>.</summary>
    public IReadOnlyList<DocumentId> DocumentsWithARoleFor(UserId user, string permission)
        => [.. Documents
            .Where(document => document.Shares.Any(share => share.MemberId == user && DocumentMembership.Rules.RolesWith(permission).Any(role => share.HoldsAt(role, Now))))
            .Select(document => document.Id)
            .Order()];

    /// <summary>The folders a member of staff holds <paramref name="permission"/> on, worked out in memory with the rules.</summary>
    public IReadOnlyList<FolderId> FoldersHeldBy(StaffCode staff, string permission)
    {
        var rules = FolderMembership.Rules;
        return [.. Folders
            .Where(folder => (rules.OwnerHolds(permission) && folder.Keeper == staff)
                || folder.Staff.Any(member => member.MemberId == staff && member.AppliesAt(Now)
                    && (rules.MembershipGives(permission) || rules.RolesWith(permission).Any(role => member.HoldsAt(role, Now)))))
            .Select(folder => folder.Id)
            .Order()];
    }

    /// <summary>The folders a member of staff sees: those it keeps, and those it is on now.</summary>
    public IReadOnlyList<FolderId> FoldersSeenBy(StaffCode staff)
        => [.. Folders.Where(folder => folder.Keeper == staff || folder.Staff.Any(member => member.MemberId == staff && member.AppliesAt(Now))).Select(folder => folder.Id).Order()];

    /// <summary>The folders where a member of staff is on it now and holds a role now that gives <paramref name="permission"/>.</summary>
    public IReadOnlyList<FolderId> FoldersWithARoleFor(StaffCode staff, string permission)
        => [.. Folders
            .Where(folder => folder.Staff.Any(member => member.MemberId == staff && FolderMembership.Rules.RolesWith(permission).Any(role => member.HoldsAt(role, Now))))
            .Select(folder => folder.Id)
            .Order()];
}
