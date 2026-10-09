using Examples.Tenancy.Tenants.Api.Directory.GraphQL;
using Examples.Tenancy.Tenants.Application.Organization;
using Examples.Tenancy.Tenants.Application.Organization.Commands;
using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.ValueObjects;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Organization.GraphQL;

/// <summary>
/// What changes the organization's tree. A mutation sends the command its route sends, then reads the unit it
/// changed and answers that; a refusal arrives in the payload's <c>errors</c>.
/// </summary>
internal static class OrganizationMutations
{
    /// <summary>Adds a unit below another, of the kind given, or of none when the kind is left out.</summary>
    [Mutation]
    public static async Task<UnitListing?> OrganizationUnitAddAsync(OrganizationUnitId parentId, string name, UnitKind? kind, [Service] ISender sender, CancellationToken cancellationToken)
    {
        var id = await sender.Send(new AddOrganizationUnit(parentId, name, kind), cancellationToken);
        return await sender.UnitNowAsync(id, cancellationToken);
    }

    /// <summary>Moves a unit, with everything below it, under another parent.</summary>
    [Mutation]
    public static async Task<UnitListing?> OrganizationUnitMoveAsync(OrganizationUnitId id, OrganizationUnitId parentId, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new MoveOrganizationUnit(id, parentId), cancellationToken);
        return await sender.UnitNowAsync(id, cancellationToken);
    }

    /// <summary>Archives a unit that is no longer in use.</summary>
    [Mutation]
    public static async Task<UnitListing?> OrganizationUnitArchiveAsync(OrganizationUnitId id, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new ArchiveOrganizationUnit(id), cancellationToken);
        return await sender.UnitNowAsync(id, cancellationToken);
    }
}
