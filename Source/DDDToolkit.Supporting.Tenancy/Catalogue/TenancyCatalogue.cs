using System.Text.RegularExpressions;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// Every permission and role pack the application has, checked once and then only read: Tenancy's permissions and
/// the default administrators' pack, the application's own permissions and packs, and what its modules contribute.
/// <para>
/// It lives in code, not in a table. <see cref="Build(ApplicationCatalogue, IEnumerable{Permission})"/> checks it
/// as a whole and reports every problem at once, so a catalogue that does not hold together stops the application
/// at start-up rather than half way through provisioning a tenant.
/// </para>
/// <para>
/// A permission is retired, never removed. A retired permission, and a stored permission the catalogue no longer knows because it
/// was removed from code against that rule, both grant nothing: they are not live. A role may keep them;
/// nothing adds them anew.
/// </para>
/// <para>
/// Some permissions manage access: they give power over other people's access, and a role holding one is given and
/// taken away only by a seat that holds it there, while the catalogue keeps them contained
/// (<see cref="ContainAccessManagingPermissions"/>). Tenancy's own permissions do, all but
/// <see cref="TenancyPermissions.HistoryView"/>, which only reads; the application marks others
/// where they are declared (<see cref="Permission.ManagesAccess"/>) or in
/// <see cref="ApplicationCatalogue.AccessManagingPermissions"/>. A permission that is not live manages nothing.
/// </para>
/// </summary>
public sealed partial class TenancyCatalogue
{
    private readonly Dictionary<string, Permission> _byName;
    private readonly HashSet<string> _accessManaging;

