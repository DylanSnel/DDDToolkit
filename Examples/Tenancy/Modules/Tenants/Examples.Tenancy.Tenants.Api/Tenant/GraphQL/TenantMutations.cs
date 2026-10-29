using Examples.Tenancy.Tenants.Application.Tenant.Commands;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Tenant.GraphQL;

/// <summary>
/// What changes the tenant itself. A mutation sends the command its route sends and answers the tenant the command
/// answers; a refusal arrives in the payload's <c>errors</c>.
/// </summary>
internal static class TenantMutations
{
    /// <summary>
    /// Changes the tenant's shape: flat to hierarchical, never back. The answer is the tenant in its new shape.
    /// </summary>
    [Mutation]
    public static async Task<Domain.Aggregates.Tenants.Tenant> TenantShapeChangeAsync(TenantShape shape, [Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new ChangeTenantShape(shape), cancellationToken);
}
