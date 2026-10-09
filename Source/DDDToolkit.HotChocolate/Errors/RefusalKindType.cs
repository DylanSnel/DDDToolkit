using DDDToolkit.Exceptions;

namespace DDDToolkit.HotChocolate.Errors;

/// <summary>
/// <see cref="RefusalKind"/> as the enum <c>RefusalKind</c>, which the mutation conventions put in a schema,
/// described for the client that reads it.
/// </summary>
/// <remarks>
/// The enum is the core's, which knows nothing of GraphQL, so its descriptions are given here and not with
/// <c>[GraphQLDescription]</c> on its members; see <see cref="ErrorDescriptions"/> for why they are given at all.
/// The values are named by the schema's naming conventions, so they are spelled as the schema's other enums
/// are, by <see cref="DependencyInjection.AddDDDToolkitEnumValues"/> when the host chose a spelling.
/// </remarks>
internal sealed class RefusalKindType : EnumType<RefusalKind>
{
    /// <inheritdoc />
    protected override void Configure(IEnumTypeDescriptor<RefusalKind> descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        descriptor.Description(
            "Why a command was refused, in the four answers an edge maps to a response: the request was not acceptable, "
            + "the caller may not do it, what it names does not exist, or the state it met does not allow it.");

        descriptor.Value(RefusalKind.Invalid)
            .Description("The request itself is not acceptable: a name too long, a key nobody declared. Usually a 400.");
        descriptor.Value(RefusalKind.NotPermitted)
            .Description("The caller may not do this. Usually a 403.");
        descriptor.Value(RefusalKind.NotFound)
            .Description("Something the request names does not exist, or is not the caller's to see. Usually a 404.");
        descriptor.Value(RefusalKind.Conflict)
            .Description("The state the command met does not allow it: already archived, the last one of its kind. Usually a 409.");
    }
}
