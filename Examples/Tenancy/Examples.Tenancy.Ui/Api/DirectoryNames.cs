using Examples.Tenancy.Ui.Api.Wire;

namespace Examples.Tenancy.Ui.Api;

/// <summary>
/// The names one load of a page shows for the ids the API answered with. Made new for every load and dropped with
/// it, so a renamed seat shows at the next load, and nothing of one tenant or person is ever shown to another.
/// </summary>
/// <remarks>
/// <para>
/// A project names its unit and its crew by id: those are Tenancy's, and the module that answers a project has no
/// name of them to give. So a page asks Tenancy's directory what the ids it was answered are called,
/// <c>POST /tenancy/directory/seats</c>, <c>/units</c> and <c>/roles</c>, and shows the answers. The roles a crew
/// holds are project roles, the Projects module's own, and a tenant has few: they are read all at once, with
/// <c>GET /project-roles</c>.
/// </para>
/// <para>
/// It asks as few questions as it can: an id is asked about once, what a page listed anyway for its pickers is
/// not asked about at all, and a kind with nothing unknown gets no question. The questions are not recorded as
/// the session's last answer, which stays the answer the page is about.
/// </para>
/// <para>
/// A name it could not get is no reason not to show the page: such an id is shown as the start of the id itself.
/// </para>
/// </remarks>
/// <param name="api">The Host's API.</param>
public sealed class DirectoryNames(SampleApi api)
{
    /// <summary>How many ids one question to the directory takes: the API's limit, which the API enforces.</summary>
    public const int MostIdsPerQuestion = 200;

    // An id that was asked about and not answered is kept with no name, so it is not asked about again.
    private readonly Dictionary<Guid, string?> _seats = [];
    private readonly Dictionary<Guid, string?> _units = [];
    private readonly Dictionary<Guid, string?> _roles = [];
    private readonly Dictionary<Guid, string?> _projectRoles = [];

    /// <summary>Takes the names of seats the page already listed for a picker; these are not asked for again.</summary>
    public void Include(IEnumerable<SeatInfo> seats)
    {
        ArgumentNullException.ThrowIfNull(seats);
        foreach (var seat in seats)
        {
            _seats[seat.Id] = seat.DisplayName;
        }
    }

    /// <summary>Takes the paths of units the page already listed for a picker; these are not asked for again.</summary>
    public void Include(IEnumerable<UnitInfo> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        foreach (var unit in units)
        {
            _units[unit.Id] = unit.Path;
        }
    }

    /// <summary>Takes the names of roles the page already listed for a picker; these are not asked for again.</summary>
    public void Include(IEnumerable<RoleInfo> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
        {
            _roles[role.Id] = role.Name;
        }
    }

    /// <summary>Takes the names of project roles the page already listed for a picker; these are not asked for again.</summary>
    public void Include(IEnumerable<ProjectRoleInfo> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
        {
            _projectRoles[role.Id] = role.Name;
        }
    }

    /// <summary>
    /// Asks the directory for every id not held yet: one question per kind that has any, in parts of at most
    /// <see cref="MostIdsPerQuestion"/>, side by side, and the tenant's project roles once when one is not held.
    /// An id the answers leave out, or whose question is refused, is kept as unknown and not asked again in this
    /// load. Never throws for a refusal.
    /// </summary>
    /// <param name="seats">The seats the page shows.</param>
    /// <param name="units">The units it shows.</param>
    /// <param name="roles">The roles of the organization it shows.</param>
    /// <param name="projectRoles">The project roles it shows: the caller's on a crew, and those of each crew member.</param>
    /// <param name="cancellationToken">Cancels the questions.</param>
    public async Task EnsureAsync(
        IEnumerable<Guid>? seats = null,
        IEnumerable<Guid>? units = null,
        IEnumerable<Guid>? roles = null,
        IEnumerable<Guid>? projectRoles = null,
        CancellationToken cancellationToken = default)
    {
        var unknownSeats = Unknown(_seats, seats);
        var unknownUnits = Unknown(_units, units);
        var unknownRoles = Unknown(_roles, roles);
        var unknownProjectRoles = Unknown(_projectRoles, projectRoles);

        // Every question is sent before the first answer is awaited, so they run side by side. What they answer
        // is kept once they are back, by this one flow of work.
        var seatsAsked = InParts(unknownSeats, part => api.SeatsByIdAsync(part, cancellationToken), seat => (seat.Id, seat.DisplayName));
        var unitsAsked = InParts(unknownUnits, part => api.UnitsByIdAsync(part, cancellationToken), unit => (unit.Id, unit.Path));
        var rolesAsked = InParts(unknownRoles, part => api.RolesByIdAsync(part, cancellationToken), role => (role.Id, role.Name));
        Task<IReadOnlyList<(Guid Id, string Name)>>[] projectRolesAsked = unknownProjectRoles.Length == 0
            ? []
            : [AnswerAsync(api.ProjectRoleNamesAsync(cancellationToken), role => (role.Id, role.Name))];

        Keep(_seats, unknownSeats, await Task.WhenAll(seatsAsked));
        Keep(_units, unknownUnits, await Task.WhenAll(unitsAsked));
        Keep(_roles, unknownRoles, await Task.WhenAll(rolesAsked));
        Keep(_projectRoles, unknownProjectRoles, await Task.WhenAll(projectRolesAsked));
    }

