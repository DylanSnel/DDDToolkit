// What every feature of this project names: the module's domain and its access vocabulary, the ids, permissions and
// gate it reads from the other modules, and the value objects the modules share. The access vocabulary is the
// module's own and the toolkit's requirement types, which every request declares one of; a handler acts on its
// request, and takes nothing from the check. The ports are not among them: they live in the Recording feature's
// folder, whose commands and queries see them without a using; the Operators feature names that folder in its own
// file.
global using DDDToolkit.Access;
global using Examples.Tenancy.Inspections.Application.Access;
global using Examples.Tenancy.Inspections.Domain.Aggregates.Inspections;
global using Examples.Tenancy.Inspections.Domain.Aggregates.Inspections.ValueObjects;
global using Examples.Tenancy.Projects.Contracts.Gate;
global using Examples.Tenancy.Projects.Contracts.Permissions;
global using Examples.Tenancy.Projects.Contracts.ValueObjects;
global using Examples.Tenancy.Shared.Domain.ValueObjects;
global using Examples.Tenancy.Tenants.Contracts.ValueObjects;

// Which of the ids this module sees are Tenancy's, once: the generator writes TenancyAccessManager into this project,
// closed over them, for the tenant the caller works in and its seat, and the infrastructure project's
// AddTenancyAccess<IInspectionsRequest, InspectionsContext>().
[assembly: DDDToolkit.Supporting.Tenancy.TenancyIds<TenantId, SeatId, OrganizationUnitId, RoleId>]
