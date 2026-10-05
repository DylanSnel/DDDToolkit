// What every feature of this project names: the module's domain and its access vocabulary, the ids, keys and
// gate it reads from the other modules, and the value objects the modules share. The access vocabulary is the
// module's own and the toolkit's requirement types, which every request declares one of; a handler acts on its
// request, and takes nothing from the check. The ports are not among them: one feature uses them, so they live in
// that feature's folder, and its commands and queries see them without a using.
global using DDDToolkit.Access;
global using Examples.Tenancy.Inspections.Application.Access;
global using Examples.Tenancy.Inspections.Domain.Aggregates.Inspections;
global using Examples.Tenancy.Inspections.Domain.Aggregates.Inspections.ValueObjects;
global using Examples.Tenancy.Projects.Contracts.Gate;
global using Examples.Tenancy.Projects.Contracts.Keys;
global using Examples.Tenancy.Projects.Contracts.ValueObjects;
global using Examples.Tenancy.Shared.Domain.ValueObjects;
global using Examples.Tenancy.Tenants.Contracts.ValueObjects;

// Tenancy's answers, closed over the application's ids: the tenant the caller works in, and its seat. An alias is
// global only in the project that declares it, and reads its target as if no other using existed, hence the full
// names.
global using SampleAnswers = DDDToolkit.Supporting.Tenancy.Access.ITenancyAnswers<
    Examples.Tenancy.Tenants.Contracts.ValueObjects.TenantId,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.SeatId,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.OrganizationUnitId,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.RoleId>;
