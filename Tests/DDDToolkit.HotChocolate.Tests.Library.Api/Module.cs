// The toolkit's module, which names what its generators write: AddLibraryGraphQlRuntimeBindings(), which binds the
// ids and registers the classes marked [GraphQLSchema] for the schema of their name.
[assembly: DDDToolkit.Abstractions.Attributes.Module("Library")]

// HotChocolate's, by its full name, so it is never taken for the toolkit's: it names what HotChocolate's generator
// writes, AddLibraryTypes(), which registers every [Query] method and every [ObjectType<T>] class of this project.
[assembly: HotChocolate.Module("LibraryTypes")]
