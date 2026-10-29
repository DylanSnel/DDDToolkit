using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Invitations.GraphQL;

/// <summary>
/// What accepting answers, as the schema shows it: the invitation manager's record of the accepted invitation, the
/// tenant the invitation was for and the seat the person now has there.
/// </summary>
/// <remarks>
/// The person sends no <c>Tenant</c> header to accept, so the answer names the tenant: its slug is what the next
/// request names it by.
/// </remarks>
[ObjectType<InvitationManager.AcceptedInvitation>]
internal static partial class AcceptedInvitationType
{
    static partial void Configure(IObjectTypeDescriptor<InvitationManager.AcceptedInvitation> descriptor)
    {
        descriptor.Name("AcceptedInvitation");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(accepted => accepted.Tenant);
        descriptor.Field(accepted => accepted.Seat);
    }
}
