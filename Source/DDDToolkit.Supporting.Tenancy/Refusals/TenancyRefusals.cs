using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Every way Tenancy refuses a command, as codes a caller branches on, and the one place their kind and
/// English text are written down.
/// <para>
/// An argument a message shows is a value, such as a key or an id, never a word: the English and the Dutch
/// texts are whole sentences of their own. Where a client wants to say more, it reads the stable tokens the
/// arguments carry. A refusal of kind <see cref="RefusalKind.Invalid"/> that is about one input names it in
/// the argument <see cref="RefusalException.FieldArgument"/>, in the word the use cases call that input by,
/// such as <c>name</c> or <c>until</c>, so a form puts the text under the input; an edge whose input is
/// called otherwise maps the word. <c>tenancy.tenant-required</c> carries none, because the tenant is no
/// input of the command: a request names it beside the command, in a header or in its address.
/// </para>
/// <para>
/// The Dutch texts have no word for the reader. Dutch makes a writer choose between a familiar and a formal
/// word for "you", and that choice is the application's, so a Dutch sentence says what is the case or what is
/// needed and leaves the word out. An application that wants another tone, or other words, registers a resx
/// of its own in front of this one.
/// </para>
/// <para>
/// Each code is also the code of the nested invariant that states the same rule after the fact, where a rule
/// has both: the method refuses before anything changes, and the invariant is the net under code that got
/// round the method. <c>TenancyFailures.resx</c> holds the same English text, and its Dutch sibling a
/// translation, for <c>DDDToolkit.Localization</c> to phrase a refusal in the reader's language:
/// </para>
/// <code>
/// services.AddLocalization();
/// services.AddDDDToolkitLocalization(options =&gt; options.AddResource&lt;TenancyFailures&gt;());
/// </code>
/// </summary>
public static class TenancyRefusals
{
    // ---------------------------------------------------------------- who is asking

    /// <summary>The caller has no seat in the tenant it named.</summary>
    public const string NotSeated = "tenancy.not-seated";

    /// <summary>The caller named no tenant. It carries no field: the tenant is named beside the command, not in it.</summary>
    public const string TenantRequired = "tenancy.tenant-required";

    /// <summary>The caller's seat in the tenant is suspended.</summary>
    public const string SeatSuspended = "tenancy.seat-suspended";

    /// <summary>The tenant the caller named is not active.</summary>
    public const string TenantInactive = "tenancy.tenant-inactive";

    /// <summary>
    /// The caller does not hold the key the command needs where it needs it. Arguments: <c>Key</c> and <c>Unit</c>
    /// (the unit asked about, or <see langword="null"/> for the whole tenant). The message names only the key: a
    /// client that shows the unit finds it among the arguments.
    /// </summary>
    public const string NotPermitted = "tenancy.not-permitted";

    /// <summary>
    /// A seat would give or take away, at a unit, a role that manages access without holding there each of its
    /// keys that do, for at least as long as the grant runs, or suspend, deactivate or reactivate a seat that
    /// holds one; would give itself a role for longer than it holds <c>tenancy.grants.manage</c> there; would add
    /// or take out a key that manages access, or archive a role that holds one, without being an administrator;
    /// or would, by moving a unit, gain keys or hold one for longer, or give or take away from anyone a key that
    /// manages access it lacks at the unit for that long. Arguments: <c>Missing</c>, the keys it lacks there or
    /// lacks for long enough, and <c>Role</c>, the role refused, for all but a move.
    /// </summary>
    public const string GrantExceedsOwn = "tenancy.grant-exceeds-own";

    /// <summary>A seat would be placed by itself.</summary>
    public const string SelfAssignment = "tenancy.self-assignment";

    /// <summary>A seat would give itself a role that manages access. Argument: <c>Role</c>.</summary>
    public const string SelfAppointment = "tenancy.self-appointment";

    /// <summary>A change would write a row of another tenant.</summary>
    public const string OtherTenant = "tenancy.other-tenant";

    /// <summary>Only system work chooses when a grant starts.</summary>
    public const string StartSystemOnly = "tenancy.start-system-only";

