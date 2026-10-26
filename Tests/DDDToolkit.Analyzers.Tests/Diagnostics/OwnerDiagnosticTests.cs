using System.Globalization;
using DDDToolkit.Analyzers.Tests.Generation;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00087 and DDD00088: <c>[Owner]</c> marks the one owner of an aggregate root, of a type the caller has an own of. Told
/// where the property is declared, for what any project can tell, and at a request that names the aggregate, for what
/// only the requests' project can: whether something says the caller's own of the owner's type. Each is an error, since a
/// request that reads as if the owner came through, and lets nobody through, is a door a review would misread.
/// </summary>
public class OwnerDiagnosticTests
{
    /// <summary>A domain of its own, compiled with the core generators.</summary>
    private static GeneratorRunOutcome Declared(string domain)
        => GeneratorTestHost.Create(
                """
                using System;
                using System.Collections.Generic;
                using DDDToolkit.Abstractions.Attributes;

                namespace Shop.Boards;

                [EntityId<Guid>] public readonly partial record struct BoardId;
                [EntityId<Guid>] public readonly partial record struct ColumnId;
                [EntityId<Guid>] public readonly partial record struct UserId;

                """ + domain,
                "Domain.cs")
            .RunCore();

    private static string Message(GeneratorRunOutcome result, string id, string at)
        => result.ShouldHaveDiagnostic(id, at).GetMessage(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ DDD00087: the one owner of an aggregate root

    [Fact]
    public void The_one_owner_of_an_aggregate_root_of_a_type_with_an_own_is_no_diagnostic_and_may_be_nullable()
    {
        Declared(
                """
                [AggregateRoot<BoardId>]
                public sealed partial class Board
                {
                    [Owner] public UserId? Owner { get; private set; }
                }
                """)
            .ShouldHaveExactlyDiagnostics();
    }

    [Theory]
    [InlineData(
        """
        [AggregateRoot<BoardId>]
        public sealed partial class Board
        {
            public partial IReadOnlyList<Column> Columns { get; }
        }

        [Entity<ColumnId>]
        public sealed partial class Column
        {
            [Owner] public UserId Keeper { get; private set; }
        }
        """,
        "Keeper", "'Column' is an entity inside an aggregate")]
    [InlineData(
        """
        public sealed class BoardView
        {
            [Owner] public UserId Owner { get; init; }
        }
        """,
        "Owner", "'BoardView' is no aggregate root")]
    [InlineData(
        """
        [AggregateRoot<BoardId>]
        public sealed partial class Board
        {
            [Owner] public static UserId Everyone { get; } = default;
        }
        """,
        "Everyone", "is static")]
    public void Owner_on_anything_but_an_instance_property_of_an_aggregate_root_is_DDD00087(string domain, string at, string said)
    {
        var result = Declared(domain);

        result.ShouldHaveExactlyDiagnostics("DDD00087");
        Message(result, "DDD00087", at: at).Should().Contain(said);
        result.ShouldHaveDiagnostic("DDD00087", at: at).Severity.Should().Be(DiagnosticSeverity.Error);
    }

    [Fact]
    public void Two_owners_on_one_aggregate_are_DDD00087_once_at_the_second()
    {
        var result = Declared(
            """
            [AggregateRoot<BoardId>]
            public sealed partial class Board
            {
                [Owner] public UserId Owner { get; private set; }

                [Owner] public UserId CoOwner { get; private set; }
            }
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00087");
        result.Count("DDD00087").Should().Be(1, "the first written is the one, and the other is told");
        Message(result, "DDD00087", at: "CoOwner").Should().Contain("is marked [Owner] beside 'Owner': an aggregate has one owner").And.Contain("Membership");
    }

    // ------------------------------------------------------------------ DDD00088: a type the caller has an own of

    [Theory]
    [InlineData("[Owner] public string Owner { get; private set; } = string.Empty;", "Owner", "is a 'string'")]
    [InlineData("[Owner] public Person Owner { get; private set; } = null!;", "Owner", "is a 'Person'")]
    [InlineData("[Owner] public Shift Owner { get; private set; }", "Owner", "is a 'Shift'")]
    public void An_owner_no_handle_could_ever_say_the_caller_s_own_of_is_DDD00088_where_it_is_declared(string property, string at, string said)
    {
        var result = Declared(
            """
            public sealed class Person { }

            public enum Shift { Day, Night }

            [AggregateRoot<BoardId>]
            public sealed partial class Board
            {
            """ + property + """

            }
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00088");
        Message(result, "DDD00088", at: at).Should().Contain(said).And.Contain("a struct that is IEquatable of itself");
    }

    [Fact]
    public void A_request_about_an_owner_whose_type_nothing_says_the_caller_s_own_of_is_DDD00088_and_gets_no_line()
    {
        // The board's owner is the application's UserId, and no handle of it is beside the requests: the domain cannot tell
        // that, the requests' project can.
        var result = OwnerRequirementGenerationTests.OverTheDomain("[OwnOnly] public sealed record ArchiveBoard([Own] BoardId Board) : IBillingRequest;").RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00088");
        Message(result, "DDD00088", at: "Own").Should().Contain("marks 'Board' [Own], the id of 'Board', whose [Owner] 'Owner' is a 'UserId': nothing says what the caller's own UserId is")
            .And.Contain("services.AddCallersOwn<UserId, ...>()");
        result.ShouldNotContain("BillingAccessBehavior.", "ArchiveBoard", "a request stopped every time it is sent gets no line");
    }

    // ------------------------------------------------------------------ and what stays DDD00086

    [Fact]
    public void The_id_of_an_aggregate_that_marks_no_owner_stays_DDD00086_and_says_where_the_owner_would_be_marked()
    {
        var result = OwnerRequirementGenerationTests.OverTheDomain("[OwnOnly] public sealed record CloseLedger([Own] LedgerId Ledger) : IBillingRequest;").RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00086");
        Message(result, "DDD00086", at: "Own").Should().Contain("nothing says what the caller's own LedgerId is")
            .And.Contain("for the id of 'Ledger', a request about its owner, mark the property of 'Ledger' that names its owner [Owner]");
    }

    [Fact]
    public void A_member_that_may_be_null_stays_DDD00086_whatever_its_aggregate_marks()
    {
        var result = OwnerRequirementGenerationTests.OverTheDomain(OwnerRequirementGenerationTests.CallersUser + "[OwnOnly] public sealed record ArchiveBoard([Own] BoardId? Board) : IBillingRequest;").RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00086");
        Message(result, "DDD00086", at: "Own").Should().Contain("which is 'BoardId?' and may be null");
    }

    [Fact]
    public void An_id_of_two_aggregates_that_each_mark_an_owner_is_DDD00087_at_the_request()
    {
        var result = OwnerRequirementGenerationTests.OverTheDomain(
            OwnerRequirementGenerationTests.CallersUser + "[OwnOnly] public sealed record ArchiveBoard([Own] BoardId Board) : IBillingRequest;",
            OwnerRequirementGenerationTests.Owned + "\n[AggregateRoot<BoardId>]\npublic sealed partial class Whiteboard\n{\n    [Owner] public UserId Owner { get; private set; }\n}\n").RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00087");
        Message(result, "DDD00087", at: "Own").Should().Contain("the id of 'Board' and 'Whiteboard', which each mark an [Owner]");
    }
}
