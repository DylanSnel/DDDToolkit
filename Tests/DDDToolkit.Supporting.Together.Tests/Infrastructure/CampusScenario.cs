namespace DDDToolkit.Supporting.Together.Tests.Infrastructure;

/// <summary>Somebody asking in a college: a person, and the tenant their request names.</summary>
/// <param name="Person">Who signed in.</param>
/// <param name="College">The slug of the college the request names, or <see langword="null"/> for a request that names none.</param>
public sealed record Asking(CampusPerson Person, string? College)
{
    /// <inheritdoc />
    public override string ToString() => Person.Name + (College is null ? " in no college" : " in " + College);
}

/// <summary>
/// The data the questions are asked over: two colleges, each with an organization, seats and roles of
/// Tenancy's, and beside them courses and labs with members of Membership's. Every period is days away from
/// the moment it is made for, so a clock that is a little off, a database's own among them, answers the same.
/// </summary>
/// <remarks>
/// <para>
/// <b>Alder</b> is a tree: the college, the faculty of Science with the institute of Physics below it, and the
/// faculty of Arts. Ada administers it, so she holds every key of the catalogue for the whole college. Dee is
/// the dean of Science: she sees and grades courses and sees and equips labs there and below. Owen and Tom
/// are placed at Physics, Pia and Ned at Arts. Sue is a dean of Science as well, and suspended.
/// </para>
/// <para>
/// <b>Birch</b> is flat. Bea administers it, and Tom has a seat there too: the same person, another seat.
/// Ivo signed in and has a seat nowhere.
/// </para>
/// <para>
/// <b>The courses</b>, whose roles are kept for each college: Optics, at Physics, owned by Owen, with Tom
/// tutoring, Pia convening until next week, Dee on it with no role, and Sue tutoring; Poetry, at Arts, owned
/// by Pia; and Latin, in Birch, owned by Bea.
/// </para>
/// <para>
/// <b>The labs</b>, whose roles are the college's own: the laser lab, at Physics, owned by Owen, with Tom as
/// a demonstrator; and the darkroom, at Arts, owned by Pia.
/// </para>
/// </remarks>
public sealed class CampusScenario
{
    /// <summary>The slug of Alder.</summary>
    public const string InAlder = "alder";

    /// <summary>The slug of Birch.</summary>
    public const string InBirch = "birch";

    private readonly Dictionary<OrganizationUnitId, OrganizationUnitId?> _partOf;
    private readonly List<(SeatId Seat, OrganizationUnitId Unit, IReadOnlyList<string> Keys)> _heldInTheOrganization;
    private readonly Dictionary<CourseRoleId, IReadOnlyList<string>> _courseRoleKeys;
    private readonly Dictionary<RoleId, IReadOnlyList<string>> _tenantRoleKeys;
    private readonly List<(CampusPerson Person, string College, TenantId Tenant, SeatId Seat, bool Active)> _seats;

