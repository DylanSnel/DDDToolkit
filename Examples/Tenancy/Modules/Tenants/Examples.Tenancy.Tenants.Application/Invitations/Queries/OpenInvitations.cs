using Examples.Tenancy.Tenants.Domain.Aggregates.Invitations;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Invitations.Queries;

/// <summary>
/// The tenant's invitations that can still be accepted, the soonest to end first: the module's own invitations, each
/// with the address it is for and what it offers by id.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant, and the package decides what the caller reads: the invitations
/// into the units where it holds <c>tenancy.seats.manage</c>, which may be none. Nobody is refused with its own
/// reason, such as <c>tenancy.not-seated</c>. No invitation holds a token: that was shown once, when the invitation
/// was issued, and the store keeps its digest apart. Each holds the account the identity provider made for its
/// address, so whatever shows an invitation writes what it chooses of it, and never that account.
/// </remarks>
[InTenant]
public sealed record OpenInvitations : IQuery<IReadOnlyList<Invitation>>, ITenantsRequest;

/// <summary>Answers <see cref="OpenInvitations"/> from the Tenancy package, which checks the caller and reads.</summary>
/// <param name="reads">Where Tenancy is read.</param>
public sealed class OpenInvitationsHandler(ITenancyReads reads) : IQueryHandler<OpenInvitations, IReadOnlyList<Invitation>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package refuses, with its code.</exception>
    public async ValueTask<IReadOnlyList<Invitation>> Handle(OpenInvitations query, CancellationToken cancellationToken)
        => await reads.OpenInvitationsAsync(cancellationToken);
}
