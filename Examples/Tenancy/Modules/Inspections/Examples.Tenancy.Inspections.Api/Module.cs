// The toolkit's module is not declared here. The build declares it for every project in the module's folder,
// from Modules/Directory.Build.props; what this file holds is HotChocolate's.
//
// HotChocolate's [Module], by its full name, so it is never taken for the toolkit's: it names what HotChocolate's
// generator writes for this project, AddInspectionsTypes, which registers every method marked [Query] or [Mutation]
// here, every type class and every data loader.
[assembly: HotChocolate.Module("InspectionsTypes")]

// The loaders HotChocolate's generator writes from the [DataLoader] methods are this project's own, like
// everything else in it: only its fields take them.
[assembly: GreenDonut.DataLoaderDefaults(AccessModifier = GreenDonut.DataLoaderAccessModifier.Internal)]
