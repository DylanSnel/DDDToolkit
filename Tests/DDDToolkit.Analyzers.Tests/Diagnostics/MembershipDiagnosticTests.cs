using System.Globalization;
using DDDToolkit.Analyzers.Analyzers;
using DDDToolkit.Supporting.Membership.Analyzers;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// What is said of a resource with members and of the permissions on it, where it is declared: the name a resource is
/// given (DDD00091, and the plural it is named by), a permission of a <c>[Permissions]</c> class (DDD00092), the level
/// <c>[On&lt;TResource&gt;]</c> and its settings (DDD00093), rules made for a resource nothing is declared on (DDD00094),
/// <c>[OrOwn]</c> beside a permission the owner holds already (DDD00096), <c>[At]</c> (DDD00097), and rules that reach from
/// above a resource that marks no place (DDD00098).
/// </summary>
public class MembershipDiagnosticTests
{
    private const string Ids =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;

        namespace Shop;

        [EntityId<Guid>] public readonly partial record struct UserId;
        [EntityId<Guid>] public readonly partial record struct BoardId;
        [EntityId<Guid>] public readonly partial record struct BoardMemberId;
        [EntityId<Guid>] public readonly partial record struct ColumnId;
        [EntityId<Guid>] public readonly partial record struct UnitId;
        """;

    /// <summary>A board with members, its class named <paramref name="name"/>, with whatever else the test says.</summary>
    private static string Board(string body = "", string attributes = "", string name = "Board")
        => $$"""
             using System;
             using System.Collections.Generic;
             using DDDToolkit.Abstractions.Attributes;
             using DDDToolkit.Access;
             using DDDToolkit.Supporting.Membership;
             using DDDToolkit.Supporting.Membership.Access;

             namespace Shop;

             [Member<BoardMemberId, UserId, NamedRole, {{name}}>]
             public sealed partial class BoardMember;

             {{attributes}}
             [AggregateRoot<BoardId>]
             public sealed partial class {{name}}
             {
                 public {{name}}(BoardId id, UserId owner, DateTimeOffset now) : base(id)
                 {
                     OwnerId = owner;
                     Members.Open(now);
                 }

                 [Owner] public UserId OwnerId { get; private set; }

                 public partial IReadOnlyList<BoardMember> BoardMembers { get; }

             {{body}}
             }
             """;

    /// <summary>The board's permissions, each level the test's to change, and whatever else it adds to the class.</summary>
    private static string PermissionsWith(
        string view = "[On<Board>(EveryMember = true)]",
        string edit = "[On<Board>]",
        string handOn = "[On<Board>(Owner = false, InRoles = false)]",
        string handOnPermission = "boards.hand-on",
        string modifiers = "public static",
        string extra = "")
        => $$"""
             using DDDToolkit.Access;
             using DDDToolkit.Supporting.Membership.Access;

             namespace Shop;

