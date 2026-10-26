namespace DDDToolkit.Supporting.Membership.Tests.Support;

/// <summary>The repository as a test reads it from disk: its root, and the sources somebody wrote in a project.</summary>
internal static class Repository
{
    /// <summary>The folder that holds <c>DDDToolkit.slnx</c>.</summary>
    public static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DDDToolkit.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("DDDToolkit.slnx was not found above " + AppContext.BaseDirectory + ".");
    }

    /// <summary>
    /// Every source file of the project in <paramref name="folder"/>, by its path below that folder with
    /// forward slashes, without what a build wrote.
    /// </summary>
    /// <param name="folder">The project's folder, below the repository's root: <c>Source/DDDToolkit.Supporting.Membership</c>.</param>
    public static IReadOnlyList<string> SourcesOf(string folder)
    {
        var project = Path.Combine(Root(), folder);
        return [.. Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(project, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(file => !file.StartsWith("obj/", StringComparison.Ordinal) && !file.StartsWith("bin/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>The text of one source file of the project in <paramref name="folder"/>.</summary>
    public static string Read(string folder, string file) => File.ReadAllText(Path.Combine(Root(), folder, file));
}
