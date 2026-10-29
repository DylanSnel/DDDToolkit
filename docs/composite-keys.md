# Composite keys

Some tables have a key of more than the aggregate's identifier. A schema partitioned by region keys every table on
`(region_id, id)`. The foreign key of an owned child also includes `region_id`, so the database refuses a child that
points at a parent in another region. Ledgers, periods and tenants use the same shape.

`[KeyPart]` maps this shape. The toolkit provides one attribute and one Entity Framework convention, and it only builds
the key. Your application decides what the key part means, where its value comes from, and who may see which rows.

This page is about Entity Framework only. `KeyPartConvention` builds the key, and `AddDDDToolkitConventions()` from
`DDDToolkit.EntityFramework` adds the convention to the model. Without that package, the attribute changes nothing
about how an entity behaves.

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

For a key part, the generator writes one thing, in the entity's generated part. It writes the names of the key parts,
in declaration order, behind the `IHasKeyParts` interface. The convention reads this list.

**Project.g.cs, shortened**

```csharp
partial class Project : AggregateRoot<ProjectId>, IHasKeyParts
{
    // ...

    static IReadOnlyList<string> IHasKeyParts.KeyParts => new string[] { nameof(RegionId) };

    // ...
}
```

`Milestone` gets the same list. The Entity Framework generator writes nothing for a key part. `Milestone`'s Entity
Framework part is the `[Owned]` that every child entity gets, and nothing more. No `HasKey` or `HasForeignKey` is
generated. The convention decides the key while Entity Framework builds the model. With `AddDDDToolkitConventions()`
in `ConfigureConventions` and nothing in `OnModelCreating`, the example maps to this schema:

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

## What `[KeyPart]` does not do

`[KeyPart]` builds a key. It does not fill in the value, filter queries by the value, or check that the caller may see
a row. Your application handles these:

- An interceptor or the constructor sets the value.
- A global query filter, or the database's row level security (RLS), restricts what code reads.

The toolkit leaves these to the application on purpose, so the attribute means the same in every application.

Everything else about an entity stays the same. Refer to other aggregates by id as usual. A composite key does not
change what another aggregate holds. A `CustomerId` is still a `CustomerId`, and a query that needs the other
aggregate's region supplies the region.

## How the key is built

**A key part joins the primary key before the identifier.** `Project` is keyed `(RegionId, Id)`.

**Every owned relationship includes the key part.** The foreign key from `Milestone` to `Project` is
`(RegionId, ProjectId)`, and `Milestone`'s own key is `(RegionId, ProjectId, Id)`. The `RegionId` in that foreign key
is the child's own `RegionId` property, and no hidden copy exists. So the table has one `RegionId` column, and a child
row can only belong to a parent in the same region. This applies to owned collections, to single owned entities (which
live in the owner's row and share its `RegionId` column), and to everything owned further down. If an owned type
declares a key part that its owner does not have, that part joins the owned type's key after the foreign key and
before its `Id`.

Because the child's property is the foreign key, Entity Framework treats it as the foreign key. When Entity Framework
saves an owned child, it fills in the foreign key from the owner. So a child created with a different region is
stored, and read back, with its owner's region. Nothing throws. Set the child's value from the parent, as `Plan` does
above, and the problem does not arise.

**The child must have the property.** An owned type needs a property with the same name and type as each key part of
its owner. Without that property, the model fails to build, with an exception that names the owner, the child and the
property. This happens the first time the context builds its model, before any query. The failure is on purpose. The
alternative is a narrower key than the one you asked for, found in production. Mark the child's property `[KeyPart]`
too. The foreign key does not need the attribute, but with it the child gets the same public-setter check as its
parent.

**With more than one key part, the key follows the declaration order.** The key uses the order of the properties in
the source, from top to bottom:

```csharp
[KeyPart] public int Period { get; }        // first
[KeyPart] public RegionId RegionId { get; } // second
// key: (Period, RegionId, Id)
```

The order is not alphabetical, and it is not the order that reflection returns. The generator reads the order from the
source and writes it into the class, in the `KeyParts` list shown above (`nameof(Period), nameof(RegionId)` for these
two). The convention reads the order from that list. For the same reason, all key parts of a class must be declared in
one file ([DDD00030](diagnostics.md#ddd00030)). The files of a partial class have no declaration order between them.

**A key part is an ordinary property.** You set it, usually through the constructor. The toolkit never assigns,
validates or interprets it. Declare it get-only, `{ get; }`, or with a private setter. Entity Framework does not map
get-only properties by convention, so the key-part convention maps them itself. A public setter reports
[DDD00029](diagnostics.md#ddd00029), because code could change a key value after the row exists, and that is a bug.

**A key part is not part of identity.** Two instances with the same `Id` are equal, whatever their key parts hold, in
the same way as `Version` plays no part in equality. Entity Framework's change tracker uses the whole key, so it can
track both instances. The domain treats them as the same entity.

## Explicit configuration

The convention leaves your own configuration alone:

- A key set in `OnModelCreating` with `HasKey`, or with `[PrimaryKey]` on the class, stays as written. The convention
  then also leaves that type's owned types alone.
- An ownership whose foreign key you set with `HasForeignKey` stays as written, with everything owned further down.

If the convention does something you do not want, configure that entity yourself, and the convention leaves it alone:

```csharp
modelBuilder.Entity<Project>().HasKey(project => project.Id);   // back to (Id), with or without a key part
```

## Limits

- **Owned entities only.** The convention rewrites ownership foreign keys, which is how `[Entity<T>]` children are
  mapped. You key a relationship between two aggregate roots yourself.
- **An owned entity inside an owned entity.** Entity Framework does not discover this nesting by itself, with or
  without key parts. Declare the nesting with `OwnsMany`/`OwnsOne`, and the convention keys it as usual:
  ```csharp
  modelBuilder.Entity<Project>().OwnsMany(p => p.Milestones, m => m.OwnsMany(x => x.Tasks));
  ```
  The inner type's foreign key is then `(RegionId, MilestoneProjectId, MilestoneId)`.
- **Changing a key needs a migration.** Adding `[KeyPart]` to an existing aggregate changes its primary key and the
  foreign key of every owned table. Entity Framework generates the migration. Rebuilding a primary key on a large table
  is expensive, so plan for it before you run `dotnet ef migrations add`.

## Models without key parts

The convention does not change a model without `[KeyPart]`. It looks for key parts first, and returns without a change
when it finds none. In a model with key parts, every type that neither declares key parts nor is owned by such a type
is mapped as it would be without the convention. Tests compare the whole model with and without the convention for
both cases.

The generator adds nothing to a class without key parts. Only a class with key parts implements
`DDDToolkit.Interfaces.IHasKeyParts`, which lists the parts in declaration order for the convention. You never
implement that interface yourself.
