namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// An SQL function an <see cref="IRowAccessContribution"/> writes, created in the schema of the context it
/// answered for as <c>LANGUAGE sql</c> with <c>SET search_path = ''</c>, unless it is <see cref="Inlinable"/>,
/// so its body names every table and function with its schema:
/// <code>
/// new ContributedFunction("entries_i_wrote", "", "SETOF uuid",
///     $"SELECT {RowAccessModel.Column(entries, "Id")} FROM {RowAccessModel.Table(entries)} WHERE {RowAccessModel.Column(entries, "WrittenBy")} = {{caller:uid}}",
///     SecurityDefiner: true, GrantTo: [RowAccessRoles.User])
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Rules and other contributions ask it by its logical name, <c>owner/name</c> with the contribution's
/// <see cref="IRowAccessContribution.Owner"/>, and the script writes that as the name it has in the database.
/// It carries the comment of the context's access functions, so the context's next script keeps it while the
/// contribution still writes it, replacing it in place, and drops it once it does not. Postgres cannot change
/// a function's parameters or what it returns in place, so the Supabase export refuses a change of either
/// against the context's newest access file: give such a function a new name.
/// </para>
/// <para>
/// <see cref="GrantTo"/> is who may execute it, besides its owner: the script revokes it from <c>PUBLIC</c>,
/// takes back every other grant on it, what a schema's default privileges gave included, and grants it to
/// those roles. A policy for any other role that asks it, itself or through an access function, which runs
/// as its owner, is refused, naming the rule and the function, so a function meant for signed-in users stays
/// out of anonymous callers' reach even where a rule without <c>To</c> asks it.
/// </para>
/// <para>
/// A function that answers rows for other queries to read, as a view would, can be <see cref="Inlinable"/>:
/// Postgres then plans a query over it as a query over the tables it reads, with their indexes and with the
/// caller's policies on them, rather than running it first and narrowing its rows afterwards.
/// </para>
/// </remarks>
/// <param name="Name">The function's name, without a schema: letters, digits and underscores.</param>
/// <param name="Parameters">Its parameters as its signature spells them, <c>key text</c>; empty for none.</param>
/// <param name="Returns">
/// What it returns: <c>boolean</c>, <c>uuid</c>, <c>SETOF uuid</c>, <c>timestamp with time zone</c>, or rows
/// of named columns, <c>TABLE ("SeatId" uuid, "EndsAt" timestamp with time zone)</c>; a type, or the columns'
/// names and types, and nothing else, since it is written into the function as it is.
/// </param>
/// <param name="Body">
/// Its body, one SQL statement, which may ask functions by <c>{fn:owner/name}</c> and the caller by
/// <c>{caller:uid}</c>, <c>{caller:role}</c>, <c>{caller:claims}</c>, <c>{caller:signedin}</c> and
/// <c>{caller:claim:path}</c>; <c>{{</c> and <c>}}</c> are braces.
/// </param>
/// <param name="SecurityDefiner">
/// Whether it runs as its owner, the role that ran the migrations, rather than as the caller, which lets it
/// read tables the caller's policies would hide. False by default.
/// </param>
/// <param name="GrantTo">
/// The roles that may execute it, symbolic ones such as <c>RowAccessRoles.User</c> or roles' own names;
/// null or empty for none, which leaves it to the functions that run as their owner, access functions
/// among them.
/// </param>
/// <param name="Volatility">
/// <c>STABLE</c>, the default, for a function that reads the database; <c>IMMUTABLE</c> for one whose answer
/// depends on its arguments alone; or <c>VOLATILE</c>.
/// </param>
/// <param name="Inlinable">
/// Whether the planner may fold the function into the query that calls it, as it folds a view: the script writes
/// it with no <c>SET</c> clause, which Postgres will not fold a function with. Only for a function that does not
/// run as its owner, is <c>STABLE</c> or <c>IMMUTABLE</c>, and whose body is one <c>SELECT</c>; anything else is
/// refused. It then runs with the caller's search path rather than an empty one, so its body names every table,
/// function and type with its schema, as every contributed function's must: nothing in it is then looked up
/// along that path. False by default.
/// </param>
/// <param name="Answers">
/// The set it answers for a resource, for the rules that ask that set by the resource's id through a
/// <c>[ResourceAccessContract&lt;TKey&gt;]</c> rather than by this function's name; null for none. See
/// <see cref="ResourceAccessAnswer"/>.
/// </param>
public sealed record ContributedFunction(
    string Name,
    string Parameters,
    string Returns,
    string Body,
    bool SecurityDefiner = false,
    IReadOnlyList<string>? GrantTo = null,
    string Volatility = "STABLE",
    bool Inlinable = false,
    ResourceAccessAnswer? Answers = null);
