using Examples.Tenancy.Tenants.Application.Organization.Queries;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Organization.GraphQL;

/// <summary>What is asked about the organization's units. A field sends the query its route sends, and nothing else.</summary>
internal static class OrganizationQueries
{
    /// <summary>
    /// The units the caller sees: those it is placed in, and every unit below them. A list, not pages: the
    /// package's directory answers a tenant's units whole, and a tenant has few.
    /// </summary>
    [Query]
    public static async Task<IReadOnlyList<SampleTenancy.UnitSummary>> GetOrganizationUnitsAsync([Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new OrganizationUnits(), cancellationToken);
}
