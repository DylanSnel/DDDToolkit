using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// What the build step runs: <see cref="SupabaseMigrationBuild.Run"/> is everything the generated module
/// initializer does apart from reading the environment and ending the process.
/// </summary>
public sealed class SupabaseMigrationBuildTests : IDisposable
{
    private static readonly SupabaseMigrationSource Shelves = SupabaseMigrationSource.For(() => SupabaseShelfContext.Create());

    private readonly string _project = Path.Combine(Path.GetTempPath(), "ddd-supabase-build-" + Guid.NewGuid().ToString("N"));

    public SupabaseMigrationBuildTests()
    {
        Directory.CreateDirectory(Path.Combine(_project, "supabase"));
        File.WriteAllText(Path.Combine(_project, "supabase", "config.toml"), "project_id = \"test\"\n");
    }

    public void Dispose() => Directory.Delete(_project, recursive: true);

    private string Migrations => Path.Combine(_project, "supabase", "migrations");

    private (int ExitCode, string Output) Run(string mode, params SupabaseMigrationSource[] sources)
    {
        using var output = new StringWriter();
        var exitCode = SupabaseMigrationBuild.Run(mode, sources, directory: null, start: Path.Combine(_project, "src", "Host"), output);
        return (exitCode, output.ToString());
    }

    [Fact]
    public void Write_finds_the_project_from_the_start_directory_writes_the_files_and_succeeds()
    {
        Directory.CreateDirectory(Path.Combine(_project, "src", "Host"));

        var (exitCode, output) = Run("Write", Shelves);

        exitCode.Should().Be(0);
        output.Should().Contain($"Created      {CreateShelves.Id}.supabaseshelf.ddd.sql");
        Directory.GetFiles(Migrations).Should().HaveCount(3, "the two migrations, and the access file that gives the module's outbox its privileges");
        Run("write", Shelves).ExitCode.Should().Be(0, "the mode is not case sensitive, and a second run finds everything unchanged");
    }

    [Fact]
    public void Check_writes_nothing_and_fails_with_errors_msbuild_shows_as_build_errors()
    {
        Directory.CreateDirectory(Path.Combine(_project, "src", "Host"));

        var (exitCode, output) = Run("Check", Shelves);

        exitCode.Should().Be(1);
        output.Should().Contain($"error : {CreateShelves.Id}: has no file.");
        output.Should().Contain("error : This build only checks.");
        Directory.Exists(Migrations).Should().BeFalse();
    }

    [Fact]
    public void Check_succeeds_once_the_files_are_there()
    {
        Directory.CreateDirectory(Path.Combine(_project, "src", "Host"));
        Run("Write", Shelves);

        Run("Check", Shelves).ExitCode.Should().Be(0);
    }

    [Fact]
    public void An_unknown_mode_is_refused_with_the_two_it_knows()
    {
        var (exitCode, output) = Run("Always", Shelves);

        exitCode.Should().Be(2);
        output.Should().Contain("error : SupabaseMigrationsExport is 'Always'. Use Write").And.Contain("or Check");
    }

    [Fact]
    public void A_project_that_references_no_marked_factory_is_warned_but_not_failed()
    {
        var (exitCode, output) = Run("Write");

        exitCode.Should().Be(0);
        output.Should().StartWith("warning : No factory marked [SupabaseMigrations]");
    }

