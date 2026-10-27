namespace DDDToolkit.Build.ApiReference.Pages;

/// <summary>
/// Tenancy's reference, in <c>docs/tenancy/reference</c>: which types each page holds. The folders are where the packages
/// keep their code; a type in a folder no claim takes still gets a section, under "Other" on its package's last page, and
/// the tool says so, so a new folder is never left out of the reference.
/// </summary>
internal static class TenancyReference
{
    public const string Tenancy = "DDDToolkit.Supporting.Tenancy";
    public const string Markers = "DDDToolkit.Supporting.Tenancy.Abstractions";
    public const string EntityFramework = "DDDToolkit.Supporting.Tenancy.EntityFramework";
    public const string Postgres = "DDDToolkit.Supporting.Tenancy.Postgres";

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
                + "the application sees, the template in brackets, and the attribute that declares the application's class; its "
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
                "use-cases.md",
                "Use cases",
                "The managers, one per aggregate. Each is a class the generator writes in the project that declares the "
                + "application's classes, named as the package's class is: `SeatManager` derives from `SeatManager<…>` closed "
                + "over them, so every type nested there is named through it, `SeatManager.SeatOverview`. This page names them "
                + "that way. What fills the nine type parameters:\n\n"
                + PageWriter.TypeParameterTable + "SeatManager}\n\n"
                + "The registration a host calls, from all three packages, is at the end, under [Registration](#registration).",
                [
                    new("Stores", [new(Tenancy, Folder: "UseCases")]),
                    new("Tenants", [new(Tenancy, Folder: "UseCases/Tenants")]),
                    new("Organization", [new(Tenancy, Folder: "UseCases/Organizations")]),
                    new("Seats", [new(Tenancy, Folder: "UseCases/Seats")]),
                    new("Roles", [new(Tenancy, Folder: "UseCases/Roles")]),
                    new("Invitations", [new(Tenancy, Folder: "UseCases/Invitations")]),
                    new(
                        "Registration",
                        [
                            new(Tenancy, Folder: "Registration"),
                            new(EntityFramework, Folder: "Registration"),
                            new(EntityFramework, Type: "TenancyModelBuilderExtensions"),
                            new(EntityFramework, Type: "TenancyTableNames"),
                            new(Postgres, Folder: "Registration"),
                        ],
                        "What a host calls, from all three packages: the storage-agnostic core, Entity Framework's tables and "
                        + "services, the checks of requests, and Postgres."),
                ]),
            new(
                "catalogue.md",
                "Catalogue and starter roles",
                "The permissions the modules declare, the catalogue the host builds from them, the starter roles a tenant's roles "
                + "start from, and the sync that keeps those roles following their starter roles.",
                [
                    new("Catalogue", [new(Tenancy, Folder: "Catalogue")], Lead: ["ApplicationCatalogue", "Permission", "StarterRole", "TenancyCatalogue", "TenancyPermissions", "TenancyStarterRoles"]),
                    new("Starter roles", [new(Tenancy, Folder: "StarterRoles")]),
                ]),
            new(
                "callers.md",
                "Callers and requirements",
                "Who is calling and in which tenant, the actor a change records, the selection of a seat in the tenant a request "
                + "names, and the requirements a request states with an attribute.",
                [
                    new("Callers", [new(Tenancy, Folder: "Access/Callers")], Lead: ["TenancyCaller", "ITenancyCaller", "TenancyCallers", "TenancyWork"]),
                    new("Actors", [new(Tenancy, Folder: "Access/ActedBy")]),
                    new("Tenant selection", [new(Tenancy, Folder: "Access/Selection")]),
                    new("Requirements", [new(Tenancy, Folder: "Access/RequiredAccess")]),
                ]),
            new(
                "queries.md",
                "ITenancy, its queries and row access",
                "`ITenancy`, through which a module asks about the caller, and the queries it gives in a context; the read model "
                + "they read, the projection that keeps it, and the row access rules a database enforces.",
                [
                    new("Queries", [new(Tenancy, Folder: "Access/Queries")], Lead: ["ITenancy", "ITenancyQueries", "CurrentTenancy"]),
                    new("Read model", [new(Tenancy, Folder: "Access/ReadModel")]),
                    new("Projection", [new(Tenancy, Folder: "Access/Projection")]),
                    new("Row access", [new(Tenancy, Folder: "Access/RowAccess")]),
                ]),
            new(
                "refusals.md",
                "Refusals",
                "Every code Tenancy refuses a command with, in the members of `TenancyRefusals`, and the resources that hold "
                + "their texts.",
                [
                    new("Refusal codes", [new(Tenancy, Folder: "Refusals")]),
                    new("Texts", [new(Tenancy, Folder: "Resources")]),
                ]),
            new(
                "entity-framework.md",
                "Entity Framework",
                "The public API of `DDDToolkit.Supporting.Tenancy.EntityFramework`, by folder. Its registration, "
                + "`AddTenancy`, `AddTenancyAccess` and the table names, is with the use cases, under "
                + "[Registration](use-cases.md#registration).",
                [
                    new("Mapping", [new(EntityFramework, Folder: "Mapping"), new(Markers, Folder: "")], Lead: ["OfTenantAttribute", "OfTenantConvention"]),
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
                + "`AddTenancyPostgres`, is with the use cases, under [Registration](use-cases.md#registration).",
                [
                    new("Policies", [new(Postgres, Folder: "Policies")]),
                    new("SQL", [new(Postgres, Folder: "Sql")]),
                    new("Store", [new(Postgres, Folder: "Store")]),
                    new("Start-up checks", [new(Postgres, Folder: "Checks")]),
                ]),
        ]);
}
