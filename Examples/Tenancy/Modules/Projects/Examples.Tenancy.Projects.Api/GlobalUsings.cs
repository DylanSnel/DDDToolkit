// This module's ids, its project roles' among them, the ids of Tenancy's the bodies name, and the value objects
// the modules share: every feature's routes read them. The commands and queries a feature's routes send are named
// in that feature's own file, with a using of the application project's feature of the same name. The
// infrastructure project's namespace is not among these: only the entry names it, in its own file.
global using Examples.Tenancy.Projects.Contracts.ValueObjects;
global using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;
global using Examples.Tenancy.Shared.Domain.ValueObjects;
global using Examples.Tenancy.Tenants.Contracts.ValueObjects;