    [Theory]
    [InlineData("user", null, "error : SupabaseRowAccessRoles has 'user', which is not a key=value pair. It takes user, anonymous, system-in and system, each at most once and separated by '|', as in user=authenticated|anonymous=anon|system-in=ddd_system_in|system=ddd_system, and token:<role> for each token role the host mapped, as in token:analyst=desk_analyst.")]
    [InlineData("user=", null, "error : SupabaseRowAccessRoles has 'user=', which is not a key=value pair. It takes user, anonymous, system-in and system, each at most once and separated by '|', as in user=authenticated|anonymous=anon|system-in=ddd_system_in|system=ddd_system, and token:<role> for each token role the host mapped, as in token:analyst=desk_analyst.")]
    [InlineData("=anon", null, "error : SupabaseRowAccessRoles has '=anon', which is not a key=value pair. It takes user, anonymous, system-in and system, each at most once and separated by '|', as in user=authenticated|anonymous=anon|system-in=ddd_system_in|system=ddd_system, and token:<role> for each token role the host mapped, as in token:analyst=desk_analyst.")]
    [InlineData("user=authenticated|admin=boss", null, "error : SupabaseRowAccessRoles has the key 'admin', which it does not know. It takes user, anonymous, system-in and system, each at most once and separated by '|', as in user=authenticated|anonymous=anon|system-in=ddd_system_in|system=ddd_system, and token:<role> for each token role the host mapped, as in token:analyst=desk_analyst.")]
    [InlineData("user=authenticated|User=members", null, "error : SupabaseRowAccessRoles has the key 'User' twice. It takes user, anonymous, system-in and system, each at most once and separated by '|', as in user=authenticated|anonymous=anon|system-in=ddd_system_in|system=ddd_system, and token:<role> for each token role the host mapped, as in token:analyst=desk_analyst.")]
    [InlineData("anonymous=PUBLIC", null, "error : SupabaseRowAccessRoles has 'anonymous=PUBLIC'. PUBLIC is every role there is, the application's own and the system's included, and no policy is ever for it. Use a role such as anon.")]
    [InlineData("system-in=anon", null, "error : SupabaseRowAccessRoles has 'system-in=anon'. 'anon' is the role of a user or an anonymous caller as well, and the scoped system role must be one of its own: its policies would let those callers do what only the application's own work may. Use a role such as ddd_system_in.")]
    [InlineData("user=ddd_system_in", null, "error : SupabaseRowAccessRoles has 'user=ddd_system_in'. 'ddd_system_in' is the role of a user or an anonymous caller as well, and the scoped system role must be one of its own: its policies would let those callers do what only the application's own work may. Use a role such as authenticated.")]
    [InlineData("system-in=pg_write_all_data", null,"error : SupabaseRowAccessRoles has 'system-in=pg_write_all_data'. 'pg_write_all_data' starts with pg_, which Postgres keeps for its own roles. Use a role such as ddd_system_in.")]
    [InlineData("user=desk$user", null, "error : SupabaseRowAccessRoles has 'user=desk$user'. 'desk$user' has a '$', which a script cannot write into the dollar-quoted blocks it puts role names in. Use a role such as authenticated.")]
    [InlineData("token:=desk_analyst", null, "error : SupabaseRowAccessRoles has 'token:=desk_analyst', which names no token role after 'token:'. It takes user, anonymous, system-in and system, each at most once and separated by '|', as in user=authenticated|anonymous=anon|system-in=ddd_system_in|system=ddd_system, and token:<role> for each token role the host mapped, as in token:analyst=desk_analyst.")]
    [InlineData("token:analyst", null, "error : SupabaseRowAccessRoles has 'token:analyst', which is not a key=value pair. It takes user, anonymous, system-in and system, each at most once and separated by '|', as in user=authenticated|anonymous=anon|system-in=ddd_system_in|system=ddd_system, and token:<role> for each token role the host mapped, as in token:analyst=desk_analyst.")]
    [InlineData("token:analyst=desk_analyst|token:analyst=desk_other", null, "error : SupabaseRowAccessRoles has the key 'token:analyst' twice. It takes user, anonymous, system-in and system, each at most once and separated by '|', as in user=authenticated|anonymous=anon|system-in=ddd_system_in|system=ddd_system, and token:<role> for each token role the host mapped, as in token:analyst=desk_analyst.")]
    [InlineData("analyst=desk_analyst", null, "error : SupabaseRowAccessRoles has the key 'analyst', which it does not know. It takes user, anonymous, system-in and system, each at most once and separated by '|', as in user=authenticated|anonymous=anon|system-in=ddd_system_in|system=ddd_system, and token:<role> for each token role the host mapped, as in token:analyst=desk_analyst.")]
    [InlineData("token:analyst=anon", null, "error : SupabaseRowAccessRoles has a token role it cannot map. The token role 'analyst' is mapped to 'anon'. That is the role of callers who did not sign in: a rule for them would hold for the holders of this token role as well, with their identity. Map it to a role of its own.")]
    [InlineData("anonymous=guest|token:analyst=guest", null, "error : SupabaseRowAccessRoles has a token role it cannot map. The token role 'analyst' is mapped to 'guest'. That is the role of callers who did not sign in: a rule for them would hold for the holders of this token role as well, with their identity. Map it to a role of its own.")]
    [InlineData("token:analyst=ddd_system_in", null, "error : SupabaseRowAccessRoles has a token role it cannot map. The token role 'analyst' is mapped to 'ddd_system_in'. That is the scoped system role, which only the application's own work runs as: its policies would let the holder of a token do that work.")]
    [InlineData("token:analyst=PUBLIC", null, "error : SupabaseRowAccessRoles has a token role it cannot map. The token role 'analyst' is mapped to 'PUBLIC'. PUBLIC is every role there is, the application's own and the system's included, and no policy is ever for it.")]
    [InlineData(null, "uid=auth.uid()|jwt=auth.jwt()", "error : SupabaseCallerFunctions has the key 'jwt', which it does not know. It takes uid, role and claims, each at most once and separated by '|', as in uid=auth.uid()|role=auth.role()|claims=auth.jwt().")]
    [InlineData(null, "uid=auth.uid()) OR (true", "error : SupabaseCallerFunctions has 'uid=auth.uid()) OR (true', which is not a function called without arguments. Each value is one, with its schema or without, such as auth.uid(), and the policies call it as it is written.")]
    [InlineData(null, "claims=coalesce(auth.jwt(), '{}') || x", "error : SupabaseCallerFunctions has 'x', which is not a key=value pair. It takes uid, role and claims, each at most once and separated by '|', as in uid=auth.uid()|role=auth.role()|claims=auth.jwt().")]
    public void A_malformed_roles_variable_is_an_error_line_and_exit_code_2(string? roles, string? callerFunctions, string error)
    {
        Directory.CreateDirectory(Path.Combine(_project, "src", "Host"));
        using var output = new StringWriter();

        var exitCode = SupabaseMigrationBuild.Run("Write", [Shelves], [], [], directory: null, start: Path.Combine(_project, "src", "Host"), roles, callerFunctions, output);

        exitCode.Should().Be(2, "the export could not run as the project asked");
        output.ToString().TrimEnd().Should().Be(error, "the whole line is what the build shows");
        Directory.Exists(Migrations).Should().BeFalse("nothing is written with roles or caller functions the project did not mean");
    }

