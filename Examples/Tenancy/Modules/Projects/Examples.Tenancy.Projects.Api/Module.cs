using DDDToolkit.Abstractions.Attributes;

[assembly: Module("Projects")]

// HotChocolate's attribute, not the toolkit's above, hence its full name: it names what HotChocolate's generator
// writes for this project, AddProjectsTypes, which registers every method marked [Query] or [Mutation] here, the
// types declared with [ObjectType<T>], the class of paged fields and the data loaders.
[assembly: HotChocolate.Module("ProjectsTypes")]

// The loaders HotChocolate writes for the [DataLoader] methods are this project's own, like the fields that take
// them: the module's entry stays the one public type it declares.
[assembly: GreenDonut.DataLoaderDefaults(AccessModifier = GreenDonut.DataLoaderAccessModifier.Internal)]
