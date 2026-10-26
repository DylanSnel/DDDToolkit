using System.Globalization;
using DDDToolkit.Analyzers.Analyzers;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// Where a permission's description comes from, and DDD00099 where it comes from nowhere. The <c>Description</c> said
/// on a level is the one a reader of the class sees, and wins; the constant's <c>&lt;summary&gt;</c> is the fallback,
/// read from the comment above it whether the project writes a documentation file or not; a permission with neither is
/// a warning, since the screens that put permissions into roles show the description. Tenancy's generator writes what
/// it finds into the catalogue, and the core's analyzer reports what is missing, so both are run.
/// </summary>
public class PermissionDescriptionDiagnosticTests
{
    private const string Declared = "Shop.Boards.DeclaredPermissions";

    /// <summary>A module of boards whose permissions are <paramref name="body"/>.</summary>
    private static string Boards(string body)
        => $$"""
             using DDDToolkit.Abstractions.Attributes;
             using DDDToolkit.Access;
             using DDDToolkit.Supporting.Tenancy.Access;

             [assembly: Module("Boards")]

             namespace Shop.Boards;

             [Permissions]
             public static class BoardPermissions
             {
             {{body}}
             }
             """;

    private static GeneratorRunOutcome Run(string body, DocumentationMode mode = DocumentationMode.Parse)
        => GeneratorTestHost.Create(Boards(body), "BoardPermissions.cs")
            .WithAssemblyName("Shop.Boards")
            .WithTenancy()
            .WithDocumentationMode(mode)
            .WithAnalyzers(new PermissionsAnalyzer())
            .RunCoreAnd(GeneratorTestHost.TenancyGenerators());

    private static IReadOnlyList<(string Name, string Description)> Catalogued(GeneratorRunOutcome result)
        => [.. ((IReadOnlyList<Permission>)result.Emit().StaticProperty(Declared, "All")!).Select(static permission => (permission.Name, permission.Description))];

    [Fact]
    public void A_description_said_on_the_level_is_the_permissions_and_is_no_diagnostic()
    {
        var result = Run(
            """
                [AtUnit(Description = "Open a board at a unit")]
                public const string Create = "boards.create";

                [AtTenant(Description = "Make and change the roles of boards")]
                public const string ManageRoles = "boards.roles.manage";
            """);

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00099");
        Catalogued(result).Should().Equal(("boards.create", "Open a board at a unit"), ("boards.roles.manage", "Make and change the roles of boards"));
    }

    [Theory]
    [InlineData(DocumentationMode.None)]
    [InlineData(DocumentationMode.Parse)]
    [InlineData(DocumentationMode.Diagnose)]
    public void The_summary_is_the_description_where_no_level_says_one_whether_the_project_writes_a_documentation_file_or_not(DocumentationMode mode)
    {
        var result = Run(
            """
                /// <summary>Open a board at a unit.</summary>
                [AtUnit]
                public const string Create = "boards.create";

                /// <summary>What the summary says beside <see cref="Create"/>.</summary>
                [AtUnit(Description = "What the level says")]
                public const string Archive = "boards.archive";
            """,
            mode);

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00099");
        Catalogued(result).Should().Equal(("boards.create", "Open a board at a unit."), ("boards.archive", "What the level says"));
    }

    [Theory]
    [InlineData(DocumentationMode.None)]
    [InlineData(DocumentationMode.Parse)]
    public void A_permission_with_neither_is_DDD00099_a_warning_on_the_constant(DocumentationMode mode)
    {
        var result = Run(
            """
                // A plain comment says nothing to a screen.
                [AtUnit]
                public const string Create = "boards.create";

                [AtTenant(Description = "   ")]
                public const string ManageRoles = "boards.roles.manage";
            """,
            mode);

        result.ShouldCompile();
        var create = result.ShouldHaveDiagnostic("DDD00099", at: "Create");
        create.Severity.Should().Be(DiagnosticSeverity.Warning);
        create.GetMessage(CultureInfo.InvariantCulture).Should().Be(
            "'BoardPermissions.Create' has no description, and the screens that put permissions into roles show it: say it on its level, [AtUnit(Description = \"...\")], or in the constant's <summary>");
        result.ShouldHaveDiagnostic("DDD00099", at: "ManageRoles").GetMessage(CultureInfo.InvariantCulture)
            .Should().Contain("[AtTenant(Description = \"...\")]", "a description of white space alone is none");
        Catalogued(result).Should().Equal(("boards.create", string.Empty), ("boards.roles.manage", string.Empty));
    }

    [Fact]
    public void A_blank_description_falls_back_to_the_summary()
    {
        var result = Run(
            """
                /// <summary>Open a board at a unit.</summary>
                [AtUnit(Description = "")]
                public const string Create = "boards.create";
            """);

        result.ShouldNotHaveDiagnostic("DDD00099");
        Catalogued(result).Should().Equal(("boards.create", "Open a board at a unit."));
    }

    [Fact]
    public void A_retired_permission_is_shown_nowhere_and_needs_no_description()
    {
        var result = Run(
            """
                /// <summary>Open a board at a unit.</summary>
                [AtUnit]
                public const string Create = "boards.create";

                [AtUnit(Retired = true)]
                public const string Pin = "boards.pin";
            """);

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00099");
    }

    [Fact]
    public void Nothing_but_a_permission_of_a_permissions_class_is_asked_for_a_description()
    {
        // A constant without a level is DDD00092 already, and a class that is not marked holds no permissions.
        var result = Run(
            """
                [AtUnit(Description = "Open a board at a unit")]
                public const string Create = "boards.create";

                public const string Prefix = "boards.";
            """);

        result.ShouldHaveDiagnostic("DDD00092", at: "Prefix");
        result.ShouldNotHaveDiagnostic("DDD00099");
    }
}
