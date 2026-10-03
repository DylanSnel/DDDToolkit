namespace DDDToolkit.Supporting.Tenancy.Access;

public abstract partial record TenancyRequirement
{
    /// <summary>
    /// A key is held by the caller for the whole tenant: at its root, now. Otherwise
    /// <c>tenancy.not-permitted</c>, naming the key, exactly as the package's own use cases refuse it; nobody
    /// is refused with its own reason first. System work in the tenant holds every key there.
    /// </summary>
    /// <remarks>
    /// For what changes or shows something of the tenant as a whole that no use case of the package stands in
    /// front of, such as the state an application adds to one of the package's aggregates. It costs one
    /// statement, on the context the check was registered over.
    /// </remarks>
    public sealed record ForTheWholeTenant : TenancyRequirement
    {
        /// <summary>The requirement that <paramref name="key"/> is held for the whole tenant.</summary>
        /// <param name="key">The key the request needs for the whole tenant: one of the catalogue's.</param>
        /// <exception cref="ArgumentException"><paramref name="key"/> is blank.</exception>
        public ForTheWholeTenant(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            Key = key;
        }

        /// <summary>The key the request needs for the whole tenant.</summary>
        public string Key { get; }
    }
}
