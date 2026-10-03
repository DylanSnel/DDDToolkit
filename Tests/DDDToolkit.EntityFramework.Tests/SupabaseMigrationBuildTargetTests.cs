using System.Diagnostics;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The build step as MSBuild runs it: a small project that imports the package's targets file, sets the
/// export's properties, and builds, so the target's own <c>Exec</c> starts this test assembly with the
/// variables it passes, and <see cref="BuildStepExport"/> exports what it was handed. What reaches the
/// access file is what reached the export.
/// </summary>
public sealed class SupabaseMigrationBuildTargetTests : IDisposable
{
    private readonly string _project = Path.Combine(Path.GetTempPath(), "ddd-supabase-target-" + Guid.NewGuid().ToString("N"));

    public SupabaseMigrationBuildTargetTests() => Directory.CreateDirectory(_project);

    public void Dispose() => Directory.Delete(_project, recursive: true);

    private string Migrations => Path.Combine(_project, "migrations");

    [Fact]
    public async Task The_build_reads_roles_and_caller_functions_from_the_environment()
    {
        var (exitCode, output) = await BuildAsync(
            roles: "user=desk_user | anonymous=desk_guest | system-in=desk_scoped | token:analyst=desk_analyst",
            callerFunctions: "uid=who.id()|role=who.role()|claims=who.claims()");

        exitCode.Should().Be(0, output);
        var sql = File.ReadAllText(Directory.GetFiles(Migrations, "*_access.desk.ddd.sql").Should().ContainSingle().Subject);

        sql.Should().Contain("CREATE POLICY \"Owners have their tickets (insert) for desk_user\" ON desk.\"Tickets\" FOR INSERT TO desk_user\n", "user=desk_user reached the export");
        sql.Should().Contain("CREATE POLICY \"Owners have their tickets (insert) for desk_guest\" ON desk.\"Tickets\" FOR INSERT TO desk_guest\n", "anonymous=desk_guest reached the export");
        sql.Should().Contain("CREATE POLICY \"Scoped work reads by role (select) for desk_scoped\" ON desk.\"Tickets\" FOR SELECT TO desk_scoped\n", "system-in=desk_scoped reached the export");
        sql.Should().Contain("        CREATE ROLE desk_scoped NOLOGIN NOINHERIT;\n", "the prelude makes the scoped role a policy names");
        sql.Should().Contain("CREATE POLICY \"Analysts read every ticket (select) for desk_analyst\" ON desk.\"Tickets\" FOR SELECT TO desk_analyst\n", "token:analyst=desk_analyst reached the export");
        sql.Should().Contain("        CREATE ROLE desk_analyst NOLOGIN NOINHERIT;\n", "and the prelude makes the role it is mapped to");
        sql.Should().Contain("(SELECT who.id())", "uid=who.id() reached the export");
        sql.Should().Contain("(SELECT who.role())", "role=who.role() reached the export");
        sql.Should().Contain("(SELECT who.claims() #>> '{app_metadata,team}')", "claims=who.claims() reached the export");
        sql.Should().Contain("SELECT coalesce(((SELECT who.claims()) ->> 'on_duty')::boolean, false)", "the caller functions reached the contribution the build handed over");
        sql.Should().Contain("GRANT EXECUTE ON FUNCTION desk.on_duty() TO desk_user;\n", "and so did the roles its grants name");
        sql.Should().NotContain("authenticated").And.NotContain("auth.", "nothing of the defaults is left");
    }

    [Fact]
    public async Task The_build_reads_the_privileges_and_the_force_switch_and_the_system_role_from_the_environment()
    {
        var (exitCode, output) = await BuildAsync(
            roles: "token:analyst=desk_analyst|system=desk_books",
            callerFunctions: null,
            grants: "Write",
            force: "true");

        exitCode.Should().Be(0, output);
        var sql = File.ReadAllText(Directory.GetFiles(Migrations, "*_access.desk.ddd.sql").Should().ContainSingle().Subject);

        sql.Should().Contain("-- Privileges, from the policies above", "SupabaseRowAccessGrants=Write reached the export");
        sql.Should().Contain("REVOKE ALL ON TABLE desk.\"Tickets\" FROM PUBLIC, anon, authenticated, ddd_system_in, desk_analyst, desk_books;\n", "and system=desk_books did, as one more role a table gave nothing to");
        sql.Should().Contain("GRANT SELECT ON TABLE desk.\"Tickets\" TO desk_analyst;\n", "an analyst reads, and no more");
        sql.Should().Contain("        CREATE ROLE desk_books NOLOGIN NOINHERIT;\n", "the access file makes the bookkeeping role");
        sql.Should().Contain("ALTER TABLE desk.\"Tickets\" ENABLE ROW LEVEL SECURITY;\nALTER TABLE desk.\"Tickets\" FORCE ROW LEVEL SECURITY;\n", "SupabaseForceRowLevelSecurity=true reached the export");

        // Left out, both are off, and the file says nothing of either.
        Directory.Delete(Migrations, recursive: true);
        (await BuildAsync(roles: "token:analyst=desk_analyst", callerFunctions: null)).ExitCode.Should().Be(0);
        File.ReadAllText(Directory.GetFiles(Migrations, "*_access.desk.ddd.sql").Single()).Should().NotContain(" ON TABLE ").And.NotContain("FORCE");
    }

