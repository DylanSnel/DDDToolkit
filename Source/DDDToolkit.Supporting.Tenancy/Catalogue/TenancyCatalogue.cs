using System.Text.RegularExpressions;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// Every permission key, role pack and unit kind the application has, checked once and then only read:
/// Tenancy's keys, the application's own, and what its modules contribute.
/// <para>
/// It lives in code, not in a table. <see cref="Build"/> checks it as a whole and reports every problem at
/// once, so a catalogue that does not hold together stops the application at start-up rather than half way
/// through provisioning a tenant.
/// </para>
/// <para>
/// A key is retired, never removed. A retired key, and a stored key the catalogue no longer knows because it
/// was removed from code against that rule, both grant nothing: they are not live. A role may keep them;
/// nothing adds them anew.
/// </para>
/// <para>
/// Some keys manage access: they give power over other people's access, and a role holding one is given and
/// taken away only by a seat that holds it there. Tenancy's own keys do, all but
/// <see cref="TenancyKeys.HistoryView"/>, which only reads; the application marks others
/// where they are declared (<see cref="Permission.ManagesAccess"/>) or in
/// <see cref="ApplicationCatalogue.AccessManagingKeys"/>. A key that is not live manages nothing.
/// </para>
/// </summary>
public sealed partial class TenancyCatalogue
{
    private readonly Dictionary<string, Permission> _byKey;
    private readonly HashSet<string> _unitKinds;
    private readonly HashSet<string> _accessManaging;

    private TenancyCatalogue(
        IReadOnlyList<Permission> permissions,
        IReadOnlyList<string> liveKeys,
        IReadOnlyList<string> accessManagingKeys,
        IReadOnlyList<RolePack> packs,
        IReadOnlyList<UnitKind> unitKinds)
    {
        Permissions = permissions;
        LiveKeys = liveKeys;
        AccessManagingKeys = accessManagingKeys;
        Packs = packs;
        UnitKinds = unitKinds;

        _byKey = permissions.ToDictionary(permission => permission.Key, StringComparer.Ordinal);
        _unitKinds = unitKinds.Select(kind => kind.Key).ToHashSet(StringComparer.Ordinal);
        _accessManaging = accessManagingKeys.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Every key, retired ones included: Tenancy's, the application's and the contributions', by module, order
    /// and key. <see cref="Permission.ManagesAccess"/> is set on every key that is marked, where it is declared
    /// or by the application.
    /// </summary>
    public IReadOnlyList<Permission> Permissions { get; }

    /// <summary>Every key that is declared and not retired, in ordinal order.</summary>
    public IReadOnlyList<string> LiveKeys { get; }

    /// <summary>
    /// Every live key that manages access, in ordinal order: marked where it is declared, or by the
    /// application in <see cref="ApplicationCatalogue.AccessManagingKeys"/>.
    /// </summary>
    public IReadOnlyList<string> AccessManagingKeys { get; }

    /// <summary>
    /// The role packs as built, their keys expanded. An administrators' pack that lists no keys holds
    /// <see cref="LiveKeys"/>; one that lists keys holds those, as any other pack does.
    /// </summary>
    public IReadOnlyList<RolePack> Packs { get; }

    /// <summary>The kinds of unit.</summary>
    public IReadOnlyList<UnitKind> UnitKinds { get; }

    /// <summary>
    /// Builds the catalogue from the application's part and the modules' contributions, and checks it as a
    /// whole.
    /// </summary>
    /// <param name="application">The application's packs, unit kinds and keys.</param>
    /// <param name="contributed">The keys the modules contribute.</param>
    /// <exception cref="TenancyCatalogueException">Something does not hold together; every problem found is listed.</exception>
    public static TenancyCatalogue Build(ApplicationCatalogue application, IEnumerable<Permission> contributed)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(contributed);

        var problems = new List<string>();
        var declared = CheckPermissions(application.Permissions ?? [], [.. contributed], problems);
        var byKey = declared.ToDictionary(permission => permission.Key, StringComparer.Ordinal);
        var marked = CheckMarks(declared, application.AccessManagingKeys ?? [], byKey, problems);

        // Which keys are live, and which of those manage access, is settled before the packs are looked at: an
        // administrators' pack is built from the first and checked against the second.
        var live = declared.Where(permission => !permission.Retired).Select(permission => permission.Key).Order(StringComparer.Ordinal).ToArray();
        var managing = live.Where(marked.Contains).ToArray();

        CheckImplications(declared, byKey, marked, problems);
        var packs = CheckPacks(application.Packs ?? [], byKey, live, managing, problems);
        CheckUnitKinds(application.UnitKinds ?? [], problems);

        if (problems.Count > 0)
        {
            throw new TenancyCatalogueException(problems);
        }

        // A mark the application lists shows on the key itself, as a pack shows the keys it was built with.
        var permissions = byKey.Values
            .Select(permission => marked.Contains(permission.Key) && !permission.ManagesAccess ? permission with { ManagesAccess = true } : permission)
            .OrderBy(permission => permission.Module, StringComparer.Ordinal)
            .ThenBy(permission => permission.Order)
            .ThenBy(permission => permission.Key, StringComparer.Ordinal)
            .ToArray();

        return new TenancyCatalogue(permissions, live, managing, [.. packs], [.. application.UnitKinds!]);
    }

