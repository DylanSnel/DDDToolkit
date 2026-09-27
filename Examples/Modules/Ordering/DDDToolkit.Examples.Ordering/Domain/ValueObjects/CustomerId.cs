using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Examples.Ordering.Domain.ValueObjects;

/// <summary>
/// Who placed an order: the id the identity provider gave a signed-in user, <c>sub</c> in their token.
/// </summary>
/// <remarks>
/// Ordering keeps no customers. The id is the identity provider's, and this type only stops it being
/// mixed up with every other <see cref="Guid"/> in the module, the way <c>OrderId</c> does for orders.
/// </remarks>
[EntityId<Guid>]
public readonly partial record struct CustomerId
{
    /// <summary>The customer <paramref name="caller"/> is, or <see langword="null"/> for a guest: nobody signed in.</summary>
    public static CustomerId? Of(Caller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return caller.UserId is { } user ? new CustomerId(user) : null;
    }
}
