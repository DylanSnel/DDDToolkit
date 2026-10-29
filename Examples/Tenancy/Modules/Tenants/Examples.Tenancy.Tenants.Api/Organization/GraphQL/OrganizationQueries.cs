using Examples.Tenancy.Tenants.Application.Organization.Queries;
using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Organization.GraphQL;

/// <summary>What is asked about the organization's units. A field sends the query its route sends, and nothing else.</summary>
internal static class OrganizationQueries
{
    /// <summary>
    /// The units the caller sees: those it is placed in, and every unit below them, each with its kind. A list, not
    /// pages: the organization manager answers a tenant's units whole, and a tenant has few. The query answers each
    /// unit beside its path and depth, which the route writes beside it; here the unit's type has a field for each.
    /// </summary>
    [Query]
    public static async Task<IReadOnlyList<OrganizationUnit>> GetOrganizationUnitsAsync([Service] ISender sender, CancellationToken cancellationToken)
        => [.. (await sender.Send(new OrganizationUnits(), cancellationToken)).Select(found => found.Unit)];
}
