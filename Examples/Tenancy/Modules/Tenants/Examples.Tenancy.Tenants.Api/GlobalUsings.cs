// This module's ids: every feature's routes read them. The commands and queries a feature's routes send are named
// in that feature's own file, with a using of the application project's feature of the same name. The
// infrastructure project's namespace is not among these: only the entry names it, in its own file.
global using Examples.Tenancy.Tenants.Contracts.ValueObjects;

// The package's shapes and statuses: the bodies and the answers name them.
global using DDDToolkit.Supporting.Tenancy;

// Tenancy's use cases, closed once over this application's classes and ids. The package nests its DTOs in one
// generic class, so the routes describe a SampleTenancy.SeatOverview rather than nine type arguments each time. An
// alias is global only in the project that declares it; it reads its target as if no other using existed, hence
// the full names.
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
