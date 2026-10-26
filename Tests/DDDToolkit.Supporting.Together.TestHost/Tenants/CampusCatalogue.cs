using Campus.Courses;
using Campus.Labs;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Campus.Tenants;

/// <summary>
/// The application's catalogue: the roles a college starts with, and the keys of its courses and labs that a
/// role of the organization can hold. A key that is in it can be put into a role of
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

    /// <summary>
    /// The keys of the courses and the labs that the organization's roles can hold: what the module adds to
    /// the catalogue. <see cref="CoursePermissions.Dissolve"/> is not among them.
    /// </summary>
    public static IReadOnlyList<Permission> Permissions { get; } =
    [
        new(CoursePermissions.See, "Courses", "See courses", Order: 10),
        new(CoursePermissions.Grade, "Courses", "Grade the work handed in for a course", Order: 20),
        new(CoursePermissions.Outline, "Courses", "Write a course's outline", Order: 30),
        new(LabPermissions.See, "Labs", "See labs", Order: 10),
        new(LabPermissions.Equip, "Labs", "Equip a lab", Order: 20),
        new(LabPermissions.Calibrate, "Labs", "Calibrate a lab's instruments", Order: 30),
    ];

    /// <summary>What the application passes as <c>TenancyOptions.Catalogue</c>.</summary>
    public static ApplicationCatalogue Application { get; } = new(
        Packs:
        [
            new(AdministratorPack, "Administrator", "Runs the college", [], Administers: true, Order: 10),
            new(DeanPack, "Dean", "Runs a faculty", [CoursePermissions.See, CoursePermissions.Grade, LabPermissions.See, LabPermissions.Equip], Order: 20),
            new(LabChiefPack, "Lab chief", "Is responsible for a lab", [LabPermissions.See, LabPermissions.Equip, LabPermissions.Calibrate], Order: 30),
            new(LabDemonstratorPack, "Lab demonstrator", "Shows a lab's equipment", [LabPermissions.See, LabPermissions.Equip, TenancyKeys.HistoryView], Order: 40),
        ]);

    /// <summary>The catalogue as the application runs with it, for whatever is written before its services exist.</summary>
    public static TenancyCatalogue Built { get; } = TenancyCatalogue.Build(Application, Permissions);
}