    /// <summary>
    /// Only an operator of the application reads the tenants' directory, or sends a request that requires one
    /// (<c>TenancyAccess.RequiresOperator()</c>).
    /// </summary>
    public const string OperatorsOnly = "tenancy.operators-only";

    /// <summary>
    /// The invitation is for another address than the one the application says the accepting identity has. It
    /// names neither address.
    /// </summary>
    public const string AddressMismatch = "tenancy.address-mismatch";

    // ---------------------------------------------------------------- not found

    /// <summary>The unit is not one of the tenant's.</summary>
    public const string UnitNotFound = "tenancy.unit-not-found";

    /// <summary>The seat is not one of the tenant's, or belongs to another tenant.</summary>
    public const string SeatNotFound = "tenancy.seat-not-found";

    /// <summary>The role is not one of the tenant's, or belongs to another tenant.</summary>
    public const string RoleNotFound = "tenancy.role-not-found";

    /// <summary>The seat has no placement at the unit.</summary>
    public const string PlacementNotFound = "tenancy.placement-not-found";

    /// <summary>The seat was never granted the role at the unit, or it was revoked.</summary>
    public const string GrantNotFound = "tenancy.grant-not-found";

    /// <summary>
    /// No invitation has the token, or what was sent is not written as a token is. It is the one answer for
    /// both, and for an invitation of another tenant, so it says nothing about which tokens exist.
    /// </summary>
    public const string InvitationNotFound = "tenancy.invitation-not-found";

    // ---------------------------------------------------------------- conflicts

    /// <summary>After the change the tenant, which has an administrator now, would have none.</summary>
    public const string LastAdmin = "tenancy.last-admin";

    /// <summary>Another tenant has the slug. Argument: <c>Slug</c>.</summary>
    public const string SlugTaken = "tenancy.slug-taken";

    /// <summary>The person already has a seat in the tenant.</summary>
    public const string IdentityHasSeat = "tenancy.identity-has-seat";

    /// <summary>Another role of the tenant has the name, ignoring case. Argument: <c>Name</c>.</summary>
    public const string RoleNameTaken = "tenancy.role-name-taken";

    /// <summary>
    /// The tenant's status does not allow the change. Arguments: <c>Status</c>, the status in lowercase, and
    /// <c>Action</c>, one of <c>activate</c>, <c>suspend</c>, <c>reactivate</c>, <c>close</c> and
    /// <c>change-shape</c>.
    /// </summary>
    public const string TenantState = "tenancy.tenant-state";

    /// <summary>
    /// The seat's status does not allow the change. Arguments: <c>Status</c>, the status in lowercase, and
    /// <c>Action</c>, one of <c>suspend</c>, <c>reactivate</c>, <c>deactivate</c>, <c>place</c> and
    /// <c>grant</c>.
    /// </summary>
    public const string SeatState = "tenancy.seat-state";

    /// <summary>Something new would go to an archived unit. Argument: <c>Unit</c>.</summary>
    public const string UnitNotActive = "tenancy.unit-not-active";

    /// <summary>A move names the root.</summary>
    public const string RootImmovable = "tenancy.root-immovable";

    /// <summary>An archive names the root.</summary>
    public const string RootNotArchivable = "tenancy.root-not-archivable";

    /// <summary>The organization has no root, or more than one.</summary>
    public const string OneRoot = "tenancy.one-root";

    /// <summary>A move would hang a unit below itself.</summary>
    public const string Cycle = "tenancy.cycle";

    /// <summary>A move names the parent the unit has now.</summary>
    public const string SameParent = "tenancy.same-parent";

    /// <summary>The tree would have more levels than it may. Argument: <c>Max</c>.</summary>
    public const string DepthExceeded = "tenancy.depth-exceeded";

    /// <summary>An archive names a unit that still has active children.</summary>
    public const string UnitHasActiveChildren = "tenancy.unit-has-active-children";

    /// <summary>A rename, move or archive names an archived unit.</summary>
    public const string UnitArchived = "tenancy.unit-archived";

    /// <summary>A unit would be added to a flat tenant, which has its root alone.</summary>
    public const string FlatTenant = "tenancy.flat-tenant";

    /// <summary>A shape change other than flat to hierarchical.</summary>
    public const string ShapeChange = "tenancy.shape-change";

