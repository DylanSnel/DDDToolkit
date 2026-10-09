using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Tenancy.Postgres;

/// <summary>
/// Tenancy's row level security, written from each context's model when the policies are exported: the SQL
/// functions rules ask (<see cref="TenancyRowAccess"/>), the policies on Tenancy's own tables and on every table
/// a module keeps to a tenant, and the triggers that check at commit what no policy can see. This package declares
/// it with <c>[assembly: RowAccessContribution]</c>, so the Supabase export of every application that references the
/// package writes it, and the application writes nothing for it but, where it has them, two marks:
/// <code>
/// [TenancyCatalogue]
/// public static ApplicationCatalogue Application { get; } = new(Packs: [...]);
///
/// [TenancyOperators]
/// public static IReadOnlyList&lt;string&gt; OperatorTokenRoles { get; } = ["operator"];
/// </code>
/// The catalogue is the one the application runs with, the application's part and every module's keys: its
/// marks decide which roles manage access, its <see cref="ApplicationCatalogue.ContainAccessManagingKeys"/> whether
/// the policies contain the grants of those roles, and its packs which roles a settings manager may add. When any of
/// them changes, a key added to a pack or to the administrators' pack included, the next export writes the access
/// file again. The export makes the class before the application starts, so it builds the catalogue as the host's
/// registration does, without the application's services: from the member marked
/// <see cref="TenancyCatalogueAttribute"/>, or <c>new ApplicationCatalogue()</c> where none is, and the lists every
/// module the exporting project references marks with <see cref="TenancyPermissionsAttribute"/>
/// (<see cref="TenancyRowAccessContribution(IEnumerable{IEnumerable{Permission}}, ApplicationCatalogue?, IReadOnlyCollection{string}?)"/>).
/// A script written by hand, or a test, makes it with the catalogue built
/// (<see cref="TenancyRowAccessContribution(TenancyCatalogue, IReadOnlyCollection{string}?)"/>).
/// </summary>
/// <remarks>
/// <para>
/// For the context that maps Tenancy's tables, in its default schema: the functions <c>caller_seat</c>,
/// <c>caller_tenant</c>, <c>system_tenant</c>, <c>units_where_i_hold</c>, <c>readable_units</c>,
/// <c>seats_in_my_units</c>, <c>roles_with_key</c>, <c>holds_key</c>, <c>holds_tenant_wide</c>,
/// <c>identity_tenants</c>, <c>unit_parent</c>,
/// <c>manages_access</c>, <c>key_is_contained</c> and <c>pack_keys</c>, executable by signed-in users and by system
/// work in a tenant; the
/// policies on Tenancy's tables, which only this contribution writes; and the triggers that keep an administrator
/// in every active or suspended tenant, every right backed by a grant, the closure of the tree exact, and fixed
/// what a seat, a placement and a grant are about.
/// </para>
/// <para>
/// Who reads the seats, and who besides the seats managers at its unit reads an invitation, are defaults
/// (<see cref="ContributedDefault"/>), since what people may see of each other is the application's choice: a
/// <c>[RowAccess]</c> rule of the application's on its seat class, or on its invitation class, that allows
/// <c>Read</c> to signed-in users takes the place of the default, held to the calling seat's tenant. What
/// Tenancy's own work reads stays whatever the rule says: a person's own seats, every seat of the tenant to a seat
/// that manages seats, grants or units anywhere or roles for the whole tenant, and an invitation to the seats
/// managers at its unit. The functions that answer about the caller and about rights run as their owner and are no
/// read of a table, so a rule changes nothing they answer; <c>tenant_seats</c>, which runs as its caller, answers
/// the seats the rule lets the caller read. Every other read, and every write, stays this contribution's.
/// </para>
/// <para>
/// The database keeps the rights: a trigger on the grants, the seats and the roles writes each seat's rights as
/// they change, from <c>key_is_live</c>, which says which keys the catalogue has live, and no caller writes one.
/// A seat reads its own rights and no other seat's; a grant is read by its seat, by the seats that manage grants,
/// seats or units at its unit, held there or above it, and by the seats that manage roles for the whole tenant. What
/// a seat may learn of other seats' rights, three functions answer, executable by signed-in users alone, as ids,
/// keys and dates: <c>tenant_administrators</c> and <c>seats_holding_at</c> each right whose grant the seat may read,
/// and <c>rights_a_move_changes</c> to a seat that manages units at both parents of the move, of another seat's
/// rights only those the move changes, which reach one parent and not the other. Tenancy's own
/// system work in a tenant writes that tenant's rights again with <c>rewrite_tenant_rights</c>, after rows were
/// written past the trigger.
/// </para>
/// <para>
/// A module reads Tenancy through six more functions, one for each row of the read model, which its context maps
/// with <c>AddTenancyReadFunctions</c>: <c>caller_rights</c>, <c>tenant_unit_paths</c>, <c>tenant_units</c>,
/// <c>tenant_roles</c>, <c>tenant_placements</c> and <c>tenant_seats</c>. They are executable by signed-in users
/// and by system work in a tenant, run as their caller, so the policies on Tenancy's tables decide their rows, and
/// are written for Postgres to fold into the query that asks them: a module's question is planned over the
/// tables, with their indexes. They answer under column names of their own and a status as the name of its enum
/// member, whatever the application calls Tenancy's tables and columns and however it stores them. They answer
/// access facts only: ids, keys, periods, statuses, a unit's parent, and a role's pack and keys, and never what a
/// seat, a unit or a role is called. A start-up check compares the columns each answers with the read model's
/// (<see cref="TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync"/>).
/// </para>
/// <para>
/// What has to be read before any tenant is known, three functions answer, to system work alone, which asks them
/// in no tenant, where the policies show it no row: <c>role_keys_in_use</c>, the keys stored on every tenant's
/// roles, and <c>seats_of_identity</c>, the seats a person has in every tenant, to Tenancy's own work and to no
/// other scope's; and <c>tenants_to_sweep</c>, the ids of the active and the suspended tenants, to the work of
/// every module, which then visits each tenant under its own scope. So nothing of Tenancy's runs as the
/// application itself, past the policies.
/// </para>
/// <para>
/// Five questions take the tenant as an argument instead of reading it from the connection, for a policy that
/// runs where the application's connection is not the one asking, on the path of a stored file or on a
/// channel: <c>seat_in_tenant</c>, <c>seated_in_tenant</c>, <c>holds_key_in_tenant</c>,
/// <c>units_where_i_hold_in_tenant</c> and <c>roles_with_key_in_tenant</c>, executable by signed-in users alone.
/// Each answers for the seat the verified identity has in that tenant, while that seat and the tenant are
/// active, and nothing to anyone else: naming a tenant says nothing of it to someone who has no seat there.
/// </para>
/// <para>
/// For every context, the tables of the entities it keeps to a tenant with <c>ScopeToTenant</c> get row level
/// security, restrictive policies that keep a signed-in user to the calling seat's tenant and system work to its
/// own, and that let anonymous callers reach nothing, and a policy that lets system work of any scope read and
/// write its tenant.
/// What a signed-in user may do inside the tenant is the module's rules' to say; without one, nothing. A rule for
/// a role of the application's own, named by its database name rather than as a signed-in user, system work or an
/// anonymous caller, is not kept to a tenant: a contribution does not see the rules, so that rule must name the
/// tenant itself.
/// </para>
/// <para>
/// A token role the host mapped to a database role of its own, <see cref="RowAccessRoleNames.TokenRoles"/> of the
/// export, holds no seat: Tenancy's tables and every table kept to a tenant are closed to it by a restrictive
/// policy, as they are to anonymous callers, so a module's rule for <c>RowAccessRoles.Token(...)</c> on such a
/// table lets nothing through. A token role mapped to the role of a signed-in user is that role, kept to the
/// calling seat's tenant like any other user.
/// </para>
/// <para>
/// The token roles of the application's operators, given to the constructor, are the exception: staff who look
/// across tenants and hold no seat. The database role of each reads every row of Tenancy's tables, of the tables
/// of the application's entities on them and of Tenancy's access history, in every tenant, and writes none of
/// them. On a module's table kept to a tenant its restrictive policies let it read and nothing else, so it reads
/// there what a rule of the module for <c>RowAccessRoles.Token(...)</c> admits, and never writes, whatever a rule says. What
/// an operator asks for is carried out by system work that names it. An operator's token role is mapped to a
/// database role of its own, which no other token role shares: one that is not is refused when the policies are
/// written, since the policies would then open every tenant to callers who are no operators.
/// </para>
/// <para>
/// A table whose entity keeps who changed its rows (<c>RecordsWhoChanged</c>) gets a trigger that holds callers
/// to it: a signed-in user writes its own seat and no other as who wrote or changed the row, the application's
/// own work never writes a seat's kind, and neither changes who wrote the row first. A role that is no caller's,
/// the tables' owner in a migration say, is not held.
/// </para>
/// <para>
/// Tenancy's access history, where the context maps one with <c>AddTenancyEventLogTable</c>, is kept to this
/// contribution like Tenancy's tables: a person adds rows about their own seat, the calling one, in the tenant
/// the connection names, and a seat reads its tenant's rows with <c>tenancy.history.view</c> held for the whole tenant; system
/// work reads its tenant's rows, and adds to them in Tenancy's own scope; and where the export writes privileges
/// and the host has a role for its own bookkeeping, that role reads and removes rows, which the table's guard
/// lets go only once they are old enough.
/// </para>
/// <para>
/// Invitations, where the context maps them with <c>AddTenancyInvitations</c>, are kept to this contribution as
/// well. An invitation is read by the seats that manage seats at its unit; added by a seat that could add the
/// seat and make the grant itself, open and as its own; and changed by a seat to cancelled and nothing else,
/// while a trigger keeps what it offers as it was issued, for every role. The digest of an invitation's token is
/// in a table of its own that no caller's role reads, an operator's included: whoever issues an invitation adds
/// its row in the same transaction, and <c>invitation_of_digest</c> answers which invitation a digest is for, as
/// ids, to Tenancy's own system work in no tenant, which is how accepting one finds the tenant its token names.
/// </para>
/// <para>
/// A signed-in user is found as the active seat of the verified identity in the tenant the connection names,
/// in an active tenant; the tenant is the application's word and cannot reach a seat the identity does not
/// have. This is a second lock behind the application, against its own queries that forget a condition, not
/// against SQL an attacker runs on its connection, which can switch roles and set anything.
/// </para>
/// </remarks>
public class TenancyRowAccessContribution : IRowAccessContribution
{
    /// <summary>What was written for each model, per set of token roles the export maps.</summary>
    private readonly ConditionalWeakTable<IModel, ConcurrentDictionary<string, Answer>> _answers = new();

