// What every feature of this project names: the module's ids and domain, its project roles, its access
// vocabulary, the ports its features share, the ids of Tenancy's it reads, and the value objects the modules
// share. The access vocabulary is the module's own, the toolkit's and the Membership package's requirement
// types, which every request declares one of; a handler acts on its request, and takes nothing from the check. A
// feature that names another one says so in its own file, with a using of that feature's namespace, so what leans
// on what stays in plain sight.
global using DDDToolkit.Access;
global using DDDToolkit.Supporting.Membership.Access;
global using Examples.Tenancy.Projects.Application.Access;
global using Examples.Tenancy.Projects.Application.StoredProjects;
global using Examples.Tenancy.Projects.Contracts.Gate;
global using Examples.Tenancy.Projects.Contracts.Keys;
global using Examples.Tenancy.Projects.Contracts.ValueObjects;
global using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles;
global using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;
global using Examples.Tenancy.Projects.Domain.Aggregates.Projects;
global using Examples.Tenancy.Projects.Domain.Aggregates.Projects.ValueObjects;
global using Examples.Tenancy.Shared.Domain.ValueObjects;
global using Examples.Tenancy.Tenants.Contracts.ValueObjects;

// Tenancy's answers, closed over the application's ids. Every class here that asks who may do what asks
// through this one interface, so it is named once. An alias is global only in the project that declares it, and
// reads its target as if no other using existed, hence the full names.
global using SampleAnswers = DDDToolkit.Supporting.Tenancy.Access.ITenancyAnswers<
    Examples.Tenancy.Tenants.Contracts.ValueObjects.TenantId,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.SeatId,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.OrganizationUnitId,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.RoleId>;
global using SampleQuestions = DDDToolkit.Supporting.Tenancy.Access.ITenancyQuestions<
    Examples.Tenancy.Tenants.Contracts.ValueObjects.TenantId,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.SeatId,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.OrganizationUnitId,
    Examples.Tenancy.Tenants.Contracts.ValueObjects.RoleId>;