    [Theory]
    [InlineData("SupabaseRowAccessRoles", "user=desk_user;anonymous=desk_guest")]
    [InlineData("SupabaseCallerFunctions", "uid=who.id();role=who.role()")]
    public async Task A_semicolon_in_a_build_property_is_refused_naming_the_bar(string property, string value)
    {
        var (exitCode, output) = await BuildAsync(
            roles: property == "SupabaseRowAccessRoles" ? value : null,
            callerFunctions: property == "SupabaseCallerFunctions" ? value : null);

        exitCode.Should().NotBe(0);
        output.Should().Contain($"{property} is '{value}', with a ';'. Separate its pairs with '|' instead");
        Directory.Exists(Migrations).Should().BeFalse("the export never ran");
    }

    /// <summary>
    /// Builds a project that imports the targets file with the export on, writing into
    /// <see cref="Migrations"/>, and <paramref name="roles"/>, <paramref name="callerFunctions"/>,
    /// <paramref name="grants"/> and <paramref name="force"/> as the project would set them.
    /// </summary>
    private async Task<(int ExitCode, string Output)> BuildAsync(string? roles, string? callerFunctions, string? grants = null, string? force = null)
    {
        var targets = Path.Combine(AppContext.BaseDirectory, "BuildStep", "DDDToolkit.EntityFramework.Supabase.targets");
        var project = Path.Combine(_project, "Host.proj");

        File.WriteAllText(
            project,
            $"""
            <Project>
              <PropertyGroup>
                <SupabaseMigrationsExport>Write</SupabaseMigrationsExport>
                <SupabaseMigrationsDirectory>{Escaped(Migrations)}</SupabaseMigrationsDirectory>
                <TargetPath>{Escaped(typeof(BuildStepExport).Assembly.Location)}</TargetPath>
                {(roles is null ? "" : $"<SupabaseRowAccessRoles>{Escaped(roles)}</SupabaseRowAccessRoles>")}
                {(callerFunctions is null ? "" : $"<SupabaseCallerFunctions>{Escaped(callerFunctions)}</SupabaseCallerFunctions>")}
                {(grants is null ? "" : $"<SupabaseRowAccessGrants>{Escaped(grants)}</SupabaseRowAccessGrants>")}
                {(force is null ? "" : $"<SupabaseForceRowLevelSecurity>{Escaped(force)}</SupabaseForceRowLevelSecurity>")}
              </PropertyGroup>
              <Import Project="{Escaped(targets)}" />
              <Target Name="Build" />
            </Project>
            """);

        var start = new ProcessStartInfo(DotnetHost())
        {
            WorkingDirectory = _project,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in (string[])["msbuild", project, "-t:Build", "-nologo", "-nodeReuse:false", "-noAutoResponse", "-v:m"])
        {
            start.ArgumentList.Add(argument);
        }

        // The build's own MSBuild, not the one that built this test run.
        foreach (var inherited in (string[])["MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath", "MSBuildLoadMicrosoftTargetsReadOnly"])
        {
            start.Environment.Remove(inherited);
        }

        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment[BuildStepExport.Variable] = "1";

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await output + await error);
    }

    /// <summary>The dotnet that runs this test, which the build step's target starts too.</summary>
    private static string DotnetHost()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host))
        {
            return host;
        }

        if (Environment.ProcessPath is { } process && string.Equals(Path.GetFileNameWithoutExtension(process), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return process;
        }

        var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root && File.Exists(Path.Combine(root, executable)))
        {
            return Path.Combine(root, executable);
        }

        return "dotnet";
    }

    /// <summary>Text as MSBuild reads it back unchanged inside an XML project: its own special characters escaped, then XML's.</summary>
    private static string Escaped(string text)
        => System.Security.SecurityElement.Escape(text.Replace("%", "%25", StringComparison.Ordinal).Replace("$", "%24", StringComparison.Ordinal).Replace("@", "%40", StringComparison.Ordinal))!;
}
