// This module's ids and classes, the application project's port and the context, and the Tenancy package's templates.
// Global so that moving a file between folders stays a change to that file. The port's adapter names the package's
// directory as TenantsTenancy.TenancyDirectory, through the class the toolkit's generator wrote into the domain
// project.
global using Examples.Tenancy.Tenants.Application.StoredTenancy;
global using Examples.Tenancy.Tenants.Contracts.TokenRoles;
global using Examples.Tenancy.Tenants.Contracts.ValueObjects;
global using Examples.Tenancy.Tenants.Domain.Aggregates.Invitations;
global using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations;
global using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities;
global using Examples.Tenancy.Tenants.Domain.Aggregates.Roles;
global using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
global using Examples.Tenancy.Tenants.Domain.Aggregates.Tenants;
global using Examples.Tenancy.Tenants.Infrastructure.Persistence;
global using DDDToolkit.Supporting.Tenancy;
