using System.Collections.Concurrent;
using System.ComponentModel;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// The permissions declared on each resource with members, as the package's generator hands them over: what
/// <c>MembershipRules.For&lt;TResource&gt;</c> makes the rules of a resource from. Called by generated code, which
/// declares, when its assembly is first used, the permissions marked <see cref="OnAttribute{TResource}"/> in the
/// <c>[Permissions]</c> classes of its own project, and, in a project that makes rules with
/// <c>MembershipRules.For&lt;TResource&gt;</c>, those of the projects it references.
/// </summary>
/// <remarks>
/// A test that makes rules for a resource of its own, without a <c>[Permissions]</c> class, declares them here
/// itself, once for each class it stands in for. One class declared twice must say the same each time: the projects
/// that declare it see one class.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class MembershipPermissions
{
    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, Declaration>> Declared = new();

    /// <summary>Declares the permissions one <c>[Permissions]</c> class says on <typeparamref name="TResource"/>.</summary>
    /// <param name="declaredIn">The class that declares them, by its full name.</param>
    /// <param name="permissions">The permissions on the resource, in the order they are declared.</param>
    /// <param name="elsewhere">
    /// The keys the same class declares at another level, held at a unit or across the whole organization say: keys of the
    /// module that are known not to be the resource's, which no role on it gives, whatever keys of other modules its
    /// roles take.
    /// </param>
    /// <typeparam name="TResource">The resource.</typeparam>
    /// <exception cref="ArgumentException">A permission is null, or <paramref name="declaredIn"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">The class was declared before with other permissions.</exception>
    public static void Declare<TResource>(string declaredIn, ResourcePermission[] permissions, string[]? elsewhere = null)
        where TResource : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredIn);
        ArgumentNullException.ThrowIfNull(permissions);
        if (permissions.Any(permission => permission is null))
        {
            throw new ArgumentException("A permission is not null.", nameof(permissions));
        }

        var declaration = new Declaration([.. permissions], [.. elsewhere ?? []]);
        var ofTheResource = Declared.GetOrAdd(typeof(TResource), static _ => new ConcurrentDictionary<string, Declaration>(StringComparer.Ordinal));
        var kept = ofTheResource.GetOrAdd(declaredIn, declaration);
        if (!kept.SaysTheSameAs(declaration))
        {
            throw new InvalidOperationException(
                declaredIn + " declares other permissions on " + typeof(TResource).Name + " than it did before. A class is declared once, from what it says; "
                + "two assemblies that declare it see two different builds of it.");
        }
    }

    /// <summary>
    /// The permissions declared on <typeparamref name="TResource"/>, of every class that declared some, those
    /// classes in the order of their names and each class's in the order it declares them; and the keys those
    /// classes declare at another level.
    /// </summary>
    internal static (IReadOnlyList<ResourcePermission> Permissions, IReadOnlyList<string> Elsewhere) Of<TResource>()
    {
        if (!Declared.TryGetValue(typeof(TResource), out var ofTheResource))
        {
            return ([], []);
        }

        var declarations = ofTheResource.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToList();
        return ([.. declarations.SelectMany(declaration => declaration.Permissions)], [.. declarations.SelectMany(declaration => declaration.Elsewhere).Distinct(StringComparer.Ordinal)]);
    }

    private sealed record Declaration(IReadOnlyList<ResourcePermission> Permissions, IReadOnlyList<string> Elsewhere)
    {
        public bool SaysTheSameAs(Declaration other)
            => Permissions.SequenceEqual(other.Permissions) && Elsewhere.SequenceEqual(other.Elsewhere, StringComparer.Ordinal);
    }
}
