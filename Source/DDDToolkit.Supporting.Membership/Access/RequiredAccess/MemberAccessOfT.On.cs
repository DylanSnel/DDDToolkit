namespace DDDToolkit.Supporting.Membership.Access;

public abstract partial record MemberAccess<TResourceId>
{
    /// <summary>
    /// The permission is held by the caller on <see cref="Resource"/>. A resource the caller may not see is
    /// <c>not-found</c>, exactly as one that does not exist; one it may see without holding the permission is
    /// <c>not-permitted</c>, naming the permission. Both under the resource's own codes.
    /// </summary>
    public sealed record On : MemberAccess<TResourceId>
    {
        /// <summary>
        /// The requirement that <paramref name="permission"/> is held on <paramref name="resource"/>. Made by
        /// <see cref="MemberAccess.On{TResourceId}"/>, the one spelling a request writes.
        /// </summary>
        /// <param name="permission">The permission the request needs on the resource.</param>
        /// <param name="resource">The resource, from the request.</param>
        /// <param name="expectedVersion">The version of the resource the caller last read, or <see langword="null"/>.</param>
        /// <exception cref="ArgumentException"><paramref name="permission"/> is blank.</exception>
        internal On(string permission, TResourceId resource, long? expectedVersion = null)
            : base(permission)
        {
            Resource = resource;
            ExpectedVersion = expectedVersion;
        }

        /// <summary>The resource, from the request.</summary>
        public TResourceId Resource { get; }

        /// <summary>
        /// The version of the resource the caller last read, for a command that says so, or
        /// <see langword="null"/>. It is compared only once the caller is known to see the resource and to hold
        /// the permission, and any other version is a lost race
        /// (<see cref="Exceptions.ConcurrencyConflictException"/>). In that order, so a caller without access
        /// learns nothing from a version: whatever it sends, it is answered as if it had sent none.
        /// </summary>
        public long? ExpectedVersion { get; }
    }
}
