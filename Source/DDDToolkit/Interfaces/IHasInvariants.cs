namespace DDDToolkit.Interfaces;

/// <summary>
/// One non generic way to run an object's invariant checks. Every <c>[Entity]</c> and
/// <c>[AggregateRoot]</c> has it, because <c>Entity&lt;TId&gt;</c> implements it, so the persistence
/// layer can ask a tracked object to prove it is consistent without knowing its id type.
/// </summary>
/// <remarks>
/// You rarely name this interface yourself. Call <c>EnsureInvariants()</c> on the aggregate, and let
/// <c>DDDToolkit.EntityFramework</c>'s <c>InvariantInterceptor</c> call it for you on every save.
/// </remarks>
public interface IHasInvariants
{
    /// <summary>
    /// Runs the invariant checks of this object and throws an
    /// <c>InvariantViolationException</c> when one of them is broken.
    /// </summary>
    void EnsureInvariants();
}
