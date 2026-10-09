using System.Globalization;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;
using DDDToolkit.Supporting.Membership;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects;

/// <summary>
/// Every way Projects refuses a command, as codes a client branches on, with the kind that decides the HTTP
/// status and the English text a client may show.
/// </summary>
/// <remarks>
/// The aggregate throws the ones that are about its own state (closed or open, a number, a name), and the
/// commands throw the ones that need the tenant. A crew's rules are the Membership package's, which refuses under
/// the codes of <see cref="Membership"/>: the package's rules under this module's own codes, so a client reads
/// <c>projects.already-on-crew</c> whichever of the two refused. The rules that are also nested invariants of
/// <see cref="Project"/> carry the same code, so a client reads one code whether the command refused or the
/// save's check caught code that went round the command.
/// <para>
/// An argument is a value, such as a key or an id, never a word, so a translation can be a sentence of its own.
/// The translations are beside this class, in the two resource files of <see cref="ProjectFailures"/>: the same
/// English, and Dutch.
/// </para>
/// </remarks>
public static class ProjectRefusals
{
    /// <summary>
    /// The project is not one the caller may see: it does not exist, belongs to another tenant, or is out of the
    /// caller's reach. One answer for all three, so a refusal never tells anyone a project exists.
    /// </summary>
    public const string NotFound = "projects.not-found";

    /// <summary>The caller may see the project but does not hold the key the command needs. Argument: <c>Key</c>.</summary>
    public const string NotPermitted = "projects.not-permitted";

    /// <summary>The project is closed, and a closed project does not change until it is reopened.</summary>
    public const string Closed = "projects.closed";

    /// <summary>The project is open, and only a closed project is reopened.</summary>
    public const string NotClosed = "projects.not-closed";

    /// <summary>Another project of the tenant has the number. Argument: <c>Number</c>.</summary>
    public const string NumberTaken = "projects.number-taken";

    /// <summary>
    /// The tenant has no project role in use made from the crew lead's starter role, so a project cannot be given
    /// an owner: the tenant was not set up. The package's rule <see cref="MembershipRefusals.NoOwnerRole"/>.
    /// </summary>
    public const string NoLeadRole = "projects.no-lead-role";

    /// <summary>
    /// The role cannot go on a crew: it is not one of the tenant's project roles in use, so a role of the
    /// organization, an archived project role and one of another tenant alike. Argument: <c>Role</c>. The
    /// package's rule <see cref="MembershipRefusals.RoleNotForMembers"/>.
    /// </summary>
    public const string RoleNotForMembers = "projects.role-not-for-members";

    /// <summary>The change would take the owner off the crew, or take the owner's lead role.</summary>
    public const string OwnerProtected = "projects.owner-protected";

    /// <summary>
    /// The seat is on the crew already, now or from a later start; a seat has one membership, which holds its
    /// roles. A membership that has ended is not in the way: the seat can be put on the crew again. Argument:
    /// <c>Seat</c>.
    /// </summary>
    public const string AlreadyOnCrew = "projects.already-on-crew";

    /// <summary>The seat holds the role on the crew already, now or from a later start. Arguments: <c>Seat</c>, <c>Role</c>.</summary>
    public const string CrewRoleHeld = "projects.crew-role-held";

    /// <summary>The seat is on the crew, without that role. Arguments: <c>Seat</c>, <c>Role</c>.</summary>
    public const string CrewRoleNotFound = "projects.crew-role-not-found";

    /// <summary>The seat owns the project already.</summary>
    public const string AlreadyOwner = "projects.already-owner";

    /// <summary>
    /// The seat is not on the project's crew, or its membership has ended, which a role would not bring back.
    /// Argument: <c>Seat</c>.
    /// </summary>
    public const string MemberNotFound = "projects.member-not-found";

    /// <summary>The seat is suspended or deactivated, or is not the tenant's. Argument: <c>Seat</c>.</summary>
    public const string SeatNotActive = "projects.seat-not-active";

    /// <summary>The unit is archived, or is not the tenant's. Argument: <c>Unit</c>.</summary>
    public const string UnitNotActive = "projects.unit-not-active";

    /// <summary>The name is blank or too long. Argument: <c>Max</c>.</summary>
    public const string NameInvalid = "projects.name-invalid";

    /// <summary>The number is blank or too long. Argument: <c>Max</c>.</summary>
    public const string NumberInvalid = "projects.number-invalid";

    /// <summary>The planned range lacks a day, or its last day is before its first.</summary>
    public const string PlannedRangeInvalid = "projects.planned-range-invalid";

    /// <summary>The marker a page of the project list was asked with is not a cursor that list gave.</summary>
    public const string CursorInvalid = "projects.cursor-invalid";