    /// <summary>Whether the catalogue declares <paramref name="key"/>, retired or not.</summary>
    public bool Knows(string key) => key is not null && _byKey.ContainsKey(key);

    /// <summary>Whether <paramref name="key"/> is declared and not retired. A key the catalogue does not know is not live.</summary>
    public bool IsLive(string key) => key is not null && _byKey.TryGetValue(key, out var permission) && !permission.Retired;

    /// <summary>
    /// Whether <paramref name="key"/> manages access: it is declared, not retired, and marked, where it is
    /// declared or by the application. A key the catalogue does not know manages nothing.
    /// </summary>
    public bool ManagesAccess(string key) => key is not null && _accessManaging.Contains(key);

    /// <summary>
    /// The keys of a role that manage access, each once, in ordinal order: none for an archived role, which
    /// grants nothing, and none that are not live. A role manages access when this is not empty, and is then
    /// given and taken away only by a seat that holds these keys there.
    /// </summary>
    /// <param name="role">What is known about the role.</param>
    public IReadOnlyList<string> AccessManagingKeysOf(RoleFacts role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return role.IsActive
            ? [.. role.Keys.Where(ManagesAccess).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>
    /// The keys a role made of <paramref name="keys"/> grants: trimmed, with the live keys each one implies
    /// added, distinct, in ordinal order.
    /// </summary>
    /// <param name="keys">The keys asked for.</param>
    /// <exception cref="RefusalException">
    /// <c>tenancy.unknown-permission</c>, with the offending keys in <c>Keys</c>: a key the catalogue does not
    /// know, or has retired.
    /// </exception>
    public IReadOnlyList<string> Expand(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var expanded = Expand(keys, _byKey, out var offenders);
        return offenders.Count > 0
            ? throw TenancyRefusals.Of(TenancyRefusals.UnknownPermission, ("Keys", string.Join(", ", offenders)))
            : expanded;
    }

    /// <summary>The packs a newly provisioned tenant of <paramref name="shape"/> gets a copy of, in their order.</summary>
    public IEnumerable<RolePack> PacksFor(TenantShape shape)
        => Packs.Where(pack => pack.SeedOnProvision && (pack.Shape is null || pack.Shape == shape)).OrderBy(pack => pack.Order);

    /// <summary>The pack the first administrator of a tenant of <paramref name="shape"/> is granted.</summary>
    public RolePack AdministratorPackFor(TenantShape shape)
        => PacksFor(shape).Single(pack => pack.Administers);

    /// <summary>Whether the application has a unit kind with this key.</summary>
    public bool KnowsUnitKind(string kind) => kind is not null && _unitKinds.Contains(kind.Trim());

    /// <summary>
    /// Checks a key that code is about to ask about. A key the catalogue does not know is a typo in code, not
    /// something a caller did, so it is an <see cref="ArgumentException"/> rather than a refusal. A retired key
    /// may be asked about, and holds nowhere.
    /// </summary>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="key"/>.</exception>
    public void RequireAskable(string key)
    {
        if (!Knows(key))
        {
            throw new ArgumentException("'" + key + "' is not a key of the permission catalogue.", nameof(key));
        }
    }

    // ---------------------------------------------------------------- the checks Build makes

    /// <summary>What every one of Tenancy's keys starts with, and no other key does.</summary>
    private const string TenancyPrefix = "tenancy.";

    [GeneratedRegex("^[a-z][a-z0-9-]*(\\.[a-z0-9-]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    /// <summary>
    /// Tenancy's keys first, then the application's and the contributions', each checked on its own and against
    /// the others. A key longer than <see cref="Permission.MaxKeyLength"/> is refused here: a stored right keeps
    /// it in a column of that length, and a database that enforces it would refuse the first grant instead.
    /// </summary>
    private static List<Permission> CheckPermissions(IReadOnlyList<Permission> application, IReadOnlyList<Permission> contributed, List<string> problems)
    {
        var declared = new List<Permission>(TenancyKeys.Permissions);
        var seen = declared.Select(permission => permission.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var (permission, source) in application.Select(permission => (permission, "The application"))
                     .Concat(contributed.Select(permission => (permission, "A contribution"))))
        {
            if (permission is null)
            {
                problems.Add(source + " declares a null permission.");
                continue;
            }

            if (permission.Key is null || !KeyPattern().IsMatch(permission.Key))
            {
                problems.Add(source + " declares the key '" + permission.Key + "', which is not lowercase words and dashes joined by dots, such as 'projects.edit'.");
                continue;
            }

            if (permission.Key.Length > Permission.MaxKeyLength)
            {
                problems.Add(source + " declares the key '" + permission.Key + "', which is longer than " + Permission.MaxKeyLength + " characters.");
                continue;
            }

            if (permission.Key.StartsWith(TenancyPrefix, StringComparison.Ordinal))
            {
                problems.Add(source + " declares '" + permission.Key + "': keys under 'tenancy.' belong to the Tenancy package.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(permission.Module))
            {
                problems.Add("'" + permission.Key + "' names no module.");
            }

            if (!seen.Add(permission.Key))
            {
                problems.Add("'" + permission.Key + "' is declared more than once.");
                continue;
            }

            declared.Add(permission);
        }

        return declared;
    }

    /// <summary>
    /// The keys that manage access: those marked where they are declared, and those the application lists. A
    /// listed key must be known, and listed once. One that is marked already, Tenancy's included, or retired,
    /// is accepted, so a module that starts marking its own key breaks no application that listed it.
    /// </summary>
    private static HashSet<string> CheckMarks(
        List<Permission> declared,
        IReadOnlyList<string> listed,
        Dictionary<string, Permission> byKey,
        List<string> problems)
    {
        var marked = declared.Where(permission => permission.ManagesAccess).Select(permission => permission.Key).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in listed)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                problems.Add("The application marks a blank key as managing access.");
            }
            else if (!byKey.ContainsKey(key))
            {
                problems.Add("The application marks '" + key + "' as managing access, which is unknown to the catalogue.");
            }
            else if (!seen.Add(key))
            {
                problems.Add("The application marks '" + key + "' as managing access more than once.");
            }
            else
            {
                marked.Add(key);
            }
        }

        return marked;
    }

    /// <summary>
    /// What a key implies is a known key other than itself, and a key that is implied implies nothing itself.
    /// A key of the application or a module implies none of Tenancy's: a role holding it would otherwise
    /// manage the tenant, and hold the administrator's key, without anyone having put that key in it. And a
    /// live key that manages no access implies no live key that does, so what a role is labelled with is what
    /// it holds. Retired keys are left out of that: nothing follows a retired key's implications.
    /// </summary>
    private static void CheckImplications(List<Permission> declared, Dictionary<string, Permission> byKey, HashSet<string> marked, List<string> problems)
    {
        var implied = declared
            .SelectMany(permission => (permission.Implies ?? []).Where(target => !string.Equals(target, permission.Key, StringComparison.Ordinal)))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var permission in declared)
        {
            foreach (var target in permission.Implies ?? [])
            {
                if (string.Equals(target, permission.Key, StringComparison.Ordinal))
                {
                    problems.Add("'" + permission.Key + "' implies itself.");
                }
                else if (target is null || !byKey.ContainsKey(target))
                {
                    problems.Add("'" + permission.Key + "' implies '" + target + "', which is unknown to the catalogue.");
                }
                else if (!permission.Key.StartsWith(TenancyPrefix, StringComparison.Ordinal) && target.StartsWith(TenancyPrefix, StringComparison.Ordinal))
                {
                    problems.Add("'" + permission.Key + "' implies '" + target + "': only Tenancy's own keys imply keys under 'tenancy.'.");
                }
                else if (!permission.Retired && !marked.Contains(permission.Key) && !byKey[target].Retired && marked.Contains(target))
                {
                    problems.Add("'" + permission.Key + "' implies '" + target + "', which manages access: a key that manages no access implies none that does.");
                }
            }

            if (permission.Implies is { Count: > 0 } && implied.Contains(permission.Key))
            {
                problems.Add("'" + permission.Key + "' is implied by another key and implies keys itself; implications are one hop.");
            }
        }
    }

    /// <summary>
    /// Pack keys are unique, not blank and at most <see cref="RolePack.MaxKeyLength"/> long; the names and descriptions of the roles made from them follow a
    /// role's rules, and the names are unique ignoring case, as a tenant's role names are; every key a pack
    /// lists is known and not retired; an administrators' pack that lists keys leaves out none an administrator
    /// needs (<see cref="CheckAdministratorsKeys"/>); and each shape has exactly one administrators' pack that is
    /// seeded. Returns the packs as built: their keys expanded, and for an administrators' pack that lists none,
    /// every live key.
    /// </summary>
    private static List<RolePack> CheckPacks(
        IReadOnlyList<RolePack> packs,
        Dictionary<string, Permission> byKey,
        string[] live,
        string[] managing,
        List<string> problems)
    {
        var result = new List<RolePack>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pack in packs)
        {
            if (pack is null)
            {
                problems.Add("The application declares a null pack.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(pack.Key))
            {
                problems.Add("A pack has no key.");
                continue;
            }

            if (pack.Key.Length > RolePack.MaxKeyLength)
            {
                problems.Add("The pack '" + pack.Key + "' has a key longer than " + RolePack.MaxKeyLength + " characters.");
                continue;
            }

            if (!seen.Add(pack.Key))
            {
                problems.Add("The pack '" + pack.Key + "' is declared more than once.");
                continue;
            }

            var name = pack.Name?.Trim() ?? string.Empty;
            if (name.Length == 0 || name.Length > TenancyNames.MaxRoleNameLength)
            {
                problems.Add("The pack '" + pack.Key + "' has a name of " + name.Length + " characters; a role's name is 1 to "
                             + TenancyNames.MaxRoleNameLength + ".");
            }
            else if (!names.TryAdd(name, pack.Key))
            {
                problems.Add("The packs '" + names[name] + "' and '" + pack.Key + "' are both named '" + name
                             + "', ignoring case; a tenant's roles have names of their own.");
            }

            if ((pack.Description?.Trim().Length ?? 0) > TenancyNames.MaxRoleDescriptionLength)
            {
                problems.Add("The pack '" + pack.Key + "' has a description longer than " + TenancyNames.MaxRoleDescriptionLength + " characters.");
            }

            if (pack.Administers && pack.Keys is not { Count: > 0 })
            {
                // It lists nothing, and so holds every live key: one declared later comes with it.
                result.Add(pack with { Keys = live });
                continue;
            }

            var expanded = Expand(pack.Keys ?? [], byKey, out var offenders);
            foreach (var offender in offenders)
            {
                problems.Add("The pack '" + pack.Key + "' lists '" + offender + "', which is "
                             + (byKey.ContainsKey(offender) ? "retired." : "unknown to the catalogue."));
            }

            if (pack.Administers)
            {
                CheckAdministratorsKeys(pack.Key, expanded, live, managing, problems);
            }

            result.Add(pack with { Keys = expanded });
        }

        foreach (var shape in Enum.GetValues<TenantShape>())
        {
            var administrators = result
                .Where(pack => pack.Administers && pack.SeedOnProvision && (pack.Shape is null || pack.Shape == shape))
                .Select(pack => pack.Key)
                .ToArray();

            if (administrators.Length != 1)
            {
                problems.Add("A " + shape.ToString().ToLowerInvariant() + " tenant needs exactly one administrators' pack that is seeded, and has "
                             + (administrators.Length == 0 ? "none." : administrators.Length + ": " + string.Join(", ", administrators) + "."));
            }
        }

        return result;
    }

    /// <summary>
    /// An administrators' pack that lists its keys holds, listed or implied by a key it lists, every live key of
    /// Tenancy's own and every live key that manages access. An administrator runs the tenant, which takes
    /// Tenancy's keys, <see cref="TenancyKeys.AdministratorKey"/> among them, and gives every role, which takes
    /// each key that manages access: a role holding one is given only by a seat that holds it. A pack short of
    /// one would make tenants whose administrators cannot give one of their own roles, so each key it leaves
    /// out is a problem of its own. A retired key is not asked for: it manages nothing, and no pack may list it.
    /// </summary>
    /// <param name="pack">The pack's key, for the problems.</param>
    /// <param name="held">The keys the pack lists, expanded.</param>
    /// <param name="live">Every live key, in ordinal order.</param>
    /// <param name="managing">Every live key that manages access.</param>
    /// <param name="problems">Where a key the pack leaves out is reported.</param>
    private static void CheckAdministratorsKeys(string pack, string[] held, string[] live, string[] managing, List<string> problems)
    {
        var holds = held.ToHashSet(StringComparer.Ordinal);
        var manages = managing.ToHashSet(StringComparer.Ordinal);

        foreach (var key in live)
        {
            // Tenancy's keys are asked for as Tenancy's own, whether or not they are marked as well.
            var own = key.StartsWith(TenancyPrefix, StringComparison.Ordinal);
            if ((own || manages.Contains(key)) && !holds.Contains(key))
            {
                problems.Add("The administrators' pack '" + pack + "' lists keys but not '" + key + "', which "
                             + (own ? "is one of Tenancy's own" : "manages access")
                             + ": an administrator holds every key that manages access, and Tenancy's own.");
            }
        }
    }

    /// <summary>There is at least one unit kind, and their keys are unique, not blank and at most 64 characters.</summary>
    private static void CheckUnitKinds(IReadOnlyList<UnitKind> unitKinds, List<string> problems)
    {
        if (unitKinds.Count == 0)
        {
            problems.Add("The application declares no unit kind; the root needs one.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in unitKinds)
        {
            if (kind is null || string.IsNullOrWhiteSpace(kind.Key) || kind.Key.Length > TenancyNames.MaxUnitKindLength)
            {
                problems.Add("The unit kind '" + kind?.Key + "' is blank or longer than " + TenancyNames.MaxUnitKindLength + " characters.");
            }
            else if (!seen.Add(kind.Key))
            {
                problems.Add("The unit kind '" + kind.Key + "' is declared more than once.");
            }
        }
    }

    /// <summary>
    /// Trims the keys, adds the live keys each one implies, and returns them distinct and in ordinal order;
    /// <paramref name="offenders"/> are the keys that are unknown or retired.
    /// </summary>
    private static string[] Expand(IEnumerable<string> keys, Dictionary<string, Permission> byKey, out List<string> offenders)
    {
        offenders = [];
        var expanded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            var trimmed = key?.Trim() ?? string.Empty;
            if (!byKey.TryGetValue(trimmed, out var permission) || permission.Retired)
            {
                if (!offenders.Contains(trimmed, StringComparer.Ordinal))
                {
                    offenders.Add(trimmed);
                }

                continue;
            }

            expanded.Add(trimmed);
            foreach (var implied in permission.Implies ?? [])
            {
                if (byKey.TryGetValue(implied, out var impliedPermission) && !impliedPermission.Retired)
                {
                    expanded.Add(implied);
                }
            }
        }

        return [.. expanded.Order(StringComparer.Ordinal)];
    }
}