    /// <summary>
    /// The contribution as the Supabase export makes it, in the project that runs the export, from what the
    /// application marks: the catalogue built from <paramref name="application"/> and <paramref name="modules"/>, as
    /// the host's registration builds it from <c>TenancyOptions.Catalogue</c> and the modules' keys it registers.
    /// </summary>
    /// <param name="modules">
    /// Every list of keys a module marks with <see cref="TenancyPermissionsAttribute"/>, as the host registers them
    /// with <c>AddTenancyPermissionsOfModules()</c>: those of every project the exporting project references.
    /// </param>
    /// <param name="application">
    /// The application's part of the catalogue, the member it marks with <see cref="TenancyCatalogueAttribute"/>;
    /// <c>new ApplicationCatalogue()</c> when it marks none, as when <c>TenancyOptions.Catalogue</c> is left unset.
    /// </param>
    /// <param name="operatorTokenRoles">
    /// The token roles of the application's operators, the member it marks with <see cref="TenancyOperatorsAttribute"/>;
    /// none when it marks none, and then nothing is written for operators.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="modules"/> is null.</exception>
    /// <exception cref="TenancyCatalogueException">The catalogue does not hold together; every problem found is listed.</exception>
    /// <exception cref="ArgumentException">An operator token role has no name.</exception>
    public TenancyRowAccessContribution(
        [FromApplication(typeof(TenancyPermissionsAttribute), Every = true)] IEnumerable<IEnumerable<Permission>> modules,
        [FromApplication(typeof(TenancyCatalogueAttribute))] ApplicationCatalogue? application = null,
        [FromApplication(typeof(TenancyOperatorsAttribute))] IReadOnlyCollection<string>? operatorTokenRoles = null)
        : this(BuiltFrom(application, modules), operatorTokenRoles)
    {
    }