    /// <summary>The scenario, as it is at <paramref name="now"/>.</summary>
    public CampusScenario(DateTimeOffset now)
    {
        Now = now;
        var monthAgo = now.AddDays(-30);
        var before = now.AddDays(-10);
        NextWeek = now.AddDays(7);

        _partOf = new()
        {
            [AlderRoot] = null,
            [Science] = AlderRoot,
            [Physics] = Science,
            [Arts] = AlderRoot,
            [BirchRoot] = null,
        };

        _seats =
        [
            (Ada, InAlder, Alder, AdaSeat, true),
            (Dee, InAlder, Alder, DeeSeat, true),
            (Owen, InAlder, Alder, OwenSeat, true),
            (Tom, InAlder, Alder, TomSeat, true),
            (Pia, InAlder, Alder, PiaSeat, true),
            (Sue, InAlder, Alder, SueSeat, false),
            (Ned, InAlder, Alder, NedSeat, true),
            (Bea, InBirch, Birch, BeaSeat, true),
            (Tom, InBirch, Birch, TomInBirch, true),
        ];

        var pack = CampusCatalogue.Built.Packs.ToDictionary(each => each.Key, each => each.Keys);
        var everyKey = CampusCatalogue.Built.LiveKeys;
        _heldInTheOrganization =
        [
            (AdaSeat, AlderRoot, everyKey),
            (DeeSeat, Science, pack[CampusCatalogue.DeanPack]),
            (SueSeat, Science, pack[CampusCatalogue.DeanPack]),
            (BeaSeat, BirchRoot, everyKey),
        ];

        _tenantRoleKeys = new()
        {
            [AlderDean] = pack[CampusCatalogue.DeanPack],
            [AlderLabChief] = pack[CampusCatalogue.LabChiefPack],
            [AlderDemonstrator] = pack[CampusCatalogue.LabDemonstratorPack],
            [BirchLabChief] = pack[CampusCatalogue.LabChiefPack],
        };

        // The roles of courses of each college: the starter roles the rules declare, made as the application makes them.
        var starters = StarterRoles.Missing<CourseRoleId>(CourseMembership.Rules, []);
        CourseRoles =
        [
            .. starters.Select(draft => new CourseRole(AlderCourseRoles[draft.MadeFrom!], Alder, draft)),
            .. starters.Select(draft => new CourseRole(BirchCourseRoles[draft.MadeFrom!], Birch, draft)),
        ];
        _courseRoleKeys = CourseRoles.ToDictionary(role => role.Id, role => role.Keys);

        var optics = new Course(Optics, Alder, Physics, "Optics", OwenSeat, AlderCourseRoles[MembershipRules.DefaultOwnerRole], monthAgo);
        optics.TakeOn(TomSeat, AlderCourseRoles[CourseMembership.Tutor], MemberPeriod.Open(before), now, by: OwenSeat);
        optics.TakeOn(PiaSeat, AlderCourseRoles[CourseMembership.Convenor], MemberPeriod.Between(before, NextWeek), now, by: OwenSeat);
        optics.TakeOn(DeeSeat, MemberPeriod.Open(before), now, by: OwenSeat);
        optics.TakeOn(SueSeat, AlderCourseRoles[CourseMembership.Tutor], MemberPeriod.Open(before), now, by: OwenSeat);

        var poetry = new Course(Poetry, Alder, Arts, "Poetry", PiaSeat, AlderCourseRoles[MembershipRules.DefaultOwnerRole], monthAgo);
        var latin = new Course(Latin, Birch, BirchRoot, "Latin", BeaSeat, BirchCourseRoles[MembershipRules.DefaultOwnerRole], monthAgo);
        Courses = [optics, poetry, latin];

        var laser = new Lab(Laser, Alder, Physics, "Laser lab", OwenSeat, AlderLabChief, monthAgo);
        laser.LetIn(TomSeat, AlderDemonstrator, MemberPeriod.Open(before), now, by: OwenSeat);
        var darkroom = new Lab(Darkroom, Alder, Arts, "Darkroom", PiaSeat, AlderLabChief, monthAgo);
        Labs = [laser, darkroom];
    }

    /// <summary>The moment the scenario is made for.</summary>
    public DateTimeOffset Now { get; }

    /// <summary>When Pia stops convening Optics.</summary>
    public DateTimeOffset NextWeek { get; }

    public CampusPerson Ada { get; } = Person("Ada");

    public CampusPerson Dee { get; } = Person("Dee");

    public CampusPerson Owen { get; } = Person("Owen");

    public CampusPerson Tom { get; } = Person("Tom");

    public CampusPerson Pia { get; } = Person("Pia");

    /// <summary>A dean of Science and a tutor of Optics whose seat is suspended: nobody, while it is.</summary>
    public CampusPerson Sue { get; } = Person("Sue");

    /// <summary>A seat of Alder that is on no list and holds nothing.</summary>
    public CampusPerson Ned { get; } = Person("Ned");

    public CampusPerson Bea { get; } = Person("Bea");

    /// <summary>Somebody who signed in and has a seat nowhere.</summary>
    public CampusPerson Ivo { get; } = Person("Ivo");