             [Permissions]
             {{modifiers}} class BoardPermissions
             {
                 /// <summary>See a board.</summary>
                 {{view}}
                 public const string View = "boards.view";

                 /// <summary>Change a board.</summary>
                 {{edit}}
                 public const string Edit = "boards.edit";

                 /// <summary>Hand a board on.</summary>
                 {{handOn}}
                 public const string HandOn = "{{handOnPermission}}";

             {{extra}}
             }
             """;

    private static string Permissions => PermissionsWith();

    private static GeneratorRunOutcome Run(params string[] sources)
    {
        var host = GeneratorTestHost.Create(Ids, "Ids.cs");
        for (var index = 0; index < sources.Length; index++)
        {
            host = host.WithSource(sources[index], "Source" + index + ".cs");
        }

        return host.WithMembershipAlone()
            .WithAnalyzers(new PermissionsAnalyzer(), new OwnAlternativeAnalyzer())
            .RunCoreAnd(GeneratorTestHost.MemberListGenerators());
    }

    private static string Message(GeneratorRunOutcome result, string id, string at)
        => result.ShouldHaveDiagnostic(id, at).GetMessage(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ names

    [Theory]
    [InlineData("Board", "boards")]
    [InlineData("CraneBoard", "crane-boards")]
    [InlineData("Category", "categories")]
    [InlineData("Box", "boxes")]
    [InlineData("Day", "days")]
    [InlineData("HTMLPage", "html-pages")]
    public void A_resource_is_named_after_its_class_made_plural_on_its_last_word(string name, string expected)
    {
        var result = Run(Board(name: name));

        result.ShouldNotHaveDiagnostic("DDD00091");
        result.ShouldContain(name + ".Membership.", "MembershipName => \"" + expected + "\";");
        result.ShouldContain(name + ".Membership.", "MembershipCodes.Under(\"" + expected + "\")");
    }

    [Theory]
    [InlineData("People", "people")]
    [InlineData("CraneBoards", "crane-boards")]
    [InlineData("crane-boards", "crane-boards")]
    [InlineData("crane_boards", "crane-boards")]
    public void Members_names_a_resource_as_it_says_converted_and_not_made_plural(string said, string expected)
    {
        var result = Run(Board(attributes: "[Members(\"" + said + "\")]"));

        result.ShouldNotHaveDiagnostic("DDD00091");
        result.ShouldContain("Board.Membership.", "MembershipName => \"" + expected + "\";");
    }

    [Theory]
    [InlineData("Crane boards")]
    [InlineData("1st-boards")]
    [InlineData("boards!")]
    [InlineData("boards-")]
    public void A_name_that_cannot_be_converted_is_DDD00091_and_nothing_of_the_resource_is_written(string said)
    {
        var result = Run(Board(attributes: "[Members(\"" + said + "\")]"));

        Message(result, "DDD00091", "Members(\"" + said + "\")").Should()
            .Contain("'Board' is named '" + said + "' with [Members], which cannot be converted to a code and a function's name");
        result.ShouldNotHaveGeneratedFor("Board.Membership.");
    }

    // ------------------------------------------------------------------ DDD00092: a permission of a [Permissions] class

    [Fact]
    public void Permissions_of_the_shape_the_generators_read_are_no_diagnostic()
    {
        Run(Board(), Permissions).ShouldNotHaveDiagnostic("DDD00092").ShouldNotHaveDiagnostic("DDD00093");
    }

    [Fact]
    public void A_permission_on_a_resource_with_no_description_is_DDD00099_naming_its_level()
    {
        var result = Run(Board(), PermissionsWith(extra:
            """
                [On<Board>]
                public const string Pin = "boards.pin";
            """));

        Message(result, "DDD00099", "Pin").Should()
            .Be("'BoardPermissions.Pin' has no description, and the screens that put permissions into roles show it: say it on its level, [On<Board>(Description = \"...\")], or in the constant's <summary>");
    }

    [Fact]
    public void A_permissions_class_that_is_not_static_is_DDD00092()
    {
        var result = Run(Board(), PermissionsWith(modifiers: "public"));

        Message(result, "DDD00092", "BoardPermissions").Should().Contain("is marked [Permissions] and is not a static class");
    }

    [Fact]
    public void A_constant_without_a_level_a_level_on_no_constant_and_a_level_outside_the_class_are_DDD00092()
    {
        var result = Run(
            Board(),
            PermissionsWith(extra:
                """
                    public const string Comment = "boards.comment";

                    [On<Board>]
                    public static readonly string Archive = "boards.archive";
                """),
            """
            using DDDToolkit.Supporting.Membership.Access;

            namespace Shop;

            public static class MoreBoardPermissions
            {
                [On<Board>]
                public const string Pin = "boards.pin";
            }
            """);

        Message(result, "DDD00092", "Comment").Should().Contain("is a permission without a level");
        var levels = result.ReportedDiagnostics.Where(diagnostic => diagnostic.Id == "DDD00092").Select(diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)).ToList();
        levels.Should().Contain(message => message.Contains("is no public const string"));
        levels.Should().Contain(message => message.Contains("'MoreBoardPermissions' is not marked [Permissions]"));
        levels.Should().HaveCount(3);
    }

    [Fact]
    public void A_permission_declared_twice_in_one_class_is_DDD00092_on_the_second()
    {
        var result = Run(Board(), PermissionsWith(handOnPermission: "boards.edit"));

        Message(result, "DDD00092", "HandOn").Should().Contain("declares 'boards.edit', which 'Edit' declares already");
    }

    // ------------------------------------------------------------------ DDD00093: the level fits what it is asked on

    [Fact]
    public void On_a_class_that_is_no_resource_with_members_is_DDD00093()
    {
        var result = Run(
            Board(),
            Permissions,
            """
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Access;
            using DDDToolkit.Supporting.Membership.Access;

            namespace Shop;

            [AggregateRoot<ColumnId>]
            public sealed partial class Column
            {
                public Column(ColumnId id) : base(id) { }
            }