    /// <summary>A contribution for an application whose catalogue is <paramref name="catalogue"/>, as a script written by hand makes it.</summary>
    /// <param name="catalogue">
    /// The catalogue the application runs with, built as its registration builds it: which keys are live, which
    /// of them manage access, and whether those stay contained.
    /// </param>
    /// <param name="operatorTokenRoles">
    /// The token roles of the application's operators, as <c>TenancyOptions.OperatorTokenRoles</c> lists them:
    /// each is given policies that let its database role read every tenant and write nothing. None when left
    /// out, and then nothing is written for operators.
    /// <code>
    /// var tenancy = new TenancyRowAccessContribution(TenancyCatalogue.Build(ShopCatalogue.Application, TenancyPermissionsOfModules.All), ["operator"]);
    /// </code>
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="catalogue"/> is null.</exception>
    /// <exception cref="ArgumentException">An operator token role has no name.</exception>
    public TenancyRowAccessContribution(TenancyCatalogue catalogue, IReadOnlyCollection<string>? operatorTokenRoles = null)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        Catalogue = catalogue;

        // Each once, in order; RowAccessRoles.Token refuses one without a name.
        OperatorTokenRoles = [.. (operatorTokenRoles ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        foreach (var tokenRole in OperatorTokenRoles)
        {
            RowAccessRoles.Token(tokenRole);
        }
    }

    /// <summary>The catalogue the functions are written from.</summary>
    public TenancyCatalogue Catalogue { get; }

    /// <summary>The catalogue of <paramref name="application"/> and the modules' keys, one list after the other.</summary>
    private static TenancyCatalogue BuiltFrom(ApplicationCatalogue? application, IEnumerable<IEnumerable<Permission>> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        return TenancyCatalogue.Build(application ?? new ApplicationCatalogue(), [.. modules.SelectMany(keys => keys)]);
    }

    /// <summary>The token roles of the application's operators, each once, in ordinal order.</summary>
    public IReadOnlyList<string> OperatorTokenRoles { get; }

    /// <inheritdoc />
    public string Owner => TenancyRowAccess.Owner;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="export"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The context maps some of Tenancy's tables but not all of them, stores a role's keys in neither an array nor
    /// a JSON array, keeps a type to a tenant in a table of its own apart from the type it derives from, or maps
    /// Tenancy's access history without Tenancy's tables; or an operator's token role is mapped to the role of a
    /// signed-in user, or to a role another token role shares.
    /// </exception>
    public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(export);

        // Asked several times per export, and the model of a context type is built once: written once per model
        // and per everything of the export the SQL is written from, which an export keeps for all of its contexts.
        var written = WrittenWith(export);
        return _answers.GetValue(context.Model, _ => new ConcurrentDictionary<string, Answer>(StringComparer.Ordinal))
            .GetOrAdd(written.Key, _ => new Answer(TenancySql.For(context, Catalogue, written)))
            .Result;
    }

