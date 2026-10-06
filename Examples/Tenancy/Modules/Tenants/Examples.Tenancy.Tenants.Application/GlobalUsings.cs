// What every feature of this project names: the module's ids, its access vocabulary, the port its features share,
// and the Tenancy package's refusals. The access vocabulary is the module's request interface, the toolkit's
// requirement types and Tenancy's cases of them, which every request declares one of. A feature that names another
// one says so in its own file, with a using of that feature's namespace, so what leans on what stays in plain sight.
//
// The module's classes are not among these. Two features of this project are called Tenant and Organization, so
// inside it those two words are the features' namespaces, whatever is imported: the classes of those names are
// reached through Tenancy's use cases, and a file that names another of the classes imports its namespace itself.
//
// Those use cases are not closed here either. The package nests every use case, its store and its records in one
// class generic over the module's five classes and four ids, and the toolkit's generator closes it over the classes
// in the domain project that declares them, as TenantsTenancy, named after the module. So a handler takes a
// TenantsTenancy.SeatCommands, and the invitation use cases are TenantsTenancy.InvitationCommands<Invitation,
// InvitationId>, closed over the module's invitation class as well.
global using DDDToolkit.Access;
global using DDDToolkit.Supporting.Tenancy.Access;
global using Examples.Tenancy.Tenants.Application.Access;
global using Examples.Tenancy.Tenants.Application.StoredTenancy;
global using Examples.Tenancy.Tenants.Contracts.ValueObjects;
global using DDDToolkit.Supporting.Tenancy;

// Tenancy's answers about the current caller, closed over the application's ids: the handlers that ask who is
// calling, and TenantWideKey, name it once. An alias is global only in the project that declares it, and reads its
// target as if no other using existed, hence the full names.
global using SampleAnswers = DDDToolkit.Supporting.Tenancy.Access.ITenancyAnswers<
    Examples.Tenancy.Tenants.Contracts.ValueObjects.TenantId,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.SeatId,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.OrganizationUnitId,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.RoleId>;