    [Fact]
    public void Unset_roles_and_caller_functions_are_the_defaults_and_each_pair_is_optional()
    {
        Directory.CreateDirectory(Path.Combine(_project, "src", "Host"));
        var rule = RowAccessRule.For<SupabaseShelf>("Shelves by name", RowOperations.Read, "({col:Name} IS NOT DISTINCT FROM {caller:claim:shelf})");
        using var output = new StringWriter();

        var exitCode = SupabaseMigrationBuild.Run("Write", [Shelves], [rule], [], directory: null, start: Path.Combine(_project, "src", "Host"), roles: "anonymous=guest", callerFunctions: " ", output);

        exitCode.Should().Be(0, output.ToString());
        var sql = File.ReadAllText(Directory.GetFiles(Migrations, "*_access.*").Single());
        sql.Should().Contain("FOR SELECT TO guest\n").And.Contain("FOR SELECT TO authenticated\n", "a pair left out keeps its default");
        sql.Should().Contain("(SELECT auth.jwt() ->> 'shelf')", "white space is no caller functions at all, so Supabase's");
    }

    [Fact]
    public void A_token_role_of_the_roles_property_is_written_by_its_database_name()
    {
        Directory.CreateDirectory(Path.Combine(_project, "src", "Host"));
        var analysts = RowAccessRule.For<SupabaseShelf>("Analysts read every shelf", RowOperations.Read, "TRUE", RowAccessRoles.Token("analyst"));
        var examiners = RowAccessRule.For<SupabaseShelf>("Examiners read every shelf", RowOperations.Read, "TRUE", RowAccessRoles.Token("Analyst"));

        // Without the pair, the rule is for a role no query runs as, and nothing is written.
        using (var refused = new StringWriter())
        {
            SupabaseMigrationBuild.Run("Write", [Shelves], [analysts], [], directory: null, start: Path.Combine(_project, "src", "Host"), roles: null, callerFunctions: null, refused)
                .Should().Be(2);
            refused.ToString().Should().Contain("error : The Supabase migrations could not be exported: The rule 'Analysts read every shelf' is for '@token:analyst', which no policy can be for.")
                .And.Contain("add 'token:analyst=<role>' to SupabaseRowAccessRoles");
        }

        // The key is read without regard to case and white space; the token role is kept as the token spells it.
        using var output = new StringWriter();
        var exitCode = SupabaseMigrationBuild.Run(
            "Write", [Shelves], [analysts, examiners], [], directory: null, start: Path.Combine(_project, "src", "Host"),
            roles: "user=members | Token: analyst = shelf_analyst | token:Analyst=shelf_examiner", callerFunctions: null, output);

        exitCode.Should().Be(0, output.ToString());
        var sql = File.ReadAllText(Directory.GetFiles(Migrations, "*_access.*").Single());
        sql.Should().Contain("CREATE POLICY \"Analysts read every shelf (select) for shelf_analyst\"").And.Contain("FOR SELECT TO shelf_analyst\n");
        sql.Should().Contain("FOR SELECT TO shelf_examiner\n", "two token roles that differ in case are two token roles");
        sql.Should().Contain("            CREATE ROLE shelf_analyst NOLOGIN NOINHERIT;\n", "the access file makes the role, as it makes the scoped system role")
            .And.Contain("callers.rolname IN ('members', 'anon')", "and holds it apart from the roles the property configured");
        sql.Should().NotContain("@token");
    }

    [Fact]
    public void A_project_outside_any_supabase_project_fails_with_the_reason()
    {
        using var output = new StringWriter();

        var exitCode = SupabaseMigrationBuild.Run("Write", [Shelves], directory: null, start: Path.GetTempPath(), output);

        exitCode.Should().Be(2);
        output.ToString().Should().Contain("error : The Supabase migrations could not be exported").And.Contain("supabase/config.toml");
    }
}
