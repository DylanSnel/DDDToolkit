# Composite keys

Some tables are keyed on more than the aggregate's identifier. A schema partitioned by region keys
every table `(region_id, id)`, and the foreign key of an owned child carries `region_id` as well, so
the database itself refuses a child that points at a parent in another region. Ledgers, periods and
tenants have the same shape.

`[KeyPart]` maps that shape. It is one attribute and one Entity Framework convention, and it is all
the toolkit does: it builds the key. What the key part *means*, where its value comes from and who
may see which rows are your application's business.

This page is about Entity Framework only. The key is built by `KeyPartConvention`, which
`AddDDDToolkitConventions()` from `DDDToolkit.EntityFramework` adds to the model. Without that
package the attribute changes nothing about how an entity behaves.

```csharp
[AggregateRoot<ProjectId>]
public partial class Project
{
    public Project(RegionId regionId, ProjectId id, string name) : base(id)
    {
        RegionId = regionId;
        Name = name;
    }

    [KeyPart]
    public RegionId RegionId { get; }

    public string Name { get; private set; }

    public partial IReadOnlyList<Milestone> Milestones { get; }

    public void Plan(string title) => _milestones.Add(new Milestone(RegionId, MilestoneId.CreateUnique(), title));
}

[Entity<MilestoneId>]
public partial class Milestone
{
    public Milestone(RegionId regionId, MilestoneId id, string title) : base(id)
    {
        RegionId = regionId;
        Title = title;
    }

    [KeyPart]
    public RegionId RegionId { get; }

    public string Title { get; private set; }
}
```

The generator writes one thing for a key part, in the entity's own generated part: the names of its
key parts, in declaration order, behind the `IHasKeyParts` interface. That list is what the
convention reads.

```csharp title="Project.g.cs, shortened"
partial class Project : AggregateRoot<ProjectId>, IHasKeyParts
{
    // ...

    static IReadOnlyList<string> IHasKeyParts.KeyParts => new string[] { nameof(RegionId) };

    // ...
}
```

`Milestone` gets the same list. The Entity Framework generator writes nothing for a key part:
`Milestone`'s Entity Framework part is the `[Owned]` every child entity gets, and nothing more. There is
no generated `HasKey` or `HasForeignKey`; the key is decided by the convention while Entity Framework
builds the model. With `AddDDDToolkitConventions()` in `ConfigureConventions` and nothing in
`OnModelCreating`, the example maps to:

```sql
CREATE TABLE "Projects" (
    "Id" uuid NOT NULL,
    "RegionId" uuid NOT NULL,
    "Name" text NOT NULL,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_Projects" PRIMARY KEY ("RegionId", "Id")
);
CREATE TABLE "Milestone" (
    "Id" uuid NOT NULL,
    "RegionId" uuid NOT NULL,
    "ProjectId" uuid NOT NULL,
    "Title" text NOT NULL,
    CONSTRAINT "PK_Milestone" PRIMARY KEY ("RegionId", "ProjectId", "Id"),
    CONSTRAINT "FK_Milestone_Projects_RegionId_ProjectId" FOREIGN KEY ("RegionId", "ProjectId")
        REFERENCES "Projects" ("RegionId", "Id") ON DELETE CASCADE
);
```

## What it is not

`[KeyPart]` builds a key. It does not fill the value in, filter queries by it, or check that the
caller may see a row. If you need those, they are application concerns: an interceptor or the
constructor to set the value, a global query filter or the database's own row-level security to
restrict what is read. The toolkit stays out of that on purpose, so the attribute means the same thing
in every application that uses it.

Everything else about an entity is unchanged. Reference other aggregates by id as usual. A composite
key does not change what another aggregate holds: `CustomerId` is still just a `CustomerId`, and a
query that needs the other aggregate's region supplies it.

## The rules

**A key part joins the primary key ahead of the identifier.** `Project` is keyed `(RegionId, Id)`.

