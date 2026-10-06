using System.Text.RegularExpressions;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// Every permission key and role pack the application has, checked once and then only read: Tenancy's keys and
/// the default administrators' pack, the application's own keys and packs, and what its modules contribute.
/// <para>
/// It lives in code, not in a table. <see cref="Build(ApplicationCatalogue, IEnumerable{Permission})"/> checks it
/// as a whole and reports every problem at once, so a catalogue that does not hold together stops the application
/// at start-up rather than half way through provisioning a tenant.
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
    private readonly HashSet<string> _accessManaging;

    private TenancyCatalogue(
        IReadOnlyList<Permission> permissions,
        IReadOnlyList<string> liveKeys,
        IReadOnlyList<string> accessManagingKeys,
        IReadOnlyList<RolePack> packs,
        bool hasDefaultAdministrators)
    {
        Permissions = permissions;
        LiveKeys = liveKeys;
        AccessManagingKeys = accessManagingKeys;
        Packs = packs;
        HasDefaultAdministrators = hasDefaultAdministrators;

        _byKey = permissions.ToDictionary(permission => permission.Key, StringComparer.Ordinal);
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
    /// <see cref="LiveKeys"/>; one that lists keys holds those, as any other pack does. When the application
    /// declares no administrators' pack, <see cref="TenancyPacks.DefaultAdministrators"/> comes first, holding
    /// <see cref="LiveKeys"/>.
    /// </summary>
    public IReadOnlyList<RolePack> Packs { get; }

    /// <summary>
    /// Whether the application declared no administrators' pack, so that <see cref="Packs"/> holds
    /// <see cref="TenancyPacks.DefaultAdministrators"/>, added by
    /// <see cref="Build(ApplicationCatalogue, IEnumerable{Permission})"/>. The pack of that key is then the
    /// package's, named in the languages the package ships; otherwise every pack is the application's.
    /// </summary>
    public bool HasDefaultAdministrators { get; }

    /// <summary>
    /// Builds the catalogue of an application that adds nothing to it, from the modules' contributions alone, as
    /// the registration does when <c>TenancyOptions.Catalogue</c> is not set: Tenancy's keys, the modules' keys,
    /// and <see cref="TenancyPacks.DefaultAdministrators"/> as the one pack. What an export that runs without the
    /// registration builds the same catalogue with: <c>TenancyCatalogue.Build(TenancyPermissionsOfModules.All)</c>, the
    /// list Tenancy's generator writes into a project that composes the modules, from the lists they mark with
    /// <see cref="TenancyPermissionsAttribute"/>.
    /// </summary>
    /// <param name="contributed">The keys the modules contribute.</param>
    /// <exception cref="TenancyCatalogueException">Something does not hold together; every problem found is listed.</exception>
    public static TenancyCatalogue Build(IEnumerable<Permission> contributed) => Build(new ApplicationCatalogue(), contributed);

    /// <summary>
    /// Builds the catalogue from the application's part and the modules' contributions, and checks it as a
    /// whole. An application that declares no administrators' pack gets
    /// <see cref="TenancyPacks.DefaultAdministrators"/>, for every shape.
    /// </summary>
    /// <param name="application">
    /// The application's packs, keys and marks. An application that adds none builds with
    /// <see cref="Build(IEnumerable{Permission})"/>, which passes <c>new ApplicationCatalogue()</c>.
    /// </param>
    /// <param name="contributed">
    /// The keys the modules contribute: in an export, <c>TenancyPermissionsOfModules.All</c>, which Tenancy's generator
    /// writes into a project that composes the modules, as the host registers them.
    /// </param>
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
        // Whether the application declares an administrators' pack is read off what it declares, a pack refused
        // for another reason included: once it declares one, it declares them for every shape.
        var declaresAdministrators = (application.Packs ?? []).Any(pack => pack is { Administers: true });
        var packs = CheckPacks(application.Packs ?? [], byKey, live, managing, declaresAdministrators, problems);

        if (problems.Count > 0)
        {
            // A key the catalogue does not know is most often one that never reached it, which the problems cannot
            // say: so the cure is said once, after them.
            throw new TenancyCatalogueException(
                problems,
                problems.Any(problem => problem.EndsWith(UnknownToTheCatalogue, StringComparison.Ordinal)) ? UnknownKeyAdvice : null);
        }

        // A mark the application lists shows on the key itself, as a pack shows the keys it was built with.
        var permissions = byKey.Values
            .Select(permission => marked.Contains(permission.Key) && !permission.ManagesAccess ? permission with { ManagesAccess = true } : permission)
            .OrderBy(permission => permission.Module, StringComparer.Ordinal)
            .ThenBy(permission => permission.Order)
            .ThenBy(permission => permission.Key, StringComparer.Ordinal)
            .ToArray();

        return new TenancyCatalogue(permissions, live, managing, [.. packs], !declaresAdministrators);
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

    /// <summary>
    /// The pack the first administrator of a tenant of <paramref name="shape"/> is granted: the application's, or
    /// <see cref="TenancyPacks.DefaultAdministrators"/> when it declares none.
    /// </summary>
    public RolePack AdministratorPackFor(TenantShape shape)
        => PacksFor(shape).Single(pack => pack.Administers);

    /// <summary>
    /// Checks a key that code is about to ask about. A key the catalogue does not know is a mistake in code, not
    /// something a caller did, so it is an <see cref="ArgumentException"/> rather than a refusal: a typo, or a
    /// module's key that never reached the catalogue, which the message names the cure of. A retired key may be
    /// asked about, and holds nowhere.
    /// </summary>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="key"/>.</exception>
    public void RequireAskable(string key)
    {
        if (!Knows(key))
        {
            throw new ArgumentException(
                "'" + key + "' is not a key of the permission catalogue. A module's keys reach it from the static list the module marks with "
                + "[TenancyPermissions], when the host calls services.AddTenancyPermissionsOfModules().",
                nameof(key));
        }
    }

    // ---------------------------------------------------------------- the checks Build makes

    /// <summary>What every one of Tenancy's keys starts with, and no other key does.</summary>
    private const string TenancyPrefix = "tenancy.";

    /// <summary>How each problem about a key the catalogue does not know ends.</summary>
    private const string UnknownToTheCatalogue = "unknown to the catalogue.";

    /// <summary>
    /// Said once after problems about keys the catalogue does not know: the usual cause is a module's keys that never
    /// reached it, such as a host that does not add them, or an export built in a project that does not reference
    /// the module.
    /// </summary>
    private const string UnknownKeyAdvice =
        "A key unknown to the catalogue is most often a module's that never reached it. A module states its keys on the static list it marks with "
        + "[TenancyPermissions]; the host adds every module's list with services.AddTenancyPermissionsOfModules(), and an export builds with "
        + "TenancyPermissionsOfModules.All, in a project that references the module.";

    [GeneratedRegex("^[a-z][a-z0-9-]*(\\.[a-z0-9-]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    /// <summary>
    /// Tenancy's keys first, then the application's and the contributions', each checked on its own and against
    /// the others. A key longer than <see cref="Permission.MaxKeyLength"/> is refused here: a stored right keeps
    /// it in a column of that length, and a database that enforces it would refuse the first grant instead.
    /// <para>
    /// A key declared twice is refused. When it is one and the same declaration both times, the same list was
    /// added twice, and the problem says so once for all of its keys: a module that marks its list with
    /// <see cref="TenancyPermissionsAttribute"/> and still adds it with <c>AddTenancyPermissions</c>, beside the
    /// host's <c>AddTenancyPermissionsOfModules</c>, is how that happens.
    /// </para>
    /// </summary>
    private static List<Permission> CheckPermissions(IReadOnlyList<Permission> application, IReadOnlyList<Permission> contributed, List<string> problems)
    {
        var declared = new List<Permission>(TenancyKeys.Permissions);
        var seen = declared.ToDictionary(permission => permission.Key, StringComparer.Ordinal);
        var addedTwice = new List<string>();

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

            if (seen.TryGetValue(permission.Key, out var first))
            {
                if (ReferenceEquals(first, permission))
                {
                    addedTwice.Add(permission.Key);
                }
                else
                {
                    problems.Add("'" + permission.Key + "' is declared more than once.");
                }

                continue;
            }

            seen.Add(permission.Key, permission);
            declared.Add(permission);
        }

        if (addedTwice.Count > 0)
        {
            problems.Add("The same declaration of " + string.Join(", ", addedTwice.Distinct(StringComparer.Ordinal).Select(key => "'" + key + "'"))
                         + " is added more than once. A module whose list is marked [TenancyPermissions] has it added by the host's AddTenancyPermissionsOfModules(),"
                         + " and adds it with AddTenancyPermissions no more; and either is called once.");
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
                problems.Add("The application marks '" + key + "' as managing access, which is " + UnknownToTheCatalogue);
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
                    problems.Add("'" + permission.Key + "' implies '" + target + "', which is " + UnknownToTheCatalogue);
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
    /// <para>
    /// An application that declares no administrators' pack at all gets <see cref="TenancyPacks.DefaultAdministrators"/>,
    /// which gives every shape its one (<see cref="AddDefaultAdministrators"/>). One that declares an
    /// administrators' pack declares them all: a shape it leaves without one is a mistake to report, not a gap
    /// to fill, since the application chose what its administrators hold and the default might hold more.
    /// </para>
    /// </summary>
    private static List<RolePack> CheckPacks(
        IReadOnlyList<RolePack> packs,
        Dictionary<string, Permission> byKey,
        string[] live,
        string[] managing,
        bool declaresAdministrators,
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
                // It lists nothing, and so holds every live key: one declared later comes with it, and reaches the
                // roles made from the pack before at the next sync of the packs.
                result.Add(pack with { Keys = live });
                continue;
            }

            var expanded = Expand(pack.Keys ?? [], byKey, out var offenders);
            foreach (var offender in offenders)
            {
                problems.Add("The pack '" + pack.Key + "' lists '" + offender + "', which is "
                             + (byKey.ContainsKey(offender) ? "retired." : UnknownToTheCatalogue));
            }

            if (pack.Administers)
            {
                CheckAdministratorsKeys(pack.Key, expanded, live, managing, problems);
            }

            result.Add(pack with { Keys = expanded });
        }

        if (!declaresAdministrators)
        {
            AddDefaultAdministrators(result, seen, names, live, problems);
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
                             + (administrators.Length == 0
                                 ? "none. The default one, '" + TenancyPacks.DefaultAdministratorsKey + "', is added only when the application declares "
                                   + "no administrators' pack at all: declare a seeded one for this shape as well, or declare none."
                                 : administrators.Length + ": " + string.Join(", ", administrators) + "."));
            }
        }

        return result;
    }

    /// <summary>
    /// Puts <see cref="TenancyPacks.DefaultAdministrators"/> first among the packs, holding every live key. Its key
    /// is its own, and so are its names, ignoring case, as a tenant's role names are: the catalogue's, and the one
    /// the package gives it in each language it ships, Beheerder in Dutch. So a pack of the application's that has
    /// the key or any of the names is refused, with what to do about it. That pack is no administrators' pack, or
    /// the default would not be added: it is renamed, or declared as the administrators' pack. A name the
    /// application's own <see cref="IRolePackTexts"/> gives a pack is not known here, and is refused at
    /// provisioning, as a clash between two of the application's own packs is.
    /// </summary>
    /// <param name="result">The application's packs as built; the default goes first.</param>
    /// <param name="keys">The keys of the application's packs.</param>
    /// <param name="names">The names of the application's packs, ignoring case, each with the key of its pack.</param>
    /// <param name="live">Every live key, in ordinal order: what the default holds.</param>
    /// <param name="problems">Where a pack that has the default's key or name is reported.</param>
    private static void AddDefaultAdministrators(
        List<RolePack> result,
        HashSet<string> keys,
        Dictionary<string, string> names,
        string[] live,
        List<string> problems)
    {
        var fallback = TenancyPacks.DefaultAdministrators;
        const string Added = "the default administrators' pack, added because the application declares no administrators' pack";
        const string Fix = ", or declare it with Administers: true to make it the administrators' pack instead.";

        if (keys.Contains(fallback.Key))
        {
            problems.Add("The pack '" + fallback.Key + "' has the key of " + Added + ". Give the pack another key" + Fix);
        }

        // The catalogue's name, which a tenant without a language gets, and the package's own in every language it
        // ships: a pack named like any of them would be refused at provisioning, in a tenant of that language.
        IEnumerable<(string Name, string InLanguage)> defaultNames =
        [
            (fallback.Name, string.Empty),
            .. TenancyPackTexts.DefaultAdministratorsTranslations().Select(translation => (translation.Name, " in " + translation.Language.EnglishName)),
        ];
        foreach (var (defaultName, inLanguage) in defaultNames)
        {
            if (names.TryGetValue(defaultName, out var taking))
            {
                var name = result.First(pack => string.Equals(pack.Key, taking, StringComparison.Ordinal)).Name.Trim();
                problems.Add("The pack '" + taking + "' is named '" + name + "', and " + Added + ", is named '" + defaultName + "'" + inLanguage
                             + "; a tenant's roles have names of their own, ignoring case. Give the pack another name" + Fix);
            }
        }

        result.Insert(0, fallback with { Keys = live });
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
