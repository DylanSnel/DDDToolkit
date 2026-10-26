namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// The token roles of the application's operators, as Tenancy was registered with them
/// (<c>TenancyOptions.OperatorTokenRoles</c>), for code that cannot name the application's id types: a start-up
/// check that compares them with what the database has. Registered once, by <c>AddTenancy</c>.
/// </summary>
/// <param name="tokenRoles">The operators' token roles, as the tokens spell them.</param>
public sealed class TenancyOperatorTokenRoles(IEnumerable<string> tokenRoles)
{
    /// <summary>The operators' token roles, each once, in ordinal order. Empty for an application without operators.</summary>
    public IReadOnlyList<string> TokenRoles { get; } =
        [.. (tokenRoles ?? throw new ArgumentNullException(nameof(tokenRoles))).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
