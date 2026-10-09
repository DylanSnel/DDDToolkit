using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// Row level security a package or a module writes for itself: SQL functions, policies and statements,
/// written from each context's Entity Framework model, which a script writes next to the policies of the
/// <c>[RowAccess]</c> rules. It covers what rules cannot: tables that are not aggregates, and SQL whose names
/// depend on the application's model, such as the schema and the column types of its ids.
/// <code>
/// [assembly: RowAccessContribution(typeof(AuditRowAccess))]   // in the package: every application that references it writes it
///
/// public sealed class AuditRowAccess : IRowAccessContribution
/// {
///     public string Owner => "audit";
///
///     public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
///         => context.Model.FindEntityType(typeof(AuditEntry)) is { } entries
///             ? new([], [new ContributedPolicy(entries, "Entries are read by who wrote them", "SELECT", RowAccessRoles.User,
///                   $"{RowAccessModel.Column(entries, nameof(AuditEntry.WrittenBy))} = {{caller:uid}}", null)], [])
///             : null;
/// }
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// A script asks every contribution its <see cref="RowAccessExport.Contributions"/> holds, for every context
/// it writes; the Supabase build hands it those of the packages the host references, which declare themselves
/// contributors with <c>[assembly: RowAccessContribution]</c>, and the host's own, which it lists with
/// <c>[assembly: UseRowAccessContribution]</c>.
/// What a contribution answers for a context goes into that context's script: its functions with the access
/// functions, in the order they ask each other; its policies with the rules', a permissive one merged with
/// theirs where it is for the same table, command and role; and its statements last.
/// </para>
/// <para>
/// SQL a contribution writes may ask functions by <c>{fn:owner/name}</c>, its own or another module's, and
/// the caller by <c>{caller:uid}</c>, <c>{caller:role}</c>, <c>{caller:claims}</c>, <c>{caller:signedin}</c>
/// and <c>{caller:claim:path}</c>, which the script fills in as it fills a rule's; <c>{{</c> and <c>}}</c> are
/// braces. The script learns from the <c>{fn:...}</c> places which functions a context asks, so it writes
/// the context that defines a function first.
/// </para>
/// <para>
/// The Supabase build makes a contribution in the project that runs the export, before the application
/// starts: one the host lists as <c>new X()</c>, and a package's in a class it writes there, from what the
/// application marks. A package's contribution whose SQL depends on what only the application knows, such as a
/// list of its own, takes it in its constructor, each parameter saying with <c>[FromApplication]</c> which
/// attribute the application marks the member with; a generic one is made once for each member marked with a
/// generic attribute, closed over its type arguments.
/// </para>
/// </remarks>
public interface IRowAccessContribution
{
    /// <summary>
    /// The owner of the functions it defines, lower case letters, digits and <c>-</c>: their logical names are
    /// <c>owner/name</c>, which rules and other contributions ask them by.
    /// </summary>
    string Owner { get; }

    /// <summary>
    /// What this contribution writes for <paramref name="context"/>, or <see langword="null"/> when the
    /// context is none of its business. It may be asked about one context several times in one export, to
    /// learn the names of its functions, what it asks and what it writes, and again whenever the export is
    /// run, so the answer is written from the model and what the contribution was made with, the same every
    /// time and cheaply.
    /// </summary>
    /// <param name="context">The context whose script is being written; it is never opened.</param>
    /// <param name="export">What the script is written with: the roles and the caller functions.</param>
    RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export);
}
