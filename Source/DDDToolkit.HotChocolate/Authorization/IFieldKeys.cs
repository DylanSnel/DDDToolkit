using DDDToolkit.Exceptions;
using HotChocolate.Resolvers;

namespace DDDToolkit.HotChocolate.Authorization;

/// <summary>
/// Answers whether the caller holds a permission key on one object of a schema: what
/// <see cref="KeyAuthorizationHandler"/> asks when a field of that object carries
/// <c>[Authorize("&lt;key&gt;")]</c>.
/// <code>
/// internal sealed class ShelfFieldKeys : IFieldKeys&lt;Shelf&gt;
/// {
///     public async ValueTask&lt;RefusalException?&gt; RefusedAsync(Shelf parent, string key, IResolverContext context, CancellationToken cancellationToken)
///         =&gt; (await context.Service&lt;HeldKeysByShelfLoader&gt;().LoadAsync(parent.Id, cancellationToken))?.Contains(key) is true
///             ? null
///             : new RefusalException("library.not-permitted", RefusalKind.NotPermitted, "The caller may not read this.", new Dictionary&lt;string, object?&gt; { ["Key"] = key });
/// }
/// </code>
/// <para>
/// The toolkit knows nothing about who holds what. A module does, for its own types, so it writes one small class
/// per parent type and registers it in the application's services, scoped:
/// <c>services.AddScoped&lt;IFieldKeys&lt;Shelf&gt;, ShelfFieldKeys&gt;()</c>. It is resolved from the request's
/// services, for the parent's own type first and then for each type it derives from.
/// </para>
/// <para>
/// <b>Ask through a data loader.</b> The question is asked once for every parent, and the parents of a list are
/// asked side by side. An implementation that awaits a data loader keyed by the parent makes that one question
/// for each batch the loader sends, which is the whole page as a rule; one that reads on its own makes it a
/// statement per row.
/// </para>
/// </summary>
/// <typeparam name="TParent">The runtime type of the object the field belongs to.</typeparam>
public interface IFieldKeys<in TParent>
{
    /// <summary>
    /// Whether the caller is refused <paramref name="key"/> on <paramref name="parent"/>.
    /// </summary>
    /// <param name="parent">The object whose field is being asked for.</param>
    /// <param name="key">The permission key the field names: the policy of its <c>[Authorize]</c>.</param>
    /// <param name="context">The field's resolver context, for its services and data loaders.</param>
    /// <param name="cancellationToken">Ends with the request.</param>
    /// <returns>
    /// <see langword="null"/> when the caller holds the key. Otherwise the refusal to answer the field with: the
    /// one the module's access check gives for the same key, so a client reads one code whether a request or a
    /// field was refused.
    /// </returns>
    ValueTask<RefusalException?> RefusedAsync(TParent parent, string key, IResolverContext context, CancellationToken cancellationToken);
}
