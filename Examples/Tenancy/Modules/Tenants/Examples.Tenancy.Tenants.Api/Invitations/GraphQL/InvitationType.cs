using Examples.Tenancy.Tenants.Api.Directory.GraphQL;
using Examples.Tenancy.Tenants.Application.Organization;
using Examples.Tenancy.Tenants.Application.Roles;
using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Invitations.GraphQL;

/// <summary>
/// An open invitation as the schema shows it: the address it is for, the unit and the role it offers, and the
/// seat that issued it. Declared over the record the package answers, so nothing is copied.
/// </summary>
/// <remarks>
/// The record names the unit, the role and the seat by id. They are this module's own, so the schema shows each
/// as what it is, read through the directory's data loaders: the units of one batch, as a rule a whole list, are
/// one question, and so are its roles and its seats. One the directory answers nothing for is
/// <see langword="null"/>. The record has no token, so the type has none.
/// </remarks>
[ObjectType<TenantsTenancy.OpenInvitation<InvitationId>>]
internal static partial class InvitationType
{
    static partial void Configure(IObjectTypeDescriptor<TenantsTenancy.OpenInvitation<InvitationId>> descriptor)
    {
        descriptor.Name("Invitation");
        descriptor.Ignore(invitation => invitation.UnitId);
        descriptor.Ignore(invitation => invitation.RoleId);

        // The route leaves it out too: the seat that issued it says who did.
        descriptor.Ignore(invitation => invitation.IssuedAsSystem);

        // The name the routes and the mutation that invites give the end of the role.
        descriptor.Field(invitation => invitation.GrantUntil).Name("until");
    }

    /// <summary>The unit the seat would be placed in.</summary>
    [Cost(DirectoryQueries.LoadedForTheRequest)]
    public static async Task<UnitListing?> GetUnitAsync(
        [Parent] TenantsTenancy.OpenInvitation<InvitationId> invitation,
        IOrganizationUnitByIdDataLoader units,
        CancellationToken cancellationToken)
        => await units.LoadAsync(invitation.UnitId, cancellationToken);

    /// <summary>The role the seat would hold there.</summary>
    [Cost(DirectoryQueries.LoadedForTheRequest)]
    public static async Task<RoleListing?> GetRoleAsync(
        [Parent] TenantsTenancy.OpenInvitation<InvitationId> invitation,
        IRoleByIdDataLoader roles,
        CancellationToken cancellationToken)
        => await roles.LoadAsync(invitation.RoleId, cancellationToken);

    /// <summary>The seat that issued it, or nothing when no seat did.</summary>
    [Cost(DirectoryQueries.LoadedForTheRequest)]
    public static async Task<TenantsTenancy.SeatSummary?> GetIssuedByAsync(
        [Parent] TenantsTenancy.OpenInvitation<InvitationId> invitation,
        ISeatByIdDataLoader seats,
        CancellationToken cancellationToken)
        => invitation.IssuedBy is { } seat ? await seats.LoadAsync(seat, cancellationToken) : null;
}
