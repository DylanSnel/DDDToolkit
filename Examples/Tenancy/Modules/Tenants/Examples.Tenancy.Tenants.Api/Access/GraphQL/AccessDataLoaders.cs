using Examples.Tenancy.Tenants.Application.Access.Queries;
using GreenDonut;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Access.GraphQL;

/// <summary>
/// Where the caller holds a key, asked once for a whole request. HotChocolate's generator writes
/// <c>IHeldUnitsByKeyDataLoader</c> from the method.
/// </summary>
/// <remarks>
/// It is what a rule on a field asks: every role of a list asks whether the caller holds the same key, each waits
/// on this loader, and the query is sent once. The query answers one key, so the method takes one key and the
/// loader the generator writes remembers each answer for the request: a key asked a hundred times is one
/// question, and two keys are two. The generated loader calls the method in a scope of services of its own, so
/// the access check of the query runs as for any other caller of it.
/// </remarks>
internal static class AccessDataLoaders
{
    /// <summary>Where the caller holds <paramref name="key"/>.</summary>
    [DataLoader]
    public static async Task<HeldUnits> GetHeldUnitsByKeyAsync(string key, ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new UnitsWhereIHold(key), cancellationToken);
}