    /// <summary>A second placement of one seat at one unit.</summary>
    public const string DuplicatePlacement = "tenancy.duplicate-placement";

    /// <summary>A second primary placement of one seat.</summary>
    public const string SecondPrimary = "tenancy.second-primary";

    /// <summary>A second grant of one role at one placement.</summary>
    public const string DuplicateGrant = "tenancy.duplicate-grant";

    /// <summary>A grant names an archived role.</summary>
    public const string RoleNotActive = "tenancy.role-not-active";

    /// <summary>A change names an archived role.</summary>
    public const string RoleArchived = "tenancy.role-archived";

    /// <summary>The invitation's time ran out before it was accepted.</summary>
    public const string InvitationLapsed = "tenancy.invitation-lapsed";

    /// <summary>The invitation was accepted already, by somebody else.</summary>
    public const string InvitationUsed = "tenancy.invitation-used";

    /// <summary>The invitation was cancelled before it was accepted.</summary>
    public const string InvitationCancelled = "tenancy.invitation-cancelled";

    /// <summary>
    /// The invitation's state does not allow the change. Arguments: <c>State</c>, the state in lowercase, and
    /// <c>Action</c>, one of <c>cancel</c> and <c>accept</c>.
    /// </summary>
    public const string InvitationState = "tenancy.invitation-state";

    /// <summary>
    /// The seat that issued the invitation may no longer give what it offers: it is not active, or it lacks a
    /// key it needed to issue it, or holds one for too short a time. It names neither the seat nor the keys: the
    /// person it is told to is no member of the tenant.
    /// </summary>
    public const string InvitationUnbacked = "tenancy.invitation-unbacked";

    // ---------------------------------------------------------------- invalid input

    /// <summary>
    /// A name is blank, or a name or text is too long. Arguments: <c>What</c>, one of <c>tenant-name</c>,
    /// <c>unit-name</c>, <c>role-name</c>, <c>role-description</c> and <c>reason</c>; <c>Min</c> and <c>Max</c>,
    /// the lengths allowed; and <c>Field</c>, the input it is about: <c>name</c> for the name of a tenant, a unit or
    /// a role, <c>description</c> or <c>reason</c>. A seat has no name in Tenancy, so none is refused here.
    /// </summary>
    public const string NameInvalid = "tenancy.name-invalid";

    /// <summary>A slug does not follow the pattern. Argument: <c>Field</c>, which is <c>slug</c>.</summary>
    public const string InvalidSlug = "tenancy.invalid-slug";

    /// <summary>A grant would end at or before its start. Argument: <c>Field</c>, which is <c>until</c>.</summary>
    public const string InvalidPeriod = "tenancy.invalid-period";

    /// <summary>A tenant would be suspended or closed without a reason. Argument: <c>Field</c>, which is <c>reason</c>.</summary>
    public const string ReasonRequired = "tenancy.reason-required";

    /// <summary>
    /// Keys that are new to a role are unknown to the catalogue, or retired. Arguments: <c>Keys</c>, the keys
    /// refused, and <c>Field</c>, which is <c>keys</c>.
    /// </summary>
    public const string UnknownPermission = "tenancy.unknown-permission";

    /// <summary>A seat would have no verified identity. Argument: <c>Field</c>, which is <c>identity</c>.</summary>
    public const string IdentityRequired = "tenancy.identity-required";

    /// <summary>A role's keys are not expanded, or hold duplicates, or are out of order. Argument: <c>Field</c>, which is <c>keys</c>.</summary>
    public const string KeysNotNormalized = "tenancy.keys-not-normalized";

    /// <summary>
    /// A question by id asks about more ids than one question takes. Arguments: <c>Max</c>, and <c>Field</c>,
    /// which is <c>ids</c>.
    /// </summary>
    public const string TooManyIds = "tenancy.too-many-ids";

    /// <summary>
    /// A page of the tenants' directory would hold no tenant, or more than a page holds. Arguments: <c>Max</c>,
    /// and <c>Field</c>, which is <c>size</c>.
    /// </summary>
    public const string PageSizeInvalid = "tenancy.page-size-invalid";

