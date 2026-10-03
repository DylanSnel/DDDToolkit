// This module's ids and classes, the application project's port and the context, and the Tenancy package's templates.
// Global so that moving a file between folders stays a change to that file.
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

// Tenancy's use cases, closed over this application's classes and ids, as the application project closes them: the
// port's adapter names the package's directory. An alias is global only in the project that declares it, and reads
// its target as if no other using existed, hence the full names.
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
