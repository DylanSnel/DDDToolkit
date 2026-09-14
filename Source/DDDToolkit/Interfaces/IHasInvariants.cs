using DDDToolkit.Invariants;

namespace DDDToolkit.Interfaces;

/// <summary>
/// One non generic way to run an object's invariant checks. Every <c>[Entity]</c> and
/// <c>[AggregateRoot]</c> has it, because <c>Entity&lt;TId&gt;</c> implements it, so the persistence
/// layer can ask a tracked object to prove it is consistent without knowing its id type.
/// </summary>
/// <remarks>
/// You rarely name this interface yourself. Call <c>EnsureInvariants()</c> on the aggregate, and let
/// <c>DDDToolkit.EntityFramework</c>'s <c>InvariantInterceptor</c> call it for you on every save.
/// <para>
/// The four members are two questions asked of two subjects. <see cref="EnsureInvariants"/> and
/// <see cref="GetInvariantViolations"/> answer for the aggregate this object holds, its child entities
/// included; <see cref="EnsureOwnInvariants"/> and <see cref="GetOwnInvariantViolations"/> answer for
/// this object alone. Ask the first pair unless you are walking the graph yourself, in which case the
/// second pair is what keeps a child from being reported once by itself and once by its parent.
/// </para>
/// </remarks>
public interface IHasInvariants
{
    /// <summary>
    /// Runs the invariant checks of this object and of every child entity it holds, and throws an
    /// <c>InvariantViolationException</c> when one of them is broken.
    /// </summary>
    void EnsureInvariants();

    /// <summary>
    /// Runs the same checks and returns what is broken instead of throwing. Empty means consistent.
    /// <para>
    /// This is the stage before the save: an application asks whether the state is acceptable and
    /// decides what to do about it. <see cref="EnsureInvariants"/> is the stage at the save, where
    /// being broken is no longer an answer. Put these violations in whatever result type your
    /// application already uses.
    /// </para>
    /// </summary>
    IReadOnlyList<InvariantViolation> GetInvariantViolations();

    /// <summary>
    /// Runs the invariant checks of this object alone, without asking the children it holds, and
    /// throws when one of them is broken.
    /// <para>
    /// For a caller that already enumerates the graph and asks every object in it, which is what
    /// <c>DDDToolkit.EntityFramework</c>'s interceptor does from the change tracker. Such a caller
    /// would be told about a child twice if it asked <see cref="EnsureInvariants"/>, once by the child
    /// and once by its parent. Anything that holds an aggregate and wants to know whether it is
    /// consistent wants <see cref="EnsureInvariants"/> instead.
    /// </para>
    /// </summary>
    void EnsureOwnInvariants();

    /// <summary>
    /// Runs the checks of this object alone and returns what is broken instead of throwing. Empty
    /// means this object is consistent and says nothing about the children it holds.
    /// <para>
    /// The non-throwing half of <see cref="EnsureOwnInvariants"/>, and it is wanted for the same one
    /// reason: the caller is walking the graph itself. Ask <see cref="GetInvariantViolations"/> to
    /// find out whether an aggregate is consistent.
    /// </para>
    /// </summary>
    IReadOnlyList<InvariantViolation> GetOwnInvariantViolations();
}
