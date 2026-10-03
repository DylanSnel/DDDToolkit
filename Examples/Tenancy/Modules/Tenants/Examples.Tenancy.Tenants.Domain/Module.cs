using DDDToolkit.Abstractions.Attributes;

// Five projects, one module. Tenants' contracts, domain, application, infrastructure and API projects all declare
// the same module name, so to the boundary analyzer they are one module, and to the generators too: the
// infrastructure project gets modelBuilder.AddTenancy() closed over the classes declared here, and registers the
// ids of the contracts project, because they are its module's. Everything Tenants declares is its own
// business unless it is marked [ModuleContract], which the ids are, in Examples.Tenancy.Tenants.Contracts.
//
// The Tenancy package declares no module of its own: what it ships becomes this application's through the
// classes declared here, so this is where the application's tenancy lives. Projects and Inspections declare
// modules too, so the boundary analyzer is live: they may name the published ids, and nothing else of
// this module's (DDD00022). They ask Tenancy through its read model instead. See docs/modules.md.
[assembly: Module("Tenants")]
