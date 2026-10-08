using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Tests.Conventions;

/// <summary>
/// A member is named after what it does. <c>Of</c> said nothing about that: <c>throw TenancyRefusals.Of(code)</c>
/// made a refusal, <c>ActedBy.Of(caller)</c> turned a caller into who did something, and
/// <c>DomainEventName.Of(type)</c> looked a name up. The packages say <c>Refuse</c> for a method that makes a
/// refusal, <c>From</c> for one that turns a value into another and <c>For</c> for one that looks something up,
/// and these tests keep a member called <c>Of</c> from coming back where a developer meets it: in what a package
/// shows, and anywhere in the samples, which applications copy.
/// <para>
/// The code is read as written, so a member is found whichever project it is in and whatever that project
/// references: every member that has a name, a method as much as a property, a field, an event, a constant, an
/// enum value or a record's positional property. A helper a package keeps to itself, internal or private, is
/// nobody else's to read and may keep its name.
/// </para>
/// <para>
/// The samples are read with their test projects (<c>Tests/Examples.*</c>) and the files those compile from other
/// projects: the docs send a reader to the sample's tests, and its architecture tests and their helpers are
/// copied with it.
/// </para>
/// </summary>
public class MemberNameTests
{
    [Fact]
    public void No_member_a_package_shows_is_named_Of()
    {
        var files = Parsed(CodeIn(Path.Combine(RepositoryRoot(), "Source")));

        files.Should().NotBeEmpty("the packages' code is under Source/");
        Declared(files, shownOnly: true).Should().BeEmpty(
            "a member a package shows is named after what it does: Refuse makes a refusal, From turns a value into another, For looks something up");
    }

    [Fact]
    public void No_member_of_the_samples_or_of_their_tests_is_named_Of()
    {
        var files = Parsed(SampleFiles());

        files.Should().Contain(file => file.Path.StartsWith("Examples/", StringComparison.Ordinal), "the samples' code is under Examples/");
        files.Should().Contain(file => file.Path.StartsWith("Tests/Examples.Tenancy.Tests/", StringComparison.Ordinal), "the sample's tests are read with it");
        files.Should().Contain(file => file.Path == "Tests/DDDToolkit.HotChocolate.Tests/Infrastructure/WholeBatches.cs", "a file the sample's tests compile from another project is read with them");
        Declared(files, shownOnly: false).Should().BeEmpty(
            "an application copies the samples, their tests and internal helpers included, so they name a member after what it does as the packages do");
    }

    [Fact]
    public void The_check_finds_a_member_named_Of_whatever_kind_of_member_it_is()
    {
        var probe = ("probe.cs", CSharpSyntaxTree.ParseText("""
            public interface IRefusals { System.Exception Of(string code); }
            public static class Codes { public static System.Exception Of(string code) => new(code); }
            public static class Names { public static System.Func<string, string> Of { get; } = name => name; }
            public static class Texts { public const string Of = "of"; }
            public class Batches { public event System.Action? Of; }
            public record Range(int Of);
            public enum Kind { Of }
            public interface IHolder { class Nested { public static string Of() => ""; } }
            internal static class Kept { public static string Of(string text) => text; }
            public class Hidden { internal static string Of(string text) => text; private protected static string Of(int number) => ""; }
            public static class Local { public static string Run() { return Of(); static string Of() => ""; } }
            """, cancellationToken: TestContext.Current.CancellationToken));

        Declared([probe], shownOnly: true).Should().Equal(
            ["probe.cs:1", "probe.cs:2", "probe.cs:3", "probe.cs:4", "probe.cs:5", "probe.cs:6", "probe.cs:7", "probe.cs:8"],
            "another assembly reaches each of them: a member of an interface and a type in one are public without saying so");
        Declared([probe], shownOnly: false).Should().Equal(
            ["probe.cs:1", "probe.cs:2", "probe.cs:3", "probe.cs:4", "probe.cs:5", "probe.cs:6", "probe.cs:7", "probe.cs:8", "probe.cs:9", "probe.cs:10", "probe.cs:10", "probe.cs:11"],
            "in the samples every member named Of counts, a helper kept to itself and a local function too");
    }

