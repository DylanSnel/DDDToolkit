namespace DDDToolkit.HotChocolate.Errors;

/// <summary>
/// The descriptions a client reads in the schema of the fields every error type has.
/// </summary>
/// <remarks>
/// The error types are described with <c>[GraphQLDescription]</c>, and not from their XML documentation: that is
/// written for the C# reader of this package, and whether HotChocolate finds it depends on whether the file lies
/// beside the assembly, which is the application's build to decide. Set here, the descriptions are the same in
/// every schema, so the source schemas a gateway composes describe the types they share alike.
/// </remarks>
internal static class ErrorDescriptions
{
    /// <summary>The field <c>message</c>, of every error and of each failure and violation in one.</summary>
    public const string Message =
        "What went wrong, in the reader's language when the server has a translation for it, and in the domain's own words otherwise.";

    /// <summary>The field <c>arguments</c>, where it holds the values of the error's own message.</summary>
    public const string Arguments = "The values the message was built from, by name, so a client can phrase it itself.";
}