    /// <summary>Everybody a question is asked as, each in every college worth asking in.</summary>
    public IReadOnlyList<Asking> Everybody =>
    [
        new(Ada, InAlder), new(Dee, InAlder), new(Owen, InAlder), new(Tom, InAlder), new(Pia, InAlder), new(Sue, InAlder), new(Ned, InAlder),
        new(Tom, InBirch), new(Bea, InBirch),

        // In a college they have no seat in, in one that does not exist, and in none.
        new(Bea, InAlder), new(Ivo, InAlder), new(Ada, "elm"), new(Ada, null),
    ];

    public TenantId Alder { get; } = TenantId.CreateSequential();

    public TenantId Birch { get; } = TenantId.CreateSequential();

    public OrganizationUnitId AlderRoot { get; } = OrganizationUnitId.CreateSequential();

    public OrganizationUnitId Science { get; } = OrganizationUnitId.CreateSequential();

    public OrganizationUnitId Physics { get; } = OrganizationUnitId.CreateSequential();

    public OrganizationUnitId Arts { get; } = OrganizationUnitId.CreateSequential();

    public OrganizationUnitId BirchRoot { get; } = OrganizationUnitId.CreateSequential();

    public SeatId AdaSeat { get; } = SeatId.CreateSequential();

    public SeatId DeeSeat { get; } = SeatId.CreateSequential();

    public SeatId OwenSeat { get; } = SeatId.CreateSequential();

    public SeatId TomSeat { get; } = SeatId.CreateSequential();

    public SeatId PiaSeat { get; } = SeatId.CreateSequential();

    public SeatId SueSeat { get; } = SeatId.CreateSequential();

    public SeatId NedSeat { get; } = SeatId.CreateSequential();

    public SeatId BeaSeat { get; } = SeatId.CreateSequential();

    /// <summary>Tom's seat in Birch: the same person, another seat.</summary>
    public SeatId TomInBirch { get; } = SeatId.CreateSequential();

    /// <summary>Alder's role made from the dean's pack.</summary>
    public RoleId AlderDean { get; } = RoleId.CreateSequential();

    /// <summary>Alder's role for a lab's chief: what an owner of a lab holds.</summary>
    public RoleId AlderLabChief { get; } = RoleId.CreateSequential();

    /// <summary>Alder's role for a lab's demonstrators.</summary>
    public RoleId AlderDemonstrator { get; } = RoleId.CreateSequential();

    /// <summary>Birch's role for a lab's chief.</summary>
    public RoleId BirchLabChief { get; } = RoleId.CreateSequential();

    /// <summary>Alder's roles of courses, by the starter role each was made from.</summary>
    public IReadOnlyDictionary<string, CourseRoleId> AlderCourseRoles { get; } = RolesOfCourses();

    /// <summary>Birch's roles of courses, by the starter role each was made from.</summary>
    public IReadOnlyDictionary<string, CourseRoleId> BirchCourseRoles { get; } = RolesOfCourses();

    public CourseId Optics { get; } = CourseId.CreateSequential();

    public CourseId Poetry { get; } = CourseId.CreateSequential();

    public CourseId Latin { get; } = CourseId.CreateSequential();

    public LabId Laser { get; } = LabId.CreateSequential();

    public LabId Darkroom { get; } = LabId.CreateSequential();

    /// <summary>The roles of courses of both colleges, as their aggregates.</summary>
    public IReadOnlyList<CourseRole> CourseRoles { get; }

    /// <summary>The courses, as their aggregates: what the questions are compared with.</summary>
    public IReadOnlyList<Course> Courses { get; }

    /// <summary>The labs, as their aggregates.</summary>
    public IReadOnlyList<Lab> Labs { get; }

    /// <summary>
    /// Every key a question about courses is asked with: the courses' own, the one no role and no catalogue
    /// holds, a lab's, one of Tenancy's, and one nobody declared.
    /// </summary>
    public static IReadOnlyList<string> CourseKeysAsked
        => [CourseKeys.See, CourseKeys.Grade, CourseKeys.Outline, CourseKeys.Dissolve, LabKeys.Equip, TenancyKeys.HistoryView, "courses.unheard-of"];