    private TenancyCatalogue(
        IReadOnlyList<Permission> permissions,
        IReadOnlyList<string> livePermissions,
        IReadOnlyList<string> accessManagingPermissions,
        IReadOnlyList<RolePack> packs,
        bool hasDefaultAdministrators,
        bool containAccessManagingPermissions)
    {
        Permissions = permissions;
        LivePermissions = livePermissions;
        AccessManagingPermissions = accessManagingPermissions;
        Packs = packs;
        HasDefaultAdministrators = hasDefaultAdministrators;
        ContainAccessManagingPermissions = containAccessManagingPermissions;

        _byName = permissions.ToDictionary(permission => permission.Name, StringComparer.Ordinal);
        _accessManaging = accessManagingPermissions.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Every permission, retired ones included: Tenancy's, the application's and the contributions', by module, order
    /// and permission. <see cref="Permission.ManagesAccess"/> is set on every permission that is marked, where it is declared
    /// or by the application.
    /// </summary>
    public IReadOnlyList<Permission> Permissions { get; }

    /// <summary>Every permission that is declared and not retired, in ordinal order.</summary>
    public IReadOnlyList<string> LivePermissions { get; }

    /// <summary>
    /// Every live permission that manages access, in ordinal order: marked where it is declared, or by the
    /// application in <see cref="ApplicationCatalogue.AccessManagingPermissions"/>.
    /// </summary>
    public IReadOnlyList<string> AccessManagingPermissions { get; }

    /// <summary>
    /// The role packs as built, their permissions expanded. An administrators' pack that lists no permissions holds
    /// <see cref="LivePermissions"/>; one that lists permissions holds those, as any other pack does. When the application
    /// declares no administrators' pack, <see cref="TenancyPacks.DefaultAdministrators"/> comes first, holding
    /// <see cref="LivePermissions"/>.
    /// </summary>
    public IReadOnlyList<RolePack> Packs { get; }

    /// <summary>
    /// Whether the application declared no administrators' pack, so that <see cref="Packs"/> holds
    /// <see cref="TenancyPacks.DefaultAdministrators"/>, added by
    /// <see cref="Build(ApplicationCatalogue, IEnumerable{Permission})"/>. The pack of that permission is then the
    /// package's, named in the languages the package ships; otherwise every pack is the application's.
    /// </summary>
    public bool HasDefaultAdministrators { get; }

    /// <summary>
    /// Whether a seat hands on the permissions of <see cref="AccessManagingPermissions"/> only where it holds them itself, as
    /// the application says in <see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/>: on unless it turns it
    /// off. The use cases ask it before every rule of containment, and the export writes it into the database's
    /// access files with the rest of the catalogue. It changes which permissions are marked in nothing:
    /// <see cref="AccessManagingPermissions"/> and <see cref="ManagesAccess"/> answer the same either way.
    /// </summary>
    public bool ContainAccessManagingPermissions { get; }

    /// <summary>
    /// Builds the catalogue of an application that adds nothing to it, from the modules' contributions alone, as
    /// the registration does when <c>TenancyOptions.Catalogue</c> is not set: Tenancy's permissions, the modules' permissions,
    /// and <see cref="TenancyPacks.DefaultAdministrators"/> as the one pack. What an export that runs without the
    /// registration builds the same catalogue with: <c>TenancyCatalogue.Build(TenancyPermissionsOfModules.All)</c>, the
    /// list Tenancy's generator writes into a project that composes the modules, from the lists they mark with
    /// <see cref="TenancyPermissionsAttribute"/>.
    /// </summary>
    /// <param name="contributed">The permissions the modules contribute.</param>
    /// <exception cref="TenancyCatalogueException">Something does not hold together; every problem found is listed.</exception>
    public static TenancyCatalogue Build(IEnumerable<Permission> contributed) => Build(new ApplicationCatalogue(), contributed);

    /// <summary>
    /// Builds the catalogue from the application's part and the modules' contributions, and checks it as a
    /// whole. An application that declares no administrators' pack gets
    /// <see cref="TenancyPacks.DefaultAdministrators"/>, for every shape.
    /// </summary>
    /// <param name="application">
    /// The application's packs, permissions and marks. An application that adds none builds with
    /// <see cref="Build(IEnumerable{Permission})"/>, which passes <c>new ApplicationCatalogue()</c>.
    /// </param>
    /// <param name="contributed">
    /// The permissions the modules contribute: in an export, <c>TenancyPermissionsOfModules.All</c>, which Tenancy's generator
    /// writes into a project that composes the modules, as the host registers them.
    /// </param>
    /// <exception cref="TenancyCatalogueException">Something does not hold together; every problem found is listed.</exception>
    public static TenancyCatalogue Build(ApplicationCatalogue application, IEnumerable<Permission> contributed)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(contributed);

        var problems = new List<string>();
        var declared = CheckPermissions(application.Permissions ?? [], [.. contributed], problems);
        var byName = declared.ToDictionary(permission => permission.Name, StringComparer.Ordinal);
        var marked = CheckMarks(declared, application.AccessManagingPermissions ?? [], byName, problems);

        // Which permissions are live, and which of those manage access, is settled before the packs are looked at: an
        // administrators' pack is built from the first and checked against the second.
        var live = declared.Where(permission => !permission.Retired).Select(permission => permission.Name).Order(StringComparer.Ordinal).ToArray();
        var managing = live.Where(marked.Contains).ToArray();

        CheckImplications(declared, byName, marked, problems);
        // Whether the application declares an administrators' pack is read off what it declares, a pack refused
        // for another reason included: once it declares one, it declares them for every shape.
        var declaresAdministrators = (application.Packs ?? []).Any(pack => pack is { Administers: true });
        var packs = CheckPacks(application.Packs ?? [], byName, live, managing, declaresAdministrators, problems);

        if (problems.Count > 0)
        {
            // A permission the catalogue does not know is most often one that never reached it, which the problems cannot
            // say: so the cure is said once, after them.
            throw new TenancyCatalogueException(
                problems,
                problems.Any(problem => problem.EndsWith(UnknownToTheCatalogue, StringComparison.Ordinal)) ? UnknownPermissionAdvice : null);
        }

        // A mark the application lists shows on the permission itself, as a pack shows the permissions it was built with.
        var permissions = byName.Values
            .Select(permission => marked.Contains(permission.Name) && !permission.ManagesAccess ? permission with { ManagesAccess = true } : permission)
            .OrderBy(permission => permission.Module, StringComparer.Ordinal)
            .ThenBy(permission => permission.Order)
            .ThenBy(permission => permission.Name, StringComparer.Ordinal)
            .ToArray();

        return new TenancyCatalogue(permissions, live, managing, [.. packs], !declaresAdministrators, application.ContainAccessManagingPermissions);
    }