    /// <summary>A page of the project list was asked with a size out of range. Argument: <c>Max</c>.</summary>
    public const string PageSizeInvalid = "projects.page-size-invalid";

    /// <summary>A page of the project list was asked for by its first rows and by its last at once.</summary>
    public const string PageFromBothEnds = "projects.page-from-both-ends";

    /// <summary>A question about several projects named too many. Argument: <c>Max</c>.</summary>
    public const string TooManyIds = "projects.too-many-ids";

    /// <summary>A period on the crew ends at or before it starts. The package's rule <see cref="MembershipRefusals.InvalidPeriod"/>.</summary>
    public const string InvalidPeriod = "projects.invalid-period";

    /// <summary>There is no such project role in the tenant, or it is not one the caller may see: one answer for both.</summary>
    public const string RoleNotFound = "projects.role-not-found";

    /// <summary>The project role is archived: it changes no more. The package's rule <see cref="MembershipRefusals.RoleIsArchived"/>.</summary>
    public const string RoleArchived = "projects.role-archived";

    /// <summary>
    /// A project role's name is blank or too long, or its description is too long. Arguments: <c>Min</c>,
    /// <c>Max</c>, and the field. The package's rule <see cref="MembershipRefusals.RoleNameInvalid"/>.
    /// </summary>
    public const string RoleNameInvalid = "projects.role-name-invalid";

    /// <summary>Another project role of the tenant has the name. Argument: <c>Name</c>.</summary>
    public const string RoleNameTaken = "projects.role-name-taken";

    /// <summary>
    /// A project role was to give keys no crew role may give. Argument: <c>Keys</c>. The package's rule
    /// <see cref="MembershipRefusals.KeyNotForMembers"/>.
    /// </summary>
    public const string KeyNotForMembers = "projects.key-not-for-members";

    /// <summary>
    /// The crew lead's role, which every owner holds, is not archived. The package's rule
    /// <see cref="MembershipRefusals.OwnerRoleStays"/>.
    /// </summary>
    public const string OwnerRoleStays = "projects.owner-role-stays";

    /// <summary>
    /// A project role's keys, as stored, are not each there once, not blank and in order: only code that went
    /// round the role writes such a row. The package's rule <see cref="MembershipRefusals.RoleKeysNotNormalized"/>.
    /// </summary>
    public const string RoleKeysNotNormalized = "projects.role-keys-not-normalized";

    private static readonly Dictionary<string, (RefusalKind Kind, string Text)> Table = new(StringComparer.Ordinal)
    {
        [NotFound] = (RefusalKind.NotFound, "There is no such project, or it is not one you can see."),
        [NotPermitted] = (RefusalKind.NotPermitted, "Doing this to the project needs the key {Key}."),
        [Closed] = (RefusalKind.Conflict, "The project is closed; nothing about it changes until it is reopened."),
        [NotClosed] = (RefusalKind.Conflict, "The project is open; only a closed project can be reopened."),
        [NumberTaken] = (RefusalKind.Conflict, "Another project in this tenant already has the number {Number}."),
        [NoLeadRole] = (RefusalKind.Conflict, "The tenant has no active crew lead role to give a project's owner."),
        [RoleNotForMembers] = (RefusalKind.Invalid, "That role is not one of this tenant's project roles in use, so it cannot go on a crew."),
        [OwnerProtected] = (RefusalKind.Conflict, "The owner keeps the lead role on the crew until somebody else is named owner."),
        [AlreadyOnCrew] = (RefusalKind.Conflict, "That seat is on the crew already, or is due to join it. Give it a crew role instead."),
        [CrewRoleHeld] = (RefusalKind.Conflict, "That seat already has this role on the crew."),
        [CrewRoleNotFound] = (RefusalKind.NotFound, "That seat does not have this role on the crew."),
        [AlreadyOwner] = (RefusalKind.Conflict, "That seat owns the project already."),
        [MemberNotFound] = (RefusalKind.NotFound, "That seat is not on the project's crew, or its time on it has ended."),
        [SeatNotActive] = (RefusalKind.Conflict, "That seat is suspended or deactivated, or is not one of this tenant's."),
        [UnitNotActive] = (RefusalKind.Conflict, "That unit is archived, or is not one of this tenant's."),
        [NameInvalid] = (RefusalKind.Invalid, "A project's name is 1 to {Max} characters."),
        [NumberInvalid] = (RefusalKind.Invalid, "A project's number is 1 to {Max} characters."),
        [PlannedRangeInvalid] = (RefusalKind.Invalid, "A project's planned range has a first and a last day, and the last is not before the first."),
        [CursorInvalid] = (RefusalKind.Invalid, "That page marker does not belong to this list. Start again from the first page."),
        [PageSizeInvalid] = (RefusalKind.Invalid, "A page holds 1 to {Max} projects."),
        [PageFromBothEnds] = (RefusalKind.Invalid, "A page is the first of a list or the last of it. Ask with first or with last, not with both."),
        [TooManyIds] = (RefusalKind.Invalid, "Ask about at most {Max} projects at a time."),
        [InvalidPeriod] = (RefusalKind.Invalid, "A time on the crew ends after it starts."),
        [RoleNotFound] = (RefusalKind.NotFound, "There is no such project role in this tenant."),
        [RoleArchived] = (RefusalKind.Conflict, "That project role is archived: it changes no more, and goes to nobody."),
        [RoleNameInvalid] = (RefusalKind.Invalid, "Enter {Min} to {Max} characters."),
        [RoleNameTaken] = (RefusalKind.Conflict, "Another project role of this tenant is called {Name} already."),
        [KeyNotForMembers] = (RefusalKind.Invalid, "A project role cannot give these keys: {Keys}."),
        [OwnerRoleStays] = (RefusalKind.Conflict, "The role every project's owner holds is not archived."),
        [RoleKeysNotNormalized] = (RefusalKind.Conflict, "A project role's keys are each there once, none of them blank, in order."),
    };

