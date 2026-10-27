using Examples.Tenancy.Tenants.Application.Organization.Commands;
using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities;
using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.ValueObjects;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Organization.GraphQL;

/// <summary>
/// What changes the organization's tree. A mutation sends the command its route sends and answers the unit the
/// command answers; a refusal arrives in the payload's <c>errors</c>.
/// </summary>
internal static class OrganizationMutations
{
    /// <summary>Adds a unit below another, of the kind given, or of none when the kind is left out.</summary>
    [Mutation]
    public static async Task<OrganizationUnit> OrganizationUnitAddAsync(OrganizationUnitId parentId, string name, UnitKind? kind, [Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new AddOrganizationUnit(parentId, name, kind), cancellationToken);

    /// <summary>Moves a unit, with everything below it, under another parent.</summary>
    [Mutation]
    public static async Task<OrganizationUnit> OrganizationUnitMoveAsync(OrganizationUnitId id, OrganizationUnitId parentId, [Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new MoveOrganizationUnit(id, parentId), cancellationToken);

    /// <summary>Archives a unit that is no longer in use.</summary>
    [Mutation]
    public static async Task<OrganizationUnit> OrganizationUnitArchiveAsync(OrganizationUnitId id, [Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new ArchiveOrganizationUnit(id), cancellationToken);
}
