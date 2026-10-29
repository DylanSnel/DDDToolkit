---
title: "Membership: resources and members"
sidebar_label: Resources and members
---

# Membership: resources and members

Read this page when you add a resource with members. It covers the member class, the owner, the member list, the
managers, the names, and several kinds of resource in one module. The [tutorial](tutorial.md) builds a resource
beside Tenancy.

## The packages

> [!NOTE]
> The packages are published as `Temp.DDDToolkit.Supporting.Membership`, `.Abstractions`, `.EntityFramework` and
> `.Postgres` ([the package ids](../migrating-to-3.md#the-package-ids)). They are prereleases, so their names can
> still change.

| Package | What it holds |
|---|---|
| `DDDToolkit.Supporting.Membership` | The templates, the member list, the managers' base classes and the generator that writes the list and the managers, the rules, the access manager (`MembershipAccessManager<TResource>`) and the requirements. It has no database code. |
| `DDDToolkit.Supporting.Membership.Abstractions` | Membership's markers for domain classes: `[At]`, on the property that says where a resource hangs, in the namespace `DDDToolkit.Supporting.Membership`. No Entity Framework. `DDDToolkit.Supporting.Membership` references it. |
| `DDDToolkit.Supporting.Membership.EntityFramework` | The mapping by convention, with `HasMembers()` and `IsCustomRole()` for names of your own, the registrations, the stores the managers load and save through, and `WhereIHold` for your own queries, on any provider. Its generator writes the [join with Tenancy](beside-an-organization.md#with-tenancy). |
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

    // Runs before every change the member manager makes to the shares
    partial void OnChangingMembers()
    {
        if (Archived)
        {
            throw new RefusalException("documents.archived", RefusalKind.Conflict, "The document is archived.");
        }
    }
}
```

- `Members` is the **member list**. The generator writes it over `Shares`
  ([The generated member list](#the-generated-member-list)), with the name `documents` ([Names](#names)), the codes in
  `Codes` (`documents.already-member` and the others), and the system-work scope `filing`.
- The generator also writes `DocumentShareManager`, the **member manager**, named after the member class
  ([The managers](#the-managers)). Every change to the shares goes through it. The document has no method for them.
- `OnChangingMembers` is a **hook**. The generator declares it on the document, and the member manager calls it
  before each change. This one refuses every change to the shares of an archived document.

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

    [On<Document>(ManagesMembers = true, NamesOwner = true, Description = "Say who a document is shared with, and hand it on")]
    public const string Share = "documents.share";
}
```

- The owner holds all three permissions by owning the document. A role can give each of them. Every member holds
  `View`.
- `ManagesMembers` marks `Share` as the permission that changes the members. `NamesOwner` marks it as the permission
  that hands the document on. The member manager requires them. Without the marks, it makes these changes only for
  system work.
- On Postgres, the database also refuses a direct write to the member rows or the owner column from a caller without
  `Share` ([Who writes the member rows](postgres.md#who-writes-the-member-rows)).

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
        services.AddDocumentMembership<FilingContext>(DocumentMembership.Rules);    // the access manager, the queries and the member manager for documents
        services.AddDocumentMemberAccess<IFilingRequest>();                         // the check for documents, on your requests
        services.RequireExplicitCurrentUser();                                      // work that begins no user fails
        return services.AddScoped<DocumentHandlers>();
    }
}
```

- The generator writes `AddDocumentMembership` and `AddDocumentMemberAccess`, and names them after the resource.
- `AddDDDToolkitConventions()` maps `Shares` as two tables, `DocumentShares` and `DocumentShareRoles`, owned by the
  document. `OnModelCreating` needs no line for them. Add a migration for the tables.
- `AddDocumentMembership` registers `MembershipAccessManager<Document>` and `DocumentShareManager` per scope, and the
  queries on `db.Documents` ([Access in your own queries](checks-and-saves.md#access-in-your-own-queries)). The
  manager loads and saves documents on `FilingContext`.
- Without `RequireExplicitCurrentUser()`, work that begins no user runs as system work, which holds every
  permission. With it, that work [fails](../row-level-security.md#requiring-an-explicit-current-user).

**A request and its handler.** `[OnResource]` marks the document the permission is held on. `[ExpectedVersion]` marks
the version the caller read. The generator writes `MemberAccess.On("documents.share", request.Document,
request.ExpectedVersion)` into the partial interface.

```csharp
[AccessRequests]
public partial interface IFilingRequest : IRequireAccess;           // what every request of your module implements

[RequiresPermission(DocumentPermissions.Share)]
public sealed record ShareDocument([OnResource] DocumentId Document, UserId With, string Role, DateTimeOffset? Until, [ExpectedVersion] long? ExpectedVersion = null) : IFilingRequest;

public sealed class DocumentHandlers(DocumentShareManager shares, ICurrentUser user, TimeProvider clock)
{
    // The user who writes a document owns it: the manager adds the new document and saves it
    public Task<Document> WriteAsync(CancellationToken cancellationToken)
        => shares.OpenAsync(new Document(DocumentId.Create(), new UserId(user.Id!.Value), clock.GetUtcNow()), cancellationToken);

    // Runs after the check let the request through: the manager loads the document at the version named,
    // checks the role, calls the hook, adds the share from now and saves
    public Task<Document> HandleAsync(ShareDocument command, CancellationToken cancellationToken)
        => shares.AddAsync(command.Document, command.With, new NamedRole(command.Role), command.Until, cancellationToken, command.ExpectedVersion);
}
```

- **The checks must run before the handler.** The behavior generated for an `[AccessRequests]` interface runs them,
  or your dispatcher calls `AccessChecks<IFilingRequest>.RequireAsync(request)` ([Access requirements](../access-requirements.md)).
  With the behavior, `AddDocumentMemberAccess` also adds a start-up check. A host without the behavior in its
  pipeline does not start ([When nothing asks the checks](../access-requirements.md#handlers-reached-without-the-access-check)).
- **`[OnResource]` goes on the id of a resource with members.** On any other id it is
  [DDD00081](../diagnostics.md#ddd00081). For a requirement computed from the request's values, implement
  `RequiredAccess` instead ([Requirements that combine](checks-and-saves.md#requirements-that-combine)).
- **The manager uses the request's check.** `ShareDocument` was checked for `documents.share` on this document, so
  `AddAsync` asks nothing again. Without such a request in hand, the manager asks the access manager itself
  ([The managers](#the-managers)).
- **The manager checks what the member list cannot know.** The role must exist for the resource. With fixed roles,
  that means a role the rules declare. Where you registered an `IMemberDirectory`, the new member must be known and
  active. Where you registered an `IMemberRolePolicy`, a member must be allowed to hold the role
  ([Who may be a member](reference/managers.md#who-may-be-a-member)).
- **Pass the version the caller read.** The manager compares it with the document it loads. A change between the
  check and the load is then a 409 ([From the check to the save](checks-and-saves.md#from-the-check-to-the-save)).
- **Each handler returns the document** as the manager saved it.

### The generated member list

The generator writes `Members` as a private property on the resource. Your constructor opens the list with it. The
member manager makes every other change through it.

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

**Document.MemberManagement.g.cs, shortened**

```csharp
partial class Document : global::DDDToolkit.Supporting.Membership.IMembershipResource<global::Filing.DocumentShare, global::Filing.UserId, global::DDDToolkit.Supporting.Membership.NamedRole>
{
    partial void OnChangingMembers();
    partial void OnMemberAdded(global::Filing.DocumentShare member);
    partial void OnMemberRemoved(global::Filing.UserId member);
    partial void OnRoleGiven(global::Filing.UserId member, global::DDDToolkit.Supporting.Membership.MemberRole<global::Filing.UserId, global::DDDToolkit.Supporting.Membership.NamedRole> role);
    partial void OnRoleTaken(global::Filing.UserId member, global::DDDToolkit.Supporting.Membership.MemberRole<global::Filing.UserId, global::DDDToolkit.Supporting.Membership.NamedRole> role);
    partial void OnOwnerNamed(global::DDDToolkit.Supporting.Membership.OwnerNamed<global::Filing.UserId, global::DDDToolkit.Supporting.Membership.NamedRole> named);

    // The member manager's way to the list, written as explicit members of the interface: AddMember, GiveRole,
    // TakeRole, RemoveMember and NameOwner. Each calls OnChangingMembers, then Members, then the hook of the change.
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
  called `Members`. The generator then writes no list and no managers. It still writes the name, the codes and the
  scope. `_shares` is the field behind `Shares`:

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

## The managers

The generator writes two managers for a resource, into the project of the member class, in the global namespace:

| Manager | Named after | Written when |
|---|---|---|
| The member manager, `DocumentShareManager` | the member class, with `Manager` after it | the generator writes the resource's member list |
| The role manager, `BoardRoleManager` | the role class, `[CustomRole<BoardRoleId, Board>]`, with `Manager` after it | the role class is declared in the project of its resource |

- **A handler takes them like any service.** The resource's registration, `AddDocumentMembership` or
  `AddBoardMembershipWithTenancy`, adds both per scope.
- **Each method loads, checks, changes and saves**, and returns what it saved. A change takes the resource or its id.
  After the cancellation token comes the version the caller read, for a command that names one.
- **Your code keeps the resource, its constructor and its hooks.** The managers keep the rest. The base classes are
  `MemberManager<...>` and `CustomRoleManager<...>` in `DDDToolkit.Supporting.Membership.Managers`
  ([Managers and access](reference/managers.md)).
- **A type of the same name in your project** keeps the manager out. One in a namespace is
  [DDD00106](../diagnostics.md#ddd00106), because the generated manager would hide it. One in the global namespace
  is your own manager, and the generator writes none.

### The member manager

| Method | What it does | The caller needs |
|---|---|---|
| `OpenAsync(resource)` | checks that the owner may be a member, then adds the new resource and saves it | nothing more than the request's check |
| `AddAsync(resource, member, role, until)` | adds a member from now, with a role for the same period where one is given | the permission marked `ManagesMembers` |
| `GiveRoleAsync(resource, member, role, until)` | gives a member a role from now | the permission marked `ManagesMembers` |
| `TakeRoleAsync(resource, member, role)` | takes a role from a member | the permission marked `ManagesMembers` |
| `RemoveAsync(resource, member)` | takes a member off, with its roles | the permission marked `ManagesMembers` |
| `LeaveAsync(resource)` | takes the caller off, with its roles | to see the resource, and to be a member that is not the owner |
| `NameOwnerAsync(resource, owner, formerOwner)` | names another owner ([Rules of the member list](#rules-of-the-member-list)) | the permission marked `NamesOwner` |
| `ListAsync(resource)` | the members of a resource the caller sees, the owner first, then by start | to see the resource |
| `ListAsync(resources)` | the same for at most 200 resources, in one statement | nothing: a resource the caller does not see is left out |

What a change checks, in this order:

1. **The caller's right.** The refusals come as the access manager's do: the caller's own refusal when the caller is
   nobody, `not-found`, then `not-permitted` naming the permission. When the request in hand was checked for that
   permission on that resource, the manager uses the check's answer and sends no statement. A resource whose rules
   mark no such permission lets only system work make the change.
2. **Who may go on the list.** An active member, where you registered an `IMemberDirectory`. A role that exists for
   the resource and that a member may hold. With Tenancy, the join answers both ([With Tenancy](beside-an-organization.md#with-tenancy)).
3. **The load**, at the version named. A resource at another version is a 409.
4. **The change.** Your guard, `OnChangingMembers()`, runs first. Then the member list applies its
   [rules](#rules-of-the-member-list), and the hook of the change runs.
5. **The save**, as the caller ([The caller's own place](#the-callers-own-place) has the exception). The method
   returns the resource as saved, with the events your hooks raised.

The hooks are `partial` methods that the generator declares on the resource. Implement the ones you need:

| Hook | Runs |
|---|---|
| `OnChangingMembers()` | before every change, for a guard of yours |
| `OnMemberAdded(member)` | after a member was added, before the save, with your member row |
| `OnMemberRemoved(memberId)` | after a member was taken off |
| `OnRoleGiven(memberId, role)`, `OnRoleTaken(memberId, role)` | after a role was given or taken |
| `OnOwnerNamed(named)` | after another owner was named, with every change it made |

Raise your own domain events from the hooks. The package raises none.

### The caller's own place

A member that takes a role from itself, takes itself off, leaves, or hands on a resource it owns can lose the right
that the save needs. Where the database checks every row, the save would then be refused. So the member manager saves
such a change as the resource's own work, in the module's scope, for the caller, when both hold:

- the request in hand is the one whose check read this resource;
- the unit of work holds no change besides the resource and its members.

Otherwise it saves as the caller, and the database judges every row. With Tenancy, the join begins that work for the
caller's seat, so the history names who left ([System work](checks-and-saves.md#system-work)).

### The role manager

The role manager changes the roles a customer makes, `Roles.Custom(...)`
([Roles a customer makes](permissions-and-roles.md#roles-a-customer-makes)):

| Method | What it does |
|---|---|
| `CreateAsync(role)` | adds a role your constructor made, and saves it |
| `CreateStarterRolesAsync(make)` | makes the starter roles the customer does not have yet, each with your function, and saves them |
| `RenameAsync(role, name, description)` | renames a role and describes it again |
| `SetPermissionsAsync(role, permissions)` | sets what a role gives |
| `ArchiveAsync(role)` | archives a role: it gives nothing from then on |
| `ListAsync()`, `ByIdAsync(roles)` | the roles there are for the caller, archived ones included |

- **Every change requires the permission the rules name in `managedWith`.** System work needs none. Rules without
  `managedWith` let only system work change the roles.
- **Who holds that permission** is answered by `ICustomRoleAccess<TResource>`. With Tenancy, the join registers it,
  and the caller must hold the permission for the whole tenant. Without Tenancy, register your own, or keep the role
  changes to system work.
- **The role class gets hooks too**: `OnRenamed()`, `OnPermissionsSet(set)` and `OnArchived()`. Each runs only when
  the role changed.

## Rules of the member list

The member list refuses each of the following before it changes anything. The member manager calls it, so these
refusals reach your handler from the manager.
[`MemberList`](reference/resource-and-members.md#memberlisttmember-tid-tmemberid-troleid) lists every method and
refusal.

- **A member is on the list once, for a period.** A membership that has ended is over. The member comes back with a
  new membership, without the old roles. While a membership runs or is still to start, adding the member again is
  refused (`documents.already-member`), whatever start the new membership has. The member manager passes the
  current time to decide this.
- **A period ends after it starts.** A period that ends at or before its start is refused
  (`documents.invalid-period`).
- **A member holds each role once, for a period of its own.** A role counts only while the membership counts.
- **A resource has one owner.** `Members.Open(now)` puts the owner on the list, with no end and no role. The owner
  cannot be removed and cannot get a role (`documents.owner-protected`) until somebody else is named owner.
- **`NameOwnerAsync(resource, owner, formerOwner)` names a new owner.** The new owner is a member from now on, with no
  end and no role. The call adds the member if needed, drops the member's roles, lifts its end and moves its start to
  now. Handing the resource on again restores none of this. Naming the current owner is refused
  (`documents.already-owner`).
- **You choose what happens to the former owner.** `FormerOwner.StaysAs(role)` keeps the former owner as a member in
  that role. `FormerOwner.Leaves` takes the former owner off the list. There is no default.
- **[`OwnerNamed`](reference/resource-and-members.md#ownernamedtmemberid-troleid) lists every change**, and the
  `OnOwnerNamed` hook receives it. One of the changes is `EndLifted`, for a new owner who should still leave when the
  old membership would have ended.
- **The members live inside your aggregate**, under its single version, so `If-Match` covers them. The package
  raises no events. Raise your own events from the [hooks](#the-member-manager).
- **Refusals carry your codes.** `Document.Codes.Refuse(MembershipRefusals.NotFound)` gives `documents.not-found`.
  `With` renames one code, and `WithMemberArgument("Staff")` renames the argument that carries the member
  ([Refusals](reference/refusals.md)).
- **The registration brings the texts**, in English and Dutch, for your
  [failure localizer](../localization.md#texts-a-packages-registration-offers). A text of your own for the same code
  is used first.
- **Invariants catch code that changed the collection another way.** Write invariants that call
  [`OneMembershipPerMember()`](reference/resource-and-members.md#onemembershippermember) and `OwnerStays()`.
- **For a screen of members**, the member manager's `ListAsync` returns the owner first, then the other members by
  start ([tutorial step 6](tutorial.md#step-6-open-a-board-see-it-list-them)).

## Several kinds of resource

Each resource has its own member class, rules, registrations, tables, codes, functions, access manager
(`MembershipAccessManager<Folder>` beside `MembershipAccessManager<Document>`) and member manager (`FolderMemberManager`
beside `DocumentShareManager`). Two resources share none of these. The
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