    /// <summary>
    /// The codes a crew's rules refuse under, which are the Membership package's: each of the package's rules
    /// under this module's prefix, the ones a crew had before the package with the codes they had, and the seat
    /// a refusal is about named <c>Seat</c>. The project's member list refuses with these, and so do the access
    /// checks and the admission of the projects' rules.
    /// </summary>
    public static MembershipCodes Membership { get; } = MembershipCodes.Under("projects")
        .With(MembershipRefusals.AlreadyMember, Suffix(AlreadyOnCrew))
        .With(MembershipRefusals.RoleHeld, Suffix(CrewRoleHeld))
        .With(MembershipRefusals.RoleNotHeld, Suffix(CrewRoleNotFound))
        .With(MembershipRefusals.MemberNotActive, Suffix(SeatNotActive))
        .With(MembershipRefusals.NoOwnerRole, Suffix(NoLeadRole))
        .WithMemberArgument("Seat");

    /// <summary>Every code above: what a test holds the resource files of <see cref="ProjectFailures"/> to.</summary>
    public static IReadOnlyList<string> Codes { get; } = [.. Table.Keys];

    /// <summary>The refusal with <paramref name="code"/>, its kind and English text, and <paramref name="arguments"/>.</summary>
    /// <param name="code">One of the codes above.</param>
    /// <param name="arguments">The values the text shows, by name.</param>
    /// <exception cref="ArgumentException"><paramref name="code"/> is not one of Projects' codes.</exception>
    public static RefusalException Refuse(string code, params (string Name, object? Value)[] arguments)
    {
        var (kind, text) = TextOf(code);
        var named = Named(arguments);
        return new RefusalException(code, kind, Fill(text, named), named);
    }

    /// <summary>
    /// The English text of <paramref name="code"/> with its placeholders as written, such as <c>{Number}</c>: what
    /// a unique index is given, so the refusal it answers reads as the one a command throws.
    /// </summary>
    /// <param name="code">One of the codes above.</param>
    /// <exception cref="ArgumentException"><paramref name="code"/> is not one of Projects' codes.</exception>
    public static string TemplateOf(string code) => TextOf(code).Text;

    /// <summary>
    /// What a nested invariant of <see cref="Project"/> reports when the rule behind <paramref name="code"/> does
    /// not hold: the refusal's text and arguments, so one translation serves both.
    /// </summary>
    internal static InvariantFailure Failure(string code, params (string Name, object? Value)[] arguments)
    {
        var named = Named(arguments);
        return new InvariantFailure(Fill(TextOf(code).Text, named), named);
    }

    /// <summary>What follows the prefix of one of the codes above: <c>already-on-crew</c> of <c>projects.already-on-crew</c>.</summary>
    private static string Suffix(string code) => code[(code.IndexOf('.', StringComparison.Ordinal) + 1)..];

    private static (RefusalKind Kind, string Text) TextOf(string code)
        => Table.TryGetValue(code, out var row) ? row : throw new ArgumentException("'" + code + "' is not one of Projects' refusal codes.", nameof(code));

    private static Dictionary<string, object?> Named((string Name, object? Value)[] arguments)
    {
        var named = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in arguments)
        {
            named[name] = value;
        }

        return named;
    }

    /// <summary>Fills each <c>{Name}</c> in the invariant culture: the English text is the domain's own.</summary>
    private static string Fill(string text, IReadOnlyDictionary<string, object?> arguments)
    {
        foreach (var (name, value) in arguments)
        {
            text = text.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        return text;
    }
}
