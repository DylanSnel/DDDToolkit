using DDDToolkit.Exceptions;
using DDDToolkit.HotChocolate.Authorization;
using Examples.Tenancy.Tenants.Api.Access.GraphQL;
using Examples.Tenancy.Tenants.Application.Roles;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Roles.GraphQL;

/// <summary>
/// Answers a permission key a field of a role asks for, such as <c>[Authorize("tenancy.roles.manage")]</c> on its
/// keys: the caller holds it when it holds it for the whole tenant, which is where a tenant's roles are managed.
/// </summary>
/// <remarks>
/// Asked through the data loader, taken from the field as every rule on a field takes its loader, so a list of
/// roles is one question. The refusal is the one the module's access check gives for the same key, so a client
/// reads one code whether a request or a field was refused.
/// </remarks>
internal sealed class RoleFieldKeys : IFieldKeys<RoleListing>
{
    /// <inheritdoc />
    public async ValueTask<RefusalException?> RefusedAsync(RoleListing parent, string key, IResolverContext context, CancellationToken cancellationToken)
        => await context.DataLoader<IHeldUnitsByKeyDataLoader>().LoadAsync(key, cancellationToken) is { WholeTenant: true }
            ? null
            : TenancyRefusals.Refuse(TenancyRefusals.NotPermitted, ("Key", key));
}
