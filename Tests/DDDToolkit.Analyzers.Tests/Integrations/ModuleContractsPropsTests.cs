using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// <c>DDD_ModuleContracts</c> as MSBuild hands it on. The props file of the DDDToolkit.Analyzers package declares it a
/// property the compiler hands the generators, the way it declares <c>DDD_Module</c>, and the toolkit's generator writes
/// <c>[assembly: ModuleContracts]</c> from it (<see cref="ModuleContractsTests"/>). No build step is involved, so nothing
/// the property says is written into an item, and it reaches the compiler alike on every platform.
/// <para>
/// The tip in docs/modules.md, which a host copies into its own props to make every project whose name ends in
/// <c>.Contracts</c> its module's contracts, is read from the page and evaluated here, so the page cannot drift from what
/// MSBuild makes of it. The toolkit itself never reads a project's name.
/// </para>
/// </summary>
public sealed class ModuleContractsPropsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ddd-module-contracts-" + Guid.NewGuid().ToString("N"));

    public ModuleContractsPropsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task The_props_hand_the_generators_DDD_ModuleContracts_as_they_hand_them_DDD_Module()
    {
        var evaluated = await EvaluateAsync("Acme.Ordering.Contracts", "<PropertyGroup><DDD_ModuleContracts>true</DDD_ModuleContracts></PropertyGroup>");

        evaluated.Visible.Should().Contain(["DDD_Module", "DDD_ModuleContracts"]);
        evaluated.ModuleContracts.Should().Be("true");
    }

    [Theory]
    [InlineData("Acme.Ordering.Contracts", "true")]
    [InlineData("Acme.Ordering.Domain", "")]
    [InlineData("Acme.Contracts.Domain", "")]
    [InlineData("Contracts", "")]
    [InlineData("Acme.Ordering.ContractsTests", "")]
    public async Task The_tip_in_the_docs_makes_the_projects_whose_name_ends_in_Contracts_contracts_and_no_other(string project, string expected)
        => (await EvaluateAsync(project, Tip())).ModuleContracts.Should().Be(expected);

    /// <summary>
    /// The snippet docs/modules.md gives as the tip: the one XML block of the page that sets <c>DDD_ModuleContracts</c>
    /// on a condition on the project's name.
    /// </summary>
    private static string Tip()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "modules.md")).ReplaceLineEndings("\n");
        var blocks = Regex.Matches(page, "```xml\n(.*?)```", RegexOptions.Singleline)
            .Select(match => match.Groups[1].Value)
            .Where(block => block.Contains("<DDD_ModuleContracts>", StringComparison.Ordinal) && block.Contains("MSBuildProjectName", StringComparison.Ordinal))
            .ToList();

        blocks.Should().ContainSingle("docs/modules.md gives the tip once");
        return blocks[0];
    }

    /// <summary>
    /// Evaluates a project named <paramref name="name"/> that imports the props file and holds <paramref name="content"/>:
    /// the value <c>DDD_ModuleContracts</c> comes to, and the properties the compiler hands the generators.
    /// </summary>
    private async Task<(string ModuleContracts, List<string?> Visible)> EvaluateAsync(string name, string content)
    {
        var props = Path.Combine(RepositoryRoot(), "Source", "DDDToolkit.Analyzers", "build", "DDDToolkit.Analyzers.props");
        var project = Path.Combine(_folder, name + ".proj");

        File.WriteAllText(
            project,
            $"""
            <Project>
              <Import Project="{System.Security.SecurityElement.Escape(props)}" />
              {content}
            </Project>
            """);

        var (exitCode, output) = await RunAsync("msbuild", project, "-getProperty:DDD_ModuleContracts", "-getItem:CompilerVisibleProperty", "-nologo", "-nodeReuse:false", "-noAutoResponse");
        exitCode.Should().Be(0, output);

        var root = JsonDocument.Parse(output).RootElement;
        return (
            root.GetProperty("Properties").GetProperty("DDD_ModuleContracts").GetString()!,
            [.. root.GetProperty("Items").GetProperty("CompilerVisibleProperty").EnumerateArray().Select(item => item.GetProperty("Identity").GetString())]);
    }

    private async Task<(int ExitCode, string Output)> RunAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo(DotnetHost())
        {
            WorkingDirectory = _folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
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

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
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

    /// <summary>The dotnet that runs this test.</summary>
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

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DDDToolkit.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No DDDToolkit.slnx above '{AppContext.BaseDirectory}'.");
    }
}
