namespace DDDToolkit.Supporting.Membership.Access;

public abstract partial record MemberAccess<TResourceId>
{
    /// <summary>
    /// The caller is shown only what it holds the key on. Nothing is refused for want of the key: the query's
    /// own statement leaves out what the caller does not reach
    /// (<see cref="IMemberQuestions{TResourceId}.Reach"/>), so a signed-in caller who reaches nothing gets an
    /// empty answer. A caller who did not sign in is refused, <c>not-permitted</c> naming the key: it reaches
    /// nothing under any rules, and no statement is sent for it. For queries only; a command that declared it
    /// would change things unchecked.
    /// </summary>
    public sealed record SeenWith : MemberAccess<TResourceId>
    {
        /// <summary>
        /// The requirement that what is shown is what <paramref name="key"/> is held on. Made by
        /// <see cref="MemberAccess.SeenWith{TResourceId}"/>, the one spelling a request writes.
        /// </summary>
        /// <param name="key">The key that decides what the query shows.</param>
        /// <exception cref="ArgumentException"><paramref name="key"/> is blank.</exception>
        internal SeenWith(string key)
            : base(key)
        {
        }
    }
}
