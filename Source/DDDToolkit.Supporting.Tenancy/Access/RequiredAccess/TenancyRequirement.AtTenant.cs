namespace DDDToolkit.Supporting.Tenancy.Access;

public abstract partial record TenancyRequirement
{
    /// <summary>
    /// A permission is held by the caller for the whole tenant: at its root, now. Otherwise
    /// <c>tenancy.not-permitted</c>, naming the permission, exactly as the package's own managers refuse it; nobody
    /// is refused with its own reason first. System work in the tenant holds every permission there. A request declares
    /// it with <see cref="TenancyAccess.AtTenant"/>.
    /// </summary>
    /// <remarks>
    /// For what changes or shows something of the tenant as a whole: a manager of the package that asks for the
    /// permission first, such as making a role, or the state an application adds to one of the package's aggregates. It
    /// costs one statement, on the context the check was registered over.
    /// </remarks>
    public sealed record AtTenant : TenancyRequirement
    {
        /// <summary>
        /// The requirement that <paramref name="permission"/> is held for the whole tenant. Made by
        /// <see cref="TenancyAccess.AtTenant"/>, the one spelling a request writes.
        /// </summary>
        /// <param name="permission">The permission the request needs for the whole tenant: one of the catalogue's.</param>
        /// <exception cref="ArgumentException"><paramref name="permission"/> is blank.</exception>
        internal AtTenant(string permission)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(permission);
            Permission = permission;
        }

        /// <summary>The permission the request needs for the whole tenant.</summary>
        public string Permission { get; }
    }
}
