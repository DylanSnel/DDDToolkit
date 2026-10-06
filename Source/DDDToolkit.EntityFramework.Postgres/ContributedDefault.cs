namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// Makes a contributed policy that lets a role read a table a default, which a rule of the application may take
/// the place of. Without such a rule the policy is written as it is. With one, a <c>[RowAccess]</c> rule about the
/// aggregate whose table the policy is on that allows <c>Read</c> to the same role, the script writes the rule in
/// its place, held to <paramref name="Within"/>, and allows <paramref name="Kept"/> beside it whatever the rule
/// says:
/// <code>
/// new ContributedPolicy(seats, "Members read the seats", "SELECT", RowAccessRoles.User, inTheTenant, null)
/// {
///     Default = new ContributedDefault(Kept: whatTheUseCasesLoad, Within: inTheTenant),
/// }
/// // with a rule of the application's: USING (whatTheUseCasesLoad OR (inTheTenant AND (the rule)))
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Who may see which rows is often the application's choice rather than the package's: one application shows every
/// member of a tenant everyone, another only the people of their own team. A package that has to choose once for
/// all of them offers its choice as a default, and keeps what its own work needs to read: the rows it loads to
/// carry out what it is asked, and the rows its other policies read in a subquery. A rule that narrows can then
/// take nothing away the package depends on, and a rule that widens stays within what the package keeps every
/// caller to.
/// </para>
/// <para>
/// A default is a permissive policy for <c>SELECT</c>, and what a role writes stays the contribution's: a script
/// refuses a default for another command. A rule that would replace it is refused unless it allows <c>Read</c>
/// and nothing else, and is for roles the contribution has a default for on that table; on a table the
/// contribution keeps to itself (<see cref="RowAccessContributionResult.ExclusiveTables"/>) every other rule is
/// refused as before. A rule there that names no role, <c>[RowAccess&lt;Seat&gt;(RowOperations.Read)]</c>, is
/// taken for the roles of the default, rather than for the signed-in and the anonymous caller as elsewhere, so the
/// developer writes it in its usual form and a role the table is closed to stays closed; the comment above its
/// policy says so. The rule replaces the default of the aggregate's own table alone: the tables of its entities
/// keep the policies of the contribution that keeps them, and a rule whose aggregate has an entity table no
/// contribution keeps is refused, since there it would allow reading and nothing else. Several rules for the same
/// role are OR-ed, as rules always are, and held to <paramref name="Within"/> together.
/// </para>
/// <para>
/// The script says what it did, in the comment above the policy, which names the rules, the default they took
/// the place of, and the contribution.
/// </para>
/// </remarks>
/// <param name="Kept">
/// What the role still reads, whatever the rule says, OR-ed with it: the rows the package's own work needs. SQL
/// as a policy's <c>Using</c> is, which may ask functions by <c>{fn:owner/name}</c> and the caller by
/// <c>{caller:...}</c>. <see langword="null"/> for nothing, and then the rule alone decides within
/// <paramref name="Within"/>.
/// </param>
/// <param name="Within">
/// What a rule that takes the default's place is held to, AND-ed with it: the caller's tenant, say. SQL as
/// <paramref name="Kept"/> is. <see langword="null"/> for nothing.
/// </param>
public sealed record ContributedDefault(string? Kept, string? Within);
