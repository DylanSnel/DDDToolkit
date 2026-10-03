// What every feature of this project names: the module's ids, its access vocabulary, the port its features share,
// and the Tenancy package's refusals. The access vocabulary is the module's request interface, the toolkit's
// requirement types and Tenancy's cases of them, which every request declares one of. A feature that names another
// one says so in its own file, with a using of that feature's namespace, so what leans on what stays in plain sight.
//
// The module's classes are not among these. Two features of this project are called Tenant and Organization, so
// inside it those two words are the features' namespaces, whatever is imported: the classes of those names are
// reached through SampleTenancy below, and a file that names another of the classes imports its namespace itself.
global using DDDToolkit.Access;
global using DDDToolkit.Supporting.Tenancy.Access;
global using Examples.Tenancy.Tenants.Application.Access;
global using Examples.Tenancy.Tenants.Application.StoredTenancy;
global using Examples.Tenancy.Tenants.Contracts.ValueObjects;
global using DDDToolkit.Supporting.Tenancy;

// Tenancy's use cases, closed once over this application's classes and ids. The package nests every use case, its
// store and its DTOs in one generic class, so this project writes SampleTenancy.SeatCommands rather than nine type
// arguments. An alias is global only in the project that declares it, and reads its target as if no other using
// existed, hence the full names.
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

// Tenancy's answers about the current caller, closed over the application's ids: the handlers that ask who is
// calling, and TenantWideKey, name it once.
global using SampleAnswers = DDDToolkit.Supporting.Tenancy.Access.ITenancyAnswers<
    Examples.Tenancy.Tenants.Contracts.ValueObjects.TenantId,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.SeatId,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.OrganizationUnitId,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.RoleId>;

// The package's invitation use cases, closed over the application's invitation class and its id as well: issuing
// one, listing the open ones, cancelling one and accepting one.
global using SampleInvitations = DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases<
    Examples.Tenancy.Tenants.Domain.Aggregates.Tenants.Tenant,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.TenantId,
    Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Organization,
    Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities.OrganizationUnit,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.OrganizationUnitId,
    Examples.Tenancy.Tenants.Domain.Aggregates.Seats.Seat,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.SeatId,
    Examples.Tenancy.Tenants.Domain.Aggregates.Roles.Role,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.RoleId>.InvitationCommands<
        Examples.Tenancy.Tenants.Domain.Aggregates.Invitations.Invitation,
        Examples.Tenancy.Tenants.Contracts.ValueObjects.InvitationId>;
