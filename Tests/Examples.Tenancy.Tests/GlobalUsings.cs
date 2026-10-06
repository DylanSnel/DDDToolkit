global using Xunit;

// The modules' namespaces follow their projects and folders: in an application project a feature's folder, with
// its commands and its queries below it; in a domain project an aggregate's folder, with its entities, events and
// value objects below it. The tests read across all of them.
global using Examples.Tenancy.Tenants.Api;
global using Examples.Tenancy.Tenants.Application;
global using Examples.Tenancy.Tenants.Application.Access;
global using Examples.Tenancy.Tenants.Application.Access.Queries;
global using Examples.Tenancy.Tenants.Application.Catalogue.Queries;
global using Examples.Tenancy.Tenants.Application.Directory.Queries;
global using Examples.Tenancy.Tenants.Application.Grants.Commands;
global using Examples.Tenancy.Tenants.Application.History;
global using Examples.Tenancy.Tenants.Application.History.Queries;
global using Examples.Tenancy.Tenants.Application.Invitations.Commands;
global using Examples.Tenancy.Tenants.Application.Invitations.Queries;
global using Examples.Tenancy.Tenants.Application.Operators.Queries;
global using Examples.Tenancy.Tenants.Application.Organization;
global using Examples.Tenancy.Tenants.Application.Organization.Commands;
global using Examples.Tenancy.Tenants.Application.Organization.Queries;
global using Examples.Tenancy.Tenants.Application.StoredTenancy;
global using Examples.Tenancy.Tenants.Application.Placements.Commands;
global using Examples.Tenancy.Tenants.Application.Roles;
global using Examples.Tenancy.Tenants.Application.Roles.Commands;
global using Examples.Tenancy.Tenants.Application.Roles.Queries;
global using Examples.Tenancy.Tenants.Application.Seats.Commands;
global using Examples.Tenancy.Tenants.Application.Seats.Queries;
global using Examples.Tenancy.Tenants.Application.Tenant.Commands;
global using Examples.Tenancy.Tenants.Contracts.ValueObjects;
global using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities;
global using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.ValueObjects;
global using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
global using Examples.Tenancy.Tenants.Domain.Aggregates.Tenants;
global using Examples.Tenancy.Tenants.Infrastructure;
global using Examples.Tenancy.Tenants.Infrastructure.Persistence;
global using Examples.Tenancy.Projects.Api;
global using Examples.Tenancy.Projects.Application;
global using Examples.Tenancy.Projects.Application.Access;
global using Examples.Tenancy.Projects.Application.Access.Queries;
global using Examples.Tenancy.Projects.Application.Crew;
global using Examples.Tenancy.Projects.Application.Crew.Commands;
global using Examples.Tenancy.Projects.Application.Crew.Queries;
global using Examples.Tenancy.Projects.Application.Lifecycle.Commands;
global using Examples.Tenancy.Projects.Application.Operators.Queries;
global using Examples.Tenancy.Projects.Application.Overview;
global using Examples.Tenancy.Projects.Application.Overview.Queries;
global using Examples.Tenancy.Projects.Application.Ownership.Commands;
global using Examples.Tenancy.Projects.Application.ProjectRoles;
global using Examples.Tenancy.Projects.Application.ProjectRoles.Commands;
global using Examples.Tenancy.Projects.Application.ProjectRoles.Queries;
global using Examples.Tenancy.Projects.Application.StoredProjects;
global using Examples.Tenancy.Projects.Contracts.Gate;
global using Examples.Tenancy.Projects.Contracts.Keys;
global using Examples.Tenancy.Projects.Contracts.ValueObjects;
global using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles;
global using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.Events;
global using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;
global using Examples.Tenancy.Projects.Domain.Aggregates.Projects;
global using Examples.Tenancy.Projects.Domain.Aggregates.Projects.Entities;
global using Examples.Tenancy.Projects.Domain.Aggregates.Projects.Events;
global using Examples.Tenancy.Projects.Domain.Aggregates.Projects.ValueObjects;
global using Examples.Tenancy.Projects.Infrastructure;
global using Examples.Tenancy.Projects.Infrastructure.Persistence;
global using Examples.Tenancy.Inspections.Api;
global using Examples.Tenancy.Inspections.Application;
global using Examples.Tenancy.Inspections.Application.Access;
global using Examples.Tenancy.Inspections.Application.Operators.Queries;
global using Examples.Tenancy.Inspections.Application.Recording;
global using Examples.Tenancy.Inspections.Application.Recording.Commands;
global using Examples.Tenancy.Inspections.Application.Recording.Queries;
global using Examples.Tenancy.Inspections.Domain.Aggregates.Inspections;
global using Examples.Tenancy.Inspections.Domain.Aggregates.Inspections.ValueObjects;
global using Examples.Tenancy.Inspections.Infrastructure;
global using Examples.Tenancy.Inspections.Infrastructure.Persistence;

// What the modules share and none owns.
global using Examples.Tenancy.Shared.Domain.ValueObjects;

// The host: its access plumbing, its request pipeline, its dev login, its GraphQL gateways and the demonstration's
// fixed data.
global using Examples.Tenancy.Host;
global using Examples.Tenancy.Host.Access;
global using Examples.Tenancy.Host.Auth;
global using Examples.Tenancy.Host.DevLogin;
global using Examples.Tenancy.Host.GraphQL;
global using Examples.Tenancy.Host.Requests;
global using Examples.Tenancy.Host.Seeding;
global using Examples.Tenancy.Host.Storage;

// The application's catalogue, which the host runs with and the exported policies are written from.
global using Examples.Tenancy.Catalogue;
global using Examples.Tenancy.Tests.Infrastructure;

// Tenancy's use cases are TenantsTenancy here as in the sample: the class the toolkit's generator wrote into the
// Tenants module's domain project, which these tests see through the projects they test.
