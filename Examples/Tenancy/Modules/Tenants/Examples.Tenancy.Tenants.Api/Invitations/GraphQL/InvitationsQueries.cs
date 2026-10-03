using Examples.Tenancy.Tenants.Application.Invitations.Queries;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Invitations.GraphQL;

/// <summary>What is asked about invitations. A field sends the query its route sends, and nothing else.</summary>
internal static class InvitationsQueries
{
    /// <summary>
    /// The invitations that can still be accepted, into the units where the caller manages seats, the soonest
    /// to end first: whom each is for and what it offers. A list, which may be empty, and never a token.
    /// </summary>
    [Query]
    public static async Task<IReadOnlyList<SampleTenancy.OpenInvitation<InvitationId>>> GetOpenInvitationsAsync([Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new OpenInvitations(), cancellationToken);
}
