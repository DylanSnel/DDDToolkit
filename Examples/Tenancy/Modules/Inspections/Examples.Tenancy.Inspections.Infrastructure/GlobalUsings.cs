// This module's domain, its ports with what the read port answers with, the context, and the ids of the other
// modules an inspection stores. Global so that moving a file between folders stays a change to that file. Of the
// application project only the folder of the one feature that declares the ports is named here, and not the
// commands and queries below it: an adapter knows no command and no query.
global using Examples.Tenancy.Inspections.Application.Recording;
global using Examples.Tenancy.Inspections.Domain.Aggregates.Inspections;
global using Examples.Tenancy.Inspections.Infrastructure.Persistence;
global using Examples.Tenancy.Projects.Contracts.ValueObjects;
global using Examples.Tenancy.Tenants.Contracts.ValueObjects;
