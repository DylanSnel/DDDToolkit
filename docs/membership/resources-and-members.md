---
title: "Membership: resources and members"
sidebar_label: Resources and members
---

# Membership: resources and members

Read this page when you add a resource with members. It covers the member class, the owner, the member list, the
names, and several kinds of resource in one module. The [tutorial](tutorial.md) builds a resource beside Tenancy.

## The packages

> [!NOTE]
> The packages are published as `Temp.DDDToolkit.Supporting.Membership`, `.Abstractions`, `.EntityFramework` and
> `.Postgres` ([the package ids](../migrating-to-3.md#the-package-ids)). They are prereleases, so their names can
> still change.

| Package | What it holds |
|---|---|
| `DDDToolkit.Supporting.Membership` | The templates, the member list and the generator that writes it, the rules, the access manager (`MembershipAccessManager<TResource>`) and the requirements. It has no database code. |
| `DDDToolkit.Supporting.Membership.Abstractions` | Membership's markers for domain classes: `[At]`, on the property that says where a resource hangs, in the namespace `DDDToolkit.Supporting.Membership`. No Entity Framework. `DDDToolkit.Supporting.Membership` references it. |
| `DDDToolkit.Supporting.Membership.EntityFramework` | The mapping by convention, with `HasMembers()` and `IsCustomRole()` for names of your own, the registrations, and `WhereIHold` for your own queries, on any provider. Its generator writes the [join with Tenancy](beside-an-organization.md#with-tenancy). |
| `DDDToolkit.Supporting.Membership.Postgres` | The functions your row access rules call, the lock on the member rows, and a start-up check ([On Postgres](postgres.md)). |

- **Membership needs no dispatcher and no Tenancy.** Members are your users unless the rules say
  [otherwise](beside-an-organization.md#three-settings-of-the-rules).
- **An aggregate with one owner and no other members needs none of this.** Mark the owner `[Owner]` on the aggregate,
  and put `[Own]` or `[OrOwn]` on the request ([The owner of an aggregate](../access-requirements.md#the-owner-of-an-aggregate)).

## A document and the people it is shared with

This example is all you write for a resource whose members are your users. A document is shared with users, and a
command shares it. The code compiles as it stands. Only the `using` lines are left out.

**The domain.** You write the aggregate, a member class of one line, and the owner property, marked `[Owner]`.

```csharp
[EntityId<Guid>] public readonly partial record struct UserId;
[EntityId<Guid>] public readonly partial record struct DocumentId;
[EntityId<Guid>] public readonly partial record struct DocumentShareId;

// The member class: the row id, the member id, the role type, and the resource
[Member<DocumentShareId, UserId, NamedRole, Document>]
public sealed partial class DocumentShare;

[AggregateRoot<DocumentId>]
public sealed partial class Document
{
    public Document(DocumentId id, UserId owner, DateTimeOffset now) : base(id)
    {
        OwnerId = owner;
        Members.Open(now);                           // puts the owner on the list, with no end and no role
    }

    [Owner]
    public UserId OwnerId { get; private set; }      // the owner holds the permissions marked for the owner

    public bool Archived { get; private set; }

    public partial IReadOnlyList<DocumentShare> Shares { get; }

    public void ShareWith(UserId user, NamedRole role, MemberPeriod period, DateTimeOffset now, UserId? by)
    {
        if (Archived)                                // your guard runs before the member list
        {
            throw new RefusalException("documents.archived", RefusalKind.Conflict, "The document is archived.");
        }

        Members.Add(user, role, period, now, by);    // Members is generated over Shares
    }
}
```

- `Members` is the **member list**. The generator writes it ([The generated member list](#the-generated-member-list)),
  with the name `documents` ([Names](#names)), the codes in `Codes` (`documents.already-member` and the others), and
  the system-work scope `filing`.

**The permissions.** Each permission is asked on one document (`[On<Document>]`). The attribute also says how the
permission is held there ([The settings of a permission](permissions-and-roles.md#the-settings-of-a-permission)).

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

- The owner holds all three permissions by owning the document. A role can give each of them. Every member holds
  `View`.
- `LocksMembers` and `LocksOwner` mark `Share` as the permission your commands require to change the members and to
  hand the document on. On Postgres, the database then refuses a direct write to the member rows or the owner column
  from a caller without `Share` ([Who writes the member rows](postgres.md#who-writes-the-member-rows)).

**The rules.** The check in C# and the functions in the database read the same rules. `For<Document>` reads the name,
the codes and the permissions from the type, so the rules only say where the roles come from.

```csharp
public static class DocumentMembership
{
    public static MembershipRules Rules { get; } = MembershipRules.For<Document>(
        Roles.Fixed([new("contributor", [DocumentPermissions.View, DocumentPermissions.Edit]), new("onlooker", [DocumentPermissions.View])]));
}
```

- `Roles.Fixed` declares the roles in code, the same for every document. The other choices are
  [`Roles.Custom`](permissions-and-roles.md#roles-a-customer-makes) and
  [`Roles.External`](beside-an-organization.md#three-settings-of-the-rules).
- Who a member is follows from the member id. A `UserId` over a `Guid` is compared with the caller's user id.

**The wiring.**

```csharp
public sealed class FilingContext(DbContextOptions<FilingContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    // The conventions map the document and both member tables. The owner is the property marked [Owner].
    protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
    {
        configuration.AddDDDToolkitConventions();
        configuration.AddFilingConverters();         // stores your ids with the converters generated for your module
    }
}

public static class FilingModule
{
    public static IServiceCollection AddFiling(this IServiceCollection services, Action<DbContextOptionsBuilder> database)
    {
        services.AddDbContext<FilingContext>(database);                             // your context, on your provider
        services.AddDocumentMembership<FilingContext>(DocumentMembership.Rules);    // the access manager, the queries and the admission for documents
        services.AddDocumentMemberAccess<IFilingRequest>();                         // the check for documents, on your requests
        services.RequireExplicitCallers();                                          // work that names no caller fails
        return services.AddScoped<DocumentHandlers>();
    }
}
```

- The generator writes `AddDocumentMembership` and `AddDocumentMemberAccess`, and names them after the resource.
- `AddDDDToolkitConventions()` maps `Shares` as two tables, `DocumentShares` and `DocumentShareRoles`, owned by the
  document. `OnModelCreating` needs no line for them. Add a migration for the tables.
- `AddDocumentMembership` registers `MembershipAccessManager<Document>` per scope, and the queries on
  `db.Documents` ([Access in your own queries](checks-and-saves.md#access-in-your-own-queries)).
- Without `RequireExplicitCallers()`, work that names no caller runs as the application, which holds every
  permission. With it, that work [fails](../row-level-security.md#requiring-explicit-callers).

**A request and its handler.** `[OnResource]` marks the document the permission is held on. `[ExpectedVersion]` marks
the version the caller read. The generator writes `MemberAccess.On("documents.share", request.Document,
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
    // The caller who writes a document owns it
    public async Task<DocumentId> WriteAsync(CancellationToken cancellationToken)
    {
        var owner = new UserId(callers.Current.UserId!.Value);
        var document = new Document(DocumentId.Create(), owner, clock.GetUtcNow());
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);
        return document.Id;
    }

    // Runs after the check let the request through
    public async Task HandleAsync(ShareDocument command, CancellationToken cancellationToken)
    {
        var role = new NamedRole(command.Role);                                      // "contributor"
        await admission.RequireRoleAsync(role, cancellationToken);                   // one of the roles the rules declare

        var document = await db.Documents.SingleOrDefaultAsync(row => row.Id == command.Document, cancellationToken)
            ?? throw Document.Codes.Refuse(MembershipRefusals.NotFound);             // deleted since the check
        db.ExpectVersion(document, command.ExpectedVersion);                         // a stale If-Match version is a 409

        var now = clock.GetUtcNow();
        document.ShareWith(command.With, role, MemberPeriod.Between(now, command.Until), now, by: new UserId(callers.Current.UserId!.Value));
        await db.SaveChangesAsync(cancellationToken);
    }
}
```

- **The checks must run before the handler.** The behavior generated for an `[AccessRequests]` interface runs them,
  or your dispatcher calls `AccessChecks<IFilingRequest>.RequireAsync(request)` ([Access requirements](../access-requirements.md)).
  With the behavior, `AddDocumentMemberAccess` also adds a start-up check. A host without the behavior in its
  pipeline does not start ([When nothing asks the checks](../access-requirements.md#handlers-reached-without-the-access-check)).
- **`[OnResource]` goes on the id of a resource with members.** On any other id it is
  [DDD00081](../diagnostics.md#ddd00081). For a requirement computed from the request's values, implement
  `RequiredAccess` instead ([Requirements that combine](checks-and-saves.md#requirements-that-combine)).
- **The admission checks what the member list cannot know.** `RequireRoleAsync` checks that the role exists for the
  resource. With fixed roles, that means a role the rules declare. Where you registered an `IMemberDirectory`,
  `RequireMemberAsync` checks that the new member is known and active. Where you registered an
  `IMemberRolePolicy`, `RequireRoleAsync` also checks that a member may hold the role
  ([`MemberAdmission`](reference/use-cases.md#memberadmissiontresourceid-tmemberid-troleid)).
- **Call `ExpectVersion` after the load.** A change between the check and the load is then a 409
  ([From the check to the save](checks-and-saves.md#from-the-check-to-the-save)).

### The generated member list

The generator writes `Members` as a private property on the resource. Write your methods and guards over it.

<details>
<summary>Show the code the generator writes for the document</summary>

```csharp title="Document.Members.g.cs"
partial class Document
{
    private global::DDDToolkit.Supporting.Membership.MemberList<global::Filing.DocumentShare, global::Filing.DocumentShareId, global::Filing.UserId, global::DDDToolkit.Supporting.Membership.NamedRole> Members
        => new(_shares, OwnerId, Codes);
}
```

**Document.Membership.g.cs, shortened**

```csharp
partial class Document : global::DDDToolkit.Supporting.Membership.IMembershipResource
{
    public static global::DDDToolkit.Supporting.Membership.MembershipCodes Codes { get; } = global::DDDToolkit.Supporting.Membership.MembershipCodes.Under("documents");

    static string IMembershipResource.MembershipName => "documents";
    static MembershipCodes IMembershipResource.MembershipCodes => Codes;
    static string? IMembershipResource.MembershipScope => "filing";
}
```

</details>

- **The generator writes the list from three things on the resource.** Each must be the only one of its kind, so the
  generator guesses nothing:
  - the get-only `partial` collection of the member class, typed `IReadOnlyList<T>`, `IReadOnlyCollection<T>` or
    `IEnumerable<T>`;
  - the `[Owner]` property, of the member id's type;
  - the static `MembershipCodes`, which the generator also writes unless you declare one.
- **A new member row gets its id in code**, from `DocumentShareId.Create()`. The generator writes `Create()` for an
  `[EntityId<Guid>]`. An id over any other type declares its own `Create()`.
- **When the generator cannot write the list**, [DDD00059](../diagnostics.md#ddd00059) says what stands in the way.
- **For another shape, declare the list yourself.** Two examples are codes in a class of their own, and a collection
  called `Members`. The generator then writes no list. It still writes the name, the codes and the scope. `_shares` is
  the field behind `Shares`:

```csharp
private MemberList<DocumentShare, DocumentShareId, UserId, NamedRole> Members
    => new(_shares, OwnerId, DocumentRefusals.Membership);
```

### Names

- **A resource is named after its class, in the plural.** `Document` becomes `documents`, `CraneBoard` becomes
  `crane-boards`, and `Category` becomes `categories`. Only the last word is made plural, by the plain English
  rules. A word that ends in `s`, `x`, `z`, `ch` or `sh` takes `es`. A consonant and `y` become `ies`. Any other word
  takes `s`.
- **The name is written two ways.** Codes use dashes (`crane-boards.already-member`). Functions use underscores
  (`crane_boards_i_see`).
- **`[Members("People")]` sets another name.** It gives `people.already-member` and `people_i_see`. This name is not
  made plural. Write it as a class name (`People`, `CraneBoards`) or in its final form (`crane-boards`). It may hold
  letters and digits, with `-` or `_` between words. A space, any other symbol, or a digit at the start is
  [DDD00091](../diagnostics.md#ddd00091).
- **Renaming the class renames the codes and the functions.** Clients then read other codes, and the next access file
  makes new functions. The export reports once which names went and which came
  ([DDD00090](../diagnostics.md#ddd00090)). To keep the old name, write `[Members("Documents")]`.
- **The system-work scope is named after the module**, for example `filing` for `Filing`
  ([System work](checks-and-saves.md#system-work)).

## Rules of the member list

The member list refuses each of the following before it changes anything.
[`MemberList`](reference/resource-and-members.md#memberlisttmember-tid-tmemberid-troleid) lists every method and
refusal.

- **A member is on the list once, for a period.** A membership that has ended is over. The member comes back with a
  new membership, without the old roles. While a membership runs or is still to start, adding the member again is
  refused (`documents.already-member`), whatever start the new membership has. `Add` and `GiveRole` take `now` to
  decide this.
- **A period ends after it starts.** A period that ends at or before its start is refused
  (`documents.invalid-period`).
- **A member holds each role once, for a period of its own.** A role counts only while the membership counts.
- **A resource has one owner.** `Members.Open(now)` puts the owner on the list, with no end and no role. The owner
  cannot be removed and cannot get a role (`documents.owner-protected`) until somebody else is named owner.
- **`Members.NameOwner(member, formerOwner, now)` names a new owner.** The new owner is a member from now on, with no
  end and no role. The call adds the member if needed, drops the member's roles, lifts its end and moves its start to
  now. Handing the resource on again restores none of this. Naming the current owner is refused
  (`documents.already-owner`).
- **You choose what happens to the former owner.** `FormerOwner.StaysAs(role)` keeps the former owner as a member in
  that role. `FormerOwner.Leaves` takes the former owner off the list. There is no default.
- **The result, [`OwnerNamed`](reference/resource-and-members.md#ownernamedtmemberid-troleid), lists every change.**
  One of them is `EndLifted`, for a new owner who should still leave when the old membership would have ended.
- **The members live inside your aggregate**, under its single version, so `If-Match` covers them. The package
  raises no events. Raise your own events from the result each method returns.
- **Refusals carry your codes.** `Document.Codes.Refuse(MembershipRefusals.NotFound)` gives `documents.not-found`.
  `With` renames one code, and `WithMemberArgument("Staff")` renames the argument that carries the member
  ([Refusals](reference/refusals.md)).
- **The registration brings the texts**, in English and Dutch, for your
  [failure localizer](../localization.md#texts-a-packages-registration-offers). A text of your own for the same code
  is used first.
- **Invariants catch code that changed the collection another way.** Write invariants that call
  [`OneMembershipPerMember()`](reference/resource-and-members.md#onemembershippermember) and `OwnerStays()`.
- **For a screen of members**, `MemberOverviews.From(...)` lists the owner first, then the other members by start
  ([tutorial step 6](tutorial.md#step-6-open-a-board-see-it-list-them)).

## Several kinds of resource

Each resource has its own member class, rules, registrations, tables, codes, functions and access manager
(`MembershipAccessManager<Folder>` beside `MembershipAccessManager<Document>`). Two resources share none of these. The
module keeps one request interface, which gets one check per resource. Adding a second resource changes none of the
existing calls:

```csharp
[Member<FolderMemberId, StaffCode, NamedRole, Folder>]
public sealed partial class FolderMember;

services.AddDocumentMembership<FilingContext>(DocumentMembership.Rules);
services.AddDocumentMemberAccess<IFilingRequest>();
services.AddFolderMembership<FilingContext>(FolderMembership.Rules);
services.AddFolderMemberAccess<IFilingRequest>();
```

- **Each resource has one member class.** A second member class for the same resource is
  [DDD00045](../diagnostics.md#ddd00045). A member class that names something other than one of your aggregate
  roots, such as a child entity, is [DDD00050](../diagnostics.md#ddd00050). Both are reported on the member class. In
  a domain project without the registrations, both mistakes are [DDD00060](../diagnostics.md#ddd00060).
- **For table and column names of your own, call `HasMembers` in `OnModelCreating`.** A hand-written call wins over
  the convention, for that resource:

  ```csharp
  modelBuilder.Entity<Folder>().HasMembers(folder => folder.Staff,
      new MemberTableNames("FolderStaff", "FolderStaffRoles", MemberColumn: "StaffCode"));
  ```

- **`IsCustomRole()` does the same for a role class** that a context maps in a table layout of its own
  ([Roles a customer makes](permissions-and-roles.md#roles-a-customer-makes)). A members collection that the context
  ignores stays unmapped.
- **A composite key works.** Both tables use the resource's full key, with its [`[KeyPart]`](../composite-keys.md)
  parts under their own names. With `HasMembers`, call `HasKey` before `HasMembers`. A key set after `HasMembers` is
  refused where the mapping is read.