    /// <summary>
    /// Where a member named <c>Of</c> is declared, as <c>path:line</c>. With <paramref name="shownOnly"/>, only one
    /// another assembly can reach: shown itself, in types that are each shown.
    /// </summary>
    private static List<string> Declared(IReadOnlyList<(string Path, SyntaxTree Tree)> files, bool shownOnly)
    {
        // A partial type is as visible as its most visible part, which may be in another file.
        var shownTypes = files
            .SelectMany(file => file.Tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            .Where(type => IsShown(type.Modifiers, type.Parent))
            .Select(TypeKey)
            .ToHashSet(StringComparer.Ordinal);

        return [.. files.SelectMany(file => Named(file.Tree.GetRoot())
            .Where(member => member.Name.ValueText == "Of")
            .Where(member => !shownOnly || (IsShown(member.Node) && member.Node.Ancestors().OfType<BaseTypeDeclarationSyntax>().All(type => shownTypes.Contains(TypeKey(type)))))
            .Select(member => file.Path + ":" + (member.Name.GetLocation().GetLineSpan().StartLinePosition.Line + 1)))];
    }

    /// <summary>
    /// Every member declared under <paramref name="root"/> with the name it declares: a field declaration names
    /// each of its variables, a record's positional parameter is a property, and a local function is a method only
    /// its own method calls.
    /// </summary>
    private static IEnumerable<(SyntaxNode Node, SyntaxToken Name)> Named(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case MethodDeclarationSyntax method:
                    yield return (node, method.Identifier);
                    break;
                case PropertyDeclarationSyntax property:
                    yield return (node, property.Identifier);
                    break;
                case EventDeclarationSyntax declared:
                    yield return (node, declared.Identifier);
                    break;
                case BaseFieldDeclarationSyntax field:
                    foreach (var variable in field.Declaration.Variables)
                    {
                        yield return (node, variable.Identifier);
                    }

                    break;
                case EnumMemberDeclarationSyntax value:
                    yield return (node, value.Identifier);
                    break;
                case ParameterSyntax { Parent.Parent: RecordDeclarationSyntax } positional:
                    yield return (node, positional.Identifier);
                    break;
                case LocalFunctionStatementSyntax local:
                    yield return (node, local.Identifier);
                    break;
            }
        }
    }

    /// <summary>
    /// Whether a member can be seen from another assembly, as far as it goes itself: an enum value and a record's
    /// positional property are as visible as their type, a local function never is.
    /// </summary>
    private static bool IsShown(SyntaxNode member) => member switch
    {
        EnumMemberDeclarationSyntax or ParameterSyntax => true,
        MemberDeclarationSyntax declared => IsShown(declared.Modifiers, declared.Parent),
        _ => false,
    };

    /// <summary>
    /// Whether a member or a type with these modifiers, declared in <paramref name="parent"/>, can be seen from
    /// another assembly, as far as it goes itself. Without an access modifier, what an interface declares is public,
    /// and anything else private or internal.
    /// </summary>
    private static bool IsShown(SyntaxTokenList modifiers, SyntaxNode? parent)
        => !modifiers.Any(SyntaxKind.PrivateKeyword)
           && (modifiers.Any(SyntaxKind.PublicKeyword)
               || modifiers.Any(SyntaxKind.ProtectedKeyword)
               || (parent is InterfaceDeclarationSyntax && !modifiers.Any(SyntaxKind.InternalKeyword)));

    /// <summary>A type's namespace and the names of the types around it, the same in every part of a partial type.</summary>
    private static string TypeKey(BaseTypeDeclarationSyntax type)
    {
        var names = type.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().Reverse()
            .Select(each => each.Identifier.ValueText + (each is TypeDeclarationSyntax { TypeParameterList: { } parameters } ? "`" + parameters.Parameters.Count : ""));
        var space = type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(each => each.Name.ToString());

        return string.Join(".", space) + "::" + string.Join("+", names);
    }

    /// <summary>
    /// The samples' code, and that of their test projects with every file such a project compiles from another
    /// project (a <c>Compile Include</c>), each file once.
    /// </summary>
    private static IEnumerable<string> SampleFiles()
    {
        var root = RepositoryRoot();
        var testProjects = Directory.EnumerateDirectories(Path.Combine(root, "Tests"), "Examples.*")
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.csproj"))
            .ToList();
        var compiledFromElsewhere = testProjects.SelectMany(project => XDocument.Load(project).Descendants()
            .Where(element => element.Name.LocalName == "Compile")
            .Select(element => (string?)element.Attribute("Include"))
            .OfType<string>()
            .Select(include => Path.Combine(Path.GetDirectoryName(project)!, include.Replace('\\', Path.DirectorySeparatorChar))));

        return CodeIn(Path.Combine(root, "Examples"))
            .Concat(testProjects.SelectMany(project => CodeIn(Path.GetDirectoryName(project)!)))
            .Concat(compiledFromElsewhere)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every C# file under <paramref name="folder"/>, without what a build wrote.</summary>
    private static IEnumerable<string> CodeIn(string folder)
        => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    /// <summary><paramref name="paths"/>, parsed, each named by its path from the repository's root.</summary>
    private static List<(string Path, SyntaxTree Tree)> Parsed(IEnumerable<string> paths)
    {
        var root = RepositoryRoot();
        return [.. paths.Select(path => (Path.GetRelativePath(root, path).Replace('\\', '/'), CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)))];
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
