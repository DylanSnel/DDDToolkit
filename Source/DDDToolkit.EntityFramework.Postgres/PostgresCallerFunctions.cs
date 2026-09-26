namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// The SQL functions a policy asks about the caller, and so what a rule's <c>caller.UserId</c>,
/// <c>caller.Role</c> and <c>caller.Claim(...)</c> become. All three read the claims
/// <see cref="PostgresRowLevelSecurityInterceptor"/> puts on the connection, <c>request.jwt.claims</c>.
/// </summary>
/// <param name="UserId">Answers the user's id as a <c>uuid</c>, or <see langword="null"/>: <c>ddd.caller_id()</c>.</param>
/// <param name="Role">Answers the caller's role as <c>text</c>: <c>ddd.caller_role()</c>.</param>
/// <param name="Claims">Answers the token's claims as <c>jsonb</c>: <c>ddd.caller_claims()</c>.</param>
public sealed record PostgresCallerFunctions(string UserId, string Role, string Claims)
{
    /// <summary>
    /// The toolkit's own, in its <c>ddd</c> schema, which <see cref="PostgresRowAccess.SetupScript"/>
    /// creates on a Postgres of your own.
    /// </summary>
    public static PostgresCallerFunctions Toolkit { get; } = new("ddd.caller_id()", "ddd.caller_role()", "ddd.caller_claims()");
}
