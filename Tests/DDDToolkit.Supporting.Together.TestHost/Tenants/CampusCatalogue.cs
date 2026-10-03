using Campus.Courses;
using Campus.Labs;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Campus.Tenants;

/// <summary>
/// The application's catalogue: the roles a college starts with, the kinds of unit it has, and the keys of its
/// courses and labs that a role of the organization can hold. A key that is in it can be put into a role of
/// the organization, and so be held at a faculty for every course or lab below it; a key that is not, such as
/// dissolving a course, is never held that way.
/// </summary>
public static class CampusCatalogue
{
    /// <summary>The administrators' pack: every key, for the whole college.</summary>
    public const string AdministratorPack = "campus-admin";

    /// <summary>Runs a faculty: sees and grades its courses, and sees and equips its labs, wherever the role is held and below.</summary>
    public const string DeanPack = "dean";

    /// <summary>The role of the college's own that the owner of a lab holds on it.</summary>
    public const string LabChiefPack = "lab-chief";

    /// <summary>
    /// A role of the college's own for those who show a lab's equipment: it sees and equips, and reads the
    /// college's access history, which is no key a lab's member gets by it.
    /// </summary>
    public const string LabDemonstratorPack = "lab-demonstrator";

    /// <summary>The kind of the root: the college itself.</summary>
    public const string College = "college";

    /// <summary>A faculty of the college.</summary>
    public const string Faculty = "faculty";

    /// <summary>An institute of a faculty.</summary>
    public const string Institute = "institute";

    /// <summary>
    /// The keys of the courses and the labs that the organization's roles can hold: what the module adds to
    /// the catalogue. <see cref="CourseKeys.Dissolve"/> is not among them.
    /// </summary>
    public static IReadOnlyList<Permission> Permissions { get; } =
    [
        new(CourseKeys.See, "Courses", "See courses", Order: 10),
        new(CourseKeys.Grade, "Courses", "Grade the work handed in for a course", Order: 20),
        new(CourseKeys.Outline, "Courses", "Write a course's outline", Order: 30),
        new(LabKeys.See, "Labs", "See labs", Order: 10),
        new(LabKeys.Equip, "Labs", "Equip a lab", Order: 20),
        new(LabKeys.Calibrate, "Labs", "Calibrate a lab's instruments", Order: 30),
    ];

    /// <summary>What the application passes as <c>TenancyOptions.Catalogue</c>.</summary>
    public static ApplicationCatalogue Application { get; } = new(
        Packs:
        [
            new(AdministratorPack, "Administrator", "Runs the college", [], Administers: true, Order: 10),
            new(DeanPack, "Dean", "Runs a faculty", [CourseKeys.See, CourseKeys.Grade, LabKeys.See, LabKeys.Equip], Order: 20),
            new(LabChiefPack, "Lab chief", "Is responsible for a lab", [LabKeys.See, LabKeys.Equip, LabKeys.Calibrate], Order: 30),
            new(LabDemonstratorPack, "Lab demonstrator", "Shows a lab's equipment", [LabKeys.See, LabKeys.Equip, TenancyKeys.HistoryView], Order: 40),
        ],
        UnitKinds: [new(College, "College", 10), new(Faculty, "Faculty", 20), new(Institute, "Institute", 30)]);

    /// <summary>The catalogue as the application runs with it, for whatever is written before its services exist.</summary>
    public static TenancyCatalogue Built { get; } = TenancyCatalogue.Build(Application, Permissions);
}

/// <summary>
/// The logical names of Tenancy's functions in a database that answers the questions itself: what the rules of
/// a course and of a lab name, so their own functions ask Tenancy's. Texts, relative to the owner Tenancy
/// gives its functions, whatever schema they live in.
/// </summary>
public static class TenancyFunctions
{
    /// <summary>Answers the calling seat, or <c>NULL</c> for a caller without an active seat in the tenant.</summary>
    public const string CallerSeat = TenancyRowAccess.Owner + "/caller_seat";

    /// <summary>Answers, for a key, the units the calling seat's hold of it reaches: where it is held, and every unit below.</summary>
    public const string UnitsWhereIHold = TenancyRowAccess.Owner + "/units_where_i_hold";

    /// <summary>Answers, for a key, the tenant's roles in use that give it.</summary>
    public const string RolesWithKey = TenancyRowAccess.Owner + "/roles_with_key";
}
