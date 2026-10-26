namespace Examples.Tenancy.Host.DevLogin;

/// <summary>
/// The people of the demonstration. Their identities are fixed, so the seeded seats, the dev login's tokens
/// and, with a real Supabase stack, the users seeded there all name the same person.
/// </summary>
/// <remarks>
/// Every id of the demonstration follows one pattern, so an id in a log or a URL says what it is: its first
/// digit gives the kind, <c>d</c> for a person, and its last digits the number. See <see cref="Seeding.DemoData"/>.
/// </remarks>
public static class DemoPeople
{
    /// <summary>The first administrator of Harbor Works, who also runs its work.</summary>
    public static DemoPerson Ada { get; } = new("ada", new("d0000000-0000-4000-8000-000000000001"), "Ada",
        "Harbor Works' first administrator: Access admin at its root, and Area manager there too, so everything in harbor is hers to do. "
        + "Maud is an administrator as well, so either of them may step down, and not both.");

    /// <summary>An area manager, for North.</summary>
    public static DemoPerson Rhea { get; } = new("rhea", new("d0000000-0000-4000-8000-000000000002"), "Rhea",
        "Area manager of North at Harbor Works: everything below North, nothing outside it.");

    /// <summary>A project's owner, and the lead of its crew, in North Coast.</summary>
    public static DemoPerson Leo { get; } = new("leo", new("d0000000-0000-4000-8000-000000000003"), "Leo",
        "Placed in North Coast with no organization role; owns a project there and leads its crew: everything on it but moving it or naming another owner.");

    /// <summary>A surveyor in North Coast.</summary>
    public static DemoPerson Juno { get; } = new("juno", new("d0000000-0000-4000-8000-000000000004"), "Juno",
        "Placed in North Coast; a surveyor on one project's crew.");

    /// <summary>An observer whose organization role has expired.</summary>
    public static DemoPerson Vic { get; } = new("vic", new("d0000000-0000-4000-8000-000000000005"), "Vic",
        "Placed in North Inland, where his organization role has expired; an observer on one crew.");

    /// <summary>A suspended seat.</summary>
    public static DemoPerson Seth { get; } = new("seth", new("d0000000-0000-4000-8000-000000000006"), "Seth",
        "Placed in South Bay, and suspended: his seat gives him nothing.");

    /// <summary>A person with seats in two tenants.</summary>
    public static DemoPerson Tove { get; } = new("tove", new("d0000000-0000-4000-8000-000000000007"), "Tove",
        "A seat in Harbor Works, in South Bay, and the only administrator of Meadow Gardens, so the one who may not step down there.");

    /// <summary>Someone in the people office: she gives people their roles, and holds nothing else.</summary>
    public static DemoPerson Hana { get; } = new("hana", new("d0000000-0000-4000-8000-000000000008"), "Hana",
        "Holds nothing but grant management, at Harbor Works' root. Try it: Grant an org role gives Leo Surveyor at North Coast, "
        + "or herself Surveyor at Harbor Works, without holding their keys. She gives others the People office, whose one key "
        + "that manages access she holds, but not Crew lead, Area manager or Access admin, and never herself a role that manages access.");

    /// <summary>An access admin: she runs who may do what in Harbor Works, and does none of its work.</summary>
    public static DemoPerson Maud { get; } = new("maud", new("d0000000-0000-4000-8000-000000000009"), "Maud",
        "Holds nothing but Access admin, at Harbor Works' root: she gives every role, names owners, manages every crew and sees every project. "
        + "She renames, closes and records on none of them: try renaming Pier 7. Put on its crew as a Crew lead, which she may do herself, she renames it.");

    /// <summary>One of the application's own staff: she has no seat in any tenant, and looks across all of them.</summary>
    public static DemoPerson Orla { get; } = new("orla", new("d0000000-0000-4000-8000-000000000101"), "Orla",
        "Works for the application, not for a tenant: with an operator's token she lists every tenant, reads their projects, "
        + "inspections and access history, and changes nothing. She has no seat, so every route inside a tenant refuses her.");

    /// <summary>
    /// Everyone with a seat, in the order the login page shows them. A seat's number is its person's place here,
    /// so a person is added at the end.
    /// </summary>
    public static IReadOnlyList<DemoPerson> All { get; } = [Ada, Rhea, Leo, Juno, Vic, Seth, Tove, Hana, Maud];

    /// <summary>
    /// The people without a seat, who are there to sign in as an operator. Kept apart from <see cref="All"/>: the
    /// seeding gives nobody here a seat, and a seat's number follows that list.
    /// </summary>
    public static IReadOnlyList<DemoPerson> WithoutASeat { get; } = [Orla];

    /// <summary>The person with this key, with a seat or without, ignoring case, or <see langword="null"/>.</summary>
    public static DemoPerson? Find(string? key)
        => All.Concat(WithoutASeat).FirstOrDefault(person => string.Equals(person.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));
}
