using DDDToolkit.Abstractions.Attributes;

// What the shop sells and for how much. Catalog is the only module that decides a price; Ordering keeps
// a copy of the prices it hears about, and nobody else cares.
[assembly: Module("Catalog")]

// HotChocolate's attribute, not the toolkit's above, hence its full name: it names what HotChocolate's
// generator writes for this project, AddCatalogTypes, which registers every GraphQL type, loader and
// operation the project declares.
[assembly: HotChocolate.Module("CatalogTypes")]
