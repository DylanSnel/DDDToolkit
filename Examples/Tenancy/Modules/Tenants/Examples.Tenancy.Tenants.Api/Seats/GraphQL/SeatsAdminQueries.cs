using DDDToolkit.HotChocolate.Attributes;
using Examples.Tenancy.Tenants.Api.GraphQL;
using Examples.Tenancy.Tenants.Application.Seats.Queries;
using HotChocolate;
using HotChocolate.Language;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// What the tenant's administration asks about seats: another person's roles. Only the administration's schema has
/// it, which the host serves at <c>/admin/graphql</c>; the schema every seat is offered at <c>/graphql</c> has not.
/// A seat reads its own roles there, in <c>overviewOfMine</c>.
/// </summary>
/// <remarks>
/// Marked for that one schema, the class carries nothing else that says what it is: its methods have no
/// <c>[Query]</c>, since HotChocolate's generator would make a method marked so a field of every schema. The
/// toolkit's generator registers them in the module's bindings, <c>AddTenantsGraphQlRuntimeBindings()</c>, which both
/// schemas call, for a builder of the administration's name only.
/// Which schema offers a field is the schema's to say; who may read it is the request's, as for every other field:
/// <see cref="SeatGrants"/> requires <c>tenancy.seats.manage</c> for the whole tenant, at <c>/admin/graphql</c> as
/// on its route.
/// </remarks>
[GraphQLSchema(TenantsGraphQL.AdministrationSchemaName, OperationType.Query)]
internal static class SeatsAdminQueries
{
    /// <summary>The roles a seat of the tenant holds, where and for which period.</summary>
    public static async Task<IReadOnlyList<SeatGrant>> GetSeatGrantsAsync(SeatId seatId, [Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new SeatGrants(seatId), cancellationToken);
}
