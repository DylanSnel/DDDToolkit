using System.Text.RegularExpressions;
using FluentAssertions;

namespace Examples.Webshop.Tests.Architecture;

/// <summary>What the shop's source files have to hold to, read from the files themselves.</summary>
public sealed partial class SourceTreeTests
{
    /// <summary>
    /// The shop is an application, and an application is not in the toolkit's namespace. Inside
    /// <c>DDDToolkit.*</c> a plain <c>HotChocolate</c> is the toolkit's <c>DDDToolkit.HotChocolate</c> and not
    /// HotChocolate's own, which the code HotChocolate generates for an <c>[ObjectType&lt;T&gt;]</c> class cannot
    /// know. So no file of the shop's modules, of its hosts or of their tests declares a namespace that starts
    /// with the toolkit's.
    /// </summary>
    [Fact]
    public void No_file_of_the_shop_declares_a_namespace_of_the_toolkit()
    {
        var root = RepositoryRoot();
        var examples = Path.Combine(root, "Examples");
        var folders = Directory.GetDirectories(examples, "ModularMonolith.*")
            .Concat(Directory.GetDirectories(examples, "Microservices.*"))
            .Append(Path.Combine(examples, "Modules"))
            .Append(Path.Combine(root, "Tests", "Examples.Webshop.Tests"))
            .Append(Path.Combine(root, "Tests", "Examples.AppHost.Tests"));

        var declared = folders
            .SelectMany(folder => Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(file => !file.Contains("/bin/", StringComparison.Ordinal) && !file.Contains("/obj/", StringComparison.Ordinal))
            .SelectMany(file => NamespaceDeclaration().Matches(File.ReadAllText(Path.Combine(root, file)))
                .Select(match => (File: file, Namespace: match.Groups["name"].Value)))
            .ToList();

        declared.Select(found => found.Namespace)
            .Should().Contain("Examples.Webshop.Ordering.Domain.Orders", "the scan reads the modules")
            .And.Contain("Examples.Webshop.Pgmq.Storefront.GraphQL", "and the hosts")
            .And.Contain("Examples.AppHost.Tests", "and the tests");
        declared.Where(found => found.Namespace == "DDDToolkit" || found.Namespace.StartsWith("DDDToolkit.", StringComparison.Ordinal))
            .Select(found => $"{found.File}: {found.Namespace}")
            .Should().BeEmpty("the shop's namespaces begin with Examples, as its projects' names do");
    }

    /// <summary>The namespace a file declares: file-scoped, or with braces as a migration's files have it.</summary>
    [GeneratedRegex(@"^namespace\s+(?<name>[\w.]+)\s*[;{]?\s*$", RegexOptions.Multiline)]
    private static partial Regex NamespaceDeclaration();

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
