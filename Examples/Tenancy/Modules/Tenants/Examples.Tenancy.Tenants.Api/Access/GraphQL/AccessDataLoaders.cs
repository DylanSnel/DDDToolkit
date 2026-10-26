using Examples.Tenancy.Tenants.Application.Access.Queries;
using GreenDonut;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Access.GraphQL;

/// <summary>
/// Where the caller holds a permission, asked once for a whole request. HotChocolate's generator writes
/// <c>IHeldUnitsByPermissionDataLoader</c> from the method.
/// </summary>
/// <remarks>
/// It is what a rule on a field asks: every role of a list asks whether the caller holds the same permission, each waits
/// on this loader, and the query is sent once. The query answers one permission, so the method takes one permission and the
/// loader the generator writes remembers each answer for the request: a permission asked a hundred times is one
/// question, and two permissions are two. The generated loader calls the method in a scope of services of its own, so
/// the access check of the query runs as for any other caller of it.
/// </remarks>
internal static class AccessDataLoaders
{
    /// <summary>Where the caller holds <paramref name="permission"/>.</summary>
    [DataLoader]
    public static async Task<HeldUnits> GetHeldUnitsByPermissionAsync(string permission, ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new UnitsWhereIHold(permission), cancellationToken);
}
