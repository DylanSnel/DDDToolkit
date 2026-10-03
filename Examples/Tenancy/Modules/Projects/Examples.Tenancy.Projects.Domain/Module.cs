using DDDToolkit.Abstractions.Attributes;

// Five projects, one module. Projects' contracts, domain, application, infrastructure and API projects all declare
// the same module name, so to the boundary analyzer they are one module, and to the generators too: the
// infrastructure project registers converters for the ids declared here and in the contracts project, and the
// domain events declared here under their names, because they are its module's. Everything Projects declares is
// Projects' own business unless it is marked [ModuleContract], which the id, the keys and the gate are, in
// Examples.Tenancy.Projects.Contracts.
//
// Tenants declares a module too, so the boundary analyzer is live: Projects may name its published ids,
// and nothing else of that module's (DDD00022). What it needs to know about people and units it asks the
// Tenancy package's questions, over the read model its own context maps. See docs/modules.md.
[assembly: Module("Projects")]
