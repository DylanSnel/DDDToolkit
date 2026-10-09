using HotChocolate;
using HotChocolate.Resolvers;

namespace DDDToolkit.HotChocolate.Errors;

/// <summary>
/// What every error in a mutation's payload has in common, so a client can read any of them without knowing
/// which it is: a code to branch on, a message to show, and the values the message was built from.
/// <para>
/// In the schema it is the interface <c>CodedError</c>, which
/// <see cref="DependencyInjection.AddDDDToolkitMutationConventions"/> makes the interface of every error type
/// of the schema's mutations:
/// </para>
/// <code>
/// interface CodedError {
///   code: String!
///   message: String!
///   arguments: [FailureArgument!]!
/// }
/// </code>
/// <para>
/// An error type of your own, added to a mutation with HotChocolate's <c>[Error&lt;T&gt;]</c>, implements it
/// too, so the list stays one a client reads the same way from top to bottom.
/// </para>
/// </summary>
[GraphQLName("CodedError")]
[GraphQLDescription("What every error in a mutation's payload has in common: a code to branch on, a message to show, and the values the message was built from.")]
public interface ICodedError
{
    /// <summary>The rule that said no, stable across releases and languages: what a client branches on.</summary>
    [GraphQLDescription("The rule that said no, stable across releases and languages: what a client branches on.")]
    string Code { get; }

    /// <summary>
    /// What went wrong, in the reader's language when the application registered an
    /// <c>IFailureLocalizer</c>, and in the domain's own words otherwise. The field <c>message</c>.
    /// </summary>
    /// <remarks>
    /// A method, because the language is the request's: the localizer is asked for when the field is resolved,
    /// from the services of the resolver that is answering it.
    /// </remarks>
    /// <param name="context">The resolver context of the <c>message</c> field.</param>
    [GraphQLDescription(ErrorDescriptions.Message)]
    string GetMessage(IResolverContext context);

    /// <summary>The values the message was built from, by name, so a client can phrase it itself.</summary>
    [GraphQLDescription(ErrorDescriptions.Arguments)]
    IReadOnlyList<FailureArgument> Arguments { get; }
}
