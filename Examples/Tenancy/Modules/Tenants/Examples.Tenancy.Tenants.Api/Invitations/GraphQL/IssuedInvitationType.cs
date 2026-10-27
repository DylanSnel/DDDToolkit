using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Invitations.GraphQL;

/// <summary>
/// What inviting answers, as the schema shows it: the invitation manager's record of the issued invitation, the
/// invitation itself and its token.
/// </summary>
/// <remarks>
/// The one type of the schema with a token, and only the mutation that invites answers it: no query does, since
/// the API keeps the token's digest and nothing else.
/// </remarks>
[ObjectType<InvitationManager.IssuedInvitation>]
internal static partial class IssuedInvitationType
{
    static partial void Configure(IObjectTypeDescriptor<InvitationManager.IssuedInvitation> descriptor)
    {
        descriptor.Name("IssuedInvitation");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(issued => issued.Invitation);
        descriptor.Field(issued => issued.Token);
    }
}
