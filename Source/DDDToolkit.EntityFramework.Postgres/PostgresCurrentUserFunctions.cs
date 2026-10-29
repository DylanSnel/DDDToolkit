namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// The SQL functions a policy asks about the caller, and so what a rule's <c>user.Id</c>,
/// <c>user.Role</c> and <c>user.FindClaim(...)</c> become. All three read the claims
/// <see cref="PostgresRowLevelSecurityInterceptor"/> puts on the connection, <c>request.jwt.claims</c>.
/// </summary>
/// <param name="UserId">Answers the user's id as a <c>uuid</c>, or <see langword="null"/>: <c>"ddd".current_user_id()</c>.</param>
/// <param name="Role">Answers the caller's role as <c>text</c>: <c>"ddd".current_user_role()</c>.</param>
/// <param name="Claims">Answers the token's claims as <c>jsonb</c>: <c>"ddd".current_user_claims()</c>.</param>
public sealed record PostgresCurrentUserFunctions(string UserId, string Role, string Claims)
{
    /// <summary>
    /// The toolkit's own, in its <c>ddd</c> schema, which <see cref="PostgresRowAccess.SetupScript"/>
    /// creates on a Postgres of your own.
    /// </summary>
    public static PostgresCurrentUserFunctions Toolkit { get; } = new("\"ddd\".current_user_id()", "\"ddd\".current_user_role()", "\"ddd\".current_user_claims()");
}
