using System.ComponentModel;

namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// The export the build runs. The build step starts the freshly built application with
/// <see cref="ModeVariable"/> set; a module initializer the generator wrote into it calls
/// <see cref="RunIfRequested"/> before <c>Main</c>, which exports every source it was handed and ends the
/// process. The application's own start-up, its configuration included, never runs.
/// <para>
/// Without the variable nothing happens, which is every start of the application outside that build step.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SupabaseMigrationBuild
{
    /// <summary><c>Write</c> writes missing files; <c>Check</c> only compares. Unset means do nothing.</summary>
    public const string ModeVariable = "DDDTOOLKIT_SUPABASE_EXPORT";

    /// <summary>The migrations directory. Unset means find it upwards from <see cref="StartVariable"/>.</summary>
    public const string DirectoryVariable = "DDDTOOLKIT_SUPABASE_DIRECTORY";

    /// <summary>Where to start looking for <c>supabase/config.toml</c>: the project directory.</summary>
    public const string StartVariable = "DDDTOOLKIT_SUPABASE_START";

    /// <summary>
    /// Exports and ends the process when the build asked for it, and returns at once otherwise. Called
    /// by generated code only.
    /// </summary>
    /// <param name="sources">Every source found at compile time; only evaluated when asked.</param>
    public static void RunIfRequested(Func<IReadOnlyList<SupabaseMigrationSource>> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var mode = Environment.GetEnvironmentVariable(ModeVariable);
        if (string.IsNullOrWhiteSpace(mode))
        {
            return;
        }

        var exitCode = Run(
            mode,
            sources(),
            Environment.GetEnvironmentVariable(DirectoryVariable),
            Environment.GetEnvironmentVariable(StartVariable),
            Console.Out);

        Console.Out.Flush();
        Environment.Exit(exitCode);
    }

    /// <summary>
    /// Runs the export and describes it on <paramref name="output"/>, problems in the
    /// <c>error : message</c> form MSBuild shows as build errors.
    /// </summary>
    /// <param name="mode"><c>Write</c> or <c>Check</c>.</param>
    /// <param name="sources">The contexts to export.</param>
    /// <param name="directory">The migrations directory, or null or empty to find it.</param>
    /// <param name="start">Where to start looking when <paramref name="directory"/> is not given.</param>
    /// <param name="output">Where to report.</param>
    /// <returns>0 when everything is in sync, 1 when something needs attention, 2 when the export could not run.</returns>
    public static int Run(string mode, IReadOnlyList<SupabaseMigrationSource> sources, string? directory, string? start, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(output);

        var write = string.Equals(mode, "Write", StringComparison.OrdinalIgnoreCase);
        if (!write && !string.Equals(mode, "Check", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine($"error : SupabaseMigrationsExport is '{mode}'. Use Write to write missing files, or Check to only compare them.");
            return 2;
        }

        if (sources.Count == 0)
        {
            output.WriteLine("warning : No factory marked [SupabaseMigrations] is referenced by this project, so there is nothing to export.");
            return 0;
        }

        try
        {
            var target = string.IsNullOrWhiteSpace(directory)
                ? SupabaseMigrations.FindDirectory(string.IsNullOrWhiteSpace(start) ? null : start)
                : directory;

            var reports = write ? SupabaseMigrations.Export(sources, target) : SupabaseMigrations.Compare(sources, target);

            foreach (var entry in reports.SelectMany(report => report.Entries))
            {
                output.WriteLine($"Supabase migrations: {entry.Status,-12} {Path.GetFileName(entry.Path)}");
            }

            if (reports.All(report => report.IsInSync))
            {
                return 0;
            }

            var problems = new SupabaseMigrationsOutOfSyncException(reports).Message.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in problems.Skip(1))
            {
                output.WriteLine($"error : {line.Trim()}");
            }

            if (!write)
            {
                output.WriteLine("error : This build only checks. Build it locally, where SupabaseMigrationsExport is Write, and commit the files it writes.");
            }

            return 1;
        }
        catch (Exception exception)
        {
            output.WriteLine($"error : The Supabase migrations could not be exported: {exception.Message}");
            return 2;
        }
    }
}
