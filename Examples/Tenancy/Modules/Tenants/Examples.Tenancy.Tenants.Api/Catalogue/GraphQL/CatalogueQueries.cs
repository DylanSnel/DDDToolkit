using Examples.Tenancy.Tenants.Application.Catalogue.Queries;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Catalogue.GraphQL;

/// <summary>What is asked about the catalogue. A field sends the query its route sends, and nothing else.</summary>
internal static class CatalogueQueries
{
    /// <summary>Every permission key and role pack of the application, for the screens that assign roles.</summary>
    [Query]
    public static async Task<CatalogueOverview> GetCatalogueAsync([Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new CatalogueContents(), cancellationToken);
}
