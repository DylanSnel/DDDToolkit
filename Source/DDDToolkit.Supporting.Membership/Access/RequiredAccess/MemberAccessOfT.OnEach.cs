using System.Text;

namespace DDDToolkit.Supporting.Membership.Access;

public abstract partial record MemberAccess<TResourceId>
{
    /// <summary>
    /// The permission is held by the caller on each of <see cref="Resources"/>. The check reads all of them in one
    /// statement for every 200 resources. A resource the caller may not see is <c>not-found</c>, exactly as one that does
    /// not exist. A resource it may see without holding the permission is <c>not-permitted</c>, naming the permission. The
    /// request is refused when one resource is refused, and the refusal names no resource. Every <c>not-found</c> comes
    /// before any <c>not-permitted</c>. An empty set of resources requires a caller the questions can be asked about, and
    /// nothing more.
    /// </summary>
    public sealed record OnEach : MemberAccess<TResourceId>
    {
        /// <summary>
        /// The requirement that <paramref name="permission"/> is held on each of <paramref name="resources"/>. Made by
        /// <see cref="MemberAccess.OnEach{TResourceId}"/>, the one spelling a request writes.
        /// </summary>
        /// <param name="permission">The permission the request needs on each resource.</param>
        /// <param name="resources">The resources, from the request.</param>
        /// <exception cref="ArgumentException"><paramref name="permission"/> is blank.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="resources"/> is null.</exception>
        internal OnEach(string permission, IEnumerable<TResourceId> resources)
            : base(permission)
        {
            ArgumentNullException.ThrowIfNull(resources);
            Resources = [.. resources.Distinct()];
        }

        /// <summary>The resources, from the request. Each resource is in the list once, at the place the request first names it.</summary>
        public IReadOnlyList<TResourceId> Resources { get; }

        /// <summary>Whether <paramref name="other"/> requires the same permission on the same resources, in the same order.</summary>
        /// <param name="other">Another requirement.</param>
        public bool Equals(OnEach? other)
            => other is not null && base.Equals(other) && Resources.SequenceEqual(other.Resources);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.GetHashCode());
            foreach (var resource in Resources)
            {
                hash.Add(resource);
            }

            return hash.ToHashCode();
        }

        /// <summary>Writes the permission and every resource, so a test that prints the requirement sees the ids.</summary>
        /// <param name="builder">What the requirement is written to.</param>
        protected override bool PrintMembers(StringBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.Append("Permission = ").Append(Permission).Append(", Resources = [").Append(string.Join(", ", Resources)).Append(']');
            return true;
        }
    }
}
