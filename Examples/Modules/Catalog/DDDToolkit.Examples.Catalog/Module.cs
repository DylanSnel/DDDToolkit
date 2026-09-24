using DDDToolkit.Abstractions.Attributes;

// What the shop sells and for how much. Catalog is the only module that decides a price; Ordering keeps
// a copy of the prices it hears about, and nobody else cares.
[assembly: Module("Catalog")]
