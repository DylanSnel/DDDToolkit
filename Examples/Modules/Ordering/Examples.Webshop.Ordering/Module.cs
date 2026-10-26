using DDDToolkit.Abstractions.Attributes;

// One assembly, one module. Everything this project declares belongs to Ordering and is Ordering's own
// business unless it is marked [ModuleContract] or is an integration event, both of which live next
// door in Examples.Webshop.Ordering.Contracts.
//
// Nothing happens until a second assembly says it is a module too. Shipping does, so from here on the
// boundary analyzer reports anything Shipping names that Ordering did not publish (DDD00022) and any
// entity of one module stored by the other (DDD00023). See docs/modules.md.
[assembly: Module("Ordering")]

// HotChocolate's attribute, not the toolkit's above, hence its full name: it names what HotChocolate's
// generator writes for this project, AddOrderingTypes, which registers every GraphQL type, loader and
// operation the project declares.
[assembly: HotChocolate.Module("OrderingTypes")]
