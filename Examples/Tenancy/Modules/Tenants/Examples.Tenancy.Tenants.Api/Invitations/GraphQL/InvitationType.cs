using DDDToolkit.Supporting.Tenancy.Access;
using Examples.Tenancy.Tenants.Api.Directory.GraphQL;
using Examples.Tenancy.Tenants.Domain;
using Examples.Tenancy.Tenants.Domain.Aggregates.Invitations;
using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Invitations.GraphQL;

/// <summary>
/// An open invitation as the schema shows it: the module's own <see cref="Invitation"/>, the class the invitation
/// manager answers, with the address it is for, the unit and the role it offers, and the seat that issued it.
/// </summary>
/// <remarks>
/// The type is the class itself, so nothing is copied. Its own fields are listed one by one: the class also holds the
/// account the identity provider made for the address, which the schema never shows. The invitation names the unit,
/// the role and the seat by id. They are this module's own, so the schema shows each as what it is, read through the
/// directory's data loaders: the units of one batch, as a rule a whole list, are one question, and so are its roles
/// and its seats. One the directory answers nothing for is <see langword="null"/>. The invitation holds no token, so
/// the type has none.
/// </remarks>
[ObjectType<Invitation>]
internal static partial class InvitationType
{
    static partial void Configure(IObjectTypeDescriptor<Invitation> descriptor)
    {
        descriptor.BindFieldsExplicitly();
        descriptor.Field(invitation => invitation.Id);
        descriptor.Field(invitation => invitation.Address);
        descriptor.Field(invitation => invitation.GrantUntil);
        descriptor.Field(invitation => invitation.IssuedAt);
        descriptor.Field(invitation => invitation.ExpiresAt);
    }

    /// <summary>The unit the seat would be placed in, by name.</summary>
    [Cost(DirectoryQueries.LoadedForTheRequest)]
    public static async Task<NamedUnit<OrganizationUnitId>?> GetUnitAsync(
        [Parent] Invitation invitation,
        IOrganizationUnitNameByIdDataLoader units,
        CancellationToken cancellationToken)
        => await units.LoadAsync(invitation.UnitId, cancellationToken);

    /// <summary>The role the seat would hold there.</summary>
    [Cost(DirectoryQueries.LoadedForTheRequest)]
    public static async Task<Role?> GetRoleAsync(
        [Parent] Invitation invitation,
        IRoleByIdDataLoader roles,
        CancellationToken cancellationToken)
        => (await roles.LoadAsync(invitation.RoleId, cancellationToken))?.Role;

    /// <summary>The seat that issued it, or nothing when no seat did.</summary>
    [Cost(DirectoryQueries.LoadedForTheRequest)]
    public static async Task<Seat?> GetIssuedByAsync(
        [Parent] Invitation invitation,
        ISeatByIdDataLoader seats,
        CancellationToken cancellationToken)
        => invitation.IssuedBy is { } seat ? await seats.LoadAsync(seat, cancellationToken) : null;
}
