using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Validation;

/// <summary>
/// A value object that can be turned into its always-valid twin. Every <c>[ValueObject]</c>,
/// <c>[SingleValueObject&lt;T&gt;]</c> and <c>[EntityId&lt;T&gt;] partial record</c> implements this, with
/// <typeparamref name="TValid"/> bound to its generated <c>Valid&lt;Name&gt;</c>.
/// <para>
/// Its point is to make the non-throwing conversion possible without a generated method per type:
/// <see cref="ValidationExtensions.TryToValid{TValid}(IValidatable{TValid}, out TValid, out IReadOnlyList{ValidationError})"/>
/// is an extension on this interface, so the twin type is inferred at the call site.
/// </para>
/// <para>
/// Struct identifiers do not implement it. They are well formed by construction and have no twin.
/// </para>
/// </summary>
/// <typeparam name="TValid">The always-valid twin this value object converts to.</typeparam>
public interface IValidatable<out TValid> : IValueObject
    where TValid : class
{
    /// <summary>The failures from the last validation run; empty when the value is valid.</summary>
    IReadOnlyList<ValidationError> ValidationErrors { get; }

    /// <summary>
    /// The always-valid twin of this value.
    /// </summary>
    /// <exception cref="Exceptions.InvalidValueObjectException">The value does not satisfy its own rules.</exception>
    TValid ToValid();
}