            [Permissions]
            public static class ColumnPermissions
            {
                /// <summary>Move a column.</summary>
                [On<Column>]
                public const string Move = "columns.move";
            }
            """);

        Message(result, "DDD00093", "On<Column>").Should().Contain("'Column' is no resource with members");
    }

    [Theory]
    [InlineData("EveryMember", "View")]
    [InlineData("LocksMembers", "Edit")]
    [InlineData("LocksOwner", "Edit")]
    public void A_second_permission_with_a_setting_that_names_the_one_permission_is_DDD00093(string setting, string first)
    {
        var second = "On<Board>(Owner = false, InRoles = false, " + setting + " = true)";
        var result = Run(
            Board(),
            PermissionsWith(
                edit: setting == "EveryMember" ? "[On<Board>]" : "[On<Board>(" + setting + " = true)]",
                handOn: "[" + second + "]"));

        Message(result, "DDD00093", second).Should().Contain("is marked " + setting + " on Board, and so is 'BoardPermissions." + first + "'");
    }

    // ------------------------------------------------------------------ DDD00094 and DDD00098: the rules

    private static string Rules(string arguments)
        => $$"""
             using DDDToolkit.Supporting.Membership.Access;

             namespace Shop;

             public static class BoardMembership
             {
                 public static MembershipRules Rules { get; } = MembershipRules.For<Board>({{arguments}});
             }
             """;

    [Fact]
    public void Rules_made_for_a_resource_its_permissions_are_declared_on_are_no_diagnostic()
    {
        Run(Board(), Permissions, Rules("Roles.Custom()"))
            .ShouldNotHaveDiagnostic("DDD00094")
            .ShouldNotHaveDiagnostic("DDD00098");
    }

    [Fact]
    public void Rules_made_for_a_resource_no_permission_is_declared_on_are_DDD00094()
    {
        var result = Run(Board(), Rules("Roles.Custom()"));

        Message(result, "DDD00094", "For<Board>").Should().Contain("finds no permission declared on 'Board'");
    }

    [Fact]
    public void Rules_that_reach_a_resource_from_above_that_marks_no_place_are_DDD00098()
    {
        var result = Run(Board(), Permissions, Rules("Roles.Custom(), reachFromAbove: true"));

        Message(result, "DDD00098", "reachFromAbove: true").Should().Contain("'Board' marks no property [At]");
    }

    [Fact]
    public void Rules_that_reach_a_resource_from_above_that_marks_its_place_are_no_diagnostic()
    {
        Run(Board("    [At] public UnitId UnitId { get; private set; }"), Permissions, Rules("Roles.Custom(), reachFromAbove: true"))
            .ShouldNotHaveDiagnostic("DDD00098")
            .ShouldNotHaveDiagnostic("DDD00097");
    }

    // ------------------------------------------------------------------ DDD00097: [At]

    [Theory]
    [InlineData("    [At] public static UnitId Everywhere { get; } = default;", "Everywhere", "is static, and marked [At]")]
    [InlineData("    [At] public UnitId? UnitId { get; private set; }", "UnitId", "may be empty")]
    [InlineData("    [At] public UnitId UnitId { get; private set; } [At] public UnitId FormerUnitId { get; private set; }", "FormerUnitId", "is marked [At] beside 'UnitId'")]
    public void An_at_that_is_no_one_place_an_aggregate_root_always_hangs_at_is_DDD00097(string body, string at, string says)
    {
        var result = Run(Board(body));

        Message(result, "DDD00097", at).Should().Contain(says);
    }

    [Fact]
    public void An_at_on_an_entity_inside_an_aggregate_is_DDD00097()
    {
        var result = Run(
            Board(),
            """
            using DDDToolkit.Abstractions.Attributes;

            namespace Shop;

            [Entity<ColumnId>]
            public sealed partial class Column
            {
                [At] public UnitId UnitId { get; private set; }
            }
            """);

        Message(result, "DDD00097", "UnitId").Should().Contain("'Column' is no aggregate root");
    }

    // ------------------------------------------------------------------ DDD00096: [OrOwn] beside what the owner holds

    private static string Request(string permission)
        => $$"""
             using DDDToolkit.Abstractions.Attributes;
             using DDDToolkit.Access;
             using DDDToolkit.Supporting.Membership.Access;

             namespace Shop;

             [AccessRequests]
             public partial interface IBoardsRequest : IRequireAccess;

             [RequiresPermission({{permission}})]
             public sealed record RenameBoard([OnResource, OrOwn] BoardId Board, string Name) : IBoardsRequest;
             """;

    [Fact]
    public void OrOwn_on_a_resource_beside_a_permission_its_owner_holds_is_DDD00096()
    {
        var result = Run(Board(), Permissions, Request("BoardPermissions.Edit"));

        Message(result, "DDD00096", "OrOwn").Should()
            .Be("[OrOwn] on 'Board' says nothing more: the owner of the Board holds 'boards.edit' already, which is declared [On<Board>] with Owner = true and is asked on it. Remove [OrOwn].");
    }

    [Fact]
    public void OrOwn_on_a_resource_beside_a_permission_its_owner_does_not_hold_is_no_diagnostic()
    {
        Run(Board(), Permissions, Request("BoardPermissions.HandOn")).ShouldNotHaveDiagnostic("DDD00096");
    }
}
