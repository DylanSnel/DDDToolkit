using Examples.Tenancy.Tenants.Application.Access.Queries;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Access.GraphQL;

/// <summary>What the caller asks about its own access. A field sends the query its route sends, and nothing else.</summary>
internal static class AccessQueries
{
    /// <summary>
    /// Where the caller holds a key, for the screens that show who may do what: the units, by id, and whether
    /// that is the whole tenant. A key the catalogue does not know is refused. The answer is the query's own
    /// record, which the schema shows as it is.
    /// </summary>
    [Query]
    public static async Task<HeldUnits> GetUnitsWhereIHoldAsync(string key, [Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new UnitsWhereIHold(key), cancellationToken);
}
