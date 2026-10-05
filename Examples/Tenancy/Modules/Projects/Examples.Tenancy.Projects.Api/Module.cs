// The toolkit's module is not declared here. The build declares it for every project in the module's folder,
// from Modules/Directory.Build.props; what this file holds is HotChocolate's.
//
// HotChocolate's [Module], by its full name, so it is never taken for the toolkit's: it names what HotChocolate's
// generator writes for this project, AddProjectsTypes, which registers every method marked [Query] or [Mutation] here,
// the types declared with [ObjectType<T>], the class of paged fields and the data loaders.
[assembly: HotChocolate.Module("ProjectsTypes")]

// The loaders HotChocolate writes for the [DataLoader] methods are this project's own, like the fields that take
// them: the module's entry stays the one public type it declares.
[assembly: GreenDonut.DataLoaderDefaults(AccessModifier = GreenDonut.DataLoaderAccessModifier.Internal)]