    /// <summary>Whether the catalogue declares <paramref name="permission"/>, retired or not.</summary>
    public bool Knows(string permission) => permission is not null && _byName.ContainsKey(permission);

    /// <summary>Whether <paramref name="permission"/> is declared and not retired. A permission the catalogue does not know is not live.</summary>
    public bool IsLive(string permission) => permission is not null && _byName.TryGetValue(permission, out var declared) && !declared.Retired;

    /// <summary>
    /// Whether <paramref name="permission"/> manages access: it is declared, not retired, and marked, where it is
    /// declared or by the application. A permission the catalogue does not know manages nothing.
    /// </summary>
    public bool ManagesAccess(string permission) => permission is not null && _accessManaging.Contains(permission);

    /// <summary>
    /// The permissions of a role that manage access, each once, in ordinal order: none for an archived role, which
    /// grants nothing, and none that are not live. A role manages access when this is not empty, and is then
    /// given and taken away only by a seat that holds these permissions there, while <see cref="ContainAccessManagingPermissions"/>
    /// is on.
    /// </summary>
    /// <param name="role">What is known about the role.</param>
    public IReadOnlyList<string> AccessManagingPermissionsOf(RoleFacts role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return role.IsActive
            ? [.. role.Permissions.Where(ManagesAccess).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>
    /// The permissions a role made of <paramref name="permissions"/> grants: trimmed, with the live permissions each one implies
    /// added, distinct, in ordinal order.
    /// </summary>
    /// <param name="permissions">The permissions asked for.</param>
    /// <exception cref="RefusalException">
    /// <c>tenancy.unknown-permission</c>, with the offending permissions in <c>Permissions</c>: a permission the catalogue does not
    /// know, or has retired.
    /// </exception>
    public IReadOnlyList<string> Expand(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var expanded = Expand(permissions, _byName, out var offenders);
        return offenders.Count > 0
            ? throw TenancyRefusals.Refuse(TenancyRefusals.UnknownPermission, ("Permissions", string.Join(", ", offenders)))
            : expanded;
    }

    /// <summary>
    /// The packs a newly provisioned tenant of <paramref name="shape"/> gets a copy of, in their order: those seeded
    /// on provision whose <see cref="RolePack.SeededFor"/> is that shape or every shape.
    /// </summary>
    public IEnumerable<RolePack> PacksFor(TenantShape shape)
        => Packs.Where(pack => IsSeededFor(pack, shape)).OrderBy(pack => pack.Order);

    /// <summary>
    /// The pack the first administrator of a tenant of <paramref name="shape"/> is granted: the application's, or
    /// <see cref="TenancyPacks.DefaultAdministrators"/> when it declares none.
    /// </summary>
    public RolePack AdministratorPackFor(TenantShape shape)
        => PacksFor(shape).Single(pack => pack.Administers);

    /// <summary>
    /// Checks a permission that code is about to ask about. A permission the catalogue does not know is a mistake in code, not
    /// something a caller did, so it is an <see cref="ArgumentException"/> rather than a refusal: a typo, or a
    /// module's permission that never reached the catalogue, which the message names the cure of. A retired permission may be
    /// asked about, and holds nowhere.
    /// </summary>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="permission"/>.</exception>
    public void RequireAskable(string permission)
    {
        if (!Knows(permission))
        {
            throw new ArgumentException(
                "'" + permission + "' is not a permission of the permission catalogue. A module's permissions reach it from the [Permissions] class the module declares them in, "
                + "when the host calls services.AddTenancyPermissionsOfModules().",
                nameof(permission));
        }
    }

    /// <summary>
    /// Whether a tenant of <paramref name="shape"/> is given a copy of <paramref name="pack"/>: the pack is seeded on
    /// provision, and seeded for that shape or for every shape. What provisioning and a change of shape copy, and
    /// what the check of the administrators' packs counts, so the two never disagree.
    /// </summary>
    private static bool IsSeededFor(RolePack pack, TenantShape shape)
        => pack.SeedOnProvision && (pack.SeededFor is null || pack.SeededFor == shape);

    // ---------------------------------------------------------------- the checks Build makes

    /// <summary>What every one of Tenancy's permissions starts with, and no other permission does.</summary>
    private const string TenancyPrefix = "tenancy.";

    /// <summary>How each problem about a permission the catalogue does not know ends.</summary>
    private const string UnknownToTheCatalogue = "unknown to the catalogue.";

    /// <summary>
    /// Said once after problems about permissions the catalogue does not know: the usual cause is a module's permissions that never
    /// reached it, such as a host that does not add them, or an export built in a project that does not reference
    /// the module.
    /// </summary>
    private const string UnknownPermissionAdvice =
        "A permission unknown to the catalogue is most often a module's that never reached it. A module declares its permissions as the constants of a static class it marks "
        + "[Permissions], each with its level; the host adds every module's with services.AddTenancyPermissionsOfModules(), and an export builds with "
        + "TenancyPermissionsOfModules.All, in a project that references the module.";

    [GeneratedRegex("^[a-z][a-z0-9-]*(\\.[a-z0-9-]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PermissionPattern();

    /// <summary>
    /// Tenancy's permissions first, then the application's and the contributions', each checked on its own and against
    /// the others. A permission longer than <see cref="Permission.MaxNameLength"/> is refused here: a stored right keeps
    /// it in a column of that length, and a database that enforces it would refuse the first grant instead.
    /// <para>
    /// A permission declared twice is refused. When it is one and the same declaration both times, the same list was
    /// added twice, and the problem says so once for all of its permissions: a module whose <c>[Permissions]</c> classes
    /// the host adds with <c>AddTenancyPermissionsOfModules</c>, and whose list it adds with
    /// <c>AddTenancyPermissions</c> as well, is how that happens.
    /// </para>
    /// </summary>
    private static List<Permission> CheckPermissions(IReadOnlyList<Permission> application, IReadOnlyList<Permission> contributed, List<string> problems)
    {
        var declared = new List<Permission>(TenancyPermissions.All);
        var seen = declared.ToDictionary(permission => permission.Name, StringComparer.Ordinal);
        var addedTwice = new List<string>();

        foreach (var (permission, source) in application.Select(permission => (permission, "The application"))
                     .Concat(contributed.Select(permission => (permission, "A contribution"))))
        {
            if (permission is null)
            {
                problems.Add(source + " declares a null permission.");
                continue;
            }

            if (permission.Name is null || !PermissionPattern().IsMatch(permission.Name))
            {
                problems.Add(source + " declares the permission '" + permission.Name + "', which is not lowercase words and dashes joined by dots, such as 'projects.edit'.");
                continue;
            }

            if (permission.Name.Length > Permission.MaxNameLength)
            {
                problems.Add(source + " declares the permission '" + permission.Name + "', which is longer than " + Permission.MaxNameLength + " characters.");
                continue;
            }

            if (permission.Name.StartsWith(TenancyPrefix, StringComparison.Ordinal))
            {
                problems.Add(source + " declares '" + permission.Name + "': permissions under 'tenancy.' belong to the Tenancy package.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(permission.Module))
            {
                problems.Add("'" + permission.Name + "' names no module.");
            }

            if (seen.TryGetValue(permission.Name, out var first))
            {
                if (ReferenceEquals(first, permission))
                {
                    addedTwice.Add(permission.Name);
                }
                else
                {
                    problems.Add("'" + permission.Name + "' is declared more than once.");
                }

                continue;
            }

            seen.Add(permission.Name, permission);
            declared.Add(permission);
        }

        if (addedTwice.Count > 0)
        {
            problems.Add("The same declaration of " + string.Join(", ", addedTwice.Distinct(StringComparer.Ordinal).Select(permission => "'" + permission + "'"))
                         + " is added more than once. A module's [Permissions] classes are added by the host's AddTenancyPermissionsOfModules(),"
                         + " and with AddTenancyPermissions no more; and either is called once.");
        }

        return declared;
    }

    /// <summary>
    /// The permissions that manage access: those marked where they are declared, and those the application lists. A
    /// listed permission must be known, and listed once. One that is marked already, Tenancy's included, or retired,
    /// is accepted, so a module that starts marking its own permission breaks no application that listed it.
    /// </summary>
    private static HashSet<string> CheckMarks(
        List<Permission> declared,
        IReadOnlyList<string> listed,
        Dictionary<string, Permission> byName,
        List<string> problems)
    {
        var marked = declared.Where(permission => permission.ManagesAccess).Select(permission => permission.Name).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var permission in listed)
        {
            if (string.IsNullOrWhiteSpace(permission))
            {
                problems.Add("The application marks a blank permission as managing access.");
            }
            else if (!byName.ContainsKey(permission))
            {
                problems.Add("The application marks '" + permission + "' as managing access, which is " + UnknownToTheCatalogue);
            }
            else if (!seen.Add(permission))
            {
                problems.Add("The application marks '" + permission + "' as managing access more than once.");
            }
            else
            {
                marked.Add(permission);
            }
        }

        return marked;
    }

    /// <summary>
    /// What a permission implies is a known permission other than itself, and a permission that is implied implies nothing itself.
    /// A permission of the application or a module implies none of Tenancy's: a role holding it would otherwise
    /// manage the tenant, and hold the administrator's permission, without anyone having put that permission in it. And a
    /// live permission that manages no access implies no live permission that does, so what a role is labelled with is what
    /// it holds. Retired permissions are left out of that: nothing follows a retired permission's implications.
    /// </summary>
    private static void CheckImplications(List<Permission> declared, Dictionary<string, Permission> byName, HashSet<string> marked, List<string> problems)
    {
        var implied = declared
            .SelectMany(permission => (permission.Implies ?? []).Where(target => !string.Equals(target, permission.Name, StringComparison.Ordinal)))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var permission in declared)
        {
            foreach (var target in permission.Implies ?? [])
            {
                if (string.Equals(target, permission.Name, StringComparison.Ordinal))
                {
                    problems.Add("'" + permission.Name + "' implies itself.");
                }
                else if (target is null || !byName.ContainsKey(target))
                {
                    problems.Add("'" + permission.Name + "' implies '" + target + "', which is " + UnknownToTheCatalogue);
                }
                else if (!permission.Name.StartsWith(TenancyPrefix, StringComparison.Ordinal) && target.StartsWith(TenancyPrefix, StringComparison.Ordinal))
                {
                    problems.Add("'" + permission.Name + "' implies '" + target + "': only Tenancy's own permissions imply permissions under 'tenancy.'.");
                }
                else if (!permission.Retired && !marked.Contains(permission.Name) && !byName[target].Retired && marked.Contains(target))
                {
                    problems.Add("'" + permission.Name + "' implies '" + target + "', which manages access: a permission that manages no access implies none that does.");
                }
            }

            if (permission.Implies is { Count: > 0 } && implied.Contains(permission.Name))
            {
                problems.Add("'" + permission.Name + "' is implied by another permission and implies permissions itself; implications are one hop.");
            }
        }
    }

    /// <summary>
    /// Pack keys are unique, not blank and at most <see cref="RolePack.MaxKeyLength"/> long; the names and descriptions of the roles made from them follow a
    /// role's rules, and the names are unique ignoring case, as a tenant's role names are; every permission a pack
    /// lists is known and not retired; an administrators' pack that lists permissions leaves out none an administrator
    /// needs (<see cref="CheckAdministratorsPermissions"/>); and each shape has exactly one administrators' pack seeded for
    /// it (<see cref="CheckAdministratorsSeededFor"/>). Returns the packs as built: their permissions expanded, and for an
    /// administrators' pack that lists none, every live permission.
    /// <para>
    /// An application that declares no administrators' pack at all gets <see cref="TenancyPacks.DefaultAdministrators"/>,
    /// which gives every shape its one (<see cref="AddDefaultAdministrators"/>). One that declares an
    /// administrators' pack declares them all: a shape it leaves without one is a mistake to report, not a gap
    /// to fill, since the application chose what its administrators hold and the default might hold more.
    /// </para>
    /// </summary>
    private static List<RolePack> CheckPacks(
        IReadOnlyList<RolePack> packs,
        Dictionary<string, Permission> byName,
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

            if (pack.Administers && pack.Permissions is not { Count: > 0 })
            {
                // It lists nothing, and so holds every live permission: one declared later comes with it, and reaches the
                // roles made from the pack before at the next sync of the packs.
                result.Add(pack with { Permissions = live });
                continue;
            }

            var expanded = Expand(pack.Permissions ?? [], byName, out var offenders);
            foreach (var offender in offenders)
            {
                problems.Add("The pack '" + pack.Key + "' lists '" + offender + "', which is "
                             + (byName.ContainsKey(offender) ? "retired." : UnknownToTheCatalogue));
            }

            if (pack.Administers)
            {
                CheckAdministratorsPermissions(pack.Key, expanded, live, managing, problems);
            }

            result.Add(pack with { Permissions = expanded });
        }

        if (!declaresAdministrators)
        {
            AddDefaultAdministrators(result, seen, names, live, problems);
        }

        foreach (var shape in Enum.GetValues<TenantShape>())
        {
            CheckAdministratorsSeededFor(shape, result, problems);
        }

        return result;
    }

    /// <summary>
    /// A tenant of <paramref name="shape"/> has exactly one administrators' pack seeded for it, the one its first
    /// administrator is granted. The problem names the property to write, so the cure reads as the code that
    /// makes it. With none, that is the <see cref="RolePack.SeedOnProvision"/> of a pack that would be seeded for
    /// the shape but is declared unseeded, or else the <see cref="RolePack.SeededFor"/> of the pack the shape
    /// lacks. With more than one, it is keeping one of them, by seeding the others for another shape or declaring
    /// them without <see cref="RolePack.Administers"/>; the second is the cure when there are more administrators'
    /// packs than shapes.
    /// </summary>
    private static void CheckAdministratorsSeededFor(TenantShape shape, List<RolePack> packs, List<string> problems)
    {
        var administrators = packs.Where(pack => pack.Administers && IsSeededFor(pack, shape)).Select(pack => pack.Key).ToArray();
        if (administrators.Length == 1)
        {
            return;
        }

        var problem = "A " + shape.ToString().ToLowerInvariant() + " tenant needs exactly one administrators' pack seeded for it, and has ";
        if (administrators.Length > 1)
        {
            problems.Add(problem + administrators.Length + ": " + string.Join(", ", administrators) + ". Keep one of them for it: seed the "
                         + "others for another shape with SeededFor, or declare them without Administers.");
            return;
        }

        // An administrators' pack for this shape, or for every shape, that is declared unseeded is the one that is
        // missing: declaring another as well would leave it in the catalogue, an administrators' pack no tenant gets.
        var unseeded = packs
            .Where(pack => pack.Administers && !pack.SeedOnProvision && (pack.SeededFor is null || pack.SeededFor == shape))
            .Select(pack => "'" + pack.Key + "'")
            .ToArray();
        problems.Add(unseeded.Length switch
        {
            0 => problem + "none. The default one, '" + TenancyPacks.DefaultAdministratorsKey + "', is added only when the application declares "
                 + "no administrators' pack at all: declare one with SeededFor: TenantShape." + shape + " as well, or declare none.",
            1 => problem + "none: " + unseeded[0] + " would be, but is declared with SeedOnProvision: false, and the default one, '"
                 + TenancyPacks.DefaultAdministratorsKey + "', is not added in its place. Leave SeedOnProvision: false off it.",
            _ => problem + "none: " + string.Join(", ", unseeded) + " would be, but are declared with SeedOnProvision: false, and the default "
                 + "one, '" + TenancyPacks.DefaultAdministratorsKey + "', is not added in their place. Leave SeedOnProvision: false off one of them.",
        });
    }

    /// <summary>
    /// Puts <see cref="TenancyPacks.DefaultAdministrators"/> first among the packs, holding every live permission. Its key
    /// is its own, and so are its names, ignoring case, as a tenant's role names are: the catalogue's, and the one
    /// the package gives it in each language it ships, Beheerder in Dutch. So a pack of the application's that has
    /// the permission or any of the names is refused, with what to do about it. That pack is no administrators' pack, or
    /// the default would not be added: it is renamed, or declared as the administrators' pack. A name the
    /// application's own <see cref="IRolePackTexts"/> gives a pack is not known here, and is refused at
    /// provisioning, as a clash between two of the application's own packs is.
    /// </summary>
    /// <param name="result">The application's packs as built; the default goes first.</param>
    /// <param name="permissions">The permissions of the application's packs.</param>
    /// <param name="names">The names of the application's packs, ignoring case, each with the key of its pack.</param>
    /// <param name="live">Every live permission, in ordinal order: what the default holds.</param>
    /// <param name="problems">Where a pack that has the default's permission or name is reported.</param>
    private static void AddDefaultAdministrators(
        List<RolePack> result,
        HashSet<string> permissions,
        Dictionary<string, string> names,
        string[] live,
        List<string> problems)
    {
        var fallback = TenancyPacks.DefaultAdministrators;
        const string Added = "the default administrators' pack, added because the application declares no administrators' pack";
        const string Fix = ", or declare it with Administers: true to make it the administrators' pack instead.";

        if (permissions.Contains(fallback.Key))
        {
            problems.Add("The pack '" + fallback.Key + "' has the permission of " + Added + ". Give the pack another permission" + Fix);
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

        result.Insert(0, fallback with { Permissions = live });
    }

    /// <summary>
    /// An administrators' pack that lists its permissions holds, listed or implied by a permission it lists, every live permission of
    /// Tenancy's own and every live permission that manages access. An administrator runs the tenant, which takes
    /// Tenancy's permissions, <see cref="TenancyPermissions.AdministratorPermission"/> among them, and gives every role, which takes
    /// each permission that manages access: a role holding one is given only by a seat that holds it. A pack short of
    /// one would make tenants whose administrators cannot give one of their own roles, so each permission it leaves
    /// out is a problem of its own. A retired permission is not asked for: it manages nothing, and no pack may list it.
    /// </summary>
    /// <param name="pack">The pack's key, for the problems.</param>
    /// <param name="held">The permissions the pack lists, expanded.</param>
    /// <param name="live">Every live permission, in ordinal order.</param>
    /// <param name="managing">Every live permission that manages access.</param>
    /// <param name="problems">Where a permission the pack leaves out is reported.</param>
    private static void CheckAdministratorsPermissions(string pack, string[] held, string[] live, string[] managing, List<string> problems)
    {
        var holds = held.ToHashSet(StringComparer.Ordinal);
        var manages = managing.ToHashSet(StringComparer.Ordinal);

        foreach (var permission in live)
        {
            // Tenancy's permissions are asked for as Tenancy's own, whether or not they are marked as well.
            var own = permission.StartsWith(TenancyPrefix, StringComparison.Ordinal);
            if ((own || manages.Contains(permission)) && !holds.Contains(permission))
            {
                problems.Add("The administrators' pack '" + pack + "' lists permissions but not '" + permission + "', which "
                             + (own ? "is one of Tenancy's own" : "manages access")
                             + ": an administrator holds every permission that manages access, and Tenancy's own.");
            }
        }
    }

    /// <summary>
    /// Trims the permissions, adds the live permissions each one implies, and returns them distinct and in ordinal order;
    /// <paramref name="offenders"/> are the permissions that are unknown or retired.
    /// </summary>
    private static string[] Expand(IEnumerable<string> permissions, Dictionary<string, Permission> byName, out List<string> offenders)
    {
        offenders = [];
        var expanded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var permission in permissions)
        {
            var trimmed = permission?.Trim() ?? string.Empty;
            if (!byName.TryGetValue(trimmed, out var declared) || declared.Retired)
            {
                if (!offenders.Contains(trimmed, StringComparer.Ordinal))
                {
                    offenders.Add(trimmed);
                }

                continue;
            }

            expanded.Add(trimmed);
            foreach (var implied in declared.Implies ?? [])
            {
                if (byName.TryGetValue(implied, out var impliedPermission) && !impliedPermission.Retired)
                {
                    expanded.Add(implied);
                }
            }
        }

        return [.. expanded.Order(StringComparer.Ordinal)];
    }
}
