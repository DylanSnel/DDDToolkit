namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// How a test fixture makes a role: the way the toolkit's own scripts make theirs, only where the server has no role
/// of that name yet, and without failing when another connection makes it at the same moment.
/// </summary>
/// <remarks>
/// <para>
/// Roles belong to the whole server, not to a database. A fixture shares its server with every test that uses it,
/// and with the toolkit's scripts those tests run, which make the roles they name; and the tests run in an order
/// no fixture decides. A bare <c>CREATE ROLE</c> then fails the run that happens to make the role second: with
/// 42710 where the other made it first, with 23505 where both made it at once. A test that needs a role nobody
/// made before it gives that role a name of its own instead.
/// </para>
/// <para>
/// This file is compiled into the suites of the supporting domains as well, so every fixture makes its roles with
/// the one statement.
/// </para>
/// </remarks>
public static class ServerRole
{
    /// <summary>The statement that makes <paramref name="name"/> with <paramref name="options"/>, unless the server has a role of that name.</summary>
    /// <param name="name">The role's name, as Postgres keeps it: lower case and unquoted.</param>
    /// <param name="options">What follows the name in <c>CREATE ROLE</c>, such as <c>NOLOGIN NOINHERIT</c>.</param>
    public static string Create(string name, string options) => $"""
        DO $role$
        BEGIN
            IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '{name}') THEN
                BEGIN
                    CREATE ROLE {name} {options};
                EXCEPTION WHEN duplicate_object OR unique_violation THEN
                    NULL; -- made by another connection at the same time
                END;
            END IF;
        END
        $role$;
        """;
}