    /// <summary>Every key a question about labs is asked with: the labs' own, a course's, one of Tenancy's that a role given in a lab holds, and one nobody declared.</summary>
    public static IReadOnlyList<string> LabKeysAsked
        => [LabKeys.See, LabKeys.Equip, LabKeys.Calibrate, CourseKeys.Grade, TenancyKeys.HistoryView, "labs.unheard-of"];

    /// <summary>
    /// Saves the scenario the way an application comes by it: the colleges, their units, seats and grants through
    /// Tenancy's use cases, as system work, and the roles of courses, the courses and the labs as the
    /// application's own work in each college.
    /// </summary>
    public async Task SaveAsync(CampusServices services)
    {
        var cancellation = TestContext.Current.CancellationToken;

        await services.BySystemAsync(provider => provider.Tenants().ProvisionAsync(
            new CampusTenancy.TenantToProvision(
                InAlder, "Alder College", TenantShape.Hierarchical, "Alder College", Ada.Identity, Ada.Name,
                TenantId: Alder,
                RootId: AlderRoot,
                AdminSeatId: AdaSeat,
                RoleIds: new Dictionary<string, RoleId>
                {
                    [CampusCatalogue.DeanPack] = AlderDean,
                    [CampusCatalogue.LabChiefPack] = AlderLabChief,
                    [CampusCatalogue.LabDemonstratorPack] = AlderDemonstrator,
                }),
            cancellation));
        await services.BySystemAsync(provider => provider.Tenants().ProvisionAsync(
            new CampusTenancy.TenantToProvision(
                InBirch, "Birch College", TenantShape.Flat, "Birch College", Bea.Identity, Bea.Name,
                TenantId: Birch,
                RootId: BirchRoot,
                AdminSeatId: BeaSeat,
                RoleIds: new Dictionary<string, RoleId> { [CampusCatalogue.LabChiefPack] = BirchLabChief }),
            cancellation));

        await services.BySystemInAsync(Alder, provider => provider.Organization().AddUnitAsync(AlderRoot, "Science", cancellation, Science));
        await services.BySystemInAsync(Alder, provider => provider.Organization().AddUnitAsync(Science, "Physics", cancellation, Physics));
        await services.BySystemInAsync(Alder, provider => provider.Organization().AddUnitAsync(AlderRoot, "Arts", cancellation, Arts));

        await GiveASeatAsync(services, Alder, Dee, DeeSeat, Science, AlderDean);
        await GiveASeatAsync(services, Alder, Owen, OwenSeat, Physics);
        await GiveASeatAsync(services, Alder, Tom, TomSeat, Physics);
        await GiveASeatAsync(services, Alder, Pia, PiaSeat, Arts);
        await GiveASeatAsync(services, Alder, Sue, SueSeat, Science, AlderDean);
        await GiveASeatAsync(services, Alder, Ned, NedSeat, Arts);
        await GiveASeatAsync(services, Birch, Tom, TomInBirch, BirchRoot);
        await services.BySystemInAsync(Alder, provider => provider.Seats().SuspendAsync(SueSeat, cancellation));

        foreach (var college in new[] { Alder, Birch })
        {
            await services.ChangeAsync(college, context =>
            {
                context.CourseRoles.AddRange(CourseRoles.Where(role => role.TenantId == college));
                context.Courses.AddRange(Courses.Where(course => course.TenantId == college));
                context.Labs.AddRange(Labs.Where(lab => lab.TenantId == college));
                return Task.CompletedTask;
            });
        }
    }

    /// <summary>The seat <paramref name="asking"/> acts through: the person's active seat in the college the request names, or none.</summary>
    public SeatId? SeatOf(Asking asking)
        => _seats.Where(seat => seat.Person == asking.Person && seat.College == asking.College && seat.Active).Select(seat => (SeatId?)seat.Seat).SingleOrDefault();

