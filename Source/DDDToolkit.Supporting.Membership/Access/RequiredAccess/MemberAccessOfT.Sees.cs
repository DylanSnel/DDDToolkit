namespace DDDToolkit.Supporting.Membership.Access;

public abstract partial record MemberAccess<TResourceId>
{
    /// <summary>
    /// The caller sees <see cref="Resource"/>, whether or not it holds <see cref="MemberAccess{TResourceId}.Permission"/> there:
    /// what a request about the caller's own on a resource requires of the resource, beside
    /// <see cref="DDDToolkit.Access.AccessRequirement.Own{TValue}"/>, where the permission on it is the alternative. A member who
    /// takes itself off a document sees the document, as every member does; it need not hold the permission that takes anybody
    /// off. A resource the caller may not see is <c>not-found</c>, exactly as one that does not exist, under the resource's
    /// own codes, and nothing else is refused.
    /// </summary>
    /// <remarks>
    /// It reads what <see cref="On"/> reads, in one statement, and keeps it the same way, a
    /// <see cref="MemberHold{TResourceId}"/> with the request in hand, so the caller's own way through holds a save of the
    /// resource to the version read as the permission's way does, under the expert hold too, and compares the version the
    /// request names. The permission is asked about so that what is kept says whether the caller holds it as well:
    /// <see cref="MemberHold{TResourceId}.Via"/> is <see langword="null"/> where it does not.
    /// <c>[RequiresPermission(permission)]</c> with <c>[OnResource]</c> on the resource and <c>[OrOwn]</c> on the member is
    /// <c>Any(All(Own(member), Sees(permission, resource)), On(permission, resource))</c>.
    /// </remarks>
    public sealed record Sees : MemberAccess<TResourceId>
    {
        /// <summary>
        /// The requirement that <paramref name="resource"/> is seen. Made by <see cref="MemberAccess.Sees{TResourceId}"/>,
        /// the one spelling a request writes.
        /// </summary>
        /// <param name="permission">The permission the request's other way through requires on the resource, asked about for what is kept.</param>
        /// <param name="resource">The resource, from the request.</param>
        /// <param name="expectedVersion">The version of the resource the caller last read, or <see langword="null"/>.</param>
        /// <exception cref="ArgumentException"><paramref name="permission"/> is blank.</exception>
        internal Sees(string permission, TResourceId resource, long? expectedVersion = null)
            : base(permission)
        {
            Resource = resource;
            ExpectedVersion = expectedVersion;
        }

        /// <summary>The resource, from the request.</summary>
        public TResourceId Resource { get; }

        /// <summary>
        /// The version of the resource the caller last read, for a command that says so, or <see langword="null"/>. It is
        /// compared once the caller is known to see the resource, and any other version is a lost race
        /// (<see cref="Exceptions.ConcurrencyConflictException"/>), as for <see cref="On.ExpectedVersion"/>.
        /// </summary>
        public long? ExpectedVersion { get; }
    }
}