    /// <summary>
    /// What of <paramref name="export"/> Tenancy's SQL is written from: the mapped token roles, apart as those the
    /// tables are closed to and those of operators, the names of the callers' roles, and whether the host's
    /// bookkeeping role gets the history's old rows.
    /// </summary>
    /// <remarks>
    /// A token role is named as a policy names it, <c>RowAccessRoles.Token(...)</c>, and each database role once:
    /// two token roles mapped to one role are that one role, named by the first of them. A token role mapped to
    /// the role of a signed-in user is that role, and gets no policy of its own: a restrictive one would close
    /// the tables to every seat as well. An operator's token role the host did not map is named all the same, so
    /// the script refuses it, naming this contribution.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// An operator's token role is mapped to the role of a signed-in user, or to a role a token role that is no
    /// operator's is mapped to as well.
    /// </exception>
    private TenancySql.Written WrittenWith(RowAccessExport export)
    {
        var roles = export.Roles;
        var closed = new List<string>();
        var operators = new List<string>();

        foreach (var sharing in roles.TokenRoles
                     .GroupBy(mapped => mapped.Value, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            List<string> tokenRoles = [.. sharing.Select(mapped => mapped.Key).Order(StringComparer.Ordinal)];
            List<string> ofOperators = [.. tokenRoles.Where(tokenRole => OperatorTokenRoles.Contains(tokenRole, StringComparer.Ordinal))];
            var isUsers = string.Equals(sharing.Key, roles.User, StringComparison.Ordinal);

            if (ofOperators.Count > 0 && isUsers)
            {
                throw new InvalidOperationException(
                    $"The operator token role '{ofOperators[0]}' is mapped to '{sharing.Key}', the role of a signed-in user: the policies that let an operator read every tenant would let every seat. " +
                    "Map it to a database role of its own, in PostgresRowLevelSecurityOptions.TokenRoles, or with 'token:" + ofOperators[0] + "=<role>' in SupabaseRowAccessRoles.");
            }

            if (ofOperators.Count > 0 && ofOperators.Count < tokenRoles.Count)
            {
                var other = tokenRoles.First(tokenRole => !ofOperators.Contains(tokenRole));
                throw new InvalidOperationException(
                    $"The operator token role '{ofOperators[0]}' and the token role '{other}', which is no operator's, are both mapped to '{sharing.Key}': the policies that let an operator read every tenant " +
                    $"would let the holders of '{other}' as well. Map each to a database role of its own, or list '{other}' among the operators' token roles too.");
            }

            if (!isUsers)
            {
                (ofOperators.Count > 0 ? operators : closed).Add(RowAccessRoles.Token(tokenRoles[0]));
            }
        }

        // Not mapped at all: no query runs as it, and the script says so when it meets the policy.
        operators.AddRange(OperatorTokenRoles.Where(tokenRole => !roles.TokenRoles.ContainsKey(tokenRole)).Select(RowAccessRoles.Token));

        return new TenancySql.Written(
            closed,
            operators,
            roles.Resolve(RowAccessRoles.User),
            roles.Resolve(RowAccessRoles.SystemIn),
            RemovesOldHistory: export.WriteGrants && roles.System is not null);
    }

    /// <summary>What was written for one model, <see langword="null"/> included.</summary>
    private sealed record Answer(RowAccessContributionResult? Result);
}
