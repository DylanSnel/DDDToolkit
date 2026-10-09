using Examples.Tenancy.Tenants.Application.Tenant.Commands;
using HotChocolate;
using HotChocolate.Types;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Tenant.GraphQL;

/// <summary>What changes the tenant itself. A mutation sends the command its route sends; a refusal arrives in the payload's <c>errors</c>.</summary>
internal static class TenantMutations
{
    /// <summary>
    /// Changes the tenant's shape: flat to hierarchical, never back. The answer is the shape the tenant has
    /// from now on, which is the one asked for, since anything else is a refusal.
    /// </summary>
    [Mutation]
    [UseMutationConvention(PayloadFieldName = "shape")]
    public static async Task<TenantShape> TenantShapeChangeAsync(TenantShape shape, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new ChangeTenantShape(shape), cancellationToken);
        return shape;
    }
}
