using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// Settings a module puts on every connection next to the caller's role and claims, in the same statement,
/// so its policies and functions can read them with <c>current_setting('prefix.name', true)</c>: the team
/// a request chose, for one. Register one with
/// <see cref="DependencyInjection.AddRowLevelSecuritySettings{TSettings}"/>; it is a singleton, asked every
/// time a context opens a connection, and before every command whether the answer changed. Where the settings
/// last one transaction (<see cref="RowLevelSecurityScope.Transaction"/>) it is asked for every command outside
/// a transaction and for every transaction, and what it answers ends with that transaction.
/// <code>
/// public sealed class TeamSetting : IRowLevelSecuritySettings
/// {
///     public IReadOnlyCollection&lt;string&gt; Names { get; } = ["teams.current"];
///
///     public IEnumerable&lt;KeyValuePair&lt;string, string&gt;&gt; For(Caller caller)
///         => TeamScope.Current is { } team ? [new("teams.current", team.ToString())] : [];
/// }
/// </code>
/// </summary>
/// <remarks>
/// A setting is data the application vouches for: any statement on the application's connection can set it
/// to anything, as it can the role. SQL that reads one should treat it as the application's word, and
/// derive whatever it can from the caller's verified claims instead.
/// </remarks>
public interface IRowLevelSecuritySettings
{
    /// <summary>
    /// The settings this provider owns, each <c>prefix.name</c> in lower case letters, digits and
    /// <c>_</c>, and none of them PostgREST's <c>request.*</c>. Every one is set every time: to the value
    /// <see cref="For"/> gives it, or to <c>''</c> when it leaves it out, so a pooled connection never keeps
    /// an earlier caller's value. Read once, when the interceptor is built.
    /// </summary>
    IReadOnlyCollection<string> Names { get; }

    /// <summary>
    /// The values of this provider's settings for <paramref name="caller"/>, and for whatever else is
    /// current in this flow of work. A name it leaves out is <c>''</c>, and so is a value that is
    /// <see langword="null"/> after all; a name that is not one of <see cref="Names"/> fails the connection.
    /// </summary>
    /// <param name="caller">
    /// Who the connection is set for. A signed-in user whose token's role is on no list, in a host that has
    /// such a caller run as an anonymous one (<see cref="UnknownTokenRole.Anonymous"/>), is
    /// <see cref="Caller.Anonymous"/> here, as it is to the database.
    /// </param>
    IEnumerable<KeyValuePair<string, string>> For(Caller caller);
}