    /// <summary>
    /// The marker a page of the tenants' directory continues from is not one the directory gave. Argument:
    /// <c>Field</c>, which is <c>after</c>.
    /// </summary>
    public const string CursorInvalid = "tenancy.cursor-invalid";

    /// <summary>
    /// An invitation is not for one address a message can be sent to. Arguments: <c>Max</c>, the most characters
    /// an address has, and <c>Field</c>, which is <c>address</c>.
    /// </summary>
    public const string AddressInvalid = "tenancy.address-invalid";

    /// <summary>
    /// An invitation would stay open for less or for longer than an invitation may. Arguments: <c>Min</c> and
    /// <c>Max</c>, in minutes, and <c>Field</c>, which is <c>lifetime</c>.
    /// </summary>
    public const string InvitationLifetime = "tenancy.invitation-lifetime";

    /// <summary>
    /// The grant an invitation offers would end before the invitation does, so it could be accepted for a role
    /// that is already over. Argument: <c>Field</c>, which is <c>grantUntil</c>.
    /// </summary>
    public const string InvitationGrantEndsFirst = "tenancy.invitation-grant-ends-first";

    /// <summary>
    /// The kind and English text of every code. The text is a template: <c>{Name}</c> is filled from the
    /// argument of that name, exactly as a translation in <c>TenancyFailures.nl.resx</c> is.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (RefusalKind Kind, string Template)> Table =
        new ReadOnlyDictionary<string, (RefusalKind Kind, string Template)>(new Dictionary<string, (RefusalKind Kind, string Template)>(StringComparer.Ordinal)
        {
            [NotSeated] = (RefusalKind.NotPermitted, "The tenant you asked for has no seat of yours."),
            [TenantRequired] = (RefusalKind.Invalid, "This request names no tenant."),
            [SeatSuspended] = (RefusalKind.NotPermitted, "Your seat in this tenant has been suspended."),
            [TenantInactive] = (RefusalKind.NotPermitted, "This tenant is not in use right now."),
            [NotPermitted] = (RefusalKind.NotPermitted, "You lack the permission {Key}."),
            [GrantExceedsOwn] = (RefusalKind.NotPermitted, "That reaches past what you hold yourself, there or for that long: {Missing}."),
            [SelfAssignment] = (RefusalKind.NotPermitted, "Someone else must place you."),
            [SelfAppointment] = (RefusalKind.NotPermitted, "Someone else must give you a role that manages access."),
            [OtherTenant] = (RefusalKind.NotPermitted, "That row belongs to a tenant you are not working in."),
            [StartSystemOnly] = (RefusalKind.NotPermitted, "A grant you make starts now; only system work picks another start."),
            [OperatorsOnly] = (RefusalKind.NotPermitted, "Only an operator can do this."),
            [AddressMismatch] = (RefusalKind.NotPermitted, "This invitation was sent to another address than this account's."),

            [UnitNotFound] = (RefusalKind.NotFound, "The unit was not found in this tenant."),
            [SeatNotFound] = (RefusalKind.NotFound, "The seat was not found in this tenant."),
            [RoleNotFound] = (RefusalKind.NotFound, "The role was not found in this tenant."),
            [PlacementNotFound] = (RefusalKind.NotFound, "The seat has no placement at that unit."),
            [GrantNotFound] = (RefusalKind.NotFound, "The seat does not hold that role at that unit."),
            [InvitationNotFound] = (RefusalKind.NotFound, "No invitation was found for this link."),

            [LastAdmin] = (RefusalKind.Conflict, "The tenant must keep at least one administrator."),
            [SlugTaken] = (RefusalKind.Conflict, "The slug {Slug} is in use by another tenant."),
            [IdentityHasSeat] = (RefusalKind.Conflict, "This person has a seat in the tenant already."),
            [RoleNameTaken] = (RefusalKind.Conflict, "The tenant has a role named {Name} already."),
            [TenantState] = (RefusalKind.Conflict, "The tenant's current status does not allow this."),
            [SeatState] = (RefusalKind.Conflict, "The seat's current status does not allow this."),
            [UnitNotActive] = (RefusalKind.Conflict, "Unit {Unit} is archived: nothing new goes there."),
            [RootImmovable] = (RefusalKind.Conflict, "The root has no parent, so it does not move."),
            [RootNotArchivable] = (RefusalKind.Conflict, "The root is never archived."),
            [OneRoot] = (RefusalKind.Conflict, "The tree has one root, no more and no fewer."),
            [Cycle] = (RefusalKind.Conflict, "A unit cannot hang below itself."),
            [SameParent] = (RefusalKind.Conflict, "The unit already hangs there."),
            [DepthExceeded] = (RefusalKind.Conflict, "The tree may have at most {Max} levels."),
            [UnitHasActiveChildren] = (RefusalKind.Conflict, "Archive the active units below it first."),
            [UnitArchived] = (RefusalKind.Conflict, "That unit is archived."),
            [FlatTenant] = (RefusalKind.Conflict, "A flat tenant has its root alone; make it hierarchical first."),
            [ShapeChange] = (RefusalKind.Conflict, "A tenant only goes from flat to hierarchical."),
            [DuplicatePlacement] = (RefusalKind.Conflict, "That seat has a placement at this unit already."),
            [SecondPrimary] = (RefusalKind.Conflict, "Only one of a seat's placements can be primary."),
            [DuplicateGrant] = (RefusalKind.Conflict, "That role is already granted at this placement."),
            [RoleNotActive] = (RefusalKind.Conflict, "An archived role cannot be granted."),
            [RoleArchived] = (RefusalKind.Conflict, "That role is archived."),
            [InvitationLapsed] = (RefusalKind.Conflict, "This invitation has run out. Ask for a new one."),
            [InvitationUsed] = (RefusalKind.Conflict, "Someone else has accepted this invitation."),
            [InvitationCancelled] = (RefusalKind.Conflict, "This invitation was cancelled."),
            [InvitationState] = (RefusalKind.Conflict, "The invitation's current state does not allow this."),
            [InvitationUnbacked] = (RefusalKind.Conflict, "Whoever sent this invitation may no longer give what it offers. Ask for a new one."),

            [NameInvalid] = (RefusalKind.Invalid, "Enter {Min} to {Max} characters."),
            [InvalidSlug] = (RefusalKind.Invalid, "A slug has 2 to 63 characters, each a lowercase letter, a digit or a dash, and does not begin with a dash."),
            [InvalidPeriod] = (RefusalKind.Invalid, "A grant's end must come after its start."),
            [ReasonRequired] = (RefusalKind.Invalid, "Give a reason to suspend or close a tenant."),
            [UnknownPermission] = (RefusalKind.Invalid, "These keys are unknown or retired: {Keys}."),
            [IdentityRequired] = (RefusalKind.Invalid, "A seat needs the verified identity of the person it belongs to."),
            [KeysNotNormalized] = (RefusalKind.Invalid, "A role's keys must be expanded, without duplicates, in ordinal order."),
            [TooManyIds] = (RefusalKind.Invalid, "Ask for at most {Max} ids at a time."),
            [PageSizeInvalid] = (RefusalKind.Invalid, "A page holds 1 to {Max} tenants."),
            [CursorInvalid] = (RefusalKind.Invalid, "That page marker does not belong to this list. Start again from the first page."),
            [AddressInvalid] = (RefusalKind.Invalid, "Enter one e-mail address of at most {Max} characters."),
            [InvitationLifetime] = (RefusalKind.Invalid, "An invitation stays open for {Min} to {Max} minutes."),
            [InvitationGrantEndsFirst] = (RefusalKind.Invalid, "The role would end before the invitation does. Pick a later end for the role."),
        });

