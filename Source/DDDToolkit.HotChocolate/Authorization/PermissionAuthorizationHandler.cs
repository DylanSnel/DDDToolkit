using System.Collections.Concurrent;
using DDDToolkit.Exceptions;
using DDDToolkit.HotChocolate.Errors;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;

namespace DDDToolkit.HotChocolate.Authorization;

/// <summary>
/// Reads the policy of HotChocolate's <c>[Authorize]</c> as a permission: a field marked
/// <c>[Authorize("library.shelves.manage")]</c> asks the <see cref="IFieldPermissions{TParent}"/> of the object it
/// belongs to whether the caller holds that permission there, before its resolver runs.
/// </summary>
/// <remarks>
/// <para>
/// It is HotChocolate's own hook, <see cref="IAuthorizationHandler"/>, and HotChocolate's own attribute and
/// directive: the rule shows in the schema as <c>@authorize(policy: "library.shelves.manage")</c>, in a source
/// schema and in the schema a gateway composes. There is no ASP.NET Core policy behind it and no principal: the
/// caller is whoever the request runs as, which is what the module's <see cref="IFieldPermissions{TParent}"/> reads.
/// </para>
/// <para>
/// <b>A refused field answers the refusal it was given.</b> The handler throws it, so the field is
/// <see langword="null"/>, the object and its other fields stay, and <c>errors</c> has one entry at the field's
/// path, shaped by <see cref="FailureErrorFilter"/> as a refused query is: the refusal's code, its kind and its
/// arguments. A field under a rule therefore has to be nullable; a refused field that is never null takes its
/// parent with it.
/// </para>
/// <para>
/// <b>What nobody can answer is refused, never allowed.</b> A parent whose type has no
/// <see cref="IFieldPermissions{TParent}"/> registered, a field with no parent (a field of <c>Query</c> declared as a
/// static method), an <c>[Authorize]</c> that names roles, or one that is applied after the resolver or during
/// validation: each answers an error that says what is missing, under HotChocolate's code for a missing policy,
/// and the field answers nothing. An <c>[Authorize]</c> with no policy answers HotChocolate's error for a
/// missing default policy.
/// </para>
/// <para>
/// A rule on a field is not the access check of a request. What a caller may read is decided where the data is
/// read, silently; a rule refuses out loud something a caller that sees the object asked for and may not have.
/// Registered by <see cref="DependencyInjection.AddDDDToolkitPermissionAuthorization"/>.
/// </para>
/// </remarks>
public sealed class PermissionAuthorizationHandler : IAuthorizationHandler
{
    /// <summary>How each parent type is asked, found once per type: reflection closes the generic, the calls after that are plain.</summary>
    private static readonly ConcurrentDictionary<Type, ParentPermissions> ByParentType = new();

    /// <summary>Asks the parent's <see cref="IFieldPermissions{TParent}"/> for the permission the directive names.</summary>
    /// <param name="context">The field being resolved.</param>
    /// <param name="directive">The field's <c>@authorize</c>.</param>
    /// <param name="cancellationToken">Ends with the request.</param>
    /// <returns><see cref="AuthorizeResult.Allowed"/> when the caller holds the permission.</returns>
    /// <exception cref="RefusalException">The caller does not hold the permission: the refusal the module gave.</exception>
    /// <exception cref="GraphQLException">Nobody can answer for the permission on this field.</exception>
    public async ValueTask<AuthorizeResult> AuthorizeAsync(IMiddlewareContext context, AuthorizeDirective directive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(directive);

        if (directive.Policy is not { Length: > 0 } permission)
        {
            return AuthorizeResult.NoDefaultPolicy;
        }

        if (directive.Roles is { Count: > 0 })
        {
            throw NobodyAnswers(context, $"The rule on '{Field(context)}' names roles. A permission is asked here, and roles are not.");
        }

        if (directive.Apply != ApplyPolicy.BeforeResolver)
        {
            throw NobodyAnswers(context, $"The permission '{permission}' on '{Field(context)}' is not asked before the resolver. A permission is asked about the object a field belongs to, before the field is resolved.");
        }

        if (context.Parent<object?>() is not { } parent)
        {
            throw NobodyAnswers(context, $"The permission '{permission}' on '{Field(context)}' has no object to be asked about: the field has no parent.");
        }

        for (var type = parent.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            if (ByParentType.GetOrAdd(type, ParentPermissions.For).Ask(parent, permission, context, cancellationToken) is { } answer)
            {
                // Thrown, not answered as "not allowed": HotChocolate has one error for that, and the module's
                // refusal has a code, a kind and arguments a client already knows from its requests.
                return await answer.ConfigureAwait(false) is { } refusal ? throw refusal : AuthorizeResult.Allowed;
            }
        }

        throw NobodyAnswers(context, $"The permission '{permission}' on '{Field(context)}' has nobody to answer for it: no IFieldPermissions<{parent.GetType().Name}> is registered.");
    }

    /// <summary>
    /// A rule applied during validation, before anything is resolved: there is no object to ask about, so it is
    /// refused.
    /// </summary>
    /// <param name="context">The request being validated.</param>
    /// <param name="directives">The rules of the request that are applied during validation.</param>
    /// <param name="cancellationToken">Ends with the request.</param>
    /// <returns><see cref="AuthorizeResult.Allowed"/> when there is no such rule.</returns>
    /// <exception cref="GraphQLException">The request has a rule that is applied during validation.</exception>
    public ValueTask<AuthorizeResult> AuthorizeAsync(AuthorizationContext context, IReadOnlyList<AuthorizeDirective> directives, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(directives);

        if (directives.Count == 0)
        {
            return new(AuthorizeResult.Allowed);
        }

        var permissions = string.Join(", ", directives.Select(static directive => "'" + directive.Policy + "'"));
        throw new GraphQLException(ErrorBuilder.New()
            .SetMessage($"The request asks for {permissions} during validation. A permission is asked about the object a field belongs to, before the field is resolved.")
            .SetCode(ErrorCodes.Authentication.PolicyNotFound)
            .Build());
    }

    private static string Field(IMiddlewareContext context) => context.Selection.Field.Coordinate.ToString();

    /// <summary>The error for a rule nobody can answer: at the field, as HotChocolate reports a policy that does not exist.</summary>
    private static GraphQLException NobodyAnswers(IMiddlewareContext context, string message)
        => new(ErrorBuilder.New()
            .SetMessage(message)
            .SetCode(ErrorCodes.Authentication.PolicyNotFound)
            .SetPath(context.Path)
            .AddLocations(context.Selection)
            .Build());

    /// <summary>Asks the <see cref="IFieldPermissions{TParent}"/> of one parent type, without knowing the type at compile time.</summary>
    private abstract class ParentPermissions
    {
        public static ParentPermissions For(Type parentType) => (ParentPermissions)Activator.CreateInstance(typeof(ParentPermissions<>).MakeGenericType(parentType))!;

        /// <summary>The answer, or <see langword="null"/> when nothing is registered for this type.</summary>
        public abstract ValueTask<RefusalException?>? Ask(object parent, string permission, IMiddlewareContext context, CancellationToken cancellationToken);
    }

    private sealed class ParentPermissions<TParent> : ParentPermissions
    {
        public override ValueTask<RefusalException?>? Ask(object parent, string permission, IMiddlewareContext context, CancellationToken cancellationToken)
            => context.Services.GetService(typeof(IFieldPermissions<TParent>)) is IFieldPermissions<TParent> permissions
                ? permissions.RefusedAsync((TParent)parent, permission, context, cancellationToken)
                : null;
    }
}
