// The host's own namespaces, one per folder that holds types. Global so that moving a file between folders
// stays a change to that file.
global using Examples.Tenancy.Host.Access;
global using Examples.Tenancy.Host.Auth;
global using Examples.Tenancy.Host.DevLogin;
global using Examples.Tenancy.Host.GraphQL;
global using Examples.Tenancy.Host.Languages;
global using Examples.Tenancy.Host.Requests;
global using Examples.Tenancy.Host.Seeding;
global using Examples.Tenancy.Host.Storage;

// The application's catalogue, which the host runs with and the program that exports the policies writes them from.
global using Examples.Tenancy.Catalogue;

// What the host names of the modules in more than one place: each module's entry, from its API project, and the
// ids every module shares. The commands and queries the seeder sends, and the keys and packs the catalogue is
// made of, are named where they are used, each with a using of the feature it comes from. The host references
// the API projects alone, and names nothing of a module's infrastructure project: no context, no store.
global using Examples.Tenancy.Tenants.Api;
global using Examples.Tenancy.Tenants.Contracts.ValueObjects;
global using Examples.Tenancy.Projects.Api;
global using Examples.Tenancy.Projects.Contracts.Keys;
global using Examples.Tenancy.Projects.Contracts.ValueObjects;
global using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;
global using Examples.Tenancy.Inspections.Api;

// Tenancy's use cases, closed over this application's classes and ids, as the Tenants module closes them. An
// alias is global only in the project that declares it, so the host declares its own. It reads its target as
// if no other using existed, hence the full names.
global using SampleTenancy = DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases<
    Examples.Tenancy.Tenants.Domain.Aggregates.Tenants.Tenant,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.TenantId,
    Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Organization,
    Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities.OrganizationUnit,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.OrganizationUnitId,
    Examples.Tenancy.Tenants.Domain.Aggregates.Seats.Seat,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.SeatId,
    Examples.Tenancy.Tenants.Domain.Aggregates.Roles.Role,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.RoleId>;
