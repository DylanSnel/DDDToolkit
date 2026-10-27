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
// ids every module shares. The commands and queries the seeder sends, and the permissions and starter roles the catalogue is
// made of, are named where they are used, each with a using of the feature it comes from. The host references
// the API projects alone, and names nothing of a module's infrastructure project: no context, no store. Tenancy's
// managers, which the seeder provisions with, are TenantManager, SeatManager and the others, the classes the toolkit's
// generator wrote into the Tenants module's domain project, which the host sees through the API project. So is the
// access manager the seeder begins system work with and the host's gates ask, closed over the module's ids: no call of
// Tenancy's here names an id type.
global using Examples.Tenancy.Tenants.Api;
global using Examples.Tenancy.Tenants.Contracts.ValueObjects;
global using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.ValueObjects;
global using Examples.Tenancy.Projects.Api;
global using Examples.Tenancy.Projects.Contracts.Permissions;
global using Examples.Tenancy.Projects.Contracts.ValueObjects;
global using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;
global using Examples.Tenancy.Inspections.Api;
