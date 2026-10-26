using DDDToolkit.Exceptions;
using DDDToolkit.HotChocolate.Authorization;
using Gallery.Application;
using GreenDonut;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gallery.Api;

// The gallery's GraphQL types, declared over the application's records the way a module's API project declares
// them: no output record of its own, no mapping, and nothing said about nullability.

/// <summary>What the gallery registers besides its schema.</summary>
public static class GalleryModule
{
    /// <summary>The name of the gallery's source schema.</summary>
    public const string SchemaName = "gallery";

    /// <summary>
    /// The gallery's services: its queries, and who answers for a key on an exhibit. A test that wants to say what
    /// the caller holds registers its own <see cref="GalleryDesk"/> first.
    /// </summary>
    public static IServiceCollection AddGalleryServices(this IServiceCollection services, bool answersForExhibits = true)
    {
        services.TryAddSingleton<GalleryDesk>();

        if (answersForExhibits)
        {
            services.AddScoped<IFieldKeys<ExhibitOverview>, ExhibitFieldKeys>();
        }

        return services;
    }
}

/// <summary>An exhibit: the application's record, with what a schema adds to it.</summary>
[ObjectType<ExhibitOverview>]
[EntityKey("id")]
public static partial class ExhibitType
{
    static partial void Configure(IObjectTypeDescriptor<ExhibitOverview> descriptor) => descriptor.Name("Exhibit");

    /// <summary>A resolver that answers a text which is never null.</summary>
    public static string GetCaption([Parent] ExhibitOverview exhibit) => $"{exhibit.Title} ({exhibit.Year})";

    /// <summary>Who looks after it: another module's entity, named by its key.</summary>
    [BindMember(nameof(ExhibitOverview.CuratorId))]
    public static ReferencedCurator GetCurator([Parent] ExhibitOverview exhibit) => new(exhibit.CuratorId);

    /// <summary>A resolver that answers a list which is never null, of items that never are.</summary>
    public static IReadOnlyList<LoanOverview> GetLoans([Parent] ExhibitOverview exhibit, [Service] GalleryDesk desk) => desk.LoansOf(exhibit.Id);

    /// <summary>What it is insured for: only for a caller that holds the key on this exhibit.</summary>
    [Authorize(GalleryKeys.ViewValuations)]
    public static int GetValuation([Parent] ExhibitOverview exhibit, [Service] GalleryDesk desk) => desk.ValuationOf(exhibit.Id);
}

/// <summary>A loan. No key, so it is no entity, and its fields stay what the record says.</summary>
[ObjectType<LoanOverview>]
public static partial class LoanType
{
    static partial void Configure(IObjectTypeDescriptor<LoanOverview> descriptor) => descriptor.Name("Loan");

    /// <summary>A resolver of a type without a key.</summary>
    public static int GetWeeks([Parent] LoanOverview loan) => loan.Days / 7;
}

/// <summary>A curator as the gallery knows one: by the key alone.</summary>
public sealed record ReferencedCurator(int Id);

/// <summary>The type of that reference: an entity of which this schema has only the key.</summary>
[ObjectType<ReferencedCurator>]
[EntityKey("id")]
public static partial class ReferencedCuratorType
{
    static partial void Configure(IObjectTypeDescriptor<ReferencedCurator> descriptor) => descriptor.Name("Curator");
}

/// <summary>The gallery's root fields.</summary>
public static class GalleryQueries
{
    /// <summary>Every exhibit: a list, so a rule on a field of an exhibit is asked for all of them at once.</summary>
    [Query]
    public static IReadOnlyList<ExhibitOverview> GetExhibits([Service] GalleryDesk desk) => desk.Exhibits;

    /// <summary>The lookup a gateway resolves a reference to an exhibit by. It answers nothing for one that is not there.</summary>
    [Query]
    [Lookup]
    [Internal]
    public static ExhibitOverview? GetExhibitById(int id, [Service] GalleryDesk desk) => desk.Find(id);
}

/// <summary>The gallery's data loaders, written by HotChocolate's generator from these methods.</summary>
public static class GalleryDataLoaders
{
    /// <summary>The keys the caller holds on each exhibit of a request: one question, however many exhibits ask.</summary>
    [DataLoader]
    public static Task<IReadOnlyDictionary<int, IReadOnlySet<string>>> GetHeldKeysByExhibitIdAsync(
        IReadOnlyList<int> exhibits,
        GalleryDesk desk,
        CancellationToken cancellationToken)
        => Task.FromResult(desk.HeldKeysOn(exhibits));
}

/// <summary>Who answers for a key on an exhibit: the loader, so the exhibits of a list are asked together.</summary>
public sealed class ExhibitFieldKeys : IFieldKeys<ExhibitOverview>
{
    public async ValueTask<RefusalException?> RefusedAsync(ExhibitOverview parent, string key, IResolverContext context, CancellationToken cancellationToken)
        => await context.Service<IHeldKeysByExhibitIdDataLoader>().LoadAsync(parent.Id, cancellationToken) is { } held && held.Contains(key)
            ? null
            : GalleryRefusals.NotPermittedFor(key, parent.Id);
}