    /// <summary>The tenant <paramref name="asking"/> acts in, when it has an active seat there.</summary>
    public TenantId? TenantOf(Asking asking)
        => _seats.Where(seat => seat.Person == asking.Person && seat.College == asking.College && seat.Active).Select(seat => (TenantId?)seat.Tenant).SingleOrDefault();

    /// <summary>The tenant a request that names <paramref name="college"/> means, whoever asks, or none for a slug no college has.</summary>
    public TenantId? TenantNamed(string? college) => college switch
    {
        InAlder => Alder,
        InBirch => Birch,
        _ => null,
    };

    /// <summary>The courses somebody is on now, in the college it asks in.</summary>
    public IReadOnlyList<CourseId> CoursesAsMember(Asking asking)
        => [.. CoursesOf(asking, (course, seat) => course.Tutors.Any(row => row.MemberId == seat && row.AppliesAt(Now)))];

    /// <summary>The courses somebody sees: those it owns, those it is on now, and those given where it holds the key that sees, or below.</summary>
    public IReadOnlyList<CourseId> CoursesSeenBy(Asking asking)
        => [.. CoursesOf(asking, (course, seat) => course.OwnerSeatId == seat || course.Tutors.Any(row => row.MemberId == seat && row.AppliesAt(Now)) || HeldAbove(seat, course.UnitId, CourseKeys.See))];

    /// <summary>
    /// The courses somebody holds <paramref name="key"/> on, worked out in memory with the rules and what the
    /// scenario gave in the organization, the way a reader of both would: by owning it, by being on it,
    /// through a role of courses, or from the organization above.
    /// </summary>
    public IReadOnlyList<CourseId> CoursesHeldBy(Asking asking, string key)
        => [.. CoursesOf(asking, (course, seat) => AsMemberOf(course, seat, key) || HeldAbove(seat, course.UnitId, key))];

    /// <summary>How somebody holds <paramref name="key"/> on a course it sees, or <see langword="null"/> when it does not hold it.</summary>
    public MemberVia? ViaOn(Asking asking, CourseId course, string key)
    {
        var found = Courses.Single(each => each.Id == course);
        return SeatOf(asking) is not { } seat ? null
            : AsMemberOf(found, seat, key) ? MemberVia.Members
            : HeldAbove(seat, found.UnitId, key) ? MemberVia.Above
            : null;
    }

    /// <summary>The labs somebody works in now, in the college it asks in.</summary>
    public IReadOnlyList<LabId> LabsAsMember(Asking asking)
        => [.. LabsOf(asking, (lab, seat) => lab.Technicians.Any(row => row.MemberId == seat && row.AppliesAt(Now)))];

    /// <summary>The labs somebody sees: those it owns, those it works in now, and those that are where it holds the key that sees, or below.</summary>
    public IReadOnlyList<LabId> LabsSeenBy(Asking asking)
        => [.. LabsOf(asking, (lab, seat) => lab.OwnerSeatId == seat || lab.Technicians.Any(row => row.MemberId == seat && row.AppliesAt(Now)) || HeldAbove(seat, lab.UnitId, LabKeys.See))];

    /// <summary>The labs somebody holds <paramref name="key"/> on: by working in it, through a role of the college's it holds there, or from the organization above.</summary>
    public IReadOnlyList<LabId> LabsHeldBy(Asking asking, string key)
        => [.. LabsOf(asking, (lab, seat) => AsMemberOf(lab, seat, key) || HeldAbove(seat, lab.UnitId, key))];

    /// <summary>The labs where somebody works now and holds a role of the college's now that gives <paramref name="key"/>.</summary>
    public IReadOnlyList<LabId> LabsWithARoleFor(Asking asking, string key)
        => [.. LabsOf(asking, (lab, seat) => ThroughARoleOf(lab, seat, key))];

    /// <summary>The courses where somebody is on it now and holds a role of courses now that gives <paramref name="key"/>.</summary>
    public IReadOnlyList<CourseId> CoursesWithARoleFor(Asking asking, string key)
        => [.. CoursesOf(asking, (course, seat) => ThroughARoleOf(course, seat, key))];

