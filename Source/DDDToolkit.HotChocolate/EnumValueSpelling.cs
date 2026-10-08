using System.Text.Json;

namespace DDDToolkit.HotChocolate;

/// <summary>
/// How a schema spells the values of its enums. GraphQL leaves it to the schema, and so does the toolkit: it
/// is the host's choice, made once for the whole schema with
/// <see cref="DependencyInjection.AddDDDToolkitEnumValues"/>.
/// </summary>
public enum EnumValueSpelling
{
    /// <summary>The way HotChocolate writes them, and GraphQL's custom: <c>NotPermitted</c> is <c>NOT_PERMITTED</c>.</summary>
    UpperSnakeCase,

    /// <summary>
    /// <c>NotPermitted</c> is <c>not_permitted</c>: what <see cref="JsonNamingPolicy.SnakeCaseLower"/> writes, so
    /// a REST API whose JSON uses that policy for its enums and a GraphQL schema beside it spell a value the
    /// same way, and so does a database that stores it so.
    /// </summary>
    LowerSnakeCase,
}

/// <summary>
/// The spelling <see cref="DependencyInjection.AddDDDToolkitEnumValues"/> gave a schema, kept in the schema's
/// services, where the error filter reads it.
/// </summary>
internal sealed record SchemaEnumValueSpelling(EnumValueSpelling Spelling);

/// <summary>The one place a value's name becomes its spelling, for the schema's enums and for the error filter's <c>kind</c>.</summary>
internal static class EnumValueSpellings
{
    /// <summary>The name of an enum's member, spelled.</summary>
    public static string Spell(string memberName, EnumValueSpelling spelling)
        => spelling switch
        {
            EnumValueSpelling.LowerSnakeCase => JsonNamingPolicy.SnakeCaseLower.ConvertName(memberName),
            EnumValueSpelling.UpperSnakeCase => JsonNamingPolicy.SnakeCaseUpper.ConvertName(memberName),
            _ => throw new ArgumentOutOfRangeException(nameof(spelling), spelling, "An enum value is spelled UpperSnakeCase or LowerSnakeCase."),
        };

    /// <summary>Refuses a spelling that is not one of the two, where the host chooses it and not when a schema is built.</summary>
    public static EnumValueSpelling Checked(EnumValueSpelling spelling, string parameterName)
        => Enum.IsDefined(spelling)
            ? spelling
            : throw new ArgumentOutOfRangeException(parameterName, spelling, "An enum value is spelled UpperSnakeCase or LowerSnakeCase.");
}
