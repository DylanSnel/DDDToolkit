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
        Directory.GetFiles(Migrations).Should().HaveCount(2);
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

    [Fact]
    public void A_project_outside_any_supabase_project_fails_with_the_reason()
    {
        using var output = new StringWriter();

        var exitCode = SupabaseMigrationBuild.Run("Write", [Shelves], directory: null, start: Path.GetTempPath(), output);

        exitCode.Should().Be(2);
        output.ToString().Should().Contain("error : The Supabase migrations could not be exported").And.Contain("supabase/config.toml");
    }
}
