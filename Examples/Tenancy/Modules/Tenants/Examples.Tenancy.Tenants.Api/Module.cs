using DDDToolkit.Abstractions.Attributes;

[assembly: Module("Tenants")]

// HotChocolate's attribute, not the toolkit's above, hence its full name: it names what HotChocolate's generator
// writes for this project, AddTenantsTypes, which registers every method marked [Query] or [Mutation] here, the
// class of paged fields marked [QueryType], every type class marked [ObjectType<T>], and the data loaders it
// writes from the methods marked [DataLoader].
[assembly: HotChocolate.Module("TenantsTypes")]

// The data loaders the generator writes are the module's own, as everything of its GraphQL is: the entry stays
// the one public type of this project.
[assembly: GreenDonut.DataLoaderDefaults(AccessModifier = GreenDonut.DataLoaderAccessModifier.Internal)]
