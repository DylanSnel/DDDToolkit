using DDDToolkit.Abstractions.Attributes;

// Four projects, one module. Inspections' domain, application, infrastructure and API projects all declare the
// same module name, so to the boundary analyzer they are one module, and to the generators too: the
// infrastructure project registers a converter for the id declared here, and the domain event declared here under
// its name, because they are its module's. Everything Inspections declares is Inspections' own business. It
// publishes nothing another module needs, so, like Shipping, it has no contracts project and marks nothing
// [ModuleContract]: no other module names its types, the host is not a module, and the UI only speaks HTTP.
//
// Projects and Tenancy declare modules too, so the boundary analyzer is live: Inspections may name the ids, keys
// and gate they publish, and nothing else of theirs (DDD00022). Whether the caller may record on a project it
// asks Projects' gate; it never reads Projects' tables or repeats its rules. See docs/modules.md.
[assembly: Module("Inspections")]