**Every owned relationship carries it.** The foreign key from `Milestone` to `Project` is
`(RegionId, ProjectId)`, and `Milestone`'s own key is `(RegionId, ProjectId, Id)`. The `RegionId` in
that foreign key is the child's own `RegionId` property, not a hidden copy, so there is one column
and a child row can only ever sit under a parent in its own region. This applies to owned
collections, to single owned entities (which live in the owner's row and share its `RegionId`
column) and to anything owned further down.

Because the child's property *is* the foreign key, Entity Framework treats it as one: when it saves
an owned child it fills the foreign key in from the owner, and a child constructed with a different
region is stored, and read back, with its owner's. Nothing throws. Set the child's value from the
parent, as `Plan` does above, and the question does not come up.

**The child must have the property.** An owned type needs a property with the same name and type as
each of its owner's key parts. Leave it out and the model refuses to build, with an exception that
names the owner, the child and the property. That happens the first time the context's model is
built, before any query, which is on purpose: the alternative is a narrower key than the one you
asked for, discovered in production. Mark the child's property `[KeyPart]` too. It is not required
for the foreign key, but it gets the child the same public-setter check as its parent.

**With more than one, declaration order.** The key follows the order the properties are declared in
the source, top to bottom:

```csharp
[KeyPart] public int Period { get; }        // first
[KeyPart] public RegionId RegionId { get; } // second
// key: (Period, RegionId, Id)
```

Not alphabetical order, and not whatever order reflection happens to return. The generator reads
the order from the source and writes it down in the class, in the `KeyParts` list shown above
(`nameof(Period), nameof(RegionId)` for these two), where the convention reads it back. That is also
why all key parts of a class must be declared in one file ([DDD00030](diagnostics.md#ddd00030)):
between the files of a partial class there is no declaration order to follow.

**It is an ordinary property.** You set it, usually through the constructor, and the toolkit never
assigns, validates or interprets it. Declare it get-only, `{ get; }`, or with a private setter.
Entity Framework does not map get-only properties by convention; the key-part convention maps them
itself. A public setter reports [DDD00029](diagnostics.md#ddd00029), because a key value that can be
reassigned after the row exists is a bug waiting to happen.

**It is not identity.** Two instances with the same `Id` are equal, whatever their key parts hold,
exactly as `Version` plays no part in equality. Entity Framework's change tracker uses the whole key,
so it will happily track both; the domain treats them as the same entity.

## Explicit configuration wins

Anything you configure yourself is left alone. A key set in `OnModelCreating` with `HasKey`, or with
`[PrimaryKey]` on the class, stays as written, and the convention then leaves that type's owned types
alone as well. So does an ownership whose foreign key you set with `HasForeignKey`. If the
convention does something you do not want, configure that entity yourself and it steps aside:

```csharp
modelBuilder.Entity<Project>().HasKey(project => project.Id);   // back to (Id), key part or not
```

## Limits

- **Owned entities only.** The foreign keys the convention rewrites are ownerships, which is how
  `[Entity<T>]` children are mapped. A relationship you configure between two aggregate roots is
  yours to key.
- **An owned entity inside an owned entity** is not discovered by Entity Framework on its own, key
  parts or not. Declare the nesting with `OwnsMany`/`OwnsOne` and the convention keys it as usual:
  ```csharp
  modelBuilder.Entity<Project>().OwnsMany(p => p.Milestones, m => m.OwnsMany(x => x.Tasks));
  ```
  The inner type's foreign key is then `(RegionId, MilestoneProjectId, MilestoneId)`.
- **Changing a key is a migration.** Adding `[KeyPart]` to an existing aggregate changes its primary
  key and every owned table's foreign key. Entity Framework generates the migration, but rebuilding a
  primary key on a large table is not something to find out about from `dotnet ef migrations add`.

## What is not touched

A model with no `[KeyPart]` anywhere is not changed in any way: the convention looks for key parts
first and returns without touching the model when there are none. In a model that has some, every
type that neither declares key parts nor is owned by one that does is mapped exactly as it would be
without the convention. Both are covered by tests that compare the whole model with and without it.

The generator adds nothing to a class without key parts either. Only a class with them implements
`DDDToolkit.Interfaces.IHasKeyParts`, which lists the parts in declaration order for the convention.
You never implement that interface by hand.
