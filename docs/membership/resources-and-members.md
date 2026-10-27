---
title: "Membership: resources and members"
sidebar_label: Resources and members
---

# Membership: resources and members

A resource, its member class, owner and member list, its names, and several kinds of resource. Read it when you add
a resource with members; the [tutorial](tutorial.md) builds one beside Tenancy.

## The packages

> [!NOTE]
> Published as `Temp.DDDToolkit.Supporting.Membership`, `.EntityFramework` and `.Postgres`
> ([the package ids](../migrating-to-3.md#the-package-ids)), as prereleases: names can still change.

| Package | What it holds |
|---|---|
| `DDDToolkit.Supporting.Membership` | The templates, the member list (its generator writes it), the rules, the questions and requirements. No database. |
| `DDDToolkit.Supporting.Membership.EntityFramework` | `HasMembers()`, `IsCustomRole()`, the registrations, the questions inside your statements, on any provider. Its generator writes the [join with Tenancy](beside-an-organization.md#with-tenancy). |
| `DDDToolkit.Supporting.Membership.Postgres` | The functions your row access rules ask, the lock on the member rows, a start-up check ([On Postgres](postgres.md)). |

- **Needs nothing else**: no dispatcher, no Tenancy. Members are your users unless the rules say
  [otherwise](beside-an-organization.md#beside-an-organization).
- **One owner and nobody else** needs none of this: `[Owner]` on the aggregate, `[Own]` or `[OrOwn]` on the request
  ([The owner of an aggregate](../access-requirements.md#the-owner-of-an-aggregate)).

## A document and the people it is shared with

All there is to write for a resource whose members are your users: a document, the people it is shared with, and a
command that shares it. It compiles as it stands, `using` lines left out.

**The domain**: your aggregate, a one-line member class, and the owner marked `[Owner]`.

```csharp
[EntityId<Guid>] public readonly partial record struct UserId;
[EntityId<Guid>] public readonly partial record struct DocumentId;
[EntityId<Guid>] public readonly partial record struct DocumentShareId;

// A member of a document: its row's id, who a member is, what a role is known by, and the resource
[Member<DocumentShareId, UserId, NamedRole, Document>]
public sealed partial class DocumentShare;

[AggregateRoot<DocumentId>]
public sealed partial class Document
{
    public Document(DocumentId id, UserId owner, DateTimeOffset now) : base(id)
    {
        OwnerId = owner;
        Members.Open(now);                           // the owner is a member, for good, holding no role
    }

    [Owner]
    public UserId OwnerId { get; private set; }      // holds what the permissions give an owner, by owning

    public bool Archived { get; private set; }

    public partial IReadOnlyList<DocumentShare> Shares { get; }

    public void ShareWith(UserId user, NamedRole role, MemberPeriod period, DateTimeOffset now, UserId? by)
    {
        if (Archived)                                // your own guard, in front of the member list
        {
            throw new RefusalException("documents.archived", RefusalKind.Conflict, "The document is archived.");
        }

        Members.Add(user, role, period, now, by);    // Members: written for you, over Shares
    }
}
```

- `Members`, the **member list**, is [written for you](#the-member-list-is-written-for-you), with the name
  `documents` ([Names](#names)), the codes `Codes` (`documents.already-member` …) and the system-work scope `filing`.

**The permissions**, asked on one document (`[On<Document>]`), each with how it is held there
([The settings of a permission](permissions-and-roles.md#the-settings-of-a-permission)):

```csharp
[Permissions]
public static class DocumentPermissions
{
    [On<Document>(EveryMember = true, Description = "See a document")]
    public const string View = "documents.view";

    [On<Document>(Description = "Change a document")]
    public const string Edit = "documents.edit";

    [On<Document>(LocksMembers = true, LocksOwner = true, Description = "Say who a document is shared with, and hand it on")]
    public const string Share = "documents.share";
}
```

- The owner holds all three by owning, a role may give each, every member holds `View`.
- `LocksMembers`, `LocksOwner`: what your commands that change the members and hand the document on require; the
  database holds direct statements to it ([Who writes the member rows](postgres.md#who-writes-the-member-rows)).

**The rules**, read by the check in C# and by the database. `For<Document>` takes the name, codes and permissions
from the type; what is left to say is the roles.

```csharp
public static class DocumentMembership
{
    public static MembershipRules Rules { get; } = MembershipRules.For<Document>(
        Roles.Fixed([new("contributor", [DocumentPermissions.View, DocumentPermissions.Edit]), new("onlooker", [DocumentPermissions.View])]));
}
```

- `Roles.Fixed`: in code, the same for every document. Or [`Roles.Custom`](permissions-and-roles.md#roles-a-customer-makes),
  [`Roles.External`](beside-an-organization.md#beside-an-organization).
- Who a member is follows from the member id: a `UserId` over a `Guid` is the caller's user id.

**The wiring**:

```csharp
public sealed class FilingContext(DbContextOptions<FilingContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
    {
        configuration.AddDDDToolkitConventions();
        configuration.AddFilingConverters();         // your ids, stored by the converters generated for your module
    }

    // Both member tables, owned by the document; the owner is the property it marks [Owner]
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Document>().HasMembers(document => document.Shares);
}

public static class FilingModule
{
    public static IServiceCollection AddFiling(this IServiceCollection services, Action<DbContextOptionsBuilder> database)
    {
        services.AddDbContext<FilingContext>(database);                             // your context, on your provider
        services.AddDocumentMembership<FilingContext>(DocumentMembership.Rules);    // the access questions of documents
        services.AddDocumentMemberAccess<IFilingRequest>();                         // their check, for your requests
        services.RequireExplicitCallers();                                          // work that says nothing about who it runs as fails
        return services.AddScoped<DocumentHandlers>();
    }
}
```

- `AddDocumentMembership` and `AddDocumentMemberAccess` are generated, named after the resource.
- `HasMembers` adds two tables, `DocumentShares` and `DocumentShareRoles`: add a migration.
- `RequireExplicitCallers()`: work that says nothing about who it runs as counts as the application, which holds every
  permission. With it, such work [fails](../row-level-security.md#fail-closed-callers).

**A request and its handler.** `[OnResource]` marks the document the permission is held on, `[ExpectedVersion]` the
version the caller read; the generator writes `MemberAccess.On("documents.share", request.Document,
request.ExpectedVersion)` into the partial interface.

```csharp
[AccessRequests]
public partial interface IFilingRequest : IRequireAccess;           // what every request of your module implements

[RequiresPermission(DocumentPermissions.Share)]
public sealed record ShareDocument([OnResource] DocumentId Document, UserId With, string Role, DateTimeOffset? Until, [ExpectedVersion] long? ExpectedVersion = null) : IFilingRequest;

public sealed class DocumentHandlers(
    FilingContext db,
    MemberAdmission<DocumentId, UserId, NamedRole> admission,
    ICallerAccessor callers,
    TimeProvider clock)
{
    // Writing a document: whoever writes it owns it
    public async Task<DocumentId> WriteAsync(CancellationToken cancellationToken)
    {
        var owner = new UserId(callers.Current.UserId!.Value);
        var document = new Document(DocumentId.Create(), owner, clock.GetUtcNow());
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);
        return document.Id;
    }

    // Sharing one: reached once the check let the request through
    public async Task HandleAsync(ShareDocument command, CancellationToken cancellationToken)
    {
        var role = new NamedRole(command.Role);                                      // "contributor"
        await admission.RequireRoleAsync(role, cancellationToken);                   // one of the roles the rules declare

        var document = await db.Documents.SingleOrDefaultAsync(row => row.Id == command.Document, cancellationToken)
            ?? throw Document.Codes.Refuse(MembershipRefusals.NotFound);             // gone since the check
        db.ExpectVersion(document, command.ExpectedVersion);                         // If-Match: a stale version is a 409

        var now = clock.GetUtcNow();
        document.ShareWith(command.With, role, MemberPeriod.Between(now, command.Until), now, by: new UserId(callers.Current.UserId!.Value));
        await db.SaveChangesAsync(cancellationToken);
    }
}
```

- **Something asks the checks** before the handler: the behavior generated for an `[AccessRequests]` interface, or
  `AccessChecks<IFilingRequest>.RequireAsync(request)` from your dispatcher ([Access requirements](../access-requirements.md)).
  With the behavior, `AddDocumentMemberAccess` adds a start-up check that it is in the pipeline
  ([When nothing asks the checks](../access-requirements.md#when-nothing-asks-the-checks)).
- **`[OnResource]` on another id** than a resource with members' is [DDD00081](../diagnostics.md#ddd00081). A
  requirement computed from the request's values answers `RequiredAccess` instead
  ([Requirements that combine](checks-and-saves.md#requirements-that-combine)).
- **The admission** asks what the member list cannot know: is the role one the rules declare (`RequireRoleAsync`),
  and, where you registered an `IMemberDirectory` and an `IMemberRolePolicy`, is the new member known
  (`RequireMemberAsync`) and may it hold the role ([`MemberAdmission`](reference/use-cases.md#memberadmissiontresourceid-tmemberid-troleid)).
- **`ExpectVersion` after the load**: a change between the check and the load is a 409
  ([From the check to the save](checks-and-saves.md#from-the-check-to-the-save)).

### The member list is written for you

`Members` is a private property the generator writes on the resource; write your methods and guards over it.

<details>
<summary>Show the code: what the generator writes for the document</summary>

```csharp title="Document.Members.g.cs"
partial class Document
{
    private global::DDDToolkit.Supporting.Membership.MemberList<global::Filing.DocumentShare, global::Filing.DocumentShareId, global::Filing.UserId, global::DDDToolkit.Supporting.Membership.NamedRole> Members
        => new(_shares, OwnerId, Codes);
}
```

```csharp title="Document.Membership.g.cs, shortened"
partial class Document : global::DDDToolkit.Supporting.Membership.IMembershipResource
{
    public static global::DDDToolkit.Supporting.Membership.MembershipCodes Codes { get; } = global::DDDToolkit.Supporting.Membership.MembershipCodes.Under("documents");

    static string IMembershipResource.MembershipName => "documents";
    static MembershipCodes IMembershipResource.MembershipCodes => Codes;
    static string? IMembershipResource.MembershipScope => "filing";
}
```

</details>

- **Written from three things**, each the only one of its kind, so nothing is guessed: the get-only `partial`
  collection of the member class, the `[Owner]` property of the member id's type, and the static `MembershipCodes`
  (written too, unless declared).
- **A new member row's id** is made in code, `DocumentShareId.Create()`; a row keyed by a `long` declares its own
  `Create()`.
- **Not written**: [DDD00059](../diagnostics.md#ddd00059) says what stands in the way.
- **Another shape** (codes in a class of their own, a collection called `Members`): declare the list yourself, and the
  generator leaves the resource alone. `_shares` is the field behind `Shares`:

```csharp
private MemberList<DocumentShare, DocumentShareId, UserId, NamedRole> Members
    => new(_shares, OwnerId, DocumentRefusals.Membership);
```

### Names

- **The class's name, made plural**: `Document` is `documents`, `CraneBoard` is `crane-boards`, `Category` is
  `categories`. Only the last word, by the plain English rules.
- **Written two ways**: dashes in codes (`crane-boards.already-member`), underscores in functions (`crane_boards_i_see`).
- **Another plural**: `[Members("People")]` gives `people.already-member` and `people_i_see`, taken as written
  (`People`, `CraneBoards` or `crane-boards`). A space or a leading digit is [DDD00091](../diagnostics.md#ddd00091).
- **Renaming the class renames both**: clients read another code, and the next access file makes new functions; the
  export says once which names went and came ([DDD00090](../diagnostics.md#ddd00090)). Keep the old name with
  `[Members("Documents")]`.
- **The system-work scope** is named after the module: `filing` for `Filing`
  ([System work](checks-and-saves.md#system-work)).

## What the package keeps

The member list refuses each of these before anything changes;
[`MemberList`](reference/resource-and-members.md#memberlisttmember-tid-tmemberid-troleid) lists every method and
refusal.

- **A member is on the list once, for a period.** An ended membership is over: the member comes back with a new one,
  without the old roles. While one runs or is still to start, adding the member again is refused, whenever the new
  one would start. `Add` and `GiveRole` take `now` for that.
- **A member holds each role once, for a period of its own**, counting only while its membership does.
- **One owner**: a member with no end and no role, put on the list by `Members.Open(now)`. It is not removed and gets
  no role (`documents.owner-protected`) until another is named owner.
- **`Members.NameOwner(member, formerOwner, now)`**: the new owner is a member from now, with no end and no role
  (added, its roles gone, its end lifted, its start moved; handing on again restores none of it). The owner before
  becomes `FormerOwner.StaysAs(role)` or `FormerOwner.Leaves`, with no default. The answer,
  [`OwnerNamed`](reference/resource-and-members.md#ownernamedtmemberid-troleid), says what changed, `EndLifted` among
  it, for a new owner who is to leave when they would have.
- **Members live inside your aggregate**, under its one version, so `If-Match` covers them. The package raises no
  events: raise yours from what the list answers.
- **Refusals carry your codes**: `Document.Codes.Refuse(MembershipRefusals.NotFound)` is `documents.not-found`.
  `With` renames a code, `WithMemberArgument("Staff")` the member's argument ([Refusals](reference/refusals.md)).
- **Texts come with the registration**, English and Dutch, for your
  [failure localizer](../localization.md#texts-a-packages-registration-offers); a text of your own is asked first.
- **A net under code that changed the collection another way**: invariants of yours that call
  [`OneMembershipPerMember()`](reference/resource-and-members.md#onemembershippermember) and `OwnerStays()`.
- **A screen of members**: `MemberOverviews.From(...)`, the owner first, then by start
  ([tutorial step 6](tutorial.md#step-6-open-a-board-see-it-list-them)).

## Several kinds of resource

Each resource has its own member class, rules, `HasMembers` call, registrations, tables, codes, functions and
questions; neither touches the other. The module's one request interface takes a check per resource, and a second
resource changes no call that was there:

```csharp
[Member<FolderMemberId, StaffCode, NamedRole, Folder>]
public sealed partial class FolderMember;

services.AddDocumentMembership<FilingContext>(DocumentMembership.Rules);
services.AddDocumentMemberAccess<IFilingRequest>();
services.AddFolderMembership<FilingContext>(FolderMembership.Rules);
services.AddFolderMemberAccess<IFilingRequest>();
```

- **One member class per resource**: a second is [DDD00045](../diagnostics.md#ddd00045), one that names no aggregate
  root of yours (a child entity included) [DDD00050](../diagnostics.md#ddd00050), reported on the member class, or as
  [DDD00060](../diagnostics.md#ddd00060) in a domain project without the registrations.
- **Your own table and column names**, passed to `HasMembers`:

  ```csharp
  modelBuilder.Entity<Folder>().HasMembers(folder => folder.Staff,
      new MemberTableNames("FolderStaff", "FolderStaffRoles", MemberColumn: "StaffCode"));
  ```

- **A composite key**: both tables are kept under the resource's whole key, its [`[KeyPart]`](../composite-keys.md)
  parts under their own names. `HasKey` goes before `HasMembers`; after it, it is refused where the mapping is read.
