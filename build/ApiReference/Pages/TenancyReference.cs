namespace DDDToolkit.Build.ApiReference.Pages;

/// <summary>
/// Tenancy's reference, in <c>docs/tenancy/reference</c>: which types each page holds. The folders are where the packages
/// keep their code; a type in a folder no claim takes still gets a section, under "Other" on its package's last page, and
/// the tool says so, so a new folder is never left out of the reference. One attribute of <c>DDDToolkit.Abstractions</c>,
/// the hook <c>[OfTenant]</c> is mapped through, is taken by name beside the package's own types.
/// </summary>
internal static class TenancyReference
{
    public const string Tenancy = "DDDToolkit.Supporting.Tenancy";
    public const string Markers = "DDDToolkit.Supporting.Tenancy.Abstractions";
    public const string EntityFramework = "DDDToolkit.Supporting.Tenancy.EntityFramework";
    public const string Postgres = "DDDToolkit.Supporting.Tenancy.Postgres";

    // Not documented whole: only the types a claim names.
    private const string Abstractions = "DDDToolkit.Abstractions";

    public static readonly ReferencePlan Plan = new(
        "Tenancy",
        "docs/tenancy/reference",
        [Tenancy, Markers, EntityFramework, Postgres],
        [
            new(
                "aggregates.md",
                "Aggregates and entities",
                "The templates the application's own classes derive from, in the order of the tree: the tenant, its organization "
                + "and units, the seat with its placements and grants, the role, the invitation. Each section opens with the name "
                + "the application sees, the template in brackets, and the attribute that declares the application's class. Its "
                + "value objects, invariants and events follow it.",
                [
                    new("Declaring the classes", [new(Tenancy, Folder: "Aggregates"), new(Tenancy, Folder: "Services")]),
                    new("Tenant", [new(Tenancy, Folder: "Aggregates/Tenants")]),
                    new("Organization", [new(Tenancy, Folder: "Aggregates/Organizations")]),
                    new("Seat", [new(Tenancy, Folder: "Aggregates/Seats")]),
                    new("Role", [new(Tenancy, Folder: "Aggregates/Roles")]),
                    new("Invitation", [new(Tenancy, Folder: "Aggregates/Invitations")]),
                    new("Shared value objects", [new(Tenancy, Folder: "ValueObjects")]),
                ]),
            new(
                "managers.md",
                "Managers",
                "The managers, one per aggregate: `TenantManager`, `OrganizationManager`, `SeatManager`, `RoleManager` and "
                + "`InvitationManager`. Your code takes each one by that name, with no type arguments. The generator writes each "
                + "one into the project that declares the application's Tenancy classes, as a sealed class that derives from the "
                + "package's class closed over those classes: `SeatManager` derives from `SeatManager<…>`. A type nested in the "
                + "package's class is named through the generated class, such as `SeatManager.SeatOverview`. This page names them "
                + "that way. `InvitationManager` is written only where the project declares an invitation class. "
                + "`AddTenancy<TContext>()` registers the first four per scope, and `AddTenancyInvitations<TContext>()` registers "
                + "`InvitationManager`.\n\n"
                + "A command that works on an aggregate has two overloads: one takes the domain objects, the other their ids. Both "
                + "load the aggregate and check the caller's permissions in the same way. A command that creates an aggregate "
                + "returns it, and a command that changes one returns it as changed. A read returns the application's own classes.\n\n"
                + "The nine type parameters are filled with:\n\n"
                + PageWriter.TypeParameterTable + "SeatManager}\n\n"
                + "The stores a storage package implements are under [Stores](#stores). The registration a host calls, from all "
                + "three packages, is under [Registration](#registration).",
                [
                    new("Tenants", [new(Tenancy, Folder: "Managers/Tenants")], Lead: ["TenantManager", "TenantToProvision"]),
                    new("Organization", [new(Tenancy, Folder: "Managers/Organizations")]),
                    new("Seats", [new(Tenancy, Folder: "Managers/Seats")]),
                    new("Roles", [new(Tenancy, Folder: "Managers/Roles")]),
                    new("Invitations", [new(Tenancy, Folder: "Managers/Invitations")], Lead: ["InvitationManager"]),
                    new(
                        "Stores",
                        [new(Tenancy, Folder: "Managers"), new(Tenancy, Type: "IInvitationStore"), new(Tenancy, Type: "InvitationOfToken")],
                        "Where the managers load and save. A storage package implements these interfaces and registers them: "
                        + "`DDDToolkit.Supporting.Tenancy.EntityFramework` does, in `AddTenancy` and `AddTenancyInvitations`. "
                        + "Your code takes the managers. A command of yours that changes a field of your own on one of the "
                        + "aggregates takes a store by the name the generator gives it, `TenancyStore` or, where you declare "
                        + "an invitation class, `InvitationStore`. Each name is an alias closed over your classes."),
                    new(
                        "Registration",
                        [
                            new(Tenancy, Folder: "Registration"),
                            new(EntityFramework, Folder: "Registration"),
                            new(Postgres, Folder: "Registration"),
                        ],
                        "What a host registers, from all three packages: Tenancy's services and options, the access checks of a "
                        + "module's requests, and Postgres. The calls in a context's model that map Tenancy's tables are with "
                        + "Entity Framework, under [Mapping](entity-framework.md#mapping)."),
                ]),
            new(
                "catalogue.md",
                "Catalogue and starter roles",
                "The permissions the modules declare, the catalogue the host builds from them, the starter roles a tenant's roles "
                + "are copied from, and the sync that keeps those roles in line with their starter roles.",
                [
                    new("Catalogue", [new(Tenancy, Folder: "Catalogue")], Lead: ["ApplicationCatalogue", "Permission", "StarterRole", "TenancyCatalogue", "TenancyPermissions", "TenancyStarterRoles"]),
                    new("Starter roles", [new(Tenancy, Folder: "StarterRoles")]),
                ]),
            new(
                "access.md",
                "Access manager and users",
                "`TenancyAccessManager` says who acts (`CurrentUser`, the toolkit's `ICurrentUser`) and which tenant and seat the "
                + "work acts in (`CurrentTenant`). Its `Begin` methods start system work. Below it: the Tenancy user it reads, the actor a change "
                + "records, the selection of a seat in the tenant a request names, and the requirements a request states with "
                + "an attribute, `[Operator]` among them.\n\n"
                + "Your code takes `TenancyAccessManager` with no type arguments. The generator writes it into the global "
                + "namespace of the project that declares the application's Tenancy classes. A project of another module "
                + "says which of its ids are Tenancy's with `[assembly: TenancyIds<TenantId, SeatId, OrganizationUnitId, RoleId>]`, "
                + "and gets the class in its root namespace. The class derives from `TenancyAccessManager<…>` closed over the "
                + "four ids:\n\n"
                + PageWriter.TypeParameterTable + "TenancyAccessManager}",
                [
                    new(
                        "Access manager",
                        [new(Tenancy, Folder: "Access/Manager")],
                        Lead: ["TenancyAccessManager", "CurrentTenant", "TenantInScope", "ITenantsToSweep"]),
                    new("Users", [new(Tenancy, Folder: "Access/Users")], Lead: ["TenancyUser", "ITenancyUser", "TenancyUsers", "TenancyWork"]),
                    new("Actors", [new(Tenancy, Folder: "Access/ActedBy")]),
                    new("Tenant selection", [new(Tenancy, Folder: "Access/Selection")]),
                    new(
                        "Requirements",
                        [new(Tenancy, Folder: "Access/RequiredAccess")],
                        Lead: ["InTenantAttribute", "AtTenantAttribute", "AtUnitAttribute", "OperatorAttribute", "ForTenantAttribute", "OperatorTenant", "TenancyAccess", "TenancyRequirement"]),
                ]),
            new(
                "queries.md",
                "Queries and row access",
                "Tenancy's queries for the current caller, which a module composes into its own queries. Over an Entity "
                + "Framework context, `db.Tenancy()` returns them. The generator writes `db.Tenancy()` for the application's ids, "
                + "so your code calls it with no type arguments. Over a read source that is not Entity Framework, "
                + "`access.In(source, executor)` on the access manager returns them. The queries a module asks most are "
                + "`UnitsWhereIHold`, the units where the caller holds a permission, and `RolesWithPermission`, the roles that "
                + "grant a permission. Below them: the read model the queries read, the projection that keeps it up to date, and "
                + "the row access rules a database enforces.",
                [
                    new(
                        "Queries",
                        [new(Tenancy, Folder: "Access/Queries"), new(EntityFramework, Type: "TenancyEntityFrameworkExtensions")],
                        Lead: ["TenancyEntityFrameworkExtensions", "ITenancyQueries", "UnitPermission", "RoleWithPermission", "MoveReach", "NamedUnit"]),
                    new("Read model", [new(Tenancy, Folder: "Access/ReadModel")], Lead: ["ITenancyReadSource", "IQueryExecutor"]),
                    new("Projection", [new(Tenancy, Folder: "Access/Projection")]),
                    new("Row access", [new(Tenancy, Folder: "Access/RowAccess")]),
                ]),
            new(
                "refusals.md",
                "Refusals",
                "Every refusal code of Tenancy, in the members of `TenancyRefusals`, and the resources that hold their texts.",
                [
                    new("Refusal codes", [new(Tenancy, Folder: "Refusals")]),
                    new("Texts", [new(Tenancy, Folder: "Resources")]),
                ]),
            new(
                "entity-framework.md",
                "Entity Framework",
                "The public API of `DDDToolkit.Supporting.Tenancy.EntityFramework`, by folder, with the marker `[OfTenant]` from "
                + "`DDDToolkit.Supporting.Tenancy.Abstractions`. `db.Tenancy()` is with the queries, under "
                + "[Queries](queries.md#queries). The services a host registers, `AddTenancy`, `AddTenancyInvitations` and "
                + "`AddTenancyAccess`, are with the managers, under [Registration](managers.md#registration).",
                [
                    new(
                        "Mapping",
                        [
                            new(EntityFramework, Folder: "Mapping"),
                            new(Markers, Folder: ""),
                            new(Abstractions, Type: "ModelConventionAttribute"),
                        ],
                        "How a context maps Tenancy. A class marks the property that holds its tenant with `[OfTenant]`. "
                        + "The marker names its convention with `[ModelConvention]`, and every context that calls "
                        + "`AddDDDToolkitConventions()` runs that convention. The convention gives the class the tenant filter "
                        + "and Tenancy's save check. A context that keeps the tenant in another property, or in a column of its "
                        + "own, calls `ScopeToTenant` on the class instead, and that call wins. `AddTenancy`, "
                        + "`AddTenancyReadModel` and `AddTenancyReadFunctions` on the model builder map Tenancy's own tables, its "
                        + "read model, or its read functions.",
                        Lead: ["OfTenantAttribute", "OfTenantConvention", "ModelConventionAttribute", "TenancyModelBuilderExtensions", "TenancyTableNames"]),
                    new("Store", [new(EntityFramework, Folder: "Store")]),
                    new("Saving", [new(EntityFramework, Folder: "Saving")]),
                    new("Read model", [new(EntityFramework, Folder: "ReadModel")]),
                    new("Read functions", [new(EntityFramework, Folder: "ReadFunctions")]),
                    new("Access checks", [new(EntityFramework, Folder: "Access")]),
                    new("Attribution", [new(EntityFramework, Folder: "Attribution")]),
                    new("History", [new(EntityFramework, Folder: "History")]),
                    new("Invitations", [new(EntityFramework, Folder: "Invitations")]),
                    new("Starter roles", [new(EntityFramework, Folder: "StarterRoles")]),
                    new("Start-up checks", [new(EntityFramework, Folder: "Checks")]),
                ]),
            new(
                "postgres.md",
                "Postgres",
                "The public API of `DDDToolkit.Supporting.Tenancy.Postgres`, by folder. Its registration, "
                + "`AddTenancyPostgres`, is with the managers, under [Registration](managers.md#registration).",
                [
                    new("Policies", [new(Postgres, Folder: "Policies")]),
                    new("SQL", [new(Postgres, Folder: "Sql")]),
                    new("Store", [new(Postgres, Folder: "Store")]),
                    new("Start-up checks", [new(Postgres, Folder: "Checks")]),
                ]),
        ]);
}