    private IEnumerable<CourseId> CoursesOf(Asking asking, Func<Course, SeatId, bool> reached)
        => SeatOf(asking) is { } seat && TenantOf(asking) is { } tenant
            ? Courses.Where(course => course.TenantId == tenant && reached(course, seat)).Select(course => course.Id).Order()
            : [];

    private IEnumerable<LabId> LabsOf(Asking asking, Func<Lab, SeatId, bool> reached)
        => SeatOf(asking) is { } seat && TenantOf(asking) is { } tenant
            ? Labs.Where(lab => lab.TenantId == tenant && reached(lab, seat)).Select(lab => lab.Id).Order()
            : [];

    /// <summary>Whether a seat holds a key on a course as one of its members: by owning it, by being on it, or through a role of courses.</summary>
    private bool AsMemberOf(Course course, SeatId seat, string key)
    {
        var rules = CourseMembership.Rules;
        return (rules.OwnerHolds(key) && course.OwnerSeatId == seat)
               || (rules.MembershipGives(key) && course.Tutors.Any(row => row.MemberId == seat && row.AppliesAt(Now)))
               || ThroughARoleOf(course, seat, key);
    }

    private bool ThroughARoleOf(Course course, SeatId seat, string key)
        => CourseMembership.Rules.MemberKeys.Allows(key)
           && course.Tutors.Any(row => row.MemberId == seat
               && _courseRoleKeys.Any(role => role.Value.Contains(key, StringComparer.Ordinal) && row.HoldsAt(role.Key, Now)));

    /// <summary>Whether a seat holds a key in a lab as one of its technicians: by working in it, or through a role of the college's.</summary>
    private bool AsMemberOf(Lab lab, SeatId seat, string key)
        => (LabMembership.Rules.MembershipGives(key) && lab.Technicians.Any(row => row.MemberId == seat && row.AppliesAt(Now)))
           || ThroughARoleOf(lab, seat, key);

    private bool ThroughARoleOf(Lab lab, SeatId seat, string key)
        => LabMembership.Rules.MemberKeys.Allows(key)
           && lab.Technicians.Any(row => row.MemberId == seat
               && _tenantRoleKeys.Any(role => role.Value.Contains(key, StringComparer.Ordinal) && row.HoldsAt(role.Key, Now)));

    /// <summary>Whether a seat holds a key in the organization at a unit, or at one above it.</summary>
    private bool HeldAbove(SeatId seat, OrganizationUnitId unit, string key)
        => _heldInTheOrganization.Any(held => held.Seat == seat && held.Keys.Contains(key, StringComparer.Ordinal) && AtOrAbove(unit).Contains(held.Unit));

    /// <summary>A unit, and every unit above it.</summary>
    private IEnumerable<OrganizationUnitId> AtOrAbove(OrganizationUnitId unit)
    {
        for (OrganizationUnitId? at = unit; at is { } here; at = _partOf[here])
        {
            yield return here;
        }
    }

    /// <summary>Adds a seat for a person, places it at a unit, and gives it roles of the organization there, as system work in the college.</summary>
    private static async Task GiveASeatAsync(CampusServices services, TenantId college, CampusPerson person, SeatId seat, OrganizationUnitId unit, params RoleId[] roles)
    {
        var cancellation = TestContext.Current.CancellationToken;
        await services.BySystemInAsync(college, provider => provider.Seats().AddSeatAsync(person.Identity, person.Name, cancellation, seat));
        await services.BySystemInAsync(college, provider => provider.Seats().PlaceAsync(seat, unit, primary: true, cancellation));
        foreach (var role in roles)
        {
            await services.BySystemInAsync(college, provider => provider.Seats().GrantAsync(seat, unit, role, until: null, reason: null, cancellation));
        }
    }

    private static CampusPerson Person(string name) => new(name, Guid.CreateVersion7());

    private static Dictionary<string, CourseRoleId> RolesOfCourses()
        => CourseMembership.Rules.Roles.ToDictionary(role => role.Name, _ => CourseRoleId.CreateSequential(), StringComparer.Ordinal);
}
