# Diagnostics

A source generator that cannot handle a declaration produces nothing. The type then compiles as a plain class
without the generated members, and nothing says why. The DDDToolkit generators and analyzers report a diagnostic
for each such misuse instead. Each section below shows code that triggers one diagnostic, why the code is a
problem, and the fix.

| Id | Severity | Meaning |
|---|---|---|
| [DDD00001](#ddd00001) | Error | Value objects must be records |
| [DDD00002](#ddd00002) | Error | Entities must be classes |
| [DDD00003](#ddd00003) | Error | Entity ids must be records |
| [DDD00004](#ddd00004) | Warning | Entity id structs should be readonly |
| [DDD00005](#ddd00005) | Error | DDDToolkit types must be partial |
| [DDD00006](#ddd00006) | Error | DDDToolkit types cannot be generic |
| [DDD00007](#ddd00007) | Error | The generated identifier name is already taken |
| [DDD00008](#ddd00008) | Error | The identifier type argument is not supported |
| [DDD00009](#ddd00009) | Error | A type is either an entity or an aggregate root |
| [DDD00010](#ddd00010) | Error | Value object properties must use protected setters |
| [DDD00011](#ddd00011) | Error | Value object properties must use init setters |
| [DDD00013](#ddd00013) | Error | Value objects cannot be sealed |
| [DDD00014](#ddd00014) | Warning | The generators cannot read the project's MSBuild properties |
| [DDD00020](#ddd00020) | Error | Generated collection properties must be get-only |
| [DDD00021](#ddd00021) | Warning | Reference another aggregate by its id |
| [DDD00022](#ddd00022) | Warning | Use only what another module publishes |
| [DDD00023](#ddd00023) | Warning | Do not hold another module's entity |
| [DDD00024](#ddd00024) | Warning | An invariant must be nested inside the entity it is about |
| [DDD00025](#ddd00025) | Warning | An invariant is nested inside a type it is not about |
| [DDD00026](#ddd00026) | Warning | Two invariants of one entity share a code |
| [DDD00027](#ddd00027) | Error | An invariant needs an accessible parameterless constructor |
| [DDD00028](#ddd00028) | Error | A key part belongs on an entity or aggregate root |
| [DDD00029](#ddd00029) | Warning | A key part should not have a public setter |
| [DDD00030](#ddd00030) | Error | Declare all key parts of a type in one file |
| [DDD00031](#ddd00031) | Error | A [SupabaseMigrations] context or factory must be one the build can make |
| [DDD00032](#ddd00032) | Warning | Do not ask HotChocolate's generator for a toolkit identifier's node id serializer |
| [DDD00033](#ddd00033) | Warning | The generated integration event registration must be able to construct the class |
| [DDD00034](#ddd00034) | Warning | An event's class name and its Version disagree |
| [DDD00035](#ddd00035) | Error | An event's class name ends in something that is not a version |
| [DDD00036](#ddd00036) | Error | Two events of one module share a name and version |
| [DDD00037](#ddd00037) | Error | Two event names give one constant name |
| [DDD00038](#ddd00038) | Error | A row access rule, an access function or a database question has the shape the generator reads |
| [DDD00039](#ddd00039) | Error | A row access rule can only say what the database can check |
| [DDD00040](#ddd00040) | Error | A row access rule guards an aggregate root |
| [DDD00041](#ddd00041) | Error | A row access rule reads the aggregate's entities through an access function |
| [DDD00042](#ddd00042) | Error | A parent for entities is an abstract generic class whose first type parameter is the id |
| [DDD00043](#ddd00043) | Error | A template's first type argument is an entity id |
| [DDD00044](#ddd00044) | Error | A template takes a type from a class nobody declares |
| [DDD00045](#ddd00045) | Error | A template takes a type from a class declared more than once |
| [DDD00046](#ddd00046) | Error | A template attribute fills exactly the type parameters of its parent |
| [DDD00047](#ddd00047) | Error | A class is declared an entity or aggregate root once |
| [DDD00048](#ddd00048) | Error | A class a template takes meets its parent's constraints |
| [DDD00049](#ddd00049) | Error | A template registration needs a class declared with each of its templates |
| [DDD00050](#ddd00050) | Error | A type a template registration takes meets the method's constraints |
| [DDD00051](#ddd00051) | Error | A set-shaped question is asked once per statement, so its arguments do not read the row |
| [DDD00052](#ddd00052) | Error | A function named without its schema belongs to a module |
| [DDD00053](#ddd00053) | Error | A type argument of a template meets its parent's constraints |
| [DDD00054](#ddd00054) | Warning | A package's row access contribution is made from what the application marks |
| [DDD00055](#ddd00055) | Warning | A context's migration files are named after its module |
| [DDD00056](#ddd00056) | Error | A request interface is one a behavior can be written for |
| [DDD00057](#ddd00057) | Error | The Mediator library's pipeline behavior has the shape the generator writes a behavior for |
| [DDD00058](#ddd00058) | Error | A notification implements no request interface |
| [DDD00059](#ddd00059) | Warning | The member list of a resource is written from what the resource declares |
| [DDD00060](#ddd00060) | Warning | A member class names an aggregate root whose members it is |
| [DDD00061](#ddd00061) | Warning | A request that declares its access is sent, not handed to its handler |
| [DDD00062](#ddd00062) | Error | A class of one GraphQL schema is one the toolkit alone registers |
| [DDD00064](#ddd00064) | Warning | DDD_Module declares the module where the package's build step runs |
| [DDD00065](#ddd00065) | Info | The class a package's use cases are named through is written where each of its templates has one class |
| [DDD00066](#ddd00066) | Error | A package's switch writes a class or an id where its name is free and its id is known |
| [DDD00067](#ddd00067) | Error | A class whose package makes its new ids is declared over an id with a Create() |
| [DDD00068](#ddd00068) | Warning | DDD_ModuleContracts makes a project its module's contracts where the project can name the attribute |
| [DDD00069](#ddd00069) | Warning | A module's row access contribution is listed by the project that runs the export |
| [DDD00070](#ddd00070) | Error | A member a library marks for a package's row access contribution is public |
| [DDD00071](#ddd00071) | Error | What a package's row access contribution is made from is found once, and is what it takes |
| [DDD00072](#ddd00072) | Error | A row access contribution a package writes is not listed again |
| [DDD00073](#ddd00073) | Warning | What [assembly: LeaveOutRowAccessContribution] names is a contribution a package writes, and a context |
| [DDD00074](#ddd00074) | Warning | A design-time factory keeps the migration history where the application does |
| [DDD00075](#ddd00075) | Warning | Every class a package's use cases are named through has a name of its own where a project sees it |
| [DDD00076](#ddd00076) | Warning | [assembly: TemplateFacadeName] names, once, a class this project gets |
| [DDD00077](#ddd00077) | Error | A request says what it requires of its caller |
| [DDD00078](#ddd00078) | Error | A request says what it requires in one way |
| [DDD00079](#ddd00079) | Error | [AllowAnonymous] goes with no other requirement attribute |
| [DDD00080](#ddd00080) | Error | A marker on a request says where the permission of its [RequiresPermission] is held |
| [DDD00081](#ddd00081) | Error | A marker is on a member of the id it marks |
| [DDD00082](#ddd00082) | Error | A requirement for queries only is on a query |
| [DDD00083](#ddd00083) | Error | A request's requirement attributes are read where its module's interface is partial |
| [DDD00084](#ddd00084) | Warning | A requirement attribute beside another adds what that one does not require already |
| [DDD00085](#ddd00085) | Error | [OrOwn] goes beside [RequiresPermission], and [Own] with [OwnOnly] |
| [DDD00086](#ddd00086) | Error | [OrOwn] and [Own] mark a member of a type something says the caller's own of |
| [DDD00087](#ddd00087) | Error | [Owner] marks the one owner of an aggregate root |
| [DDD00088](#ddd00088) | Error | [Owner] marks a property of a type the caller has an own of |
| [DDD00089](#ddd00089) | Error | A module's default requirement is one any of its requests may take |
| [DDD00090](#ddd00090) | Warning | The names of resources with members are those of the newest access file |
| [DDD00091](#ddd00091) | Error | [Members] names a resource with what can be converted to a code and a function's name |
| [DDD00092](#ddd00092) | Error | A permission is a public constant of a [Permissions] class, with a level |
| [DDD00093](#ddd00093) | Error | A permission's level fits what it is asked on |
| [DDD00094](#ddd00094) | Error | The rules of a resource are made from the permissions declared on it |
| [DDD00095](#ddd00095) | Error | A module's permissions are declared in a [Permissions] class, not on a list of its own |
| [DDD00096](#ddd00096) | Warning | [OrOwn] beside a permission the owner of the resource holds already says nothing more |
| [DDD00097](#ddd00097) | Error | [At] marks the one place an aggregate root hangs at |
| [DDD00098](#ddd00098) | Error | Rules that reach a resource from above read where it hangs from its [At] property |
| [DDD00099](#ddd00099) | Warning | A permission says what it lets somebody do |
| [DDD00100](#ddd00100) | Error | A restrictive rule narrows a table it can reach |
| [DDD00101](#ddd00101) | Error | TenancyRowAccess.CallerSeat answers with the application's seat class |
| [DDD00102](#ddd00102) | Error | A marker of a requirement's argument goes beside the attribute whose call it is |
| [DDD00103](#ddd00103) | Error | An operator's command takes its tenant from the request or from itself |
| [DDD00104](#ddd00104) | Error | [Operator] stands beside no requirement an operator cannot meet |
| [DDD00105](#ddd00105) | Warning | [Operator] on a query names no tenant and no scope |

The Meaning column is the title the IDE shows for each id.

Most of these diagnostics say that a generator could not do what a declaration asks. The others are rules about
the model. The code they report compiles, reads well, and does something other than what it seems to do. These
rules, by topic:

- [DDD00021](#ddd00021): the boundary between two aggregates.
- [DDD00022](#ddd00022) and [DDD00023](#ddd00023): the boundary between two [modules](modules.md). They report
  nothing until a project declares itself a module. [DDD00064](#ddd00064) reports a project whose `DDD_Module`
  could not declare a module. [DDD00068](#ddd00068) reports a project whose `DDD_ModuleContracts` could not make it
  its module's contracts.
- [DDD00024](#ddd00024) to [DDD00027](#ddd00027): [invariants](invariants.md). They catch a rule that is written
  and tested and never runs.
- [DDD00028](#ddd00028) to [DDD00030](#ddd00030): [composite keys](composite-keys.md). One more key-part mistake
  shows only when Entity Framework builds the model. The section after DDD00101 describes it.
- [DDD00031](#ddd00031), [DDD00054](#ddd00054), [DDD00055](#ddd00055) and [DDD00069](#ddd00069) to
  [DDD00073](#ddd00073): the [Supabase export](supabase.md). They catch a module's migrations or a package's
  policies that never reach Supabase, that are written from settings the host does not run with, or that are
  written twice. They also catch migration files that a class rename would write a second time.
- [DDD00032](#ddd00032): [Relay node ids](graphql.md#relay-node-ids). It catches a node id that stores nothing.
- [DDD00062](#ddd00062): [a field for one schema only](graphql.md#a-field-for-one-schema-only). It catches an
  administration field that every schema offers.
- [DDD00033](#ddd00033): the [generated integration event registration](integration-events.md#the-generated-registration).
  It catches an outbound class or a handler that is never registered.
- [DDD00034](#ddd00034) to [DDD00037](#ddd00037): [event names](domain-events.md#stable-names). They catch a
  stored row or a message that is read back as the wrong type or in the wrong shape.
- [DDD00038](#ddd00038) to [DDD00041](#ddd00041), [DDD00051](#ddd00051) and [DDD00052](#ddd00052):
  [row access rules](row-level-security.md#row-access-rules-written-in-c). They catch a rule that the database
  checks differently from the C# method, checks more often than needed, or does not check at all.
  [DDD00100](#ddd00100) and [DDD00101](#ddd00101) do the same for rules over Tenancy's tables.
- [DDD00056](#ddd00056) to [DDD00058](#ddd00058) and [DDD00061](#ddd00061): the
  [access behavior written for a request interface](access-requirements.md#the-generated-behavior-with-mediator).
  They catch a message that reaches its handler without a check of what it requires.
- [DDD00077](#ddd00077) to [DDD00086](#ddd00086) and [DDD00089](#ddd00089):
  [what a request declares](access-requirements.md#what-a-request-declares). They catch a request that declares
  nothing, a request whose attributes say something other than what is checked, and a request that says one
  thing twice.
- [DDD00087](#ddd00087) and [DDD00088](#ddd00088): [the owner an aggregate marks](access-requirements.md#the-owner-of-an-aggregate).
  They catch a request about the owner that no row could ever let through.
- [DDD00102](#ddd00102) to [DDD00105](#ddd00105): [an operator's request](access-requirements.md#an-operators-request),
  `[Operator]`. They catch a tenant marker that nothing reads, a command that names two tenants for its work, a
  request that no caller can send, and a query that names work it never begins.
- [DDD00059](#ddd00059) and [DDD00060](#ddd00060): the
  [member list of a resource](membership/resources-and-members.md#the-generated-member-list). They report what
  the Membership generator could not tell, and a member class that names the wrong resource.
- [DDD00092](#ddd00092) to [DDD00095](#ddd00095) and [DDD00099](#ddd00099):
  [permissions declared once](membership/permissions-and-roles.md#the-settings-of-a-permission). They catch a
  permission that is read as something else, that is declared twice, that never reaches the rules or the
  catalogue, or that has no description.
- [DDD00090](#ddd00090) and [DDD00091](#ddd00091): [the names of resources with members](membership/resources-and-members.md#names).
  They catch a code that clients read, or a function name, that changes when a class is renamed.
- [DDD00096](#ddd00096) to [DDD00098](#ddd00098): the owner and the place of a resource with members. They catch
  an attribute that adds nothing, and rules that read a place no property declares.
- [DDD00074](#ddd00074): [the migration history](entity-framework.md#the-migration-history). It catches a
  `dotnet ef database update` or an exported file that records the migrations in a table the application never
  reads.
- [DDD00075](#ddd00075) and [DDD00076](#ddd00076):
  [two modules with Tenancy's classes](tenancy/your-module.md#two-modules-with-tenancys-classes). DDD00075 catches
  a class name that compiles in each module and is two classes in the host. DDD00076 catches a naming line that
  looks as if it names the class and changes nothing.

The numbers follow this split. DDD00001 to DDD00019 are reserved for "the generator could not do what you
asked". DDD00020 and up are rules about the model. These ids are exceptions, numbered later because they were
added later:

- [DDD00042](#ddd00042) to [DDD00050](#ddd00050), [DDD00053](#ddd00053), and [DDD00065](#ddd00065) to
  [DDD00067](#ddd00067), about [supporting domains](writing-a-supporting-domain.md).
- [DDD00052](#ddd00052), about a function's name.
- [DDD00056](#ddd00056) and [DDD00057](#ddd00057), about an access behavior.

Each of these says what the generator could not do.

The severity does not follow the split:

- [DDD00020](#ddd00020) and [DDD00027](#ddd00027) are errors in the second group. In both, the generator drops
  the member instead of emitting something wrong. A warning would leave you with a rule that never runs, and no
  error.
- [DDD00014](#ddd00014) is a warning in the first group. The generator could not read the name the project
  asked for. The code it generates under the assembly's name still compiles.

The table above lists every id. DDD00012 and DDD00015 to DDD00019 were never assigned. DDD00063 was removed when
a module's permissions moved into a `[Permissions]` class. [DDD00095](#ddd00095) now reports a list marked
`[TenancyPermissions]` by hand. Ids are stable and never reused, so an id missing from this page is missing from
the compiler too.

---

## DDD00001

**Value objects must be records.**

```csharp
[ValueObject]
public partial class Address { }        // DDD00001
```

`[ValueObject]` and `[SingleValueObject<T>]` generate structural equality members and an always-valid twin that
derives from your type. Both need a record class:

```csharp
[ValueObject]
public partial record Address { }
```

The generator generates nothing for the type until it is a record. Expect follow-on errors about missing members
until you fix this one.

---

## DDD00002

**Entities must be classes.**

```csharp
[AggregateRoot<OrderId>]
public partial record Order { }         // DDD00002
```

An entity has identity. Two entities are the same entity when their ids are equal. A record gives value equality
over all its properties. For an entity this is wrong, because an order whose status changed is still the same
order. An entity also derives from a generated base class, and only a class can do that. Declare it as a
`partial class`:

```csharp
[AggregateRoot<OrderId>]
public partial class Order { }
```

The generator generates nothing for the type until it is a class.

---

## DDD00003

**Entity ids must be records.**

```csharp
[EntityId<Guid>]
public partial class OrderId { }        // DDD00003
```

An identifier uses record equality. Declare it as a `readonly partial record struct` for an id that allocates
nothing. Declare it as a `partial record` (a record class) when you need inheritance or the always-valid twin:

```csharp
[EntityId<Guid>]
public readonly partial record struct OrderId;

[EntityId<Guid>]
public partial record OrderId;
```

[Identifiers](identifiers.md) explains which one to choose. The generator generates nothing for the type until it
is a record.

---

## DDD00004

**Entity id structs should be readonly.**

```csharp
[EntityId<Guid>]
public partial record struct OrderId;   // DDD00004, generation still happens
```

DDD00004 is a warning. The identifier is generated either way. With `readonly`, the compiler refuses any change to
the id after construction, and it skips the defensive copies it otherwise makes when the struct is passed around.

```csharp
[EntityId<Guid>]
public readonly partial record struct OrderId;
```

---

## DDD00005

**DDDToolkit types must be partial.**

```csharp
[AggregateRoot<OrderId>]
public class Order { }                  // DDD00005
```

The generator adds a second declaration of your type. Two declarations of one type need `partial`. Add the
keyword:

```csharp
[AggregateRoot<OrderId>]
public partial class Order { }
```

The generator generates nothing for the type until it is `partial`.

---

## DDD00006

**DDDToolkit types cannot be generic.**

```csharp
[EntityId<Guid>]
public readonly partial record struct Reference<T>;      // DDD00006

public partial class Repository<T>
{
    [AggregateRoot<OrderId>]
    public partial class Entry { }                        // DDD00006, through its container
}
```

A type nested in a non-generic container is fine and generates normally.

The generated code names your type from places where a type parameter is not in scope:

- A struct identifier carries `[JsonConverter(typeof(Reference<T>.SystemTextJsonConverter))]`. An attribute
  argument cannot name an open generic type.
- The `Add<Module>Converters` and `Add<Module>GraphQlRuntimeBindings` registrations are outside the type. They
  cannot name an open generic type at all.

A type nested inside a generic type has the same problem. The outer type parameter is still in scope, so the
generated code still cannot name the type.

Give the type a concrete identity instead:

```csharp
[EntityId<Guid>]
public readonly partial record struct OrderReference;
```

The generator generates nothing for the type until it has no type parameters.

---

## DDD00007

**The generated identifier name is already taken.**

```csharp
public sealed class OrderId { }         // something else, in the same namespace

[AggregateRoot<Guid>("ORD")]
public partial class Order { }          // DDD00007
```

`[AggregateRoot<Guid>]` and `[Entity<Guid>]` name a raw value, so the toolkit also generates the identifier. It
names the identifier after the entity, with `Id` added: `OrderId`. A second type of that name in the same
namespace or containing type would be a duplicate definition. Without DDD00007, the compiler would report CS0101
against generated code you never wrote.

Point the attribute at the identifier you already have:

```csharp
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;

[AggregateRoot<OrderId>]
public partial class Order { }
```

Or rename the type that should not be called `OrderId`.

One case reports nothing: a `partial record struct` of that name without `[EntityId<T>]`. The generator takes it
as your part of the generated identifier. Use such a part to add members to the identifier:

```csharp
public readonly partial record struct OrderId
{
    public string Short => Value.ToString("N")[..8];
}
```

This part must be `partial`, must be a `record struct`, and must not state an accessibility other than the
entity's. A part that says `internal` where the generated part says `public` would be CS0262, so DDD00007 reports
it instead.

The generator generates nothing for the entity until the name clash is gone. Expect follow-on errors about the
entity's missing base class.

---

## DDD00008

**The identifier type argument is not supported.**

```csharp
public sealed class Money { }

[AggregateRoot<Money>]
public partial class Order { }          // DDD00008
```

The type argument of `[Entity<T>]` and `[AggregateRoot<T>]` is one of two things:

- An identifier you already have: a type with `[EntityId<T>]`, or any type that implements `IEntityId`.
- The raw value that a generated identifier wraps.

A reference type other than `string` is neither. It can be null, and it is copied by reference. An identifier
must never be null and must be copied by value.

```csharp
[AggregateRoot<Guid>("ORD")]            // a value the toolkit can wrap
public partial class Order { }

[AggregateRoot<OrderId>]                // an identifier you declared yourself
public partial class Order { }
```

`Guid`, `int`, `long`, `string`, `DateOnly` and your own structs all work. `Guid?` does not work. For an optional
identifier, make the identifier nullable: `OrderId?`.

The generator generates nothing for the entity until the type argument is one of the two.

---

## DDD00009

**A type is either an entity or an aggregate root.**

```csharp
[Entity<ThingId>]
[AggregateRoot<ThingId>]
public partial class Widget { }         // DDD00009
```

An aggregate root is a consistency boundary. A child entity lives inside one aggregate. A type cannot be both,
and the two attributes generate different base types for the same declaration.

Keep the attribute that describes the type:

- `[AggregateRoot<T>]` for something you load, save and reference from elsewhere.
- `[Entity<T>]` for something that exists only inside one aggregate.

See [Entities and aggregates](entities-and-aggregates.md). The generator generates nothing for the type until one
of the two attributes is removed.

Before DDD00009 existed, both attributes on one class made the generator throw. The generator then added nothing,
and the only error was `CS8785`, a crashed generator, without the cause.

---

## DDD00010

**Value object properties must use protected setters.**

```csharp
[ValueObject]
public partial record Address
{
    public string City { get; init; }   // DDD00010
}
```

A caller can copy a record with a public `init` setter into a state that never passed validation:

```csharp
var invalid = address with { City = "" };
```

A `protected` setter keeps `with` available inside the type and its always-valid twin, and closes it to callers:

```csharp
public string City { get; protected init; }
```

The copy skips validation. On the always-valid twin, `with` produces a `ValidAddress` that holds an invalid value,
even from code that only knows `Address`.
[Why `with` is closed to callers](value-objects.md#why-with-is-closed-to-callers) explains the mechanics, and why
the toolkit cannot check the copy at the `with`.

A code fix makes the change: `protected init`, or `private protected init` on an `internal` property. A caller
that needs a changed copy uses the generated [`With(...)`](value-objects.md#changing-a-value-with) instead of
`with`.

A positional record does not report DDD00010. Its parameters would become `public init` properties, so the
generator declares them itself as `protected init` ([Positional records](value-objects.md#positional-records)).

---

## DDD00011

**Value object properties must use init setters.**

```csharp
[ValueObject]
public partial record Address
{
    public string City { get; protected set; }   // DDD00011
}
```

A setter without `init` lets every derived type change the value after construction. Value objects are
immutable, so the setter must be `init`:

```csharp
public string City { get; protected init; }
```

DDD00010 and DDD00011 are separate rules. A property with a plain `public set` reports both. `protected init`
fixes both, and the code fix under [DDD00010](#ddd00010) applies it.

---

## DDD00013

**Value objects cannot be sealed.**

```csharp
[SingleValueObject<string>]
public sealed partial record EmailAddress { }   // DDD00013
```

The generator emits `ValidEmailAddress`, which derives from `EmailAddress`. No type can derive from a sealed
record, so the twin cannot exist. Remove `sealed`.

A record struct never reports DDD00013. A struct is sealed implicitly, and it gets no twin, because no type can
derive from a struct.

---

## DDD00014

**The generators cannot read the project's MSBuild properties.**

```xml
<PropertyGroup>
  <DDD_Module>Billing</DDD_Module>   <!-- ignored: the generators never see it -->
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Temp.DDDToolkit.Analyzers" Version="3.1.0"
                    PrivateAssets="all" ExcludeAssets="build;buildTransitive" />   <!-- DDD00014 -->
</ItemGroup>
```

A source generator sees only the MSBuild properties that the project lists as `CompilerVisibleProperty`. The
`DDDToolkit.Analyzers` package lists the properties the toolkit's generators read, in a props file. NuGet imports
that file into every project the generators arrive in. Here the generators arrived and the props file did not.
The generators then ignore `DDD_Module`, whatever the project sets. They name `{Module}EventNames`,
`Add{Module}Converters`, `Add{Module}IntegrationEvents` and `Add{Module}GraphQlRuntimeBindings` after the
assembly. Before DDD00014 existed, this happened without any message.

The generator tells the two cases apart:

- A property that is listed and not set reaches the generator as an empty value. An empty value means "use the
  assembly name". It is never reported.
- A property that is not listed does not reach the generator at all. DDD00014 reports this case.

It happens in two ways. The fix for each:

- **The reference leaves out the package's build assets**, with `ExcludeAssets`, or with an `IncludeAssets` that
  names `analyzers` and not `build` and `buildTransitive`. Remove the restriction. `PrivateAssets="all"` is fine.
  It decides what flows on to projects that reference yours, and does not limit what your project gets.
- **The generator is referenced as an assembly** instead of a package: an `<Analyzer Include="...dll" />`, or a
  project reference with `OutputItemType="Analyzer"`. Then nothing imports a props file. List the property
  yourself, in the project or in `Directory.Build.props`:

  ```xml
  <ItemGroup>
    <CompilerVisibleProperty Include="DDD_Module" />
  </ItemGroup>
  ```

  Such a project also gets no build step from the package. `DDD_Module` then names the project's generated code
  and declares no module, and [DDD00064](#ddd00064) reports that. Import the package's targets file as well, or
  declare the module with `[assembly: Module]`.

DDD00014 is reported once per project, without a line, because no line of your code is wrong. An assembly that
declares `[assembly: Module]` is not reported. The module names its generated code, and the generator does not
read the property. See [DDD_Module, and the package that brings it](modules.md#the-module-name-in-generated-code).

---

## DDD00020

**Generated collection properties must be get-only.**

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    public partial IReadOnlyList<OrderLine> Lines { get; set; }   // DDD00020
}
```

The generator implements the property as a read-only view over a private backing field. A setter would let a
caller replace the whole collection and skip the aggregate's invariants. The read-only view exists to prevent
that. Remove the setter:

```csharp
public partial IReadOnlyList<OrderLine> Lines { get; }
```

Change the collection through the generated field, from inside the entity:

```csharp
public void AddLine(OrderLine line) => _lines.Add(line);
```

The generator does not implement the property until it is get-only, so DDD00020 is an error. See
[Entities and aggregates](entities-and-aggregates.md#read-only-collections).

---

## DDD00021

**Reference another aggregate by its id.**

```csharp
[AggregateRoot<CustomerId>]
public partial class Customer { }

[AggregateRoot<OrderId>]
public partial class Order
{
    public Customer Buyer { get; private set; }              // DDD00021
    public IReadOnlyList<Customer> Watchers { get; }         // DDD00021
}
```

Hold the other aggregate's id instead:

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    public CustomerId Buyer { get; private set; }

    public void PlaceFor(Customer customer) => Buyer = customer.Id;
}
```

Each aggregate is a separate boundary for loading and for consistency. A field or property typed as another
root pulls that root inside this one:

- Entity Framework builds a navigation from it.
- One save then writes two roots.
- The `Version` of each root no longer guards its own aggregate.

[Entities and aggregates](entities-and-aggregates.md#reference-other-aggregates-by-id) explains this in more
detail.

DDD00021 is a warning. The code compiles, and everything is still generated. A team that disagrees can turn the
rule off for a project:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);DDD00021</NoWarn>
</PropertyGroup>
```

`#pragma warning disable DDD00021` around one property does **not** work, and `[SuppressMessage]` does not work
either. This is a limit of source generators. A generator reports a diagnostic with a location that it rebuilds
from a file path, without a syntax tree. This keeps the generator's pipeline cacheable. The compiler then has no
tree to match a pragma against. `NoWarn` is read from the compilation options, so it works. The same holds for
every DDD diagnostic that a generator reports. DDD00021 is the one you are most likely to turn off.

### What reports and what does not

| | Reports |
|---|---|
| A field or property typed as another root | Yes |
| A collection, array or dictionary holding another root | Yes |
| A `Customer?` property | Yes |
| A root holding another instance of its own type, such as `Employee.Manager` | Yes |
| A child `[Entity<T>]` of the same aggregate | No |
| A child entity navigating back to the root that owns it | No |
| A property typed as the other aggregate's id | No |
| A method parameter or return type | No |
| A `static` member | No |

Two of these rows need more detail.

**The back-navigation.** A child entity may hold the root that owns it. This is the inverse navigation that
Entity Framework needs:

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    public partial IReadOnlyList<OrderLine> Lines { get; }
}

[Entity<OrderLineId>]
public partial class OrderLine
{
    public Order Order { get; private set; }      // allowed
}
```

The rule checks this exception. `Order` must hold `OrderLine`, through a collection or a single property, and the
reference from the child must be a single value. A child entity that points at another root still reports,
because that reference widens the boundary.

**Methods.** `order.PlaceFor(customer)` takes the other root, reads what it needs and stores nothing. This is how
two aggregates work together. Entity Framework does not see methods, so a method creates no navigation. The rule
is about what an aggregate *holds*.

The rule reads the declared type of a member. It does not recognise a property typed as an interface that an
aggregate root implements, or a property typed as `object`.

---

## DDD00022

**Use only what another module publishes.**

```csharp
// Crm.csproj: <DDD_Module>Crm</DDD_Module>
namespace Crm;

[AggregateRoot<CustomerId>]
public partial class Customer { }

// Sales.csproj: <DDD_Module>Sales</DDD_Module>
namespace Sales;

public sealed class OrderReport
{
    public string Describe(Customer customer) => customer.Name;   // DDD00022
}
```

Publish the type in the module that owns it:

```csharp
namespace Crm;

[ModuleContract]
public sealed record CustomerSummary(CustomerId Id, string Name);
```

Or put the type in Crm's contracts project, if it belongs there. A project that declares itself its module's
contracts, with `<DDD_ModuleContracts>true</DDD_ModuleContracts>` or `[assembly: ModuleContracts]`, publishes every
public type it declares, without a mark on each ([A contracts project](modules.md#a-contracts-project)). Or use
something the module publishes already. For an aggregate, that is usually its id and its integration events:

```csharp
namespace Sales;

public sealed class OrderReport
{
    public string Describe(CustomerSummary customer) => customer.Name;
}
```

A module is an assembly that declares a module. It declares it with `DDD_Module` in its project file, which the
build turns into `[assembly: Module("Name")]`, or with that attribute in its source. A module's published contract
is:

- every type marked `[ModuleContract]`;
- every type marked `[IntegrationEvent]`;
- every type nested inside one of those;
- every public type of a project of the module that declares itself the module's contracts.

Everything else the assembly declares belongs to the team that owns the module, `public` or not. A project is
never the module's contracts because of its name. `Legal.Contracts` may be a domain about legal contracts. Only
the property or the attribute makes a project the contracts project. An `internal` type of a contracts project is
not published, even to a project that its `InternalsVisibleTo` lets in.

The rule reports nothing unless both assemblies declare a module. So it reports nothing in a codebase that has not
opted in, and nothing against the framework, a NuGet package or a shared kernel. Two assemblies that declare the
same module name are one module, with no boundary between them. If the rule reports the types of a shared kernel,
the kernel sets `DDD_Module` to name its generated code, and the build declared it a module because of that. Set
`<DDD_DeclareModule>false</DDD_DeclareModule>` beside it, or remove `DDD_Module`
([A module named by its folder](modules.md#a-module-named-by-its-folder)).

The rule reports where you *name* another module's unpublished type: a parameter, a field, a base type, a generic
argument, an attribute, a `typeof`, a `new`, a static call, a `using` alias. It does not see a type you never name
(`var`), an extension method called on an instance, an inherited member, or anything resolved by reflection.
[Modules](modules.md#what-the-analyzer-cannot-catch) has the full list. Read it before you rely on the rule.

DDD00022 is a warning, for the same reason as DDD00021. It states a design decision, and a team that adopts
modules wants the list of findings before it has to fix them. DDD00022 comes from an analyzer, so
`#pragma warning disable DDD00022`, `[SuppressMessage]` and `NoWarn` all work.
`<WarningsAsErrors>$(WarningsAsErrors);DDD00022</WarningsAsErrors>` turns it into a build error once the list is
empty.

---

## DDD00023

**Do not hold another module's entity.**

```csharp
// Sales
[AggregateRoot<Guid>("ORD")]
public partial class Order
{
    public Customer Buyer { get; private set; }   // DDD00023, Customer belongs to Crm
}
```

Hold the other module's published id, and react to the events the other module publishes:

```csharp
[AggregateRoot<Guid>("ORD")]
public partial class Order
{
    public CustomerId Buyer { get; private set; }

    public void PlaceFor(CustomerId customer) => Buyer = customer;
}
```

A property typed as another module's entity is a navigation:

- Entity Framework maps it.
- A query in one module loads rows that the other module owns.
- One `SaveChanges` writes into both modules in one transaction.

The two modules can then no longer be tested, migrated or separated on their own, and the code shows no sign of
it.

Publishing the entity does not fix this, and does not silence DDD00023. Declaring the entity in the other
module's contracts project does not either. `[ModuleContract]` and `[assembly: ModuleContracts]` let you name a
type. They do not let you make that type part of your own transaction.

### What reports and what does not

| | Reports |
|---|---|
| A field or property typed as another module's `[Entity<T>]` or `[AggregateRoot<T>]` | Yes |
| A collection, array or dictionary holding one | Yes |
| One that the other module publishes with `[ModuleContract]` | Yes |
| One in the other module's contracts project, `DDD_ModuleContracts` or `[assembly: ModuleContracts]` | Yes |
| A property typed as the other module's id | No |
| An entity of the same module | No |
| An entity of an assembly that declares no module | No |
| A method parameter or return type | No |
| A `static` member | No |
| The generated backing field of a collection property | No, the property is reported instead |

Like DDD00022, DDD00023 is a warning from an analyzer. Pragmas, `[SuppressMessage]`, `NoWarn` and
`WarningsAsErrors` all work on it.

A property that holds another module's *aggregate root* also reports [DDD00021](#ddd00021). DDD00021 asks for the
id, and DDD00023 forbids the reference across the module boundary. The same edit fixes both. See
[Modules](modules.md) for more detail.

---

## DDD00024

**An invariant must be nested inside the entity it is about.**

```csharp
// Ordering/MustHaveLines.cs
public sealed class MustHaveLines : IInvariant<Order>      // DDD00024
{
    public string Code => "ORDER_HAS_NO_LINES";

    public InvariantFailure? Check(Order order) => order.Lines.Count == 0 ? "..." : null;
}
```

Nest it inside the entity it checks, in a part of your own:

```csharp
// Ordering/Invariants/MustHaveLines.cs
public partial class Order
{
    public sealed class MustHaveLines : IInvariant<Order>
    {
        // the same body
    }
}
```

An entity runs the rules it finds among its own nested types. This lets a rule read the entity's private state,
and the generator finds the rules without a scan over the whole compilation. A rule declared anywhere else
compiles, reads well and passes the unit test you wrote for it. It never runs on a save. DDD00024 reports such a
rule, because otherwise nothing would.

DDD00024 is the only invariant diagnostic that comes from an analyzer. The generator reports the other three. A
type outside an entity is not among any entity's nested types, so only a pass over the whole compilation can find
it. As a result, `#pragma warning disable DDD00024`, `[SuppressMessage]`, `NoWarn` and `WarningsAsErrors` all work
on DDD00024. A pragma and `[SuppressMessage]` do not work on the other three.

DDD00024 is a warning. An author who means to hand the rule to other code of their own can suppress it and move
on.

### What reports and what does not

| | Reports |
|---|---|
| A top-level type implementing `IInvariant<T>` | Yes |
| One nested inside a class that is not an `[Entity<T>]` or `[AggregateRoot<T>]` | Yes |
| One that takes its `Check` from a base class | Yes, the concrete type is the rule |
| An `abstract` base shared between several rules | No, it is not a rule |
| An interface deriving from `IInvariant<T>` | No |
| An open generic rule, such as `Rule<T>` | No |
| One nested inside the wrong entity | No, that is [DDD00025](#ddd00025) |

See [Invariants](invariants.md#why-a-rule-is-a-nested-type).

---

## DDD00025

**An invariant is nested inside a type it is not about.**

```csharp
public partial class Order
{
    public sealed class LineMustCostSomething : IInvariant<OrderLine>   // DDD00025
    {
        public string Code => "LINE_IS_FREE";

        public InvariantFailure? Check(OrderLine line) => line.Price <= 0 ? "..." : null;
    }
}
```

Nest it inside the type it is about:

```csharp
public partial class OrderLine
{
    public sealed class MustCostSomething : IInvariant<OrderLine> { }
}
```

An entity runs only the nested rules that are about *itself*. This rule is in the right kind of place and still
never runs, so it is harder to spot than [DDD00024](#ddd00024). Usually the type argument was copied from a rule
next to it.

A rule about a base class or an interface of the entity is accepted, and nothing reports it. `IInvariant<T>` is
contravariant in `T`, so an `IInvariant<IHasCustomer>` nested inside `Order` runs on an `Order`. Only a type
argument that the entity cannot be passed as is reported.

The generator reports DDD00025, so `NoWarn` works on it and a pragma does not. The same holds for DDD00026 and
DDD00027. [DDD00021](#ddd00021) explains why.

---

## DDD00026

**Two invariants of one entity share a code.**

```csharp
public partial class Order
{
    public sealed class MustHaveLines : IInvariant<Order>
    {
        public string Code => "ORDER_INVALID";
    }

    public sealed class MustStayWithinTheCreditLimit : IInvariant<Order>
    {
        public string Code => "ORDER_INVALID";      // DDD00026
    }
}
```

The code lets a caller act on a broken rule without matching on its message. A caller can branch on a code. A
caller can only display a message. Two rules with one code take this away. `GetInvariantViolations()` returns two
entries that the caller cannot tell apart, and a branch meant for one rule catches both.

Both rules are still generated, and both still run. The project still compiles. Only the caller loses the
ability to tell the two rules apart.

Only the second rule is reported. The generator compares only codes it can read as a constant:

| `Code` written as | Compared |
|---|---|
| `=> "ORDER_INVALID"` | Yes |
| `{ get; } = "ORDER_INVALID"` | Yes |
| `=> ViolationCode`, a `const` | Yes, the constant's value |
| `=> $"ORDER_{Reason}"` or anything computed | No |
| Inherited from a base class as a constant | Yes |

The generator skips a code that is built at run time. A guess could report two rules as sharing a code that they
do not share.

---

## DDD00027

**An invariant needs an accessible parameterless constructor.**

```csharp
public partial class Order
{
    public sealed class MustStayWithinTheCreditLimit : IInvariant<Order>
    {
        // DDD00027, the generated code inside Order cannot write new MustStayWithinTheCreditLimit()
        public MustStayWithinTheCreditLimit(decimal limit) => _limit = limit;
    }
}
```

The generator creates one instance of every rule per entity type and reuses it for every check. So a rule must
have a constructor without parameters, and it must be stateless for the reuse to be safe. In the example, the
limit belongs to the entity. The rule reads it from the `Order` it is given:

```csharp
public InvariantFailure? Check(Order order)
    => order.Total > order.CreditLimit ? $"An order may not exceed {order.CreditLimit}." : null;
```

DDD00027 is an error. The generator generates nothing for a rule that the generated code cannot construct. As a
warning, DDD00027 would let that rule disappear with no error, and the other three invariant diagnostics exist to
prevent a rule that never runs. The entity itself is still generated, and the rest of it works. You get this one
error and no follow-on errors.

The generator asks the compiler whether the constructor is accessible. It does not read the modifiers, because
here the two differ. A rule nested inside the entity may be `private`, and the generated code still reaches it,
because the generated code is written inside the same entity. A `private` constructor on the rule is out of reach.

| | Reports |
|---|---|
| A constructor taking parameters, and no other | Yes |
| A `private` constructor on a rule nested in the entity | Yes |
| No constructor at all, so the implicit public one | No |
| A `private` rule with an accessible constructor | No |
| An `abstract` base or an open generic rule | No, neither is a rule |

See [Invariants](invariants.md#why-a-rule-is-a-nested-type).

---

## DDD00028

**A key part belongs on an entity or aggregate root.**

```csharp
[ValueObject]
public partial record Address
{
    [KeyPart]
    public RegionId Region { get; protected init; }   // DDD00028
}
```

`[KeyPart]` puts a property into the primary key, ahead of the identifier. Only an `[AggregateRoot<T>]` or an
`[Entity<T>]` has an identifier and a primary key. On any other type the attribute would do nothing, and nothing
would say so. Remove it, or move the property to the entity that is keyed on it. See
[Composite keys](composite-keys.md).

---

## DDD00029

**A key part should not have a public setter.**

```csharp
[AggregateRoot<ProjectId>]
public partial class Project
{
    [KeyPart]
    public RegionId RegionId { get; set; }   // DDD00029
}
```

Set it once, in the constructor, and make it get-only:

```csharp
[KeyPart]
public RegionId RegionId { get; }
```

A key part is part of the primary key. A primary key does not change once the row exists. Entity Framework
refuses to save a changed key value, and the foreign key of every owned child carries the same value. `{ get; }`,
`private set`, `protected set` and `init` are all fine. Only a public setter without `init` reports. DDD00029 is a
warning, and everything is still generated.

---

## DDD00030

**Declare all key parts of a type in one file.**

```csharp
// Project.cs
[AggregateRoot<ProjectId>]
public partial class Project
{
    [KeyPart] public RegionId RegionId { get; }
}

// Project.Period.cs
public partial class Project
{
    [KeyPart] public int Period { get; }       // DDD00030, reported on Project
}
```

Move them into one part of the class, in the order you want in the key:

```csharp
public partial class Project
{
    [KeyPart] public RegionId RegionId { get; }
    [KeyPart] public int Period { get; }
}
```

Key parts join the primary key in the order they are declared. Within one file, that order is clear. Between the
files of a partial class, there is no declaration order, only the order in which the compiler reads the files. A
file rename could then change the column order of the key. The generator generates nothing for the type until its
key parts are in one file.

---

## DDD00031

**A [SupabaseMigrations] context or factory must be one the build can make.**

DDD00031 is reported in two projects:

- In the project of a context marked `[SupabaseMigrations]`, about a context that the build cannot write a
  design-time factory for.
- In the project that turns the Supabase export on (`<SupabaseMigrationsExport>`), normally the host, about a
  context or a factory in a module it references.

```csharp
[SupabaseMigrations]
public sealed class OrderingContext(DbContextOptions<OrderingContext> options, string schema) : DbContext(options)  // DDD00031: no constructor that takes its options alone
```

For a marked context, the build writes a design-time factory beside it, `OrderingContextDesignTimeFactory`. That
factory makes the context with `new OrderingContext(options)` on Npgsql. The build can write the factory when:

- the context is neither abstract nor generic, and not private inside another type;
- the context has a constructor, not private or protected, that takes its options alone:
  `DbContextOptions<OrderingContext>` or `DbContextOptions`, followed only by optional or `params` parameters;
- that constructor is marked `[SetsRequiredMembers]` if the context has `required` members;
- the project references `Npgsql.EntityFrameworkCore.PostgreSQL`.

If the build cannot write a factory, write a factory of your own beside the context. The build then writes none,
uses yours, and reports nothing.

The project that exports also reports:

- A marked context whose assembly has no factory for it.
- A marked context with more than one factory, in its assembly or in the exporting project. The export cannot
  tell which factory to make the context with. Mark the factory to use `[SupabaseMigrations]` instead of the
  context.
- A marked context that the exporting project cannot see.
- A module's own factory that the exporting project cannot create: a factory that is not public, or that has no
  public parameterless constructor.

A factory may be marked instead of its context, as in versions up to 3.1. The project that exports reports it:

```csharp
[SupabaseMigrations]
internal sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>  // DDD00031: the host cannot see it
```

The build creates every marked factory from code that it generates into the host, as
`SupabaseMigrationSource.For<TContext, TFactory>()`. For this, the factory must be a public, non-abstract,
non-generic class with a public parameterless constructor. It must implement `IDesignTimeDbContextFactory<TContext>`
for a context that the host can also see:

```csharp
[SupabaseMigrations]
public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
{
    public OrderingContext CreateDbContext(string[] args) { ... }
}
```

The message names what is missing. The export leaves out a context or a factory that fails these checks, so
DDD00031 is an error. Without it, the first sign of the missing migrations would be a failed deployment, or a
branch database without the module's tables.

## DDD00032

**Do not ask HotChocolate's generator for a toolkit identifier's node id serializer.**

```csharp
graphql.AddNodeIdValueSerializerFrom<OrderId>();   // DDD00032
```

HotChocolate's own generator intercepts `AddNodeIdValueSerializerFrom<T>()`. It writes a serializer from the
properties that `T` declares in source. The toolkit's generator writes a toolkit identifier's `Value`, and source
generators do not see each other's output. So HotChocolate's generator finds no property and writes a serializer
that stores nothing. Every order's node id becomes `Order:`, and every node id reads back as an empty `OrderId`.
The code compiles, and nothing throws.

Remove the call. `Add{Module}GraphQlRuntimeBindings()` already registers a serializer, in HotChocolate's own
format, for every identifier over a `Guid`, `string`, `int`, `long` or `short`
([Relay node ids](graphql.md#relay-node-ids)).

---

## DDD00033

**The generated integration event registration must be able to construct the class.**

```csharp
public sealed class PublishOrderPlaced : IOutboundIntegrationEvent<OrderPlaced, OrderPlacedV2>
{
    private PublishOrderPlaced() { }   // DDD00033: no constructor this assembly can call
    // ...
}
```

The generated `Add{Module}IntegrationEvents()` registers every outbound class and every handler of the module. It
constructs each one with `new`, and takes each constructor parameter from the scope that the message is delivered
in. For this, a class needs:

- one accessible constructor with the most parameters;
- no `ref`, `out` or `params` parameters on that constructor;
- parameter types that the assembly can see.

The message says which of these failed.

The registration leaves out a class that it cannot construct. The domain event of a left-out outbound class is
never published. The contract of a left-out handler is never handled. Nothing else would tell you, so DDD00033 is
a warning. Give the class a constructor that the registration can call, or register the class by hand.

The registration also names the domain events of the module's projects that do not reference Entity Framework.
An example is a domain project next to the infrastructure project that holds the outbox. A domain event that this
project cannot see, such as an `internal` event, is left out in the same way. DDD00033 reports it on the
project's `[assembly: Module]` attribute. If no file of the project that somebody edits declares the module,
DDD00033 is reported at the project file instead. In that case the build declared the module from `DDD_Module`, or
an `<AssemblyAttribute>` item wrote the attribute into `obj/`
([A module named by its folder](modules.md#a-module-named-by-its-folder)). Set the severity there with `<NoWarn>`,
`<WarningsAsErrors>` or a `.globalconfig`. A `[*.cs]` section of `.editorconfig` does not reach the project file.

```
'Sales.Domain.OrderAudited' is left out of the generated integration event registration: a domain event of
Sales.Domain, a project of this module, that this project cannot see; make it public
```

Make the event public. A left-out event is still stored when it is raised, under the same name, and it is never
delivered. The processor records the missing registration on the event's row at every attempt
([Registering event types](event-delivery.md#registering-event-types)).

---

## DDD00034

**An event's class name and its Version disagree.**

```csharp
[IntegrationEvent(Version = 3)]                     // DDD00034, at Version = 3
public sealed record OrderPlacedV2(OrderId OrderId);
```

By convention, a class name that ends in `V` and a number is that version of its event. `Version` on
`[IntegrationEvent]` states a version explicitly. The stated version wins wherever the version is read: the
outbox, the published message, the generated registration. So this event is version 3, and the toolkit ignores
the `V2` in its name. The version is clear, so DDD00034 is a warning. A name that says 2 on an event that is
version 3 leads someone to write an upcaster for the wrong version.

Two code fixes make the name and the version agree:

- **Rename the class** to the version it is, `OrderPlacedV3`, everywhere it is used. The event does not change.
  This fix is offered first. It is not offered when the namespace or the containing type already has a type of
  the new name.
- **Remove `Version = 3`.** The event then becomes version 2, as its name says. Use this fix when the name was
  right and the attribute was wrong.

See [Versions are in the class name](domain-events.md#the-version-in-the-class-name).

---

## DDD00035

**An event's class name ends in something that is not a version.**

```csharp
public sealed record OrderPlacedV0(OrderId OrderId) : DomainEvent;    // DDD00035
public sealed record OrderPlacedV01(OrderId OrderId) : DomainEvent;   // DDD00035
```

The toolkit reads every event's class name for a version suffix. Versions start at 1 and have no leading zeros, so
`V0` and `V01` cannot be versions. Reading them as part of the name would make this event the only one whose
suffix is not its version. Rename the class: `V1` for a first version, or a name that does not end in `V` and
digits. Digits that do not follow a `V`, as in `Level2Reached`, are part of the name and are fine.

---

## DDD00036

**Two events of one module share a name and version.**

```csharp
[assembly: Module("Ordering")]

namespace Ordering.Orders  { public sealed record OrderPlaced(OrderId OrderId) : DomainEvent; }   // DDD00036
namespace Ordering.Returns { public sealed record OrderPlaced(OrderId OrderId) : DomainEvent; }   // DDD00036
```

When a stored row or a delivered message is read back, the toolkit finds its event by name and version. Both
events above are `ordering.order-placed` version 1. The registry would refuse the second event at start-up.
DDD00036 refuses it when the module compiles. It is reported on both classes, because neither is more wrong than
the other.

The code fix pins another name on the class you run it on. It makes the name from the class's namespace or
containing type: `[DomainEventName("ordering.returns-order-placed")]` on a domain event, or the name in
`[IntegrationEvent("...")]` on a contract. A class whose name is pinned already gets no code fix, because
somebody chose that name by hand. Renaming one of the classes also fixes the error.

The toolkit compares domain events with domain events, and contracts with contracts. A domain event and the
contract it is published as share a name on purpose. The versions of one event also share a name. Neither case is
reported. The toolkit does not make a name unique from the namespace by itself, because that name would change
when the class moved. See [Two events with one name](domain-events.md#two-events-with-one-name).

---

## DDD00037

**Two event names give one constant name.**

```csharp
[DomainEventName("ordering.order-placed")]
public sealed record OrderPlaced(OrderId OrderId) : DomainEvent;

[DomainEventName("ordering.order.placed")]                           // DDD00037, on both
public sealed record PlacedOrder(OrderId OrderId) : DomainEvent;
```

Every event name gets a constant in the generated `{Module}EventNames` class. The constant is named after the
event name without its module, in PascalCase. Both names above give `OrderingEventNames.OrderPlaced`. Whichever
name the constant held, code that used it for the other event would bind a topic or a test to the wrong event,
and nothing would show it. So DDD00037 is an error, on the events of both names. Pin one of the names to
something that reads differently. Until then, the first name in ordinal order keeps the constant. This only stops
code that uses the constant from adding errors of its own.

## DDD00038

**A row access rule, an access function or a database question has the shape the generator reads.**

```csharp
[RowAccess<Order>(RowOperations.Read)]
public static class ACustomerSeesTheirOrders                      // DDD00038: not partial
{
    public static bool Allows(Order order) => order.PlacedBy == null;   // DDD00038: no Caller
}
```

The generator writes the rule's SQL into another part of the class, so the class is `static partial`. The
generator translates `Allows`. `Allows` is a static method that takes the aggregate the rule is about and a
`Caller`, returns `bool`, and has a single expression for a body: after `=>`, or as its only `return` statement.
The generator generates nothing until the rule has this shape. A rule without SQL never reaches the database.

The same holds for the other classes the generator reads, with more requirements:

- **An `[AccessFunction]`** has a rule's shape. After the caller it may take more parameters: `string`, `bool`,
  `int`, `long`, `Guid` or an id. Each one becomes a parameter of the SQL function, after the row's primary key. A
  `decimal`, a list or a nullable value type is not allowed. With `Shape = AccessFunctionShape.Set`, the
  aggregate's primary key must be one column, because the function answers with the primary keys of the rows it
  allows.
- **An `[AccessFunctionContract]`** declares nothing, or one question for the generator to implement:
  `static partial bool Allows(TKey key, ...)`, or `static partial AccessSet<TKey> Ids(...)`. `Ids` also needs
  `Shape = AccessFunctionShape.Set` if the attribute names a shape.
- **A `[ResourceAccessContract]`** is keyed by the id of the resource's aggregate, such as `ProjectId`, because
  the export finds the resource by the type of its id. It publishes `ResourceAccessSet.Seen` or `HeldOn`. The id
  is one of these:
  - an id declared with `[EntityId<T>]`;
  - the id that the toolkit writes beside an aggregate root of the same project declared with a value,
    `[AggregateRoot<Guid>]`;
  - an id that a package's switch writes into the project, `[assembly: GenerateTenancyIds]`.

  A name that the generator finds as none of these is reported, with the line that declares it. The contract is
  declared in the module that owns the id, because a module publishes the sets of its own resources. A contract
  for an id whose assembly declares another module is reported. The other module's rules ask the contract that
  the owner publishes. The contract declares nothing, or the `Ids` its set is asked with, for the generator to
  implement: `static partial AccessSet<ProjectId> Ids()` for the resources seen, and
  `static partial AccessSet<ProjectId> Ids(string permission)` for the resources a permission is held on
  ([a resource's access, asked by its id](row-level-security.md#resource-access-contracts)).
- **A question of an `[AccessFunctions]` class** is a `static partial` method without a body, in a
  `static partial` class. An `[AccessSet]` question returns `AccessSet<T>`, and an `[AccessScalar]` question
  returns a value. Its parameters are the types above, or a type parameter constrained to `IEntityId`. No
  generator writes the body of an `[AccessSet]` or `[AccessScalar]` method in a class without `[AccessFunctions]`,
  so such a method is reported.
- **A function's name** is `schema.name`, `owner/name`, or a name relative to its owner. Each part has letters,
  digits and underscores. The owner has lower-case letters, digits and dashes. A relative name without an owner
  is [DDD00052](#ddd00052).
- **A [column rule](row-level-security.md#column-rules)**, a rule with `Columns`, is for `RowOperations.Change`
  alone. Reading, adding and removing are about whole rows. Each name in `Columns` is one of these:
  - a property of the aggregate, of its parent, or of its template's parent;
  - a property of a value object the aggregate holds, written with a dot: `"Planned.From"`.

  A name in `Columns` cannot be a collection of the aggregate's entities. Their rows are in a table of their own
  and follow the rules for the aggregate's row. A collection of values, such as a list of strings stored as an
  array, is one column and may be named as a whole. DDD00038 is reported on the `Columns` argument. Only the
  export knows whether the model stores the property in a column of the aggregate's table. The export refuses a
  property that is not stored there, and names the rule.
- **A function that a column rule calls with `Sql.Call`** names its schema: `"public.is_agent"`, or
  `"pg_catalog.lower"` for a built-in Postgres function. A column rule is checked in a trigger whose search path
  is empty. There, a name without a schema finds only Postgres's built-in functions. DDD00038 is reported on the
  name.

```csharp
[RowAccess<Project>(RowOperations.Read | RowOperations.Change, Columns = [nameof(Project.State)])]   // DDD00038: Change alone
[RowAccess<Project>(RowOperations.Change, Columns = ["Stat", nameof(Project.Crew)])]                  // DDD00038: no property, a collection of entities
```

## DDD00039

**A row access rule can only say what the database can check.**

```csharp
public static bool Allows(Order order, Caller caller)
    => order.Team!.StartsWith("north");                           // DDD00039, on the call
```

The generator turns a rule into a condition that the database checks for every row. So a rule can use only these:

- the aggregate's own properties;
- constants written in the rule;
- the caller: `caller.UserId`, `caller.IsSignedIn`, `caller.Role` and `caller.Claim("app_metadata.team")`;
- the time, as `DateTimeOffset.UtcNow` or `DateTime.UtcNow`, which the database answers as `now()`;
- SQL written with `Sql.Call` or `Sql.Raw`.

A rule compares these with `==`, `!=`, `<`, `<=`, `>` and `>=`, and combines the comparisons with `&&`, `||` and
`!`. A rule asks what only the database knows through an
[access function](row-level-security.md#access-functions), a
[question](row-level-security.md#set-shaped-access-functions) of an `[AccessFunctions]` class, or a
[resource access contract](row-level-security.md#resource-access-contracts). It asks a set-shaped
question only with `Contains`.

A method call, a local variable or another object has no column and no claim to become. If the generator left it
out, the database would answer differently from the C# method. So DDD00039 is an error, on the part that cannot
be translated. Store what the rule needs as a property of the aggregate, or put it in the caller's
`app_metadata`.

## DDD00040

**A row access rule guards an aggregate root.**

```csharp
[RowAccess<OrderLine>(RowOperations.Read)]                       // DDD00040
public static partial class LinesOfBigOrders { ... }
```

An aggregate is read and changed as a whole. A rule on one of its entities could hide some of an order's lines
and not the order. Entity Framework would then load half an aggregate, and the aggregate's invariants would check
half the data. Write the rule for the root. The export gives the tables of the aggregate's entities a policy that
follows the root. A line is visible when its order is visible, and hidden when its order is hidden.

A rule about a supporting domain's parent, `[RowAccess<SubscriptionAggregate<SubscriptionId>>]`, also reports
DDD00040. The parent is abstract and has no table. Write the rule for the application's class declared with the
parent's template. That class reads the parent's properties like its own.

---

## DDD00041

**A row access rule reads the aggregate's entities through an access function.**

```csharp
[RowAccess<Project>(RowOperations.Read)]
public static partial class MembersSeeTheirProjects
{
    public static bool Allows(Project project, Caller caller)
        => project.Members.Any(member => member.UserId == caller.UserId);   // DDD00041
}
```

The tables of an aggregate's entities have policies that ask the aggregate's table whether their row is visible.
A policy on the aggregate's table that read those tables would ask itself. Postgres stops such a query with
infinite recursion. Put the question in an
[access function](row-level-security.md#access-functions). An access function runs
as its owner (`SECURITY DEFINER`), so it reads the entities without their policies. Call it from the rule:

```csharp
[AccessFunction<Project>("projects.is_member")]
public static partial class ProjectMembership
{
    public static bool Allows(Project project, Caller caller)
        => project.Members.Any(member => member.UserId == caller.UserId);
}

[RowAccess<Project>(RowOperations.Read)]
public static partial class MembersSeeTheirProjects
{
    public static bool Allows(Project project, Caller caller) => ProjectMembership.Allows(project, caller);
}
```

The rule calls the function with the row's id. The SQL function is written once, however many rules call it.

---

## DDD00042

**A parent for entities is an abstract generic class whose first type parameter is the id.**

```csharp
[AggregateRootBase]
public partial class SubscriptionAggregate<TSubscriptionId> { ... }   // DDD00042: not abstract
```

A package ships a parent class that the application's own classes derive from. The generator writes the parent's
base class as it does for any aggregate root or entity: `AggregateRoot<TId>` or `Entity<TId>`, closed over the
parent's first type parameter. So the parent must have this shape:

- It is `abstract`, because only the classes that derive from it are ever created.
- Its first type parameter is the id.
- It is not nested in a generic type.
- The id parameter has the constraint that the toolkit's base classes require:
  `where TId : IEntityId, IEquatable<TId>`.

Like every entity, the parent is also a `partial class`. [DDD00005](#ddd00005) and [DDD00002](#ddd00002) report
that:

```csharp
[AggregateRootBase]
public abstract partial class SubscriptionAggregate<TSubscriptionId>
    where TSubscriptionId : IEntityId, IEquatable<TSubscriptionId>
{
    public string Plan { get; private set; } = "";
}
```

A parent with more than one problem reports the first one. The next one is reported once the first is fixed. A
class declared with the template of such a parent gets nothing generated either, and no diagnostic of its own.
Fix the parent. The compiler errors in that class, such as a `base(...)` call it cannot make or a parent's
property it cannot find, come from the missing base class. They go away once the parent is fixed.

---

## DDD00043

**A template's first type argument is an entity id.**

```csharp
[Subscription<Guid>]                                                  // DDD00043
public sealed partial class ShopSubscription;
```

A template attribute names the id of the class it declares, and that class derives from a parent closed over the
id. Unlike `[AggregateRoot<Guid>]`, a template never generates an id from a raw value. The id belongs to the
application. The application declares the id where every module that refers to it can see it, usually in a
contracts project:

```csharp
[EntityId<Guid>]
public readonly partial record struct SubscriptionId;                // in Shop.Contracts

[Subscription<SubscriptionId>]
public sealed partial class ShopSubscription;
```

The generator generates nothing for the class until its id is an `[EntityId<T>]`.

---

## DDD00044

**A template takes a type from a class nobody declares.**

```csharp
[Invoice<InvoiceId>]                                                  // DDD00044: no [Subscription] class
public sealed partial class ShopInvoice;
```

Some parents need more than the id of the class that derives from them. The parent of an invoice needs the
subscription's id and the class of its lines. The template attribute takes those types from the one class
declared with the template it names. So each type is declared once, and every class agrees on it. The generator
looks in the project first. If the project declares no such class, the generator looks in the projects it
references. Declare the class that the message names, with the attribute it names, in one of them.

A code fix declares the class for you, next to the class with the error:
`[Subscription<SubscriptionId>] public sealed partial class ShopSubscription;`.

- It names the new class after the class with the error. `ShopInvoice` gives the prefix `Shop`.
- It declares the class with the id whose name matches the parent's id parameter. `TSubscriptionId` gives
  `SubscriptionId`. If no id has that name, it looks for the template's name with `Id` after it. So
  `[OrganizationUnit]`, whose parent's parameter is `TUnitId`, finds your `OrganizationUnitId`.
- It is offered only when it finds exactly one such id, in the project or in a project it references.
- For a template name of more than one word, the new class gets the prefix and the last word only. An
  organization called `ShopOrganization` that has no unit gets `ShopUnit`.
- A class named after its template alone has no prefix. The new class is then named after its template in full.
  `Organization` gets `OrganizationUnit`.

---

## DDD00045

**A template takes a type from a class declared more than once.**

```csharp
[Subscription<SubscriptionId>] public sealed partial class ShopSubscription;
[Subscription<SubscriptionId>] public sealed partial class TrialSubscription;

[Invoice<InvoiceId>]                                                  // DDD00045: which subscription?
public sealed partial class ShopInvoice;
```

The parent takes a type argument from the one class declared with another template. With two such classes, the
generator cannot tell which one is meant. Picking one would bind the parent to it with no message. Keep one.

A [registration closed over your classes](writing-a-supporting-domain.md#a-registration-closed-over-your-classes)
takes its type arguments in the same way, and also reports DDD00045. It reports it on the first class of the
project declared with one of its templates: `'SubscriptionModelBuilderExtensions.AddSubscriptions' is declared
with [TemplateRegistration], which takes 'TSubscription' from the class declared with [Subscription], and there
are several`.

A package can declare that a template is meant to be used more than once, with `AllowSeveral = true` on its
marker. Examples are the comments on a subscription and the comments on an invoice. For a registration, several
classes of such a template are fine. The registration is written
[once per class, named after the class](writing-a-supporting-domain.md#a-template-declared-more-than-once).
DDD00045 then reports only the cases that still cannot be told apart, with a fix for each:

- **Two templates of one method each have several classes.** The generator cannot tell which class of one
  template goes with which class of the other. Call the package's generic method for these classes, with its type
  arguments written out.
- **Two of the classes have one name,** in two namespaces. The registrations are named after their classes. Give
  each class a name of its own.
- **Two registrations would get the same name.** A method that
  [says what its registration is called](writing-a-supporting-domain.md#naming-a-registration)
  names each registration after what its class says of itself, such as the aggregate it belongs to. Two classes
  that say the same thing give one name: `the registration of each would be called 'AddShopInvoiceDiscounts'`.
  Make the things they are named after differ. With Membership, a resource has one member class. The other
  classes keep their registrations.

The last case is reported once, however many of the package's registrations it stops, on the class to fix. If the
type that the registrations are named after holds one of the classes in a property or field, as a resource holds
its members, DDD00045 is reported on the other class. Otherwise it is reported on the class declared last.

A parent that takes a type from such a template is still closed over one class. So two classes are reported
there, as for any other template.

---

## DDD00046

**A template attribute fills exactly the type parameters of its parent.**

```csharp
[AggregateRootTemplate(typeof(InvoiceAggregate<,,,>))]
public sealed class InvoiceAttribute<TInvoiceId> : Attribute;        // DDD00046: nothing fills the other three
```

DDD00046 reports a mistake in the package that declares the template attribute. It is reported on the attribute
when the package is built. It is also reported on a class that uses the attribute when the package was built
without the generator, because there the generator first sees the attribute. The rules:

- The marker names an open parent marked `[AggregateRootBase]` for `[AggregateRootTemplate]`, or `[EntityBase]`
  for `[EntityTemplate]`.
- The attribute's own type arguments fill the parent's first type parameters, the id first.
- Exactly one `[TemplateArgument]` fills each parameter after those.

```csharp
[AggregateRootTemplate(typeof(InvoiceAggregate<,,,>))]
[TemplateArgument(1, typeof(SubscriptionAttribute<>))]
[TemplateArgument(2, typeof(InvoiceLineAttribute<>), Take = TemplateArgumentKind.Type)]
[TemplateArgument(3, typeof(InvoiceLineAttribute<>))]
public sealed class InvoiceAttribute<TInvoiceId> : Attribute;
```

A parameter that takes the application's class, with `Take = TemplateArgumentKind.Type`, has no `new()`
constraint. The generator writes a parameterless constructor for that class, and that constructor is never
public. So the class could never meet a `new()` constraint. A parent creates the application's class through a
static abstract factory instead, as
[Writing your own supporting domain](writing-a-supporting-domain.md#creating-the-applications-entities) shows.

The attribute may have more type arguments than its parent takes. These belong to the template, such as the
aggregate a class belongs to, and a registration
[takes them by position](writing-a-supporting-domain.md#naming-a-registration).

The generator generates nothing for a class declared with the attribute until the package is fixed.

---

## DDD00047

**A class is declared an entity or aggregate root once.**

```csharp
[AggregateRoot<SubscriptionId>]
[Subscription<SubscriptionId>]                                        // DDD00047
public sealed partial class ShopSubscription;
```

`[AggregateRoot<TId>]`, `[Entity<TId>]`, `[AggregateRootBase]`, `[EntityBase]` and a package's template attributes
each give the class a base class. A class has only one base class. Keep the attribute that describes the class.
Keep the template when the class is meant to extend what the package ships. `[AggregateRoot<TId>]` together with
`[Entity<TId>]` is [DDD00009](#ddd00009). The generator generates nothing for the class until one attribute is
left.

---

## DDD00048

**A class a template takes meets its parent's constraints.**

```csharp
[InvoiceLine<InvoiceLineId>]
public sealed partial class ShopInvoiceLine;                          // no IInvoiceLineFactory

[Invoice<InvoiceId>]                                                  // DDD00048
public sealed partial class ShopInvoice;
```

A `[TemplateArgument]` with `Take = TemplateArgumentKind.Type` passes one of the application's own classes to the
parent as a type argument. Here it is the class of an invoice's lines. The parent may require more of that class
than the right template, such as an interface that the parent creates the class through. If the class does not
have it, the parent closed over the class would be a compile error inside generated code, where you cannot fix
it. So the generator generates nothing, and the message names what the class is missing:

```csharp
[InvoiceLine<InvoiceLineId>]
public sealed partial class ShopInvoiceLine : IInvoiceLineFactory<ShopInvoiceLine, InvoiceLineId>
{
    private ShopInvoiceLine(InvoiceLineId id, decimal amount) : base(id, amount) { }

    public static ShopInvoiceLine Create(InvoiceLineId id, decimal amount) => new(id, amount);
}
```

---

## DDD00049

**A template registration needs a class declared with each of its templates.**

```csharp
[Subscription<SubscriptionId>]                                        // DDD00049: no [Invoice] class, nor [InvoiceLine]
public sealed partial class ShopSubscription;

modelBuilder.AddSubscriptions();                                      // closed over the subscription and the invoice
```

A package's `[TemplateRegistration]` method, such as `modelBuilder.AddSubscriptions()`, is written into every
project that declares a class with one of its templates. It is closed over that project's classes. Each of its
`[TemplateType]` type parameters takes the one class declared with a template, or that class's id. The generator
looks for the class in the project first, then in the projects it references, as it does for a
`[TemplateArgument]`. This project declares a class with one of the method's templates, so it is meant to get
the registration. No project declares a class with the template that the message names:

```
A class declared with [Invoice] is needed by 'SubscriptionModelBuilderExtensions.AddSubscriptions' and
'SubscriptionServiceCollectionExtensions.AddSubscriptions', and neither this project nor a project it
references declares one; declare one, once
```

DDD00049 is reported once for each missing template, on the first class of the project declared with one of the
methods' templates. It names every registration that needs the template. No registration is written until the
class is declared. The call to the registration then also fails to compile, because the method does not exist.
Fix DDD00049, and the call compiles again. The same code fix as for [DDD00044](#ddd00044) declares the class next
to the class with the error.

DDD00049 is not reported where another diagnostic reports the same cause:

- A class of the project whose parent takes a type from the missing class reports [DDD00044](#ddd00044). The
  registrations do not report it again.
- Two classes declared with the template are [DDD00045](#ddd00045).
- Two classes whose registrations would get the same name are reported once, as [DDD00045](#ddd00045), however
  many registrations they share, on the class to fix.

A module split into projects by layer declares its classes in its domain project, and calls the registration in
its infrastructure project, which declares none. The infrastructure project gets the registration too. It is
closed over the classes that the projects of its module declare, the projects with the same
`[assembly: Module]`, and only those. If those projects declare a class with one of the method's templates and
no class with another, DDD00049 is reported on the infrastructure project's `[assembly: Module]` attribute,
without a code fix. The missing class belongs in the project that declares the others. [DDD00045](#ddd00045) and
[DDD00050](#ddd00050) are reported there too, for such a project.

If no file of the project that somebody edits declares the module, these diagnostics are reported at the project
file. In that case the build declared the module from `DDD_Module`, or an `<AssemblyAttribute>` item wrote the
attribute into `obj/` ([A module named by its folder](modules.md#a-module-named-by-its-folder)). Set their
severity with `<NoWarn>`, `<WarningsAsErrors>` or a `.globalconfig`. A `[*.cs]` section of `.editorconfig` does
not reach the project file.

---

## DDD00050

**A type a template registration takes meets the method's constraints.**

```csharp
[TemplateRegistration]
public static IServiceCollection AddAudit<
    [TemplateType(typeof(SubscriptionAttribute<>), Take = TemplateArgumentKind.Type)] TSubscription>(this IServiceCollection services)
    where TSubscription : class, IAudited;

[Subscription<SubscriptionId>]
public sealed partial class ShopSubscription;                         // DDD00050: not IAudited
```

A `[TemplateType]` with `Take = TemplateArgumentKind.Type` passes one of your classes to the package's method as a
type argument. The method may require more of the class than the template, such as an interface. The
registration closed over a class without it would be a compile error inside generated code. So no registration
is written, and the message names what the class is missing. The generator judges the class by the parent it
derives the class from, closed over the class's own id, as [DDD00048](#ddd00048) judges a class that a template
takes. Give the class what the message names.

A `[TemplateType]` that takes an id is held to the method's `struct` or `class` constraint in the same way. An id
declared as a `record class`, where the method says `where TTenantId : struct`, is reported on the class declared
with the template. Without this check, the registration would fail inside generated code. The id's interfaces
are not checked here. The generator writes them for an entity id, and [DDD00043](#ddd00043) already requires an
entity id.

`Create()` is the exception. The generator writes `Create()` only for an id over a `Guid`. When the method
requires `ICreatableEntityId<T>`, an id without `Create()` is reported here. If the template says that its
package makes the ids of its classes, `CreatesIds = true`, as Tenancy's templates do, [DDD00067](#ddd00067)
reports it on the class instead, and the registration reports nothing more.

A [later type argument of the template](writing-a-supporting-domain.md#a-template-with-more-than-one-type-argument),
taken with `Argument = 1` and up, is whatever type you wrote there. It is held to the method's constraints as
[DDD00053](#ddd00053) holds it to the parent's. Whether it is a struct or a class is always checked. An interface
that the method requires is checked only when no generator will still complete the type.

```csharp
[TemplateRegistration]
public static ModelBuilder AddCommentsByAuthor<
    [TemplateType(typeof(CommentAttribute<,>), Take = TemplateArgumentKind.Type)] TComment,
    [TemplateType(typeof(CommentAttribute<,>))] TCommentId,
    [TemplateType(typeof(CommentAttribute<,>), Argument = 1)] TAuthorId>(this ModelBuilder modelBuilder)
    where TComment : CommentAggregate<TCommentId, TAuthorId>
    where TAuthorId : struct, IEquatable<TAuthorId>, IComparable<TAuthorId>;

public readonly record struct Initials(string Letters);              // equatable, as the parent asks, and without an order

[Comment<InvoiceCommentId, Initials>]                                // DDD00050: 'Initials' as 'TAuthorId', which requires 'IComparable<Initials>'
public sealed partial class ShopInvoiceComment;
```

A registration can require two more things of a later type argument. Both are reported in the same way, on the
class that wrote the type argument:

- A type argument [whose id the method takes](writing-a-supporting-domain.md#naming-a-registration),
  with `IdOfArgument = true`, must be one of your entities or aggregate roots, because the generator reads its id
  from how the class is declared. The message says `requires an entity or an aggregate root, whose id it takes`.
  Name the class that the template asks for, such as the aggregate your member class is a member of. A child
  entity where the registration requires an aggregate root is reported too, with `requires an aggregate root`. A
  class of your project is judged by what the generator will make of it.
- A type that the registration is named after, with `[TemplateRegistration(Name = ...)]`, must have a name of its
  own. An array has none.

```csharp
public sealed class Note;                                             // no [AggregateRoot<TId>], so no id

[Member<NoteShareId, UserId, NamedRole, Note>]                       // DDD00050: 'Note' as 'TResourceId'
public sealed partial class NoteShare;
```

---

## DDD00051

**A set-shaped question is asked once per statement, so its arguments do not read the row.**

```csharp
public static bool Allows(Ticket ticket, Caller caller)
    => DeskQuestions.TicketsForTeam(ticket.Team!).Contains(ticket.Id);   // DDD00051, on ticket.Team!
```

A [set-shaped question](row-level-security.md#set-shaped-access-functions) becomes
`"Id" = ANY (ARRAY(SELECT desk.tickets_for_team(...)))` in the policy. Postgres works out the set once, before it
reads the table. It then finds the rows through the column's index. An argument that reads the row, such as a
column of the row or a question about the row, would make the set different for every row. Postgres would then
call the function once per row. The set-shaped form exists to avoid that. Only the value compared with the set,
the argument of `Contains`, reads the row.

Pass the question constants, the caller, or, inside an access function, the function's own parameters. To ask
about the row itself, ask a question about one row: an access function's `Allows`, or an `[AccessScalar]`
question. Such a question may take the row's columns, and the database then asks it once per row. The generator
generates nothing for the rule until its arguments are constants, the caller, or the parameters of the access
function that the rule is part of.

---

## DDD00052

**A function named without its schema belongs to a module.**

```csharp
// an assembly without [assembly: Module(...)]
[AccessFunctions]
public static partial class DeskQuestions
{
    [AccessSet("tickets_i_watch")]                                       // DDD00052
    public static partial AccessSet<TicketId> TicketsIWatch();
}
```

A function name without a schema is [relative to the module](row-level-security.md#function-names-relative-to-the-module)
that owns it. `tickets_i_watch` in the Desk module is `desk/tickets_i_watch`. The export writes it as the function
of that name in the schema of the context that defines it, whatever the host calls that schema. The owner is one
of these:

- the module that the declaring assembly declares, with `DDD_Module` or `[assembly: Module]`;
- the `Owner` of the class's `[AccessFunctions]`, for a package that declares no module.

With neither, the name has no owner. A function of the same name in another module could then not be told apart
from it.

Add `[assembly: Module("Desk")]`, give the class `[AccessFunctions(Owner = "desk")]`, or write the name with its
schema: `desk.tickets_i_watch`. A question named with its schema is a function that you create yourself. Rules
call it by that name. The generator generates nothing for the class until it has an owner or the name has its
schema.

---

## DDD00053

**A type argument of a template meets its parent's constraints.**

```csharp
[EntityId<Guid>]
public partial record SubscriptionId;                                 // a class

[Subscription<SubscriptionId>]                                        // DDD00053
public sealed partial class ShopSubscription;
```

A class declared with a template derives from the package's parent, closed over the type arguments that the
template supplies:

- the attribute's own type arguments, the id first;
- the ids that a `[TemplateArgument]` takes from other classes.

The parent may require more of an id than [DDD00043](#ddd00043) does. Most often it requires a struct,
`where TSubscriptionId : struct`, because it holds ids by value. The parent closed over a `record class` would be
a compile error inside generated code. So the generator generates nothing for the class. The message names the
type, the parent's type parameter, and what that parameter requires. Declare the id as a struct:

```csharp
[EntityId<Guid>]
public readonly partial record struct SubscriptionId;
```

A class that takes the id from another class reports DDD00053 on its own line. An invoice whose parent holds the
subscription's id by value reports the subscription's id, whatever the subscription's own parent requires. So one
id of the wrong kind is reported on every class whose parent refuses it, and one fix clears all of them.

The generator always checks whether the type is a struct or a class. It checks the parent's other constraints,
such as an interface, for two kinds of type:

- a type from a referenced project, such as an id in a contracts project;
- a type written out in full.

A generator may still complete a `partial` type declared in the same project. This is how an `[EntityId<T>]` gets
`IEntityId<T>`. So DDD00053 leaves an interface that such a type does not show yet to the compiler. An
`[EntityId<T>]` with an error of its own, such as [DDD00003](#ddd00003), is not completed. DDD00053 reports it as
missing `IEntityId`, and fixing its own error clears both. [DDD00048](#ddd00048) judges a class that the template
takes.

---

## DDD00054

**A package's row access contribution is made from what the application marks.**

DDD00054 is reported in the project that turns the Supabase export on (`<SupabaseMigrationsExport>`), normally the
host, about a package that the project references:

```csharp
// The host references DDDToolkit.Supporting.Membership.Postgres, and marks no resource's rules: DDD00054
services.AddDocumentMembership<FilingContext>(DocumentMembership.Rules);
```

A package that declares itself a contributor, with `[assembly: RowAccessContribution]`, writes its
[row level security](row-level-security.md#policies-a-package-ships) into the migrations of every application
that references it. Referencing the package is your consent. The build makes the package's class in the project
where the export runs, before your host exists. So the class cannot read values from your host. A value that
only your application knows, such as a resource's rules, comes from a static property or field that you mark
with the attribute the package names.

If nothing is marked that the class cannot do without, the build does not make the class, and none of its SQL
reaches your migrations. The package's tables may then have no policies at all. A rule that asks one of the
package's functions fails the export, and the export names the rule. The message names the type of the member
and the attribute. Mark the member:

```csharp
public static class DocumentMembership
{
    [MembershipRules<DocumentShare>]
    public static MembershipRules Rules { get; } = new("documents", ...);
}
```

If your application must not have the package's SQL, because you write those policies yourself, say so with
`[assembly: LeaveOutRowAccessContribution(typeof(MembershipRowAccessContribution<>))]`. DDD00054 is then not
reported. A value that the package can do without is never reported. Tenancy's catalogue is such a value. It is
`new ApplicationCatalogue()` when you mark none.

## DDD00055

**A context's migration files are named after its module.**

DDD00055 is reported in the project that turns the Supabase export on (`<SupabaseMigrationsExport>`), normally the
host, about a marked context, or a marked factory, in a project it references:

```csharp
// no module in the context's project: no DDD_Module, no [assembly: Module]
[SupabaseMigrations]
public sealed class OrderingContext(DbContextOptions<OrderingContext> options) : DbContext(options)  // DDD00055
```

The export names every file it writes after the module the context belongs to, as in
`20260922120000_AddOrders.ordering.ddd.sql`. It finds a module's files again by that name. The module is the one
that the assembly of the export's factory for the context declares. If that assembly declares none, it is the one
that the context's assembly declares. For a marked context, the two are the same assembly. If neither declares a
module, the name comes from the context's class name, without `Context`. The message says which name that is.

This works until somebody renames the class. The export then looks for files under the new name and recognizes
none of the files it wrote:

- It reports every migration as `VersionTaken`, because the file under the old name holds its timestamp.
- It writes the module's access file again.
- You have to rename the files by hand.

```xml
<DDD_Module>Ordering</DDD_Module>
```

Declare the module in the project that holds the context, with `DDD_Module` or `[assembly: Module("Ordering")]`.
The files then keep their name, whatever the class is called. Files already written under the context's name,
`ordering` for `OrderingContext`, stay recognized when the module has the same name, Ordering. If the module has
another name, rename the files once.

## DDD00056

**A request interface is one a behavior can be written for.**

```csharp
[AccessRequests]
public interface IBillingRequest;                       // DDD00056: does not derive from IRequireAccess

[AccessRequests]
public interface IBillingRequest<TAnswer> : IRequireAccess;   // DDD00056: has type parameters
```

`[AccessRequests]` marks the interface that a module's commands and queries implement to say
[what they require of their caller](access-requirements.md). In a project that uses the Mediator library, the
generator writes a pipeline behavior for the interface. The behavior is declared beside the interface and named
after it. It asks the module's access checks before every handler. For this, the interface must meet these
rules:

| It | Because |
|---|---|
| derives from `IRequireAccess` | the checks read a request's requirement from `IRequireAccess`, and `AccessChecks<TRequests>` is closed over it |
| has no type parameters | the behavior and the module's set of checks are closed over the interface itself |
| is not declared inside a type, and is not `file`-local | the generator writes the behavior beside the interface, in a file of its own, and the behavior must name the interface |
| gives a behavior name that no other marked interface in its namespace gives | `IBillingRequest` and `IBilling` would both give `BillingAccessBehavior` |

```csharp
[AccessRequests]
public partial interface IBillingRequest : IRequireAccess;
```

The generator writes no behavior for the interface until it meets these rules, so nothing checks its requests.
That is why DDD00056 is an error. Fix the interface, or call `AccessChecks<TRequests>.RequireAsync` in front of
the handlers yourself. The generator checks the interface in the same way in a project that does not use the
library, where it writes no behavior anyway. So adding the library later brings no new errors.

## DDD00057

**The Mediator library's pipeline behavior has the shape the generator writes a behavior for.**

DDD00057 is reported on an `[AccessRequests]` interface, about the version of the Mediator library that the
project references:

```
No access behavior is written for 'IBillingRequest': the Mediator library this project references declares
IPipelineBehavior<,> otherwise than the generator knows it, with a Handle that does not take the message, a
cancellation token and the delegate that runs the next step, each once.
```

The generator implements the library's `IPipelineBehavior<TMessage, TResponse>`. It reads the shape of that
interface from the library itself:

- one method, `Handle`, that takes the message, a cancellation token and the delegate that runs the next step, in
  whatever order the referenced version declares them;
- `Handle` returns a `ValueTask` or a `Task` of the response;
- the delegate takes the message and the token, and returns the same.

So the generator follows a version that moves a parameter, without a new release of the toolkit. A version that
declares the interface in another shape is one that the generator does not know. The generator then writes
nothing instead of guessing. A behavior that does not compile, or that never asks the checks, would be worse than
none.

The same holds for `IStreamPipelineBehavior<TMessage, TResponse>`, the pipeline of the messages that are answered
with a stream. Its `Handle` returns an `IAsyncEnumerable` of the response. If the library has this interface, the
generator writes a second behavior for it. The message names the interface that the generator could not read. If
either interface is unknown, the generator writes neither behavior. With one behavior and not the other, half of a
module's messages would be checked, and the module would look fully checked.

Without the behavior, the requests of the interface reach their handlers unchecked, so DDD00057 is an error.
Write the behavior by hand against the version you use, and remove `[AccessRequests]` from the interface. The
interface still names the checks that its requests are held to. Write `Handle` with `async` and `await`, as below.
`RequireAsync` puts the request in hand for the flow of work that asked. The handler that `Handle` calls next runs
in that flow and finds the request in hand ([the request in hand](access-requirements.md#the-request-in-hand)). Run
the next step inside `BeginWork`, in the same method, so the work a check begins reaches the handler, such as the
operator work of an operator's command. The stream behavior begins no work.

```csharp
public sealed class BillingAccessBehavior<TMessage, TResponse>(AccessChecks<IBillingRequest> checks) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IBillingRequest, IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        await checks.RequireAsync(message, cancellationToken);
        using (checks.BeginWork(message, isQuery: message is IBaseQuery))
        {
            return await next(message, cancellationToken);
        }
    }
}

// Only where the module has messages answered with a stream, which pass this pipeline and not the other
public sealed class BillingAccessStreamBehavior<TMessage, TResponse>(AccessChecks<IBillingRequest> checks) : IStreamPipelineBehavior<TMessage, TResponse>
    where TMessage : IBillingRequest, IStreamMessage
{
    public async IAsyncEnumerable<TResponse> Handle(TMessage message, StreamHandlerDelegate<TMessage, TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await checks.RequireAsync(message, cancellationToken);
        await foreach (var item in next(message, cancellationToken))
        {
            yield return item;
        }
    }
}
```

---

## DDD00058

**A notification implements no request interface.**

```csharp
[AccessRequests]
public partial interface IBillingRequest : IRequireAccess;

public sealed record InvoiceClosed(InvoiceId Invoice) : INotification, IBillingRequest   // DDD00058
{
    AccessRequirement IRequireAccess.RequiredAccess => new BillingAccess.OnInvoice("billing.view", Invoice);
}
```

The behavior written for an `[AccessRequests]` interface is part of Mediator's pipeline for commands and queries.
The second behavior is part of Mediator's pipeline for messages that are answered with a stream. Mediator
publishes a notification to its handlers through neither pipeline. So a notification that implements the
interface declares a requirement that nothing checks. Every handler of the notification runs for whoever
published it, while the declaration looks as if it were checked. That is why DDD00058 is an error.

DDD00058 is reported on the type where the notification and the interface are first joined: a class, a struct or
an interface of your own that is both. It is not reported again on the types that implement or derive from that
type. A partial type is reported once, at the part that lists what it implements. A project that does not use the
library has no notifications, and reports nothing.

Send what needs a check before it is handled as a command or a query:

```csharp
public sealed record CloseInvoice(InvoiceId Invoice) : ICommand, IBillingRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => new BillingAccess.OnInvoice("billing.close", Invoice);
}

public sealed record InvoiceClosed(InvoiceId Invoice) : INotification;   // says what happened, to whoever listens
```

A notification says that something happened. Whoever publishes it has passed its own check already. If the
handlers of a notification must not run for every publisher, ask the checks before you publish:
`AccessChecks<IBillingRequest>.RequireAsync(request, cancellationToken)`, with a request of the module that
declares the requirement.

## DDD00059

**The member list of a resource is written from what the resource declares.**

```csharp
[Member<DocumentShareId, UserId, NamedRole, Document>]
public sealed partial class DocumentShare;

[AggregateRoot<DocumentId>]
public sealed partial class Document                    // DDD00059: no property marked [Owner]
{
    public UserId OwnerId { get; private set; }

    public UserId WrittenBy { get; private set; }

    public partial IReadOnlyList<DocumentShare> Shares { get; }
}
```

For a class declared with [Membership](membership/overview.md)'s member template, the package's generator writes
the member list on the resource that the template names. The member list is a private property `Members`. The
resource's own methods change its members through it. The generator writes it from three things the resource
declares and from the member class's own id. It writes the list only when it can tell each of them without a
guess:

| The list needs | Where it comes from | Not written when |
|---|---|---|
| the members | the one get-only `partial` property of `IReadOnlyList<TMember>`, `IReadOnlyCollection<TMember>` or `IEnumerable<TMember>`, which the toolkit keeps in a list | there is none, or there are two. A set property does not count, because the toolkit keeps a set in a `HashSet` |
| the owner | the one property marked `[Owner]`, of the type a member is known by, the template's second type argument | no property is marked, two are marked, or the marked one has another type |
| a new row's id | the `Create()` of the member class's own id, which the generator writes for an `[EntityId<Guid>]` | the id is over another type, such as a `long`, and declares no `Create()` |
| the codes | the one static property or field of type `MembershipCodes` on the resource. If the resource declares none, the codes that the generator writes under the resource's [name](membership/resources-and-members.md#names) | there are two, or there is a member called `Codes` of another type |

The generator also does not write the list in these cases:

- The resource already has a member called `Members`.
- Two member classes name the same resource, and the resource keeps a collection of both, or of neither. Both
  lists would get the same name.
- A type of the member class is one that the generator cannot see. The id of an `[AggregateRoot<Guid>]` is written
  by another generator, and no generator sees what another generator writes. Declare such an id yourself, with
  `[EntityId<Guid>]`, as the member class's own id is declared.

A member class that names no aggregate root, or that names a resource that keeps its members as another member
class, is [DDD00060](#ddd00060), on the member class. The message names each thing that stands in the way.

Declare what is missing, and the generator writes the list:

```csharp
[AggregateRoot<DocumentId>]
public sealed partial class Document
{
    [Owner] public UserId OwnerId { get; private set; }

    public UserId WrittenBy { get; private set; }

    public partial IReadOnlyList<DocumentShare> Shares { get; }
}
```

Or write the list yourself. This works for every shape above. The generator leaves alone a resource that declares
a `MemberList` of its member class, under any name, and reports nothing for it:

```csharp
private MemberList<DocumentShare, DocumentShareId, UserId, NamedRole> Members
    => new(_shares, OwnerId, DocumentRefusals.Membership);
```

DDD00059 is a warning, because the resource compiles. It has no member list, so its members cannot be changed. The
first method you write over `Members` only reports that the name does not exist. A resource that changes its
members in another way turns the warning off for the project: `<NoWarn>DDD00059</NoWarn>`.

## DDD00060

**A member class names an aggregate root whose members it is.**

```csharp
[AggregateRoot<DocumentId>]
public sealed partial class Document
{
    public partial IReadOnlyList<DocumentShare> Shares { get; }
}

[Member<DocumentShareId, UserId, NamedRole, Document>]
public sealed partial class DocumentShare;

[Member<DocumentWatcherId, UserId, NamedRole, Document>]
public sealed partial class DocumentWatcher;                // DDD00060: the document keeps its members as shares already

[Member<NoteShareId, UserId, NamedRole, DocumentShare>]
public sealed partial class NoteShare;                      // DDD00060: a share is no aggregate root
```

The fourth type argument of the [member template](membership/overview.md) is the resource that the members belong
to. It is an aggregate root of yours. The resource keeps its members in a collection of the member class, and the
generator writes the member list over that collection.

A resource has one member class. If two member classes name one resource, the class that the resource keeps a
collection of is its member class. The generator writes the list for that class, so the resource's own methods
still compile. The other class is reported, on itself, as the class to fix. Name the resource it is for, or
remove it.

A member class whose resource is no aggregate root is also reported on itself. Such a resource is a child entity,
the member class itself, or a class that is no entity. Name the aggregate root that the members belong to.

A project that also gets the package's registrations, such as the infrastructure project that holds the context,
reports the same problem as the errors [DDD00045](#ddd00045) and [DDD00050](#ddd00050), on the same class.
DDD00060 is not reported beside them. If the resource keeps a collection of both member classes, or of neither,
the generator cannot tell which class is the extra one. That case is [DDD00059](#ddd00059), reported once, on the
resource.

DDD00060 is a warning. Nothing else is wrong with the member class, and the generator writes nothing for it.

## DDD00061

**A request that declares its access is sent, not handed to its handler.**

```csharp
[AccessRequests]
public partial interface IBillingRequest : IRequireAccess;

public sealed class InvoiceReminders(CloseInvoiceHandler handler)
{
    public async Task CloseOverdueAsync(InvoiceId invoice, CancellationToken cancellationToken)
        => await handler.Handle(new CloseInvoice(invoice), cancellationToken);   // DDD00061
}
```

The behavior written for an `[AccessRequests]` interface asks the module's checks in Mediator's pipeline. A
command or a query passes the pipeline when it is sent. A handler called directly passes no pipeline. The handler
then runs without any check of what the request declares, and nothing at run time notices. Where the database has
[policies](row-level-security.md), only the policies stand in the way. A policy works per table, so it is coarser
than the requirement of one request. A policy must let every caller who may write a project's row write it,
whether to close the project or to rename it. So the policy lets a rename through for a caller who may only close
the project.

So DDD00061 is reported where the call is written. It reports a call to `Handle` with a message that implements a
marked interface, on one of these:

- one of Mediator's handler interfaces: `ICommandHandler`, `IQueryHandler`, `IRequestHandler` or one of their
  stream kinds;
- a class that implements one of them, or derives from such a class.

A reference to `Handle` that makes a delegate of it is reported too.

Send the request instead. It then passes the behavior on its way to the same handler:

```csharp
public sealed class InvoiceReminders(ISender sender)
{
    public async Task CloseOverdueAsync(InvoiceId invoice, CancellationToken cancellationToken)
        => await sender.Send(new CloseInvoice(invoice), cancellationToken);
}
```

A code fix does this when the call can use an `ISender`, or an `IMediator`, held in a local, a parameter, a field
or a property. It cannot use a field from a static member, or a local of the method from inside a static lambda.
It sends a query answered with a stream with `CreateStream`, and puts named arguments in the order of the
handler's parameters. If the call can use no sender, inject one. The fix does not change a constructor for you.

DDD00061 does not report these cases:

- **Constructing a handler, and injecting one.** The call skips the check, so DDD00061 is reported at the call.
  This includes a call through the interface that the handler was injected as.
- **Mediator's own dispatch.** It is generated code, and it calls every handler after the pipeline.
- **`base.Handle`** in a handler that overrides it. It passes on the request that passed the pipeline on its way
  in.
- **A decorator**: a handler that wraps another handler of the same message, and passes it the message it was
  given, unchanged. The request passed the pipeline on its way to the decorator. Sending it again would bring it
  back to the decorator. A message that the decorator makes itself is reported.
- **A test project**: a project with `IsTestProject` or `IsTestingPlatformApplication` set, as
  `Microsoft.NET.Test.Sdk` and Microsoft.Testing.Platform set them. A test that calls a handler on purpose tests
  the handler alone. A test of what a request may do sends the request.
- **A project without Mediator.** The generator writes no behavior there. You ask the checks yourself,
  [in front of every handler](access-requirements.md#asking-the-checks-without-mediator).

Outside a test project, suppress a call that you mean, where it is made, with the reason beside it:

```csharp
#pragma warning disable DDD00061 // replayed from the outbox after the behavior let it through once
await handler.Handle(replayed, cancellationToken);
#pragma warning restore DDD00061
```

The build cannot see every way around the behavior. It does not report a helper that calls `Handle` on a handler
of a type parameter that has no constraint to a marked interface, such as a transport's own dispatcher. It does
not report the call that passes such a helper a handler either. Write such a dispatcher so that it sends through
the sender, or asks the checks itself. A module whose behavior is not in the pipeline is stopped when the host
starts, by the [start-up check](startup-checks.md) `access.behaviors-registered`. See
[When nothing asks the checks](access-requirements.md#handlers-reached-without-the-access-check).

## DDD00062

**A class of one GraphQL schema is one the toolkit alone registers.**

```csharp
[GraphQLSchema("admin", OperationType.Query)]
[QueryType]                                    // DDD00062
internal static class SeatsAdminQueries
{
    [Query]                                    // DDD00062
    public static Task<IReadOnlyList<SeatGrant>> GetSeatGrantsAsync(SeatId seat, [Service] ISender sender, CancellationToken cancellationToken) => ...;
}
```

A class marked `[GraphQLSchema]` belongs to the schema it names and to no other. The toolkit's generator registers
its public static methods in the module's `Add{Module}GraphQlRuntimeBindings()`, for a builder of that schema name
only. HotChocolate's own generator registers everything it finds in a project in one method, and every schema the
project is added to calls that method. A class that HotChocolate's generator finds is therefore in every schema.
The administration field above would be offered to every caller as well. So the class must carry nothing that
HotChocolate's generator registers:

- no `[QueryType]`, `[MutationType]`, `[SubscriptionType]`, `[ExtendObjectType]` or `[ObjectType]`;
- no base class such as `ObjectTypeExtension`;
- no static method marked `[Query]`, `[Mutation]` or `[Subscription]`.

The attribute on the class says what its methods are. Remove what the message names.

DDD00062 also reports what would leave a field out with no message:

- **An instance method.** Nothing makes an instance of the class, so a field of one schema is a public static
  method. A former `[QueryType]` class often has instance methods. Make them static.
- **A class with no field**: no public static method that is not `[GraphQLIgnore]`.
- **Two methods that are one field** of one schema and operation type: two overloads, or `GetLedger` beside
  `GetLedgerAsync`. HotChocolate keeps one and drops the other. Give one of them another name, or another
  `[GraphQLName]`.
- **A method whose name another public static method has, and that takes a parameter of a type another generator
  writes**, such as the interface that HotChocolate's generator writes for a data loader. The generator finds a
  method by its name. For a shared name it also uses the parameter types, and it cannot name a type that it does
  not see. Give the method a name of its own.

DDD00062 also reports an attribute that names no schema, and a class that the generated registration cannot name:
a generic class, a `file` class, or a class that is private or protected inside another class.

Each of these puts a field where it should not be, or leaves a field out where it should be, with no message. So
DDD00062 is an error. See [A field for one schema only](graphql.md#a-field-for-one-schema-only).

## DDD00064

**DDD_Module declares the module where the package's build step runs.**

```xml
<!-- Ordering.Domain.csproj: the generators as a project reference, and no targets file -->
<PropertyGroup>
  <DDD_Module>Ordering</DDD_Module>   <!-- DDD00064 -->
</PropertyGroup>
<ItemGroup>
  <CompilerVisibleProperty Include="DDD_Module" />
  <ProjectReference Include="..\DDDToolkit.Analyzers\DDDToolkit.Analyzers.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

`DDD_Module` declares the project's module through a build step. The `DDDToolkit.Analyzers` package carries a
targets file beside its props file. The targets file writes the module declaration into the project. Every
generator of the project, and of the projects that reference it, reads the declaration there. A package reference
brings both files.

The generators can arrive in another way: as an analyzer assembly, or as a project reference with
`OutputItemType="Analyzer"`. The property then reaches them through the props file alone, or through a
`CompilerVisibleProperty` that you list yourself, as [DDD00014](#ddd00014) suggests. In that case the name
arrives and the declaration does not:

```
DDD_Module names this project's generated code after 'Ordering' and declares no module, because the build step
of the DDDToolkit.Analyzers package that declares it did not run: the project is no project of module
'Ordering'. Import the package's targets file beside its props file, or declare [assembly: Module("Ordering")].
```

The generated code is named after the module, `OrderingEventNames` and `AddOrderingConverters`, and the project
is no module:

- Its domain events are stored and published without the module's name.
- The module boundary is not checked.
- The module's other projects do not take the project for one of theirs. So a registration such as
  `modelBuilder.AddTenancy()` is not written into a project of the module that declares none of the classes.

Without DDD00064, the first sign would be a call that does not compile, or an event stored under a name that
changes once the targets file arrives.

Import `DDDToolkit.Analyzers.targets` as well, from the package's `build` folder or from the source, in the same
way that you import its props file. Or declare the module with `[assembly: Module("Ordering")]`. The targets file
tells the generators that it was imported, by passing them `DDD_DeclareModule`. Where it was imported, a test
project and a project that sets `DDD_DeclareModule` to false are no module on purpose, and are not reported. A
test project is also not reported without the targets file, if the props file says that it is a test project.

DDD00064 is reported at the project file, because no line of code is wrong. The project file is outside every
source file, so a severity in an `.editorconfig` section for `*.cs` files does not reach it. Set the severity with
`<NoWarn>` or `<WarningsAsErrors>` in the project file or a `Directory.Build.props`, or in a `.globalconfig` file
with `is_global = true`.

## DDD00065

**The class a package's use cases are named through is written where each of its templates has one class.**

```csharp
// Shop.Tenants.Domain, [assembly: Module("Tenants")], with no class declared with [SeatAggregate]
[TenantAggregate<TenantId>] public sealed partial class Tenant;   // DDD00065: 'SeatManager' is not written, ...
[OrganizationAggregate<TenantId>] public sealed partial class Organization;
[OrganizationUnit<OrganizationUnitId>] public sealed partial class OrganizationUnit;
[RoleAggregate<RoleId>] public sealed partial class Role;
```

```csharp
// The same project, with all the classes, and a type of the class's name in a namespace of its own
namespace Shop.Tenants.Settings;

public static class SeatManager;   // DDD00065, on this type: the class would hide it where Shop.Tenants.Settings is imported
```

A package names its generic classes in `[assembly: TemplateFacade]`. For each, the toolkit's generator writes a class
into the project that declares your classes. The class derives from the package's class, closed over your classes,
and has the package's class name without the type parameters. Tenancy names seven: `TenantManager`,
`OrganizationManager`, `SeatManager`, `RoleManager`, `InvitationManager`, `TenancyAccessManager` and the record
`TenantToProvision`. So your code takes a `SeatManager` and names `SeatManager.SeatOverview`
([The managers and the access manager](tenancy/your-module.md#the-managers-and-the-access-manager)). Each type parameter of the package's class is one of
your classes or ids. So the generator writes a class only when every template it takes from has one class, in that
project or in a project of the same module that the project references.

If a class is not written, the projects above that name it only get CS0246: the name does not exist. The project that
registers Tenancy would give more detail. That project references the application project, and does not build once
the application project fails. So the project that declares the classes reports the cause, on the first of the
classes, once for each class that is not written:

| What keeps it out | What to do |
|---|---|
| no class is declared with one of the templates | declare it, next to the others |
| several classes are declared with one template | keep one |
| a class does not meet the constraints of the package's class | give it what the message names |
| the project declares a type of that name in a namespace. The class would hide that type wherever the namespace is imported | rename that type, or name the class otherwise: `[assembly: TemplateFacadeName("SeatManager", "ShopSeatManager")]` |

DDD00065 has the severity Info. A module whose classes are split over two projects gets DDD00065 in the first
project, where nothing is wrong. The second project, where the classes are complete, gets the class.

Some causes are reported elsewhere, and not again here:

- A template whose class a parent in the project needs is that class's error already: [DDD00044](#ddd00044) or
  [DDD00045](#ddd00045).
- A template that a registration the project can call takes from is reported by the registration:
  [DDD00049](#ddd00049) or [DDD00045](#ddd00045).
- A class that cannot be generated has its own diagnostic.
- A class that the package marks `OnlyWith` a template is written only where that template has a class. A project
  without one gets no class and no diagnostic. Tenancy's `InvitationManager` is written only where the module declares
  an invitation class.

A package's interface gets an alias instead of a class, such as Tenancy's `TenancyStore`. The same causes keep the
alias out of the project that declares the classes. A project above that declares a type of the alias's name in a
namespace gets DDD00065 on that type, because the alias would hide it wherever the namespace is imported.

A name that you keep in the global namespace yourself is your own way of naming the classes: an alias of the name
at the top of a file, or a type or a namespace of that name. The generator leaves that name alone and reports
nothing. A name that you give with `TemplateFacadeName`, and that a type or a namespace in the global namespace
already has, is [DDD00076](#ddd00076).

## DDD00066

**A package's switch writes a class or an id where its name is free and its id is known.**

```csharp
// Shop, with the switch, and a class of its own called Role that has nothing to do with Tenancy
[assembly: GenerateTenancyClasses]   // DDD00066: [assembly: GenerateTenancyClasses] does not write 'Role': this project has a class 'Shop.Role' already, which is not declared with [RoleAggregate]: if it is meant to be that class, declare it [RoleAggregate<RoleId>] and it is yours; ...

namespace Shop;

public sealed class Role;
```

```csharp
// Two projects this one references each declare a RoleId
[assembly: GenerateTenancyClasses]   // DDD00066: ... does not write 'Role': its id would be 'RoleId', and the projects it references declare 'Shop.One.RoleId' and 'Shop.Two.RoleId'; keep one
```

A package's switch, such as Tenancy's `[assembly: GenerateTenancyClasses]`
([Quick start](tenancy/your-module.md#quick-start)), makes the generator write the package's classes that your
project leaves out, and their ids. It writes them as the package ships them, for example `Tenant` and
`TenantId`. They are public, in the project's root namespace. The switch writes nothing that would replace
something you have. If it cannot write a class or an id, it reports DDD00066 on the switch:

| What is in the way | What to do |
|---|---|
| a class of yours with the class's name in the root namespace, not declared with the template | if it is meant to be the package's class, declare it with the template, `[RoleAggregate<RoleId>]`. If not, rename it, or declare the package's class yourself under another name: `[RoleAggregate<RoleId>] public sealed partial class ShopRole` |
| another type of that name, yours or one that a referenced project declares | rename it, or declare the package's class yourself under another name |
| a namespace of that name, such as a folder `Organization/` directly in the project | declare the package's class yourself under another name, or rename the namespace |
| a type of the id's name that is no entity id | mark it `[EntityId<Guid>]`, or rename it |
| an id of that name that the generator writes for an `[AggregateRoot<Guid>]` class of yours, such as a `Tenant` that rents something | rename that class, or declare the package's class yourself under another name, with its own id |
| several ids of one name, in the project or in the projects of its module that it references | keep one |
| your own classes of templates that share an id, such as the tenant and the organization, declared over two different ids | declare them over one id |

DDD00066 reports each cause once:

- A class that takes a type from a class the switch cannot write, or that shares its id, is not written either,
  and is not reported again. The organization shares the tenant's id, and the role and the seat take it. So a
  `TenantId` in the way leaves out four classes, with one error.
- No id is written for a class that is not written.
- `AddTenancy()` and the class that the use cases are named through do not report the left-out classes as
  missing ([DDD00049](#ddd00049), [DDD00065](#ddd00065)).

A class or an id that you declare yourself always wins, and is never reported. The switch takes it, or writes the
rest around it.

## DDD00067

**A class whose package makes its new ids is declared over an id with a `Create()`.**

```csharp
[EntityId<long>]
public readonly partial record struct TenantId;

[TenantAggregate<TenantId>]   // DDD00067: 'TenantId' has no public static Create(), and 'Tenant' is declared with [TenantAggregate], whose package makes each new TenantId with TenantId.Create(), in code and before the save. ...
public sealed partial class Tenant;
```

An id is made in code before the save. The database never makes it. So a change and every event of the change
know the id from the start. Tenancy makes the ids of the tenants, units, seats, roles and invitations that it
creates itself, with the id's own `Create()`. The generator writes `Create()` for every `[EntityId<Guid>]`. It
makes a time-ordered id, as `CreateSequential()` does. An id over a `long`, an `int` or a `string` has no
`Create()`, because the generator cannot know how to make a new one. Write it in the id's partial declaration:

```csharp
[EntityId<long>]
public readonly partial record struct TenantId
{
    public static TenantId Create() => new(Snowflakes.Next());   // a snowflake, or a number of a HiLo block
}
```

A `Create()` of your own also wins over the generator's for a `Guid`. The message says what the id lacks, and
what to write:

- An `[EntityId<T>]` without a `Create()`: write one in its partial declaration, as above. The message gives an
  example of a new value for the type that the id wraps.
- An id written by hand, without `[EntityId<T>]`, which no generator completes: write the `Create()`, and add
  `ICreatableEntityId<TenantId>` to the interfaces it implements.
- An id with a fitting `Create()` and without the interface: add the interface. For an id of a project that does
  not target `net10.0`, change that project's target instead. Only the `net10.0` build of DDDToolkit.Abstractions
  has `ICreatableEntityId<T>`, and the generator implements it where the project sees it.
- A `Create()` that is not `public static`, or that returns another type: make it `public static`, returning the
  id.

The error is on the class declared with the template, and names the id. A class that a package's switch writes is
in no file of yours. For such a class, the error is on the id when the id is declared in the same project. When
the id is in another project, such as the module's contracts project, the error is on the switch,
`[assembly: GenerateTenancyClasses]`.

The code fix goes to the id in every case, wherever in the solution the id is declared. It adds what is missing: a
`Create()` whose body throws until you write it, the interface, or both. If the fix is not in the id's
declaration, such as a `Create()` that does not fit or a project's target, the code fix offers nothing.

The class itself is generated. The package's registrations and the class that its use cases are named through
require the same with a constraint. They do not report the class again, and are not written for it, so they do
not fail inside generated code.

A package says which of its templates it makes the ids of, on the marker:
`[AggregateRootTemplate(typeof(SeatAggregate<,,,>), CreatesIds = true)]`
([A package that makes the ids of its classes](writing-a-supporting-domain.md#ids-the-package-creates)).
Only Tenancy's templates do this. Any other id, one that the application makes itself, needs no `Create()`, and
nothing reports it.

## DDD00068

**DDD_ModuleContracts makes a project its module's contracts where the project can name the attribute.**

```xml
<!-- Billing.Contracts.csproj: the analyzers of this version, and a DDDToolkit.Abstractions from before the attribute -->
<PropertyGroup>
  <DDD_ModuleContracts>true</DDD_ModuleContracts>   <!-- DDD00068 -->
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="DDDToolkit.Abstractions" Version="3.1.0" />
  <PackageReference Include="DDDToolkit.Analyzers" Version="..." />
</ItemGroup>
```

`DDD_ModuleContracts` set to `true` makes a project its module's contracts. The toolkit's generator writes
`[assembly: ModuleContracts]` into the project, and every public type of the project is published to the other
modules ([A contracts project](modules.md#a-contracts-project)). The attribute is declared in
`DDDToolkit.Abstractions`. The generator ships in `DDDToolkit.Analyzers`. A contracts project may reference these
two packages separately. If the project references a `DDDToolkit.Abstractions` older than the attribute, or none,
the generator cannot write the attribute:

```
DDD_ModuleContracts is true, and the project publishes nothing by it: the toolkit's generator writes
[assembly: ModuleContracts] from it, and no DDDToolkit.Abstractions the project references declares that
attribute. Reference the DDDToolkit.Abstractions of the same version as DDDToolkit.Analyzers, or mark each type
the other modules may name [ModuleContract].
```

The property is the project's explicit choice. Without DDD00068, the build would drop it with no message. The
first sign would be [DDD00022](#ddd00022) in every module that names one of the project's types. DDD00022 says
that the type is not published, which points away from the cause.

Reference the `DDDToolkit.Abstractions` of the same version as the analyzers. A project that references the
`DDDToolkit` package gets both at one version. If you cannot do that yet, mark each type the other modules may
name `[ModuleContract]`, and remove the property. A project whose property is anything but `true` is not
reported. A project that declares `[assembly: ModuleContracts]` itself is not reported either.

DDD00068 is reported at the project file, because no line of code is wrong. The project file is outside every
source file, so a severity in an `.editorconfig` section for `*.cs` files does not reach it. Set the severity with
`<NoWarn>` or `<WarningsAsErrors>` in the project file or a `Directory.Build.props`, or in a `.globalconfig` file
with `is_global = true`.

## DDD00069

**A module's row access contribution is listed by the project that runs the export.**

DDD00069 is reported in the project that turns the Supabase export on (`<SupabaseMigrationsExport>`), about a
module it references:

```csharp
// In the module's infrastructure project
[assembly: RowAccessContribution(typeof(UnitChangesWithItsPermissions))]

// In the project that runs the export: without this line, DDD00069
[assembly: UseRowAccessContribution(typeof(UnitChangesWithItsPermissions))]
```

A module that writes SQL of its own, a trigger or a policy, declares its class with
`[assembly: RowAccessContribution]`, as a package does. The module's assembly declares a module, with
`DDD_Module` or `[assembly: Module]`. So the declaration is an offer, and the build does not write the SQL by
itself. The SQL is your application's own. The project that runs the export decides what your database gets, with
`[assembly: UseRowAccessContribution(typeof(X))]`. The export makes each class it lists with `new X()`. The offer
makes a forgotten line visible. Without it, nothing would report that a module's trigger never reached the
migrations, and the trigger's limits on the rows would hold nowhere.

List the class, a class of yours derived from it, or a generic one closed with your types. The message says which
one the offer needs, because the export makes it with a constructor that takes no parameters. If this
application's database must not have the module's SQL, leave the line out. Suppress the warning for the project
with `<NoWarn>`, with the reason beside it. A package is an assembly that declares no module. A package writes its
SQL by itself and needs no line.

## DDD00070

**A member a library marks for a package's row access contribution is public.**

DDD00070 is reported in the library that declares the member, at the member:

```csharp
public static class ShopCatalogue
{
    [TenancyCatalogue]
    internal static ApplicationCatalogue Application { get; } = new(...);   // DDD00070
}
```

A package's row access contribution is made in the project that runs the Supabase export, from the members you
mark:

- `[TenancyCatalogue]`;
- `[TenancyOperators]`;
- `[MembershipRules<TMember>]`;
- the mark of another package, whose attribute says that it is a mark with `[ApplicationMark]`.

The exporting project finds the marks in the projects it references. Of a library, it sees what is public and
nothing else. It does not see an internal member, a member in an internal class, or a property whose getter is
not public. The export would then write Tenancy's policies from the default catalogue instead of yours, or leave
out a resource's functions, and the exporting project could not say why. Make the member public, in public types,
with a public getter.

An application, the program that runs the export or the host, may keep its own marks internal. No project
references an application for its marks.

## DDD00071

**What a package's row access contribution is made from is found once, and is what it takes.**

DDD00071 is reported in the project that turns the Supabase export on (`<SupabaseMigrationsExport>`), about a
package it references that declares itself a contributor. It is reported at the member you marked, when that
member is declared in the same project:

```csharp
public static class ShopCatalogue
{
    [TenancyCatalogue]
    public static ApplicationCatalogue Application { get; } = new(...);

    [TenancyCatalogue]
    public static TenancyCatalogue Built { get; } = TenancyCatalogue.Build(Application, ...);   // DDD00071: it takes an 'ApplicationCatalogue'
}
```

A package that declares itself a contributor writes its
[row level security](row-level-security.md#policies-a-package-ships) into your migrations. It makes the SQL from
the static properties and fields that you mark with the attributes it names: `[TenancyCatalogue]` for Tenancy's
catalogue, `[MembershipRules<TMember>]` for a resource's rules. The build reads them in the project that runs the
export, so a marked member must be:

| What | Why |
|---|---|
| marked once in all the projects the export sees, or once per type for a generic mark | the build cannot tell which of two your application runs with, so it writes neither instead of guessing |
| of a type the package takes: an `ApplicationCatalogue` for `[TenancyCatalogue]`, not the catalogue built from it | the package builds the catalogue itself, from your part and your modules' permissions, as your registration does |
| static, with a getter, in a type that is not generic | the code the build writes reads it as `Type.Member` |
| readable from the exporting project | the code the build writes is in that project |

A member that another project declares, and that is not public or is in an internal class, is not reported here.
The exporting project does not see it at all, so for that project nothing is marked. The project that declares the
member reports it instead, as [DDD00070](#ddd00070).

Policies written from another catalogue, or from other rules than your host runs with, would grant other access
than your application grants. So DDD00071 is an error, and the contribution is not made until it is fixed.

DDD00071 also reports a class that the package declares in a way the build cannot make:

- a class whose constructor takes a value without saying where to find it, with `[FromApplication]`;
- a generic class that nothing you mark closes.

The message then ends with "That is the package's to fix". Tell the package's authors, or leave its contribution
out with `[assembly: LeaveOutRowAccessContribution(typeof(X))]` and write those policies yourself.

## DDD00072

**A row access contribution a package writes is not listed again.**

DDD00072 is reported in the project that turns the Supabase export on (`<SupabaseMigrationsExport>`), on the line
that lists the contribution:

```csharp
// The project that runs the export, as the toolkit once asked for
[assembly: UseRowAccessContribution(typeof(ShopTenancyRowAccess))]   // DDD00072

public sealed class ShopTenancyRowAccess()
    : TenancyRowAccessContribution(TenancyCatalogue.Build(ShopCatalogue.Application, TenancyPermissionsOfModules.All));
```

A package that declares itself a contributor writes its row level security into your migrations because the
project references it. It uses what you mark. An application built against an earlier version may still list the
package's contribution, because the toolkit once asked for that line. If you list the package's contribution, a
class derived from it, or a closing of it over a type the package already closes it with, the export gets the same
SQL twice. The result is two functions of one name, or two contributions that each claim the same tables. The
export refuses both when it runs. Remove the line and the class, and mark what the class passed in:

```csharp
public static class ShopCatalogue
{
    [TenancyCatalogue]
    public static ApplicationCatalogue Application { get; } = new(...);
}
```

To write a package's SQL with a class of your own on purpose, leave the package's contribution out and keep
listing yours: `[assembly: LeaveOutRowAccessContribution(typeof(TenancyRowAccessContribution))]`. A contributor
that another referenced project declares, derived from a package's contribution, is the same SQL a second time. It
is reported too, and the message names the project that declares it.

## DDD00073

**What `[assembly: LeaveOutRowAccessContribution]` names is a contribution a package writes, and a context.**

DDD00073 is reported in the project that turns the Supabase export on (`<SupabaseMigrationsExport>`), at the line:

```csharp
[assembly: LeaveOutRowAccessContribution(typeof(AuditRowAccess), Context = typeof(string))]   // DDD00073: no context
[assembly: LeaveOutRowAccessContribution(typeof(UnitChangesWithItsPermissions))]                       // DDD00073: a module's own
```

The line keeps a package's SQL out of your migrations. With `Context`, it keeps the SQL out of one context's
access file only. The line names the contribution as the package declares it: open where it is generic, or one
closing of it that you mark. `Context` names a class derived from `DbContext`, closed where it is generic. A line
that names anything else leaves nothing out, and still looks as if it does. So it is reported, and nothing is
left out for it:

| The line names | What to do |
|---|---|
| a `Context` that is no class derived from `DbContext`, or a generic context left open | name the context whose access file the SQL is kept out of |
| a class that no referenced package declares as a contributor | name what the package declares, or remove the line |
| a closing that nothing you mark makes, such as `MembershipRowAccessContribution<Refund>` with no rules marked for `Refund` | remove the line. That closing is never written |
| a module's own contribution | remove the module's `[assembly: UseRowAccessContribution]` line instead. Only that line writes the module's SQL |

## DDD00074

**A design-time factory keeps the migration history where the application does.**

```csharp
public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
{
    public OrderingContext CreateDbContext(string[] args)   // DDD00074: 'OrderingContextFactory' makes its 'OrderingContext' without UseDDDToolkitDesignTime(), ...
        => new(new DbContextOptionsBuilder<OrderingContext>().UseNpgsql("Host=unused").Options);
}
```

`UseDDDToolkit` and `UseDDDToolkitCore` keep a context's migration history in the context's default schema,
beside its tables. For a model with `HasDefaultSchema("ordering")`, the history table is
`ordering."__EFMigrationsHistory"` ([The migration history](entity-framework.md#the-migration-history)).
`dotnet ef` and the [Supabase export](supabase.md) make the context through its design-time factory instead,
before any host exists. So the factory must do the same, with `UseDDDToolkitDesignTime()`.

Without that call, these record every migration in the provider's default schema, `public` or `dbo`:
`dotnet ef database update`, a migrations script, a migration bundle and the exported files. The running
application reads the history in the context's own schema. `Migrate()` then tries to apply the first migration
again, and fails on a table that exists. On Supabase, the start-up check reports every migration as missing.

```csharp
public OrderingContext CreateDbContext(string[] args)
    => new(new DbContextOptionsBuilder<OrderingContext>().UseNpgsql("Host=unused").UseDDDToolkitDesignTime().Options);
```

Add the call after the provider. The code fix adds it, before the options' `.Options`.

DDD00074 is reported in a project that references `DDDToolkit.EntityFramework`, at `CreateDbContext` of a
factory. It is reported when the factory's class calls none of `UseDDDToolkitDesignTime`, `UseDDDToolkit`,
`UseDDDToolkitCore` and `MigrationsHistoryTable`, in `CreateDbContext` or in a method, property or field of the
class that `CreateDbContext` reads. The analyzer reads one class at a time. So a factory whose options a helper of
another class makes is reported too. Add the call after the helper. The call adds nothing that the options have
already.

A context that the host does not wire with the toolkit, and whose history stays where Entity Framework keeps it,
names the table in the factory's options and in the host's options alike:
`MigrationsHistoryTable(HistoryRepository.DefaultTableName)`. Or suppress the warning.

A context marked `[SupabaseMigrations]` needs no factory written by hand. The build writes one beside it that
calls `UseDDDToolkitDesignTime()` wherever the project references `DDDToolkit.EntityFramework`. The analyzer does
not read generated code. See [Supabase](supabase.md#exporting-as-part-of-the-build).

## DDD00075

**Every class a package's use cases are named through has a name of its own where a project sees it.**

```
Shop.Host.csproj: warning DDD00075: 'SeatManager' is the name of more than one class in this project: it sees the
one 'Shop.Customers.Domain' has for the module Customers and the one 'Shop.Partners.Domain' has for the module
Partners. Give one of them a name of its own, with [assembly: TemplateFacadeName("SeatManager",
"CustomersSeatManager")] in 'Shop.Customers.Domain'.
```

The toolkit's generator writes Tenancy's managers, its access manager and `TenantToProvision` into the project that
declares your Tenancy classes. Each class is named after the package's own class: `SeatManager`, `TenancyAccessManager`
and so on ([Two modules with the classes](tenancy/your-module.md#two-modules-with-tenancys-classes)). An application
with two modules that each declare Tenancy's classes gets two classes of each name, one in each module's domain
project. While neither module references the other, neither project sees the other's class, and neither reports
anything. The two classes meet in every project that references both modules, first the host and your tests.
There, the first line that names `SeatManager` gets the compiler's CS0433, with no hint of what to do. So DDD00075 is
reported in such a project, at its project file, because no line of its code is wrong. It is reported once for each
name, so a host that sees two modules with Tenancy's classes gets seven warnings for the classes.

The stores are aliases, `TenancyStore` and `InvitationStore`, which the generator writes into every project that sees
the classes. A project that sees two modules' classes would get two aliases of one name. It gets neither, and
DDD00075 says so at its project file, once for each alias:

```
Shop.Host.csproj: warning DDD00075: 'TenancyStore' is the name of more than one alias in this project: it sees the
classes 'Shop.Customers.Domain' declares for the module Customers and the classes 'Shop.Partners.Domain' declares for
the module Partners, and would get an alias of ITenancyStore for each. Give one of them a name of its own, with
[assembly: TemplateFacadeName("TenancyStore", "CustomersTenancyStore")] in 'Shop.Customers.Domain'.
```

What happens depends on where the two classes meet. DDD00075 is a warning in every case. It stops no build unless
warnings are errors there. The compiler's error on the first line that names the class stops the build:

| Where the two classes meet | What the generator writes there | Where DDD00075 is reported | What your code gets while the warning stands |
|---|---|---|---|
| a project that references both modules' projects, such as the host or a test project | nothing. The classes belong to the modules | at the project file, because no line of it is wrong | Code that names neither class builds and runs. The first line that names `SeatManager` gets CS0433, an error |
| a project that declares one module's classes, and references another module's project that has a class of that name | its own class, as usual | on the project's first Tenancy class, and not also at the project file | The project's own code builds and names its own module's classes. The compiler takes the class from the project's own source and reports CS0436, a warning. Every project above it sees both classes, gets DDD00075 at its project file, and gets CS0433 on the first line that names the class |
| a project that would get two classes of one name itself: two packages' classes of one name, or a name you gave that another of its classes has | neither class, because the two cannot be told apart | on the project's first class | Code that names neither class builds. A line that names the class gets the compiler's error that no such type exists |

Give one of the classes a name of its own, with the line that the message writes out, in the project it names.
That is the project that declares that module's classes. If you named that class already, the message names the
line that names it. Give another name in that line. A second line for one class changes nothing. Name every class
of the module that the host sees twice, one line each:

```csharp
// Shop.Customers.Domain, beside [assembly: Module("Customers")] or Tenancy's switch
using DDDToolkit.Abstractions.Attributes;

[assembly: TemplateFacadeName("TenantManager", "CustomersTenantManager")]
[assembly: TemplateFacadeName("OrganizationManager", "CustomersOrganizationManager")]
[assembly: TemplateFacadeName("SeatManager", "CustomersSeatManager")]
[assembly: TemplateFacadeName("RoleManager", "CustomersRoleManager")]
[assembly: TemplateFacadeName("InvitationManager", "CustomersInvitationManager")]   // where the module declares an invitation
[assembly: TemplateFacadeName("TenancyAccessManager", "CustomersTenancyAccessManager")]
[assembly: TemplateFacadeName("TenantToProvision", "CustomersTenantToProvision")]
[assembly: TemplateFacadeName("TenancyStore", "CustomersTenancyStore")]
[assembly: TemplateFacadeName("InvitationStore", "CustomersInvitationStore")]                 // where the module declares an invitation
```

Every project above it then takes Customers' managers as `CustomersSeatManager` and the others, and Partners keeps
`SeatManager`. Any name that a class can have works, `CustomersSeats` too, as long as no namespace or type of yours
in the global namespace has it. The message only suggests a name.

If two packages' classes share a name in one project, the line names the one it means with its namespace:
`[assembly: TemplateFacadeName("Acme.Billing.UseCases", "BillingUseCases")]`. A type, a namespace or an alias of
the name that you keep in the global namespace yourself is your own way of naming the classes. DDD00075 does not
report it.

DDD00075 is a warning in all three cases. A host that names neither class builds and runs, and the compiler stops
the first line that names one. A project that builds with `TreatWarningsAsErrors` stops at DDD00075 itself, and in
the second case also at CS0436. To make DDD00075 an error everywhere, add it to `<WarningsAsErrors>`.

Where DDD00075 is reported at the project file, it is outside every source file. Set its severity there with
`<NoWarn>` or `<WarningsAsErrors>`, or in a `.globalconfig` file with `is_global = true`. An `.editorconfig`
section for `*.cs` files does not reach it. Where DDD00075 is reported on a class, an `.editorconfig` section
works as well.

## DDD00076

**`[assembly: TemplateFacadeName]` names, once, a class this project gets.**

```csharp
[assembly: TemplateFacadeName("SeatManagers", "ShopSeats")]   // DDD00076: ... changes nothing: no package this project
                                                              // references asks for a class of that name; it can name
                                                              // 'InvitationManager', 'OrganizationManager', ... and 'TenantToProvision'
```

The line gives your own name to a class that the generator writes for a package's `[TemplateFacade]`. It works in the project
where the generator writes the class: the project that declares the classes. A module whose classes are split over
two projects may put the line beside the module declaration in either project. The project that writes the class
reads the line in the projects of its module that it takes classes from. A line that changes nothing looks as if
it changed the name. So it is reported at the line, and the class keeps its name:

| The line | What to do |
|---|---|
| names no class that a referenced package asks for | write the class name as the message lists it: the package's class name without its type parameters |
| names two classes: two packages' classes of one name | name the class you mean with its namespace, `"DDDToolkit.Supporting.Tenancy.Managers.SeatManager"` |
| is in a project that declares none of the classes, such as an application project above the domain project | move it into the project that declares the classes. The class is written and named there |
| names a class that an earlier line names already, in this project or in a project of its module that it takes classes from | keep one line. This project's line wins, then the line of the first other project by name |
| gives a name that no class can have, such as a keyword or a name with a space | give a name that a class can have |
| gives a name that a namespace or a type in the global namespace already has, such as the module's root namespace, `Customers` for `Customers.Domain` | give another name. The class cannot have that name too, and the projects above would only get CS0234 |

A line in another project of the module is reported in the project that writes the class, on the line in that
project or on its first class. The message names the project that the line is in. An alias of the name at the top
of one of the project's own files is your own way of naming the classes. The generator leaves it alone and reports
nothing.

## DDD00077

**A request says what it requires of its caller.**

```csharp
[AccessRequests]
public partial interface IBillingRequest : IRequireAccess;

public sealed record CloseInvoice(InvoiceId Invoice) : IBillingRequest;   // DDD00077: says nothing of what it requires
```

Every command and query says who may send it. The module's checks hold the caller to that before the handler
runs. A request says it in one of two ways
([What a request declares](access-requirements.md#what-a-request-declares)):

- with an attribute: `[SignedIn]`, or `[RequiresPermission(permission)]` with a marker of where the permission is
  held;
- by answering `RequiredAccess` itself, where the requirement is computed.

If the module's interface is `partial`, the generator implements `RequiredAccess` in it. The compiler then no
longer requires a request to implement the property. So a request that does neither is reported here. If such a
request were sent, the access check would refuse it every time, with an exception that says it declares
nothing. A
request that anyone may send says `[AllowAnonymous]`.

```csharp
[RequiresPermission(BillingPermissions.Close)]
public sealed record CloseInvoice(InvoiceId Invoice) : IBillingRequest;
```

A request in another project than its interface is reported too. The line that the generator writes into the
interface knows only the requests of the interface's own project. So such a request answers `RequiredAccess`
itself. An abstract request is not reported. The requests that derive from it are reported. If the interface is
not `partial`, the compiler's own CS0535 reports the same problem.

A module whose requests nearly all require the same thing says it once, on its interface:
`[AccessRequests, InTenant]`. A request of that module that states nothing then takes this default, on a line of
its own in the generated switch, and is not reported. A request of another project is not reported either. The
interface's last arm answers it with the default ([A module's default](access-requirements.md#a-modules-default)).
The default is the module's choice, and it has a cost. A command of the module that forgets its
`[RequiresPermission]` requires what the default says, and the build reports nothing.

## DDD00078

**A request says what it requires in one way.**

```csharp
[SignedIn]                                                          // DDD00078: and the property as well
public sealed record MyInvoices : IBillingRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.AllowAnonymous();
}
```

C# takes the request's own property over the line that the generator writes from the attribute. So the attribute
does nothing, while a review reads it as the requirement. That is why DDD00078 is an error. Keep one of the two:

- the attribute, where the requirement reads no value of the request;
- the property, where it computes the requirement from the request's values.

The same holds for an interface between the request and its module's interface that gives `RequiredAccess` a
default of its own, such as `interface IPublicBillingRequest : IBillingRequest`. C# takes that default over the
module's. The message then says that the request answers `RequiredAccess` through that interface. A request that
derives from one that answers `RequiredAccess` answers it too, so it carries no requirement attribute of its own.

## DDD00079

**`[AllowAnonymous]` goes with no other requirement attribute.**

```csharp
[AllowAnonymous, RequiresPermission(BillingPermissions.Close)]                   // DDD00079: open beside closed
public sealed record CloseInvoice(InvoiceId Invoice) : IBillingRequest;
```

Requirement attributes stacked on one request all hold, as several `[Authorize]` attributes do in ASP.NET Core
([Stacked attributes are all](access-requirements.md#stacked-attributes)). `[AllowAnonymous]` says that
anyone may send the request, including a caller who did not sign in. A requirement beside it limits who may send
the request. The two contradict each other, and a review cannot tell which one was meant. So DDD00079 is an error
beside any other requirement attribute, `[RequiresPermission]` and `[OwnOnly]` included, and the generator writes
no line for the request. Keep the attribute that the request needs. A request that anyone may send carries
`[AllowAnonymous]` alone.

A request also holds to the attributes of the requests it derives from, theirs first. So `[AllowAnonymous]` on a
request that derives from one with `[SignedIn]` is the same contradiction. DDD00079 reports it at the derived
request, and names the base's attribute. A derived request cannot take back what its base requires.

Two other requirement attributes on one request are not a mistake. They stack, and both hold. An attribute that
its neighbour already requires is [DDD00084](#ddd00084), a warning.

## DDD00080

**A marker on a request says where the permission of its `[RequiresPermission]` is held.**

```csharp
[InTenant]
public sealed record OpenLedger([AtUnit] OrganizationUnitId Unit) : IBillingRequest;                  // DDD00080: no permission to hold there

[RequiresPermission(BillingPermissions.Move)]
public sealed record MoveLedger([AtUnit] OrganizationUnitId From, [OnResource] LedgerId Ledger) : IBillingRequest;   // DDD00080: two places
```

`[RequiresPermission(permission)]` names the permission. One marker on one of the request's members says where the
caller holds it ([Where the permission is held](access-requirements.md#where-the-permission-is-held)):

- no marker: for the whole tenant;
- `[AtUnit]`: at that unit or above it;
- `[OnResource]`: on that resource, through its members. `[ExpectedVersion]` beside `[OnResource]` gives the
  version the caller read.

DDD00080 is reported at the marker or the request:

| It | Because |
|---|---|
| marks a member, and requires no permission | a marker says where the permission of `[RequiresPermission]` is held. Without a permission, it marks nothing |
| marks two places for its permission, or `[AtUnit]` with `[OnResource]` | a permission is held in one place. If the place depends on the request, compute the requirement as `RequiredAccess` |
| marks `[ExpectedVersion]` and no member `[OnResource]` | the version belongs to the resource it goes with |
| marks one argument twice, such as two `[ExpectedVersion]` | the call takes each argument once |
| marks a parameter of a class's primary constructor | the requirement cannot read such a parameter. Mark the property. A positional record's parameter is its property, and is fine |
| marks a private or protected property | the requirement is read in the module's interface, which needs a public or internal getter |
| requires a permission and marks nothing, where no package says where such a permission is held | Tenancy says it with `[assembly: PermissionWithoutMarker]`: for the whole tenant. A project with Membership alone, or with two such packages, needs a marker |
| requires two permissions: its own `[RequiresPermission]` and one of a request it derives from | the attributes of a request and of its base all hold, and one marker says where for one permission. Write two permissions as `AccessRequirement.All` of the two, in `RequiredAccess` |
| is a module's interface whose default is `[RequiresPermission(permission)]`, where no package says where a permission without a marker is held | a default reads no member of a request, so its permission has no marker |
| marks a member, requires no permission of its own, and its module's default is a permission | the default's permission reads no member of a request. A marker goes with the request's own `[RequiresPermission]` |

An abstract request is a shape that the requests deriving from it share, such as a record that carries the unit.
So an abstract request is not reported for a marker without a permission. Each request that derives from it names
its permission. A derived request that names none is reported where it is declared, for the marker it inherits.

A record's positional parameter is the property that its base record declares, if the base record has it. So
`[AtUnit]` goes on the derived record's parameter as well:

```csharp
public abstract record UnitCommand(OrganizationUnitId Unit) : IBillingRequest;

[RequiresPermission(BillingPermissions.OpenLedger)]
public sealed record OpenLedger([AtUnit] OrganizationUnitId Unit) : UnitCommand(Unit);
```

## DDD00081

**A marker is on a member of the id it marks.**

```csharp
[RequiresPermission(TenancyPermissions.SeatsManage)]
public sealed record MakePlacement(SeatId Seat, [AtUnit] SeatId Unit) : ITenantsRequest;           // DDD00081: a seat's id, no unit's

[RequiresPermission(BillingPermissions.Read)]
public sealed record ListLedgers([AtUnit] OrganizationUnitId? Unit) : IBillingRequest;              // DDD00081: may be null
```

The call that a marker stands for takes any id. So a unit marker on a seat's id compiles and lets nobody through.
The package's access check refuses a case over an id that it does not decide. The marked member must have the right
type:

- `[AtUnit]` marks your unit id: the id that your class declared with `[OrganizationUnit<TUnitId>]` is declared
  over.
- `[OnResource]` marks the id of a resource with members: the aggregate root that a class declared with
  `[Member<TId, TMemberId, TRoleId, TResource>]` names.

The package says which id a marker takes, with `[PermissionMarker(..., IdOf = ...)]`. DDD00081 is reported where
the project sees such classes, as a module's application project sees its domain's classes. Where the project sees
none, nothing can be told, and the access check reports the problem when the request is sent.

A member that may be null is a requirement computed from whether the value is given. Write it as
`RequiredAccess`: `Unit is { } unit ? TenancyAccess.AtUnit(permission, unit) : TenancyAccess.AtTenant(permission)`.
`[ExpectedVersion]` marks a member of the type that the call takes there: a `long?` or a `long`.

## DDD00082

**A requirement for queries only is on a query.**

```csharp
[SeenWith<DocumentId>(DocumentPermissions.View)]
public sealed record ArchiveDocuments : ICommand, IDocumentsRequest;                                // DDD00082
```

Some requirements are for queries only. Their packages say so with `[StatesRequirement(..., QueriesOnly = true)]`.
Membership's `[SeenWith<TResourceId>(permission)]` is one. It refuses nobody for lack of the permission. It lets every
signed-in caller through, also one that holds nothing, and says what the query's own statement filters by. That
filter is the check. On a command, the handler would run unchecked.

DDD00082 reports a message of the Mediator library that is not a query and carries such an attribute:

- a command, `ICommand` or `IStreamCommand`;
- a plain `IRequest` or `IStreamRequest`, which may change as much as a command does.

What to do instead:

| The request | Instead |
|---|---|
| only reads | make it a query, `IQuery` or `IStreamQuery` |
| changes something, `[SeenWith]` on it | require the permission where it acts, `[RequiresPermission(permission)]` with `[OnResource]` on the resource |

Tenancy's `[Operator]` is no requirement for queries only. On a command, the pipeline runs the handler as operator work
([An operator's request](access-requirements.md#an-operators-request)).

A request of another dispatcher is not reported, because the generator cannot tell whether it is a command or a
query. The same requirement answered as `RequiredAccess` on a command is not reported either. A test covers both
cases ([Holding it with a test](access-requirements.md#testing-the-requirements)).

## DDD00083

**A request's requirement attributes are read where its module's interface is partial.**

```csharp
[AccessRequests]
public interface IBillingRequest : IRequireAccess;                  // not partial

[SignedIn]                                                          // DDD00083: nothing turns this into what it requires
public sealed record MyInvoices : IBillingRequest;
```

The generator turns a request's attributes into `RequiredAccess` at compile time, one line per request. It writes
the line into the interface marked `[AccessRequests]` that the request implements, declared `partial` in the same
project. Without such an interface, nothing reads the attributes, and the request looks as if it declares what it
requires. DDD00083 reports each case, with what to change:

| The request | Change |
|---|---|
| implements an interface that is not `partial` | declare it `public partial interface IBillingRequest : IRequireAccess;` |
| implements no interface marked `[AccessRequests]` | implement the module's request interface |
| implements one declared in another project | declare the request in that project, or answer `RequiredAccess` |
| has type parameters | answer `RequiredAccess`. A generated line names one closed type |
| is nested private in another type | make it internal, or answer `RequiredAccess`. The interface cannot name a private nested type |
| implements two module interfaces that are both `partial` | answer `RequiredAccess`, or implement one of them. Each interface implements the property, so C# takes neither (CS8705) |

A requirement attribute on an interface is the default of a module's requests
([A module's default](access-requirements.md#a-modules-default)). The generator reads it only on the module's
interface, marked `[AccessRequests]` and `partial`. On a module's interface that is not `partial`, the
attribute is reported at the interface, with the line to write. On any other interface, such as one between the
module's interface and its requests, nothing reads the attribute either. Such an interface answers
`RequiredAccess` itself where its requests share a requirement. C# takes that over the module's default.

## DDD00084

**A requirement attribute beside another adds what that one does not require already.**

```csharp
[InTenant]                                                          // DDD00084: the permission requires it already
[RequiresPermission(BillingPermissions.Close)]
public sealed record CloseTheBooks(int Year) : IBillingRequest;

[SignedIn, Operator]                                                // DDD00084: an operator is signed in
public sealed record AllLedgers : IQuery<IReadOnlyList<Ledger>>, IBillingRequest;

[OwnOnly, SignedIn]                                                 // DDD00084: only a signed-in caller has an identity
public sealed record CloseAccount([Own] Guid User) : IAccountsRequest;
```

Stacked requirement attributes all hold. So an attribute that its neighbour already requires adds nothing:

- Only a seat in the tenant, or system work there, holds a permission of Tenancy's, with or without `[AtUnit]`.
  So `[InTenant]` beside such a permission adds nothing.
- `[Operator]` lets through only a signed-in caller with an operator's token role. So `[SignedIn]` beside it adds
  nothing.

The extra attribute changes nothing that is checked, and the generator writes the line. A reader takes it for a
second condition and looks for what it adds. So DDD00084 is a warning. Remove the attribute.

A package says what its requirement already requires, with `Implies` on its mark: `[StatesRequirement]`,
`[PermissionMarker]` or `[PermissionWithoutMarker]`. A module's own attribute is held to the same rule. The
package of a type says what the caller's own of that type requires, with `Implies` on `[assembly: CallersOwn]`:

- A `Guid` is a signed-in caller's identity, so it implies `[SignedIn]`.
- Tenancy's seat id is a seat in the tenant, so it implies `[InTenant]` and `[SignedIn]`.

Beside `[OwnOnly]` with `[Own]` on such a member, these attributes are reported. Beside a permission with
`[OrOwn]`, only what both the permission and the caller's own require counts.

These are reported too:

- An attribute that a request's base request carries already. The base's attributes hold for the request as
  well.
- A request's own attributes that say what its module's default says, call for call. Without them, the request
  takes the default and requires the same ([A module's default](access-requirements.md#a-modules-default)). They
  are reported only on a request that nothing derives from, because a request that derives from it holds to those
  attributes beside its own.
- On the module's interface, an attribute of the default that another attribute there already requires. It is
  reported as on a request.

Examples:

| Beside | Reported | Because |
|---|---|---|
| `[SignedIn]` beside `[RequiresPermission]` | no | system work holds every permission and has no sign-in. So the two together require more than the permission alone |
| `[InTenant]` beside `[RequiresPermission]` with `[OrOwn]` on the seat id | yes | both ways through need a caller in the tenant: the caller's own seat, or a permission of Tenancy's |
| `[InTenant]` beside `[RequiresPermission]` with `[OrOwn]` on a `Guid` | no | a verified identity is no seat, so the caller's own way through does not require the tenant |
| `[SignedIn]` beside `[RequiresPermission]` with `[OrOwn]` on the seat id | no | system work holds the permission and has no sign-in |
| `[InTenant]` on `CloseTheBooks : Closing`, where `Closing` is `[RequiresPermission(permission)]` | yes | the base's permission holds for the derived request too |
| `[InTenant]` on a sealed request of a module whose default is `[InTenant]` | yes | without it the request takes the default, which is the same |
| `[SignedIn, InTenant]` on a request of a module whose default is `[InTenant]` | no | it requires more than the default: a signed-in caller in the tenant |
| `[InTenant]` on a request another derives from, in that module | no | the derived request holds to this attribute beside its own, and would require less without it |
| `[InTenant]` beside `[RequiresPermission(permission)]` on the module's interface | yes | as on a request, the default's permission requires it already |

## DDD00085

**`[OrOwn]` goes beside `[RequiresPermission]`, and `[Own]` with `[OwnOnly]`.**

```csharp
[InTenant]
public sealed record RenameSeat([OrOwn] SeatId Seat) : ITenantsRequest;                              // DDD00085: no permission to be the alternative of

[RequiresPermission(TenancyPermissions.SeatsManage)]
public sealed record MoveSeat([OrOwn] SeatId Seat, [AtUnit] OrganizationUnitId To) : ITenantsRequest;   // DDD00085: beside [AtUnit]
```

A request about the caller's own declares it in one of two ways
([Yours, or the permission](access-requirements.md#the-callers-own-or-a-permission)):

- `[OrOwn]` on the member, beside `[RequiresPermission(permission)]`: the caller's own, or the permission.
- `[Own]` on the member, with `[OwnOnly]` on the request: the caller's own alone.

DDD00085 is reported at the marker or the request:

| It | Because |
|---|---|
| marks `[OrOwn]`, and requires no permission | `[OrOwn]` makes the caller's own an alternative to the permission. For the caller's own alone, use `[OwnOnly]` with `[Own]` |
| marks `[Own]`, and is not `[OwnOnly]` | `[Own]` names the value for a request about the caller's own alone, which `[OwnOnly]` declares |
| carries `[OwnOnly]`, and marks no member `[Own]` | the request names no value to compare with the caller's own |
| marks one member `[Own]` and `[OrOwn]` | `[Own]` means the caller's own alone, and `[OrOwn]` means the caller's own or the permission |
| marks two members `[OrOwn]`, or two `[Own]` | the caller's own is one value. A request about one of two values computes its requirement as `RequiredAccess` |
| marks `[OrOwn]` beside `[AtUnit]` | the unit says where the permission is checked, and the request acts on something else. A caller could send a unit where it holds the permission, together with another person's seat. A permission held at one unit would then act on a seat anywhere |
| marks a parameter of a class's primary constructor, or a private property | the line is written in the module's interface, which reads a property with a public or internal getter |

`[OrOwn]` beside `[OnResource]` is fine, because the resource is what the request acts on. Membership says so with
`OwnAlternative` on its `[PermissionMarker]`. `OwnAlternative` names the case that the caller's own way through
still meets on the resource: `MemberAccess.Sees`. The caller sees the resource, and the access check reads the resource,
compares its version and keeps its hold
([The caller's own on a resource](access-requirements.md#the-callers-own-on-a-resource)). A package's marker of
where a permission is checked, whatever the request acts on, names no `OwnAlternative`. `[OrOwn]` beside such a
marker is reported in the same way as beside `[AtUnit]`.

## DDD00086

**`[OrOwn]` and `[Own]` mark a member of a type something says the caller's own of.**

```csharp
[RequiresPermission(ProfilePermissions.Edit)]
public sealed record EditProfile([OrOwn] UserId User) : IProfilesRequest;                          // DDD00086: nothing says the caller's own UserId

[OwnOnly]
public sealed record LeaveTenant([Own] SeatId? Seat) : ITenantsRequest;                             // DDD00086: may be null
```

The access check compares the value that the request names with the caller's own value of the same type. It does
this before the handler runs, so the request does not need to know who sends it. An `ICallersOwn<TValue>` says what the
caller's own value of a type is:

- the core's, for a `Guid`: the caller's verified identity, the id that its token names;
- Tenancy's, for your seat id: the caller's seat in the tenant of the request;
- yours, for a kind of your own: a small implementation beside your requests, which the host registers.

```csharp
public sealed class CallersUser : ICallersOwn<UserId>
{
    public UserId? Find(Caller caller) => caller.UserId is { } id ? new UserId(id) : null;
}

services.AddCallersOwn<UserId, CallersUser>();
```

On a member of a type without such an implementation, the access check would refuse the request every time it is
sent. So
DDD00086 reports the member, and the generator writes no line. The generator finds an implementation in the
requests' project, or a public one in a project that the requests' project references. A package names its types
with `[assembly: CallersOwn(...)]`. A package may name its type by the template that an application declares its
class with, as Tenancy names its seat id. If the project sees no class declared with that template, nothing can be
told, and the access check reports the problem when the request is sent.

A member that may be null names no value when it is left out. Write a requirement computed from whether the value
is given as `RequiredAccess`. The caller's own is a value compared with the caller's: a struct that is
`IEquatable` of itself, such as a `Guid` or an id declared with `[EntityId]`. A member that is a `string`, a class
or an enum has no own value, whatever the project sees, and is always reported. `[OrOwn] string Name` is an
example.

The id of an aggregate root that marks its owner with `[Owner]` is an exception, even though nothing says the
caller's own of the id. `[Own] BoardId Board` is then about the board's owner, and the generated line is
`AccessRequirement.OwnerOf(request.Board)` ([The owner of an aggregate](access-requirements.md#the-owner-of-an-aggregate)).
The id of an aggregate that marks no owner is reported as above, and the message says where to mark the owner. If
an `ICallersOwn` exists for the id itself, that implementation wins, and the owner is not read.

## DDD00087

**`[Owner]` marks the one owner of an aggregate root.**

```csharp
[Entity<ColumnId>]
public sealed partial class Column
{
    [Owner] public UserId Keeper { get; private set; }                       // DDD00087: an entity inside an aggregate
}

[AggregateRoot<BoardId>]
public sealed partial class Board
{
    [Owner] public UserId Owner { get; private set; }

    [Owner] public UserId CoOwner { get; private set; }                      // DDD00087: an aggregate has one owner
}
```

The aggregate states who owns it. That is a fact about what the aggregate is. A request about the owner's own
names the aggregate by its id. The access check reads the owner column of the one row that the id names, as the caller
([The owner of an aggregate](access-requirements.md#the-owner-of-an-aggregate)). So `[Owner]` goes on one instance
property of the aggregate root itself. DDD00087 is reported where the property is declared:

| It is on | Because |
|---|---|
| an entity inside an aggregate | a request names the root by the root's id, and the access check reads the root's row |
| a type that is no aggregate root | no request names it by an id whose row the access check reads |
| a static property | the owner is a column of each row |
| a second property of one aggregate | an aggregate has one owner. Several owners are the members of a resource with members, which [Membership](membership/overview.md) keeps |

Of two `[Owner]` properties, the first in the order they are written is the owner, and the second is reported.

DDD00087 is also reported at a request whose id the project sees as the id of two aggregate roots that each mark an
owner. The request cannot say which aggregate it is about. Give each aggregate an id of its own.

## DDD00088

**`[Owner]` marks a property of a type the caller has an own of.**

```csharp
[AggregateRoot<BoardId>]
public sealed partial class Board
{
    [Owner] public string Owner { get; private set; }                        // DDD00088: a string has no own
}

[OwnOnly]
public sealed record ArchiveBoard([Own] BoardId Board) : IBoardsRequest;     // DDD00088: nothing says what the caller's own UserId is
```

The access check compares the owner that the row holds with the caller's own value of the same type, as
[DDD00086](#ddd00086) compares a value that the request names. The owner's type must be one of these:

- a `Guid`: the caller's verified identity;
- your seat id: the caller's seat, from Tenancy;
- a kind of your own, with a small `ICallersOwn<TValue>` beside your requests, which the host registers with
  `services.AddCallersOwn<UserId, CallersUser>()`.

An owner that may be null is fine. A row whose owner is null belongs to nobody, and nobody comes through as its
owner.

A `string`, a class or an enum can never be compared, whatever a project sees. So DDD00088 reports such an owner
where the property is declared.

Whether an `ICallersOwn` exists for the owner's type can only be told where the requests are. Your
`ICallersOwn` is declared beside them, and the domain does not see it. So a request that names the aggregate is
reported there, at its marker, where the project can tell that none exists. The generator writes no line for such
a request. If a package names the owner's type by a template whose classes the project does not see, nothing can
be told, and the access check reports the problem when the request is sent.

## DDD00089

**A module's default requirement is one any of its requests may take.**

```csharp
[AccessRequests, AllowAnonymous]                                    // DDD00089: a request that forgets its requirement would let anyone in
public partial interface IBillingRequest : IRequireAccess;

[AccessRequests, SeenWith<DocumentId>(DocumentPermissions.View)]     // DDD00089: a command that says nothing would take it
public partial interface IDocumentsRequest : IRequireAccess;
```

A requirement attribute on a module's interface is the default of its requests. A request that states nothing of
its own takes the default, on a line of its own in the generated switch
([A module's default](access-requirements.md#a-modules-default)). So every request that forgets its requirement
takes the default, including a command that forgets its permission. A default must therefore be a requirement
that any of the requests may take. DDD00089 is reported at the attribute:

| On the interface | Because | Instead |
|---|---|---|
| `[AllowAnonymous]` | a request that forgot its requirement would let anyone in. Without a default, the access check refuses such a request | `[AllowAnonymous]` on each request that anyone may send, where a review sees it |
| `[SeenWith<TResourceId>(permission)]`, any attribute its package declares for queries only | a command of the module that states nothing would take it, and on a command it checks nothing that the command changes ([DDD00082](#ddd00082)) | the attribute on each query |
| `[OwnOnly]` | it reads the member of a request marked `[Own]`, and a default reads no member | `[OwnOnly]` on each request about the caller's own |
| an attribute whose class's `AttributeUsage` leaves out interfaces, such as Tenancy's `[Operator]` | the attribute is declared for requests only. The compiler reports this as well (CS0592) | for a module's own attribute, add `AttributeTargets.Interface` to its class's `AttributeUsage`, if it may be a default. Put `[Operator]` on each request of the application's staff |

A refused default is not used. The interface's last arm then stops a request that states nothing, as it does
without a default. The attributes that a package ships and that may be a default already allow an interface:
`[SignedIn]`, `[InTenant]`, and `[RequiresPermission(permission)]`. As a default, the permission is held for the
whole tenant, because a default reads no member and has no marker.

## DDD00090

**The names of resources with members are those of the newest access file.**

```text
warning DDD00090: Membership names changed since the last access file. Gone: boards (was Board). New: whiteboards
(Whiteboard). Codes clients read change with them, and the database gets new functions. A class you renamed keeps
its old name with [Members("...")].
```

A resource with members is [named after its class](membership/resources-and-members.md#names). Its refusal codes
start with that name, `boards.already-member`, and its five functions in the database are named after it,
`boards_i_see`. Renaming the class renames both. A client that branches on a refusal code then reads another code.
The next access file writes new functions and drops the old ones.

The Supabase export that the build runs compares the names of the resources it writes with the names that the
newest access file of each module recorded, with the class each name came from. In that build, it reports once
which names are gone and which are new. It does not guess which name became which.

A class renamed on purpose that should keep its name says so: `[Members("Boards")]`. A class that should take the
new name needs nothing more. The warning is not given again once the new access file exists. Without the export,
with `SupabaseMigrationsExport` unset, nothing compares the names, and no warning is given.

## DDD00091

**[Members] names a resource with what can be converted to a code and a function's name.**

```csharp
[Members("Crane boards")]                              // DDD00091: a space
[AggregateRoot<BoardId>]
public sealed partial class Board;
```

The name that `[Members]` gives is written in two forms, as a class name is:

- in lower case with a dash between words, for the resource's refusal codes: `crane-boards.already-member`;
- with an underscore between words, for its functions: `crane_boards_i_see`.

The generator converts the name in the same way as a class name, and splits PascalCase into words. It takes the
name as it is, and does not make it plural. Write the name as a class name, `CraneBoards`, or as it comes out,
`crane-boards`. Use letters and digits, with `-` or `_` between words, starting with a letter. A space, another
symbol, a digit or a separator first, or a separator last cannot be converted. The generator then writes nothing
for the resource. Its rules, `MembershipRules.For<Board>`, do not compile until the name is fixed
([Names](membership/resources-and-members.md#names)).

## DDD00092

**A permission is a public constant of a [Permissions] class, with a level.**

```csharp
[Permissions]
public class BoardPermissions                                          // DDD00092: not static
{
    public const string View = "boards.view";                          // DDD00092: no level

    [On<Board>] internal const string Edit = "boards.edit";            // DDD00092: no public const string

    [On<Board>] public const string Comment = "boards.view";           // DDD00092: 'View' declares it already
}

public static class BoardPermissions
{
    [AtTenant] public const string Manage = "boards.manage";           // DDD00092: the class is not marked [Permissions]
}
```

A module declares its permissions in a static class marked `[Permissions]`. Every public `const string` of the
class is a permission. A request names it in `[RequiresPermission]`, and a role gives it. Each permission carries
its **level**: the attribute that says where the permission is asked. The levels are Tenancy's `[AtTenant]` and
`[AtUnit]`, and Membership's `[On<TResource>]`
([The settings of a permission](membership/permissions-and-roles.md#the-settings-of-a-permission),
[A module states its permissions once](tenancy/roles-and-permissions.md#declaring-a-modules-permissions)). The
packages' generators read this class and nothing else:

- Tenancy's generator puts every permission into its catalogue.
- Membership's generator puts every permission on a resource into the rules of that resource.

So each case below would be read as something else, or not at all, and is refused:

| Reported | Because |
|---|---|
| a class marked `[Permissions]` that is not static | it holds constants and nothing else |
| a public `const string` of such a class without a level | nobody could tell where it is asked, so no catalogue and no rules would hold it |
| a level on a field that is no public `const string` | a request names a permission by its constant, also from another module, so the constant must be public |
| a level on a constant of a class not marked `[Permissions]` | nothing reads that class |
| one permission declared twice in one class | a permission is declared once. The second constant is the mistake |

## DDD00093

**A permission's level fits what it is asked on.**

```csharp
[Permissions]
public static class BoardPermissions
{
    [On<Column>] public const string Move = "columns.move";                   // DDD00093: no member class names Column

    [On<Board>(EveryMember = true)] public const string View = "boards.view";
    [On<Board>(EveryMember = true)] public const string Read = "boards.read";  // DDD00093: one permission every member holds
}
```

`[On<TResource>]` says that a permission is asked on one resource with members, and how members hold it there.
`TResource` is the aggregate root that a member class names, `[Member<TId, TMemberId, TRoleId, TResource>]`. On
another class, the permission would be asked on something that has no members, and its rules could not be made.

Of the permissions on one resource:

- At most one is held by every member (`EveryMember`). That permission is also the one that sees the resource
  from above.
- At most one locks the members (`LocksMembers`), and at most one locks the owner (`LocksOwner`). The member lock
  and the owner lock each ask for one permission.

A permission declared on one resource by two constants is reported as well. Each case is reported where the
permission is declared, among the permissions that the project sees.

## DDD00094

**The rules of a resource are made from the permissions declared on it.**

```csharp
public static MembershipRules Rules { get; } = MembershipRules.For<Board>(   // DDD00094: no permission on Board
    Roles.Custom());
```

`MembershipRules.For<TResource>` makes the rules of a resource from the permissions declared on it. These are the
constants of a `[Permissions]` class marked `[On<TResource>]`, in the project that makes the rules or in a project
it references. The package's generator passes them in when the project is first used. Rules without a permission
give the owner nothing and every role nothing, and the call would throw when the rules are made. So DDD00094
reports the problem where the rules are made. Declare the permissions asked on the resource:
[The settings of a permission](membership/permissions-and-roles.md#the-settings-of-a-permission).

## DDD00095

**A module's permissions are declared in a [Permissions] class, not on a list of its own.**

```csharp
public static class OrderingList
{
    [TenancyPermissions]                                                       // DDD00095
    public static IReadOnlyList<Permission> Permissions { get; } = [new("orders.view", "Ordering", "See the orders")];
}
```

A module declares its permissions once, as the constants of a static class marked `[Permissions]`. Each constant
has its level. Its `Description` or its summary says what it lets somebody do. Tenancy's generator writes the
module's list for the catalogue from that class, under the module that `DDD_Module` or `[assembly: Module]` names,
and marks the list `[TenancyPermissions]`. The project that composes the modules collects every such list
([A module states its permissions once](tenancy/roles-and-permissions.md#declaring-a-modules-permissions)). A list
that a module marks with `[TenancyPermissions]` by hand repeats the permissions, apart from the constants that a
request requires. DDD00095 refuses it. Declare each permission as a constant instead:

```csharp
[Permissions]
public static class OrderingPermissions
{
    [AtTenant(Description = "See the orders")]
    public const string View = "orders.view";
}
```

## DDD00096

**[OrOwn] beside a permission the owner of the resource holds already says nothing more.**

```csharp
[RequiresPermission(BoardPermissions.Edit)]
public sealed record RenameBoard([OnResource, OrOwn] BoardId Board, string Name) : IBoardsRequest;   // DDD00096
```

`[OrOwn]` on a request's member lets the caller through when the member names the caller's own, or when the
caller holds the permission of `[RequiresPermission]`. On the id of a resource with members, beside `[OnResource]`,
the permission is asked on that resource. The resource's owner holds every permission declared `[On<TResource>]`
with `Owner = true`, because the owner owns the resource. So the permission already lets the owner through.
`[OrOwn]` adds nothing, and only makes the request look as if it did. Remove it.

`[Own]` and `[OrOwn]` are meant for two cases:

- an aggregate with an `[Owner]` and no members
  ([The owner of an aggregate](access-requirements.md#the-owner-of-an-aggregate));
- a permission that the owner does not hold, declared with `Owner = false`.

DDD00096 is a warning, because the request does the same with or without the attribute.

## DDD00097

**[At] marks the one place an aggregate root hangs at.**

```csharp
[AggregateRoot<BoardId>]
public sealed partial class Board
{
    [At] public OrganizationUnitId UnitId { get; private set; }

    [At] public OrganizationUnitId? FormerUnitId { get; private set; }     // DDD00097: a second, and may be empty
}

[Entity<ColumnId>]
public sealed partial class Column
{
    [At] public OrganizationUnitId UnitId { get; private set; }            // DDD00097: an entity inside an aggregate
}
```

`[At]` marks the property of an aggregate root that says where the aggregate hangs: the unit of an organization that
a board belongs to, or the bay a crate stands in. This is a fact of the domain. The rules of a resource with members
read it where they let the resource be reached from above. A permission held at that place, or above it, reaches the
aggregate ([Beside an organization](membership/beside-an-organization.md#reach-from-above)).

So `[At]` goes on one instance property of an aggregate root, on the root itself and never on an entity inside
it. That property is never empty. An aggregate that hangs nowhere could not be reached from above, and reaching
from above is what `[At]` is for. DDD00097 refuses each of these, where the property is declared:

- a static property;
- a property on a type that is no aggregate root;
- a second `[At]` property;
- a nullable property.

Keep one `[At]` property, on the root, of a type that is never empty:

```csharp
[AggregateRoot<BoardId>]
public sealed partial class Board
{
    [At] public OrganizationUnitId UnitId { get; private set; }            // the one place, never empty

    public OrganizationUnitId? FormerUnitId { get; private set; }          // a plain property
}
```

`[At]` is Membership's marker, in the namespace `DDDToolkit.Supporting.Membership`. It ships in the package
`Temp.DDDToolkit.Supporting.Membership.Abstractions`, which references `DDDToolkit.Abstractions` alone and no Entity
Framework, so a domain project references it for the marker. Membership's generator reports DDD00097. That generator
ships in the package `Temp.DDDToolkit.Supporting.Membership`, which references the markers' package. A project that
declares the aggregate and references only the markers' package gets no DDD00097. `[Owner]` stays in the core,
because the core's checks of the owner read it.

## DDD00098

**Rules that reach a resource from above read where it hangs from its [At] property.**

```csharp
public static MembershipRules Rules { get; } = MembershipRules.For<Board>(    // DDD00098: Board marks no property [At]
    Roles.Custom(),
    reachFromAbove: true);
```

`reachFromAbove` in the rules of a resource with members says that a permission held where the resource hangs,
or above it, reaches the resource. Where the resource hangs is a fact of the domain. The resource's class declares
it with `[At]` on the property that holds it. The mapping and the functions in the database read it from there.
Rules that reach from above a resource that marks no such property would be refused when the resource is first
asked about, and when its functions are written. DDD00098 reports the problem earlier, where the rules are made.
Mark the property `[At]`, or remove `reachFromAbove`.

## DDD00099

**A permission says what it lets somebody do.**

```csharp
[Permissions]
public static class BoardPermissions
{
    [On<Board>] public const string Pin = "boards.pin";                                   // DDD00099: no description

    [On<Board>(Description = "Archive a board")] public const string Archive = "boards.archive";   // said on the level

    /// <summary>Comment on a board.</summary>
    [On<Board>] public const string Comment = "boards.comment";                           // or in its summary
}
```

The catalogue gives every permission a description. A screen that puts permissions into roles shows the
description beside the permission. It says what holding the permission lets somebody do. The description comes
from one of two places:

- `Description` on the permission's level. This wins when the constant also has a `<summary>`.
- The constant's `<summary>`, when no level gives a description. The analyzer reads it from the comment above the
  constant, whether or not the project writes a documentation file.

A permission with neither would show as a bare name, so DDD00099 is a warning, on the constant. A description of
white space alone counts as none. A retired permission, which no screen offers, needs no description. A
translation under the permission's name, in a resx that your failure localizer reads, is applied over either
description ([A module states its permissions once](tenancy/roles-and-permissions.md#declaring-a-modules-permissions)).

## DDD00100

**A restrictive rule narrows a table it can reach.**

```csharp
[RowAccess<Organization>(RowOperations.Read, Restrictive = true)]   // the organization, its units and its tree: fine
public static partial class OnlyEmployeesReadTheOrganization { ... }

[RowAccess<Tenant>(RowOperations.Read, Restrictive = true)]         // DDD00100: a tenant of Tenancy's
public static partial class OnlyEmployeesReadTheTenant { ... }
```

A restrictive rule (`Restrictive = true`) is AND-ed with every policy that allows rows on its aggregate's table and
on the tables of the aggregate's entities. It narrows what those policies allow. On Tenancy's tables, a
restrictive rule can narrow:

- the organization, with its units and the closure of its tree;
- the seats, with their placements and grants;
- the roles.

An application narrows these to keep a seat that is not an employee out of the organization. Tenancy reads
tenants and invitations in its own operations. A person who accepts an invitation reads it before having a seat.
So a restrictive rule on a tenant or an invitation is refused. Put the rule on one of the other tables, or remove
`Restrictive` ([Rules that narrow the organization](tenancy/postgres.md#restrictive-rules)).

## DDD00101

**`TenancyRowAccess.CallerSeat` answers with the application's seat class.**

```csharp
TenancyRowAccess.CallerSeat<Seat>().Kind == SeatKind.Employee      // the class declared with [SeatAggregate]
TenancyRowAccess.CallerSeat<Role>().Name == "Employee"             // DDD00101: no seat's row
```

`CallerSeat<TSeat>()` is the calling seat's own row, and a rule reads a field of it. The function behind it answers
with a row of Tenancy's seats table. So `TSeat` must be the class that this table is mapped to: the class declared
with `[SeatAggregate]`. In a rule of a module whose context does not map the seats, use
`TenancyRowAccess.CallerSeatId<SeatId>()` for the seat's id.

## DDD00102

**A marker of a requirement's argument goes beside the attribute whose call it is.**

```csharp
[InTenant]
public sealed record RenameTenant([ForTenant] TenantId Tenant, string Name) : ICommand, ITenancyRequest;               // DDD00102: no [Operator]

[Operator]
public sealed record MergeTenants([ForTenant] TenantId Tenant, [ForTenant] TenantId Into) : ICommand, ITenancyRequest;  // DDD00102: two members
```

A package marks an attribute as one more argument of a requirement's call with `[RequirementArgument]`. Tenancy's
`[ForTenant]` is one. The generator hands the marked member to the call of the attribute it goes with: `[Operator]`
with `[ForTenant] TenantId Tenant` is `TenancyAccess.Operator(request.Tenant)`
([An operator's request](access-requirements.md#an-operators-request)). DDD00102 reports a marker that the call cannot
take:

- **The attribute is not on the request.** The member is handed to nothing, and the request reads as if it said
  something it does not.
- **Two members carry one marker.** The call would get two arguments where it takes one.
- **The module's interface cannot read the member.** The generator writes the call into the module's interface. So
  the marker goes on a positional parameter of a record, which is its property, or on a property the interface can
  read. A parameter of a class's primary constructor is no member, and neither is a private or a protected property.

Put the attribute on the request, or take the marker off. Mark one member:

```csharp
[Operator]
public sealed record SuspendTenant([ForTenant] TenantId Tenant, string Reason) : ICommand<Tenant>, ITenancyRequest;
```

A member of another type than the marker names is [DDD00081](#ddd00081): `[ForTenant]` marks the application's
tenant id.

## DDD00103

**An operator's command takes its tenant from the request or from itself.**

```csharp
[Operator(Tenant = OperatorTenant.FromRequest)]
public sealed record SuspendTenant([ForTenant] TenantId Tenant, string Reason) : ICommand<Tenant>, ITenancyRequest;   // DDD00103
```

`[Operator]` on a command runs its handler as operator work. The work acts in one place:

| The command says | The work acts in |
|---|---|
| a `[ForTenant]` member | the tenant the command names |
| `Tenant = OperatorTenant.FromRequest` | the tenant the request names beside the command, as tenant selection reads it |
| neither | no tenant, as provisioning needs |

Both at once name two tenants for one piece of work. Keep `[ForTenant]` for a command that names its tenant, or
`FromRequest` for a command that takes the tenant the request names:

```csharp
[Operator]
public sealed record SuspendTenant([ForTenant] TenantId Tenant, string Reason) : ICommand<Tenant>, ITenancyRequest;

[Operator(Tenant = OperatorTenant.FromRequest)]
public sealed record CloseStaleInvitations : ICommand, ITenancyRequest;
```

The same requirement written as the property, `TenancyAccess.Operator(Tenant, OperatorTenant.FromRequest)`, throws an
`ArgumentException` that names DDD00103.

## DDD00104

**`[Operator]` stands beside no requirement an operator cannot meet.**

```csharp
[Operator, InTenant]                                               // DDD00104
public sealed record ArchiveUnits : ICommand, ITenancyRequest;

[Operator, RequiresPermission(TenancyPermissions.SeatsManage)]     // DDD00104
public sealed record TenantSeats : IQuery<IReadOnlyList<SeatRow>>, ITenancyRequest;
```

Requirement attributes on one request all hold. `[Operator]` lets through only a signed-in user with an operator's
token role. An operator holds no seat in any tenant and no permission, so `[InTenant]` and
`[RequiresPermission(permission)]` refuse every operator. A seat is no operator, so `[Operator]` refuses every seat.
Together they refuse every caller, and the request can never be sent.

Keep `[Operator]` for a request of the application's staff, or the other attribute for a request of a tenant's
seats. A request that either may send implements `RequiredAccess` itself, with `AccessRequirement.Any`:

```csharp
public sealed record TenantSeats : IQuery<IReadOnlyList<SeatRow>>, ITenancyRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.Any(
        TenancyAccess.Operator(),
        TenancyAccess.AtTenant(TenancyPermissions.SeatsManage));
}
```

## DDD00105

**`[Operator]` on a query names no tenant and no scope.**

```csharp
[Operator(Tenant = OperatorTenant.FromRequest)]            // DDD00105, a warning
public sealed record AllTenants(string? After) : IQuery<TenantManager.TenantPage>, ITenancyRequest;
```

On a command, `[Operator]` runs the handler as operator work. `Tenant` and `Scope` say where the work acts and in
which scope. On a query nothing begins. An operator reads across tenants through the database role of its token. So
`Tenant` and `Scope` on a query say nothing, and a reader takes them for the tenant the query reads in. Take them off:

```csharp
[Operator]
public sealed record AllTenants(string? After) : IQuery<TenantManager.TenantPage>, ITenancyRequest;
```

`[ForTenant]` on a query is not reported. It still refuses a request that names another tenant than the query does,
with `tenancy.tenant-mismatch`.

## Building the model fails: the owned type must carry the key part

This is no compiler diagnostic, because it depends on how the Entity Framework model is built. It is reported as
early as possible: when the context builds its model, before the first query.

```
'Project' is keyed on 'RegionId', so the foreign key of its owned 'Milestone' (through
'Project.Milestones') must carry it too, but 'Milestone' has no such property. ...
```

`Project` has `[KeyPart] RegionId`, so every table it owns carries `RegionId` in its foreign key. `Milestone` has no
property to keep it in. Give the child the property, and set it from the parent:

```csharp
[Entity<MilestoneId>]
public partial class Milestone
{
    public Milestone(RegionId regionId, MilestoneId id) : base(id) => RegionId = regionId;

    [KeyPart]
    public RegionId RegionId { get; }
}
```

The property must have the same name and the same type as the owner's property. If the child should not carry
it, configure the foreign key of that ownership yourself with `OwnsMany(...).WithOwner().HasForeignKey(...)`. The
convention leaves explicit configuration alone.

---

## Nothing was generated and there is no diagnostic

Check these, in order:

1. **The generator package is referenced.** `DDDToolkit` brings the core generators. The Entity Framework,
   FluentValidation and HotChocolate generators come with their own packages.
2. **The type is `partial`**, and the attribute is the generic one, `[EntityId<Guid>]`, and not a hand-written
   attribute with the same name.
3. **The build output**, with `EmitCompilerGeneratedFiles` turned on, as
   [Getting started](getting-started.md#see-the-generated-code) describes.

If a generator throws, the compiler reports CS8785 or CS8784. That is a bug in the toolkit. Please report it, with
the declaration that triggered it.
