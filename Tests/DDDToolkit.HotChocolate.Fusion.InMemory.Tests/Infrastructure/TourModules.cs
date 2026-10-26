using Gallery.Api;
using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Composite;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests.Infrastructure;

// Two modules for a reference to an entity whose owner declares its types over its own records. Gallery owns the
// exhibit: its schema is the one of the Gallery.Api project, written with HotChocolate's generator, and its only
// lookup is internal. Tours names an exhibit by its key, and the gateway fetches the rest.

/// <summary>Registers the two modules.</summary>
internal static class TourModules
{
    public const string Tours = "tours";

    /// <summary>
    /// The module that owns exhibits, with what a host gives every module's schema: the toolkit's conventions, the
    /// nullable fields of entities and the keys on fields.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="entityNullability">Whether the schema gets the convention; without it the fields are what the records say.</param>
    public static IRequestExecutorBuilder AddGallery(this IServiceCollection services, bool entityNullability = true)
    {
        var schema = services
            .AddGalleryServices()
            .AddGraphQLServer(GalleryModule.SchemaName)
            .AddSourceSchemaDefaults()
            .AddGalleryTypes()
            .AddHostConventions()
            .AddDDDToolkitKeyAuthorization();

        return entityNullability ? schema.AddDDDToolkitEntityNullability() : schema;
    }

    /// <summary>The module that names an exhibit by its key. It has the same conventions, and nothing of an exhibit to change.</summary>
    public static IRequestExecutorBuilder AddTours(this IServiceCollection services)
        => services
            .AddGraphQLServer(Tours)
            .AddSourceSchemaDefaults()
            .AddHostConventions()
            .AddDDDToolkitKeyAuthorization()
            .AddDDDToolkitEntityNullability()
            .AddQueryType()
            .AddTypeExtension<TourQueries>();
}

/// <summary>A tour, which stops at an exhibit.</summary>
[GraphQLName("Tour")]
public sealed record TourRow(int Id, ReferencedExhibit? Exhibit);

/// <summary>An exhibit as Tours knows it: by its key alone.</summary>
[GraphQLName("Exhibit")]
[EntityKey("id")]
public sealed record ReferencedExhibit(int Id);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class TourQueries
{
    /// <summary>Two tours, each stopping at an exhibit the gallery has.</summary>
    public TourRow[] GetTours() => [new(1, new ReferencedExhibit(1)), new(2, new ReferencedExhibit(3))];

    /// <summary>A tour that stops at exhibit 9, for which the gallery's lookup answers nothing.</summary>
    public TourRow GetStrayTour() => new(3, new ReferencedExhibit(9));
}
