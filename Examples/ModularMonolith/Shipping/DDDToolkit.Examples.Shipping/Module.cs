using DDDToolkit.Abstractions.Attributes;

// Shipping publishes nothing. Nobody reads its shipments and nobody points at them, so there is no
// contracts assembly here and no [ModuleContract] anywhere in the project. A module that only consumes
// is a perfectly ordinary module.
[assembly: Module("Shipping")]
