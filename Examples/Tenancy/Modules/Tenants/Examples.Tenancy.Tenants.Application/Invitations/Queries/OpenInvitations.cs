using Mediator;

namespace Examples.Tenancy.Tenants.Application.Invitations.Queries;

/// <summary>
/// The tenant's invitations that can still be accepted, the soonest to end first, each with the address it is
/// for and what it offers by id.
/// </summary>
/// <remarks>
/// The package decides what the caller reads: the invitations into the units where it holds
/// <c>tenancy.seats.manage</c>, which may be none. Nobody is refused with its own reason, such as
/// <c>tenancy.not-seated</c>. No answer carries a token: that was shown once, when the invitation was issued.
/// </remarks>
public sealed record OpenInvitations : IQuery<IReadOnlyList<SampleTenancy.OpenInvitation<InvitationId>>>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.DecidedByThePackage();
}

/// <summary>Answers <see cref="OpenInvitations"/> from the Tenancy package, which checks the caller and reads.</summary>
/// <param name="reads">Where Tenancy is read.</param>
public sealed class OpenInvitationsHandler(ITenancyReads reads) : IQueryHandler<OpenInvitations, IReadOnlyList<SampleTenancy.OpenInvitation<InvitationId>>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package refuses, with its code.</exception>
    public async ValueTask<IReadOnlyList<SampleTenancy.OpenInvitation<InvitationId>>> Handle(OpenInvitations query, CancellationToken cancellationToken)
        => await reads.OpenInvitationsAsync(cancellationToken);
}