    /// <summary>The name a seat is shown by, or, when the directory did not answer it, the start of its id.</summary>
    public string OfSeat(Guid id) => _seats.GetValueOrDefault(id) ?? Unnamed(id);

    /// <summary>A unit's path from the root, or, when the directory did not answer it, the start of its id.</summary>
    public string OfUnit(Guid id) => _units.GetValueOrDefault(id) ?? Unnamed(id);

    /// <summary>A role's name, or, when the directory did not answer it, the start of its id.</summary>
    public string OfRole(Guid id) => _roles.GetValueOrDefault(id) ?? Unnamed(id);

    /// <summary>A project role's name, or, when it could not be read, the start of its id.</summary>
    public string OfProjectRole(Guid id) => _projectRoles.GetValueOrDefault(id) ?? Unnamed(id);

    /// <summary>
    /// The names of several project roles as one text, by name and joined with commas; <see langword="null"/> for
    /// none, so a page says "no role" in its own words.
    /// </summary>
    public string? OfProjectRoles(IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var names = ids.Select(OfProjectRole).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return names.Count == 0 ? null : string.Join(", ", names);
    }

    /// <summary>A member's project roles as a page lists them: by the names the roles are shown by.</summary>
    public IReadOnlyList<CrewRoleInfo> RolesByName(CrewInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return [.. member.RolesHeld.OrderBy(held => OfProjectRole(held.RoleId), StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// A crew as a page shows it: the owner first, then by the names the seats are shown by, and two seats shown
    /// by one name by their ids, so the order is the same on every load.
    /// </summary>
    public IReadOnlyList<CrewInfo> CrewByName(IEnumerable<CrewInfo> crew)
    {
        ArgumentNullException.ThrowIfNull(crew);
        return [.. crew.OrderByDescending(member => member.IsOwner).ThenBy(member => OfSeat(member.SeatId), StringComparer.OrdinalIgnoreCase).ThenBy(member => member.SeatId)];
    }

    /// <summary>
    /// What <paramref name="id"/> is called when it is a seat, a unit, a role or a project role this page knows by
    /// name, or <see langword="null"/>: for an id whose kind is not known, such as an argument of a refusal.
    /// </summary>
    public string? NameOf(Guid id)
        => _seats.GetValueOrDefault(id) ?? _roles.GetValueOrDefault(id) ?? _projectRoles.GetValueOrDefault(id) ?? _units.GetValueOrDefault(id);

    /// <summary>What stands in for a name that could not be read: the id's first eight characters and an ellipsis.</summary>
    private static string Unnamed(Guid id) => id.ToString()[..8] + "…";

    /// <summary>The ids among <paramref name="ids"/> that are not held yet, each once.</summary>
    private static Guid[] Unknown(Dictionary<Guid, string?> held, IEnumerable<Guid>? ids)
        => ids is null ? [] : [.. ids.Distinct().Where(id => !held.ContainsKey(id))];

    /// <summary>
    /// Asks about <paramref name="ids"/> in parts one question takes, each part a question of its own, already
    /// sent when this returns. A question that is refused, or does not arrive, answers nothing.
    /// </summary>
    private static Task<IReadOnlyList<(Guid Id, string Name)>>[] InParts<T>(
        Guid[] ids,
        Func<Guid[], Task<ApiOutcome<IReadOnlyList<T>>>> ask,
        Func<T, (Guid Id, string Name)> named)
    {
        return [.. ids.Chunk(MostIdsPerQuestion).Select(part => AnswerAsync(ask(part), named))];
    }

    /// <summary>The names <paramref name="question"/> answers; none when it is refused, or does not arrive.</summary>
    private static async Task<IReadOnlyList<(Guid Id, string Name)>> AnswerAsync<T>(
        Task<ApiOutcome<IReadOnlyList<T>>> question,
        Func<T, (Guid Id, string Name)> named)
        => (await question).Value is { } answered ? [.. answered.Select(named)] : [];

    /// <summary>
    /// Holds every id that was asked about, with the name it was answered with or, where it was not answered,
    /// with none: so it is not asked about again.
    /// </summary>
    private static void Keep(Dictionary<Guid, string?> held, Guid[] asked, IReadOnlyList<(Guid Id, string Name)>[] answers)
    {
        foreach (var id in asked)
        {
            held[id] = null;
        }

        foreach (var (id, name) in answers.SelectMany(answer => answer))
        {
            held[id] = name;
        }
    }
}
