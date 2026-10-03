using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Invitations.GraphQL;

/// <summary>
/// What inviting answers, as the schema shows it: the invitation's id, when it ends, and its token. Declared over
/// the record the package answers.
/// </summary>
/// <remarks>
/// The one type of the schema with a token, and only the mutation that invites answers it: no query does, since
/// the API keeps the token's digest and nothing else.
/// </remarks>
[ObjectType<SampleTenancy.IssuedInvitation<InvitationId>>]
internal static partial class IssuedInvitationType
{
    static partial void Configure(IObjectTypeDescriptor<SampleTenancy.IssuedInvitation<InvitationId>> descriptor) => descriptor.Name("IssuedInvitation");
}
