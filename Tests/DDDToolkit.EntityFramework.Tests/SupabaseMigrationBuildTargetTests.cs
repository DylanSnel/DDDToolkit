using System.Diagnostics;
using System.Text.Json;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The build step as MSBuild runs it: a small project that imports the package's targets file, sets the
/// export's properties, and builds, so the target's own <c>Exec</c> starts this test assembly with the
/// variables it passes, and <see cref="BuildStepExport"/> exports what it was handed. What reaches the
/// access file is what reached the export.
/// <para>
/// The project is an application, as a host is. As a library or a test project it shows where the step does
/// not run: since this assembly exports whenever it is started, a file written or a check failed would say it did.
/// </para>
/// <para>
/// The other step, which records <c>SupabaseRowAccessRoles</c> in the application, runs before the compiler: the
/// file it adds to the project's <c>Compile</c> items is what the compiler gets, and what the registration reads.
/// </para>
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
        // Left out, both switches are on.
        var (exitCode, output) = await BuildAsync(roles: "token:analyst=desk_analyst|system=desk_books", callerFunctions: null);

        exitCode.Should().Be(0, output);
        var sql = File.ReadAllText(Directory.GetFiles(Migrations, "*_access.desk.ddd.sql").Should().ContainSingle().Subject);

        sql.Should().Contain("-- Privileges, from the policies above", "the access file writes the privileges unless the project says None");
        sql.Should().Contain("REVOKE ALL ON TABLE desk.\"Tickets\" FROM PUBLIC, anon, authenticated, ddd_system_in, desk_analyst, desk_books;\n", "and system=desk_books reached the export, as one more role a table gave nothing to");
        sql.Should().Contain("GRANT SELECT ON TABLE desk.\"Tickets\" TO desk_analyst;\n", "an analyst reads, and no more");
        sql.Should().Contain("        CREATE ROLE desk_books NOLOGIN NOINHERIT;\n", "the access file makes the bookkeeping role");
        sql.Should().Contain("ALTER TABLE desk.\"Tickets\" ENABLE ROW LEVEL SECURITY;\nALTER TABLE desk.\"Tickets\" FORCE ROW LEVEL SECURITY;\n", "and forces the policies unless the project says false");

        // Turned off, both reach the export, and the file says nothing of either.
        Directory.Delete(Migrations, recursive: true);
        (await BuildAsync(roles: "token:analyst=desk_analyst", callerFunctions: null, grants: "None", force: "false")).ExitCode.Should().Be(0);
        File.ReadAllText(Directory.GetFiles(Migrations, "*_access.desk.ddd.sql").Single()).Should().NotContain(" ON TABLE ").And.NotContain("FORCE");
    }

    [Fact]
    public async Task The_build_reads_the_login_role_from_the_environment()
    {
        var (exitCode, output) = await BuildAsync(roles: "token:analyst=desk_analyst|system=desk_books", callerFunctions: null, loginRole: "desk_api");

        exitCode.Should().Be(0, output);
        var files = Directory.GetFiles(Migrations).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal).ToList();
        files[^1].Should().MatchRegex(@"^\d{14}_login_role\.desk_api\.ddd\.sql$", "SupabaseLoginRole reached the export, which wrote the file after every other");
        File.ReadAllText(Path.Combine(Migrations, files[^1])).Split('\n').Select(line => line.Trim())
            .Where(line => line.StartsWith("GRANT ", StringComparison.Ordinal) && line.EndsWith(" TO desk_api;", StringComparison.Ordinal))
            .Should().Equal(
                ["GRANT anon TO desk_api;", "GRANT authenticated TO desk_api;", "GRANT ddd_system_in TO desk_api;", "GRANT desk_books TO desk_api;", "GRANT desk_analyst TO desk_api;"],
                "with the roles the same build mapped");

        // Left out, there is no such file.
        Directory.Delete(Migrations, recursive: true);
        (await BuildAsync(roles: "token:analyst=desk_analyst", callerFunctions: null)).ExitCode.Should().Be(0);
        Directory.GetFiles(Migrations, "*_login_role.*").Should().BeEmpty();
    }

    [Fact]
    public async Task A_semicolon_in_the_login_role_is_refused_naming_what_a_name_is()
    {
        var (exitCode, output) = await BuildAsync(roles: null, callerFunctions: null, loginRole: "desk_api;DDDTOOLKIT_SUPABASE_EXPORT=Check");

        exitCode.Should().NotBe(0);
        output.Should().Contain("SupabaseLoginRole is 'desk_api;DDDTOOLKIT_SUPABASE_EXPORT=Check', with a ';'. It is the name of one role, a plain lowercase identifier such as sample_api");
        Directory.Exists(Migrations).Should().BeFalse("the export never ran");
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

    [Theory]
    [InlineData("Exe")]
    [InlineData("WinExe")]
    public async Task An_application_exports_when_the_mode_is_given_for_the_whole_build(string outputType)
    {
        // Given on the command line, the way a CI script gives it, and not in the project: it still reaches the host.
        var written = await BuildProjectAsync(Project(outputType), "-p:SupabaseMigrationsExport=Write");

        written.ExitCode.Should().Be(0, written.Output);
        Directory.GetFiles(Migrations, "*_access.desk.ddd.sql").Should().ContainSingle();

        var checkedOnly = await BuildProjectAsync(Project(outputType), "-p:SupabaseMigrationsExport=Check");

        checkedOnly.ExitCode.Should().Be(0, checkedOnly.Output);
        checkedOnly.Output.Should().Contain("Supabase migrations: Unchanged");
    }

    [Fact]
    public async Task A_mode_given_for_the_whole_build_wins_over_the_hosts_own()
    {
        // The host writes locally; a CI script that asks for Check gets Check, and a file that is missing fails it.
        var properties = Project("Exe");
        properties["SupabaseMigrationsExport"] = "Write";

        var (exitCode, output) = await BuildProjectAsync(properties, "-p:SupabaseMigrationsExport=Check");

        exitCode.Should().NotBe(0);
        output.Should().Contain("Supabase migrations: Missing").And.Contain("This build only checks.");
        Directory.Exists(Migrations).Should().BeFalse("a build that only checks writes nothing");
    }

    [Theory]
    [InlineData("Library", "Write", true)]
    [InlineData("Library", "Check", true)]
    [InlineData("Library", "Write", false)]
    [InlineData("Library", "Check", false)]
    [InlineData("", "Write", true)]
    [InlineData("", "Check", false)]
    public async Task A_project_that_is_no_application_leaves_the_export_alone_however_it_was_given(string outputType, string mode, bool onTheCommandLine)
    {
        // On the command line, or in the project the way a Directory.Build.props sets it for every project: the
        // step would start this test assembly, which exports, so a file or a failed check would show it ran.
        var properties = Project(outputType);
        if (!onTheCommandLine)
        {
            properties["SupabaseMigrationsExport"] = mode;
        }

        var (exitCode, output) = await BuildProjectAsync(properties, onTheCommandLine ? [$"-p:SupabaseMigrationsExport={mode}"] : []);

        exitCode.Should().Be(0, output);
        output.Should().NotContain("Supabase migrations").And.NotContain("warning").And.NotContain("error");
        Directory.Exists(Migrations).Should().BeFalse("the step never started a program");
    }

    [Theory]
    [InlineData("IsTestProject")]
    [InlineData("IsTestingPlatformApplication")]
    public async Task A_test_project_leaves_the_export_alone_although_it_is_an_application(string marker)
    {
        var properties = Project("Exe");
        properties[marker] = "true";

        var (exitCode, output) = await BuildProjectAsync(properties, "-p:SupabaseMigrationsExport=Write");

        exitCode.Should().Be(0, output);
        output.Should().NotContain("Supabase migrations").And.NotContain("warning").And.NotContain("error");
        Directory.Exists(Migrations).Should().BeFalse("the step never started the test assembly");
    }

    [Fact]
    public async Task The_generator_is_handed_every_property_the_step_decides_by()
    {
        // The generator writes the module initializer only where the step runs, deciding by the same mode and the
        // same two marks of a test project. A property reaches it only when the targets declare it compiler
        // visible, so a mark left out here would have it write into a test project the step stays out of. The
        // output type needs no declaring: it reaches the generator as the compilation's own kind.
        var (exitCode, output) = await BuildProjectAsync(Project("Exe"), "-getItem:CompilerVisibleProperty");

        exitCode.Should().Be(0, output);
        JsonDocument.Parse(output).RootElement.GetProperty("Items").GetProperty("CompilerVisibleProperty")
            .EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString())
            .Should().BeEquivalentTo(["SupabaseMigrationsExport", "IsTestProject", "IsTestingPlatformApplication"]);
    }

    [Fact]
    public async Task An_application_records_its_roles_in_its_assembly_for_the_registration_to_read()
    {
        // Whether or not it exports: a host whose export runs elsewhere says its roles in its own project file.
        var recorded = await RecordedRolesAsync(new() { ["OutputType"] = "Exe", ["SupabaseRowAccessRoles"] = " token:analyst=desk_analyst|system=none " });

        recorded.Should().StartWith("// <auto-generated/>", "the analyzers take it for generated code")
            .And.Contain("[assembly: global::System.Reflection.AssemblyMetadata(\"SupabaseRowAccessRoles\", \"token:analyst=desk_analyst|system=none\")]", "the value is trimmed, and written as it is");
        (await RecordedRolesAsync(new() { ["OutputType"] = "WinExe", ["SupabaseRowAccessRoles"] = "user=desk_user*" }))
            .Should().Contain("AssemblyMetadata(\"SupabaseRowAccessRoles\", \"user=desk_user*\")]", "what MSBuild reads in an item reaches the compiler as it was written");
    }

    [Fact]
    public async Task A_value_written_over_several_lines_is_recorded_on_one_and_compiles()
    {
        // A C# string holds no line break, and the pairs are read without the white space around them, as the export reads them.
        var recorded = await RecordedRolesAsync(new()
        {
            ["OutputType"] = "Exe",
            ["SupabaseRowAccessRoles"] = "\n      token:analyst=desk_analyst |\r\n\ttoken:auditor=desk_auditor\n    ",
        });

        recorded.Should().MatchRegex(
            """\[assembly: global::System\.Reflection\.AssemblyMetadata\("SupabaseRowAccessRoles", "token:analyst=desk_analyst \| +token:auditor=desk_auditor"\)\]\n""",
            "a line break or a tab becomes a space, and the file compiled, which the step's neighbour checks");
    }

    [Theory]
    [InlineData("Library", "token:analyst=desk_analyst", null)]
    [InlineData("Exe", "token:analyst=desk_analyst", "IsTestProject")]
    [InlineData("Exe", "token:analyst=desk_analyst", "IsTestingPlatformApplication")]
    [InlineData("Exe", "  ", null)]
    public async Task A_library_a_test_project_and_a_project_without_the_property_record_nothing(string outputType, string roles, string? marker)
    {
        // A library registers no row level security, and a test is no host, so the property may be given for a whole build.
        var properties = new Dictionary<string, string> { ["OutputType"] = outputType, ["SupabaseRowAccessRoles"] = roles };
        if (marker is not null)
        {
            properties[marker] = "true";
        }

        (await RecordedRolesAsync(properties)).Should().BeNull();
    }

    [Theory]
    [InlineData("user=\"desk user\"", "and a role's name holds no double quote and no backslash")]
    [InlineData("user=desk\\user", "and a role's name holds no double quote and no backslash")]
    [InlineData("user=desk_user;anonymous=desk_guest", "with a ';'. Separate its pairs with '|' instead")]
    public async Task A_value_the_record_cannot_hold_as_it_is_stops_the_build_with_the_reason(string roles, string reason)
    {
        // Escaping a quote or a backslash in the C# string would take a backslash, which MSBuild on Linux and macOS makes a slash.
        var (exitCode, output) = await BuildProjectAsync(new() { ["OutputType"] = "Exe", ["SupabaseRowAccessRoles"] = roles }, "-t:CoreCompile");

        exitCode.Should().NotBe(0, output);
        output.Should().Contain("SupabaseRowAccessRoles is '").And.Contain(reason);
    }

    /// <summary>
    /// Runs the step that records the roles in a project that sets <paramref name="properties"/>, and hands back the file
    /// it added to the project's <c>Compile</c> items, or null when it added none. The project's <c>CoreCompile</c>
    /// compiles that file, so a record the compiler refuses fails here.
    /// </summary>
    private async Task<string?> RecordedRolesAsync(Dictionary<string, string> properties)
    {
        var (exitCode, output) = await BuildProjectAsync(properties, "-t:CoreCompile", "-getItem:Compile");
        exitCode.Should().Be(0, output);

        var compiled = JsonDocument.Parse(output).RootElement.GetProperty("Items").TryGetProperty("Compile", out var items)
            ? items.EnumerateArray().Select(item => item.GetProperty("FullPath").GetString()!).ToList()
            : [];
        compiled.Should().HaveCountLessThan(2, "the step records the roles in one file");
        File.Exists(Path.Combine(_project, "obj", "Recorded.dll")).Should().Be(compiled.Count == 1, "the compiler took the file the step recorded, and only that");
        return compiled.Count == 0 ? null : File.ReadAllText(compiled[0]).ReplaceLineEndings("\n");
    }

    /// <summary>
    /// A project of the <paramref name="outputType"/> given that maps the token role one of the rules this assembly
    /// exports is for, so wherever the step runs, it exports, and a file shows it did. The export is not on.
    /// </summary>
    private static Dictionary<string, string> Project(string outputType) => new()
    {
        ["OutputType"] = outputType,
        ["SupabaseRowAccessRoles"] = "token:analyst=desk_analyst",
    };

    /// <summary>
    /// Builds a project that imports the targets file as a host does, an application with the export on, writing
    /// into <see cref="Migrations"/>, and <paramref name="roles"/>, <paramref name="callerFunctions"/>,
    /// <paramref name="grants"/>, <paramref name="force"/> and <paramref name="loginRole"/> as the project would set them.
    /// </summary>
    private Task<(int ExitCode, string Output)> BuildAsync(string? roles, string? callerFunctions, string? grants = null, string? force = null, string? loginRole = null)
    {
        var properties = new Dictionary<string, string>
        {
            ["OutputType"] = "Exe",
            ["SupabaseMigrationsExport"] = "Write",
        };

        foreach (var (name, value) in (IEnumerable<(string, string?)>)[
            ("SupabaseRowAccessRoles", roles),
            ("SupabaseCallerFunctions", callerFunctions),
            ("SupabaseRowAccessGrants", grants),
            ("SupabaseForceRowLevelSecurity", force),
            ("SupabaseLoginRole", loginRole)])
        {
            if (value is not null)
            {
                properties[name] = value;
            }
        }

        return BuildProjectAsync(properties);
    }

    /// <summary>
    /// Builds a project that imports the targets file, writes into <see cref="Migrations"/> when the step runs, and
    /// sets <paramref name="properties"/> as the project would; <paramref name="commandLine"/> is added to the
    /// command line, where <c>-p:</c> gives a property for the whole build.
    /// </summary>
    private async Task<(int ExitCode, string Output)> BuildProjectAsync(Dictionary<string, string> properties, params string[] commandLine)
    {
        var targets = Path.Combine(AppContext.BaseDirectory, "BuildStep", "DDDToolkit.EntityFramework.Supabase.targets");
        var project = Path.Combine(_project, "Host.proj");

        File.WriteAllText(
            project,
            $"""
            <Project>
              <PropertyGroup>
                <Language>C#</Language>
                <IntermediateOutputPath>obj\</IntermediateOutputPath>
                <SupabaseMigrationsDirectory>{Escaped(Migrations)}</SupabaseMigrationsDirectory>
                <TargetPath>{Escaped(typeof(BuildStepExport).Assembly.Location)}</TargetPath>
                {string.Concat(properties.Select(property => $"<{property.Key}>{Escaped(property.Value)}</{property.Key}>"))}
              </PropertyGroup>
              <Import Project="{Escaped(targets)}" />
              <UsingTask TaskName="Microsoft.CodeAnalysis.BuildTasks.Csc" AssemblyFile="$(RoslynTargetsPath)/Microsoft.Build.Tasks.CodeAnalysis.dll" />
              <Target Name="Build" />
              <!--
                The two targets the step that records the roles names as its neighbours, which the SDK brings. CoreCompile
                compiles what the step added, with the build's own compiler and the runtime's core library alone, which
                holds the attribute: the file is C# the compiler takes, or the build fails.
              -->
              <Target Name="PrepareForBuild" />
              <Target Name="CoreCompile">
                <Csc Condition="'@(Compile)' != ''" Sources="@(Compile)" OutputAssembly="obj/Recorded.dll" TargetType="library"
                     NoStandardLib="true" NoConfig="true" UseSharedCompilation="false" References="{Escaped(typeof(object).Assembly.Location)}" />
              </Target>
            </Project>
            """);

        var start = new ProcessStartInfo(DotnetHost())
        {
            WorkingDirectory = _project,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in (string[])["msbuild", project, "-t:Build", "-nologo", "-nodeReuse:false", "-noAutoResponse", "-v:m", .. commandLine])
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
