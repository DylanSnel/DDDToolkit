// This module's ids, domain, project roles, ports and access vocabulary, the context, and the ids of Tenancy's it
// reads. What a port answers with is a feature's, and the adapter that answers it names that feature in its own
// file. Global so that moving a file between folders stays a change to that file.
global using Examples.Tenancy.Projects.Application.Access;
global using Examples.Tenancy.Projects.Application.StoredProjects;
global using Examples.Tenancy.Projects.Contracts.Keys;
global using Examples.Tenancy.Projects.Contracts.ValueObjects;
global using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles;
global using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;
global using Examples.Tenancy.Projects.Domain.Aggregates.Projects;
global using Examples.Tenancy.Projects.Domain.Aggregates.Projects.Entities;
global using Examples.Tenancy.Projects.Infrastructure.Persistence;
global using Examples.Tenancy.Tenants.Contracts.ValueObjects;
