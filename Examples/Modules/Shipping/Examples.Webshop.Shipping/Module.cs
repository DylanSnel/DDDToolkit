using DDDToolkit.Abstractions.Attributes;

// Shipping publishes nothing. Nobody reads its shipments and nobody points at them, so there is no
// contracts assembly here and no [ModuleContract] anywhere in the project. A module that only consumes
// is a perfectly ordinary module.
[assembly: Module("Shipping")]

// HotChocolate's attribute, not the toolkit's above, hence its full name: it names what HotChocolate's
// generator writes for this project, AddShippingTypes, which registers every GraphQL type, loader and
// operation the project declares.
[assembly: HotChocolate.Module("ShippingTypes")]
