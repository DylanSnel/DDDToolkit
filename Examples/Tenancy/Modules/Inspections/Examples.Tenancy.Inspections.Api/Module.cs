using DDDToolkit.Abstractions.Attributes;

[assembly: Module("Inspections")]

// HotChocolate's attribute, not the toolkit's above, hence its full name: it names what HotChocolate's generator
// writes for this project, AddInspectionsTypes, which registers every method marked [Query] or [Mutation] here,
// every type class and every data loader.
[assembly: HotChocolate.Module("InspectionsTypes")]

// The loaders HotChocolate's generator writes from the [DataLoader] methods are this project's own, like
// everything else in it: only its fields take them.
[assembly: GreenDonut.DataLoaderDefaults(AccessModifier = GreenDonut.DataLoaderAccessModifier.Internal)]