    /// <summary>
    /// The input each code is about, where a code is always about the same one, in the word the use cases call
    /// it by. <see cref="Refuse"/> and <see cref="Failure"/> add it as the argument
    /// <see cref="RefusalException.FieldArgument"/>, so the place that refuses does not have to remember to.
    /// <para>
    /// <see cref="NameInvalid"/> is not here: which input it is about depends on which name it refuses, so
    /// <see cref="TenancyNames"/> names the field next to its <c>What</c>. <see cref="TenantRequired"/> is not
    /// here either, and never names one.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Fields =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [InvalidSlug] = "slug",
            [InvalidPeriod] = "until",
            [ReasonRequired] = "reason",
            [UnknownPermission] = "keys",
            [IdentityRequired] = "identity",
            [KeysNotNormalized] = "keys",
            [TooManyIds] = "ids",
            [PageSizeInvalid] = "size",
            [CursorInvalid] = "after",
            [AddressInvalid] = "address",
            [InvitationLifetime] = "lifetime",
            [InvitationGrantEndsFirst] = "grantUntil",
        });

    /// <summary>Every code, in the order the table lists them.</summary>
    public static IReadOnlyCollection<string> Codes { get; } = Table.Keys.ToArray();

    /// <summary>The kind of refusal a code is.</summary>
    /// <param name="code">One of the codes above.</param>
    /// <exception cref="ArgumentException">The code is not one of Tenancy's.</exception>
    public static RefusalKind KindOf(string code) => Row(code).Kind;

    /// <summary>
    /// The English text of a code, as a template: a placeholder such as <c>{Max}</c> is filled from the
    /// argument of that name.
    /// </summary>
    /// <param name="code">One of the codes above.</param>
    /// <exception cref="ArgumentException">The code is not one of Tenancy's.</exception>
    public static string TemplateOf(string code) => Row(code).Template;

    /// <summary>
    /// The refusal for <paramref name="code"/>, with its kind, its English message filled from
    /// <paramref name="arguments"/>, and the arguments themselves for a translation to use. A code that is
    /// always about the same input also carries that input's name, as the argument
    /// <see cref="RefusalException.FieldArgument"/>, unless <paramref name="arguments"/> name one themselves.
    /// </summary>
    /// <param name="code">One of the codes above.</param>
    /// <param name="arguments">The values the message names, by name.</param>
    /// <exception cref="ArgumentException">The code is not one of Tenancy's: a refusal nobody can look up is a bug.</exception>
    public static RefusalException Refuse(string code, params (string Name, object? Value)[] arguments)
    {
        var (kind, template) = Row(code);
        var named = Named(code, arguments);

        return new RefusalException(code, kind, Fill(template, named), named);
    }

    /// <summary>
    /// The arguments a value object's failure with <paramref name="code"/> carries: the input the code is
    /// about, so the failure names its field as the refusal and the invariant of the same code do.
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> ArgumentsOf(string code) => Named(code, []);

    /// <summary>
    /// What a nested invariant returns when the rule behind <paramref name="code"/> does not hold: the same
    /// English text as the refusal, and the same arguments, so one translation serves both.
    /// </summary>
    internal static InvariantFailure Failure(string code, params (string Name, object? Value)[] arguments)
    {
        var named = Named(code, arguments);
        return new InvariantFailure(Fill(TemplateOf(code), named), named);
    }

    private static (RefusalKind Kind, string Template) Row(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return Table.TryGetValue(code, out var row)
            ? row
            : throw new ArgumentException("'" + code + "' is not one of Tenancy's refusal codes.", nameof(code));
    }

    private static Dictionary<string, object?> Named(string code, (string Name, object? Value)[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var named = new Dictionary<string, object?>(arguments.Length + 1, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in arguments)
        {
            named[name] = value;
        }

        if (Fields.TryGetValue(code, out var field))
        {
            named.TryAdd(RefusalException.FieldArgument, field);
        }

        return named;
    }

    /// <summary>
    /// Fills <c>{Name}</c> placeholders, in the invariant culture because the English text is the domain's
    /// own and must read the same wherever it runs. A placeholder with no argument is left as written.
    /// </summary>
    private static string Fill(string template, IReadOnlyDictionary<string, object?> arguments)
    {
        if (template.IndexOf('{') < 0)
        {
            return template;
        }

        var result = new StringBuilder(template.Length + 16);
        var index = 0;
        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);
            var close = open < 0 ? -1 : template.IndexOf('}', open + 1);
            if (close < 0)
            {
                result.Append(template, index, template.Length - index);
                break;
            }

            result.Append(template, index, open - index);
            var name = template.Substring(open + 1, close - open - 1);
            if (arguments.TryGetValue(name, out var value))
            {
                result.Append(value switch
                {
                    null => string.Empty,
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString(),
                });
            }
            else
            {
                result.Append(template, open, close - open + 1);
            }

            index = close + 1;
        }

        return result.ToString();
    }
}
