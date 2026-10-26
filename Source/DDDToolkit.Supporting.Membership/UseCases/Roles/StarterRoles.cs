using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Membership.Access;

namespace DDDToolkit.Supporting.Membership.UseCases;

/// <summary>
/// The first roles of a resource whose roles a customer makes (<see cref="Roles.Custom"/>): the starter roles its
/// rules declare, each made once into a row a customer can rename, give other keys and archive.
/// <para>
/// Roles are kept in scopes the package does not know: one set for each customer of an application, or one
/// for the whole of it. So the application says which scope it means by handing over the roles that scope
/// has, and makes the ones that are missing with its own constructor, which sets what it added:
/// </para>
/// <code>
/// var existing = await db.PlotRoles.Where(role =&gt; role.GardenId == garden).ToListAsync(cancellationToken);
/// foreach (var draft in StarterRoles.Missing(PlotMembership.Rules, existing))
/// {
///     db.PlotRoles.Add(new PlotRole(PlotRoleId.Create(), garden, draft));
/// }
/// </code>
/// <para>
/// Asked again for the same scope, it answers nothing: a starter role is made once, and is told by what it
/// was made from (<see cref="CustomRoleAggregate{TRoleId}.MadeFrom"/>), not by its name, so a role a
/// customer renamed or archived is not made a second time. Two requests that ask at the same moment are kept
/// apart by a unique index the application declares over its scope and that property.
/// </para>
/// </summary>
public static class StarterRoles
{
    /// <summary>
    /// The starter roles a scope does not have yet, each as the draft its role is made from: its name as the
    /// rules declare it, the keys the rules let it give, and the starter role it is, in the order the rules
    /// declare them.
    /// </summary>
    /// <param name="rules">The rules of the resource the roles are of.</param>
    /// <param name="rolesOfTheScope">Every role the scope has, archived ones included.</param>
    /// <typeparam name="TRoleId">The application's id of a role.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> or <paramref name="rolesOfTheScope"/> is null.</exception>
    /// <exception cref="ArgumentException">The rules do not say a customer makes the resource's roles.</exception>
    public static IReadOnlyList<CustomRoleDraft> Missing<TRoleId>(MembershipRules rules, IEnumerable<CustomRoleAggregate<TRoleId>> rolesOfTheScope)
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(rolesOfTheScope);
        if (rules.Roles.Kind != RolesKind.Custom)
        {
            throw new ArgumentException(
                "The rules '" + rules.Name + "' do not say a customer makes the roles a member holds, so there are no rows to make from them. "
                + "Starter roles are for rules whose roles a customer makes: Roles.Custom(starters: [...]).",
                nameof(rules));
        }

        var made = rolesOfTheScope
            .Select(role => role.MadeFrom)
            .Where(starter => starter is not null)
            .ToHashSet(StringComparer.Ordinal);

        return [.. rules.Roles.Declared
            .Where(starter => !made.Contains(starter.Name))
            .Select(starter => new CustomRoleDraft(starter.Name, null, rules.KeysOf(new NamedRole(starter.Name)), starter.Name))];
    }
}
