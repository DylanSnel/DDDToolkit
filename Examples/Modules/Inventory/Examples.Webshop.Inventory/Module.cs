using DDDToolkit.Abstractions.Attributes;

// What is on the shelf, and what is set aside for orders that have not shipped yet.
[assembly: Module("Inventory")]

// HotChocolate's attribute, not the toolkit's above, hence its full name: it names what HotChocolate's
// generator writes for this project, AddInventoryTypes, which registers every GraphQL type, loader and
// operation the project declares.
[assembly: HotChocolate.Module("InventoryTypes")]
