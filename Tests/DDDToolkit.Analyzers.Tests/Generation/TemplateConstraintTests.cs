using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// What a parent asks of the type arguments a template supplies: the attribute's own, the id first, and the
/// ids it takes from other classes. A parent that holds its ids by value says <c>where TId : struct</c>, and
/// an application that declares its id as a record class would get the parent closed over it anyway: a
/// compile error in generated code, <c>CS0453</c>, on a line nobody wrote.
/// <para>
/// DDD00053 says it on the class instead, and nothing is generated for that class. The other half is what
/// it must not say: an id declared in the project gets its interfaces from the generator, which the
/// compilation being read does not show yet, so of such an id only what its declaration settles is judged.
/// </para>
/// </summary>
public class TemplateConstraintTests
{
    private const string Usings =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;

        namespace Sample;


        """;

    private const string AnyId = "IEntityId, IEquatable<TSubscriptionId>";

    private const string StructId = "struct, " + AnyId;

    /// <summary>
    /// A package of two parents: a subscription, and an invoice that takes the subscription's id from the class
    /// declared with <c>[Subscription]</c>. Each says what it asks of the subscription's id.
    /// </summary>
    private static string Package(string subscriptionAsksOfItsId = StructId, string invoiceAsksOfTheSubscriptionId = StructId)
        => Usings +
           $$"""
           [AggregateRootBase]
           public abstract partial class SubscriptionAggregate<TSubscriptionId>
               where TSubscriptionId : {{subscriptionAsksOfItsId}};

           [AggregateRootTemplate(typeof(SubscriptionAggregate<>))]
           [AttributeUsage(AttributeTargets.Class, Inherited = false)]
           public sealed class SubscriptionAttribute<TSubscriptionId> : Attribute;

           [AggregateRootBase]
           public abstract partial class InvoiceAggregate<TInvoiceId, TSubscriptionId>
               where TInvoiceId : IEntityId, IEquatable<TInvoiceId>
               where TSubscriptionId : {{invoiceAsksOfTheSubscriptionId}};

           [AggregateRootTemplate(typeof(InvoiceAggregate<,>))]
           [TemplateArgument(1, typeof(SubscriptionAttribute<>))]
           [AttributeUsage(AttributeTargets.Class, Inherited = false)]
           public sealed class InvoiceAttribute<TInvoiceId> : Attribute;
           """;

    /// <summary>The application: a subscription and an invoice, over the subscription id the test declares.</summary>
    private static string Application(string subscriptionId)
        => Usings + subscriptionId +
           """


           [EntityId<Guid>]
           public readonly partial record struct InvoiceId;

           [Subscription<SubscriptionId>]
           public sealed partial class ShopSubscription;

           [Invoice<InvoiceId>]
           public sealed partial class ShopInvoice;
           """;

    private const string StructSubscriptionId =
        """
        [EntityId<Guid>]
        public readonly partial record struct SubscriptionId;
        """;

    private const string ClassSubscriptionId =
        """
        [EntityId<Guid>]
        public partial record SubscriptionId;
        """;

    private static GeneratorRunOutcome Run(string package, string application)
        => GeneratorTestHost.Create(package, "Package.cs").WithSource(application, "Application.cs").RunCore();

    private static bool Generated(GeneratorRunOutcome result, string typeName)
        => result.GeneratedSources.Any(source => source.HintName.StartsWith(typeName + ".", StringComparison.Ordinal));

    /// <summary>The mistake is one diagnostic on the author's code, and the compiler has nothing to add to it.</summary>
    private static void ShouldLeaveTheCompilerNothingToSay(GeneratorRunOutcome result)
    {
        result.ShouldNotCrash();
        result.CompilationErrors.Select(error => error.ToString())
            .Should().BeEmpty("no parent is written for a class whose type argument its parent refuses");
    }

    // ------------------------------------------------------------------ the id the attribute names

    [Fact]
    public void A_class_id_where_the_parent_needs_a_struct_is_DDD00053_and_nothing_else()
    {
        var result = Run(
            Package(),
            Usings + ClassSubscriptionId +
            """


            [Subscription<SubscriptionId>]
            public sealed partial class ShopSubscription;
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00053");
        var diagnostic = result.ShouldHaveDiagnostic("DDD00053", at: "ShopSubscription");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Be(
            "'ShopSubscription' is declared with [Subscription], but 'SubscriptionId' does not meet the parent's constraint on 'TSubscriptionId', which requires a struct");
        Generated(result, "ShopSubscription").Should().BeFalse("the parent closed over a class would be CS0453 in generated code");
        Generated(result, "SubscriptionId").Should().BeTrue("the id itself is a good one; it is the parent that cannot take it");
        ShouldLeaveTheCompilerNothingToSay(result);
    }

    [Fact]
    public void A_struct_id_missing_IEquatable_is_DDD00053()
    {
        // Written out in full, so nothing will be added to it: an entity id by its interface, and no more.
        var result = Run(
            Package(),
            Usings +
            """
            public readonly struct SubscriptionId : IEntityId;

            [Subscription<SubscriptionId>]
            public sealed partial class ShopSubscription;
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00053");
        result.ShouldHaveDiagnostic("DDD00053", at: "ShopSubscription").GetMessage()
            .Should().Contain("'SubscriptionId' does not meet the parent's constraint on 'TSubscriptionId'")
            .And.EndWith("which requires 'IEquatable<SubscriptionId>'");
        Generated(result, "ShopSubscription").Should().BeFalse();
        ShouldLeaveTheCompilerNothingToSay(result);
    }

    [Fact]
    public void A_valid_id_reports_nothing()
    {
        var result = Run(Package(), Application(StructSubscriptionId));

        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
        result.ShouldContain("ShopSubscription.", "global::Sample.SubscriptionAggregate<global::Sample.SubscriptionId>");
        result.ShouldContain("ShopInvoice.", "global::Sample.InvoiceAggregate<global::Sample.InvoiceId, global::Sample.SubscriptionId>");
    }

    [Fact]
    public void A_struct_id_where_the_parent_needs_a_class_is_DDD00053()
    {
        const string ClassId = "class, " + AnyId;

        var result = Run(Package(ClassId, ClassId), Application(StructSubscriptionId));

        result.ShouldHaveDiagnostic("DDD00053", at: "ShopSubscription").GetMessage().Should().EndWith("which requires a class");
        result.ShouldHaveDiagnostic("DDD00053", at: "ShopInvoice").GetMessage().Should().EndWith("which requires a class");
        result.ShouldHaveExactlyDiagnostics("DDD00053", "DDD00053");
        ShouldLeaveTheCompilerNothingToSay(result);

        Run(Package(ClassId, ClassId), Application(ClassSubscriptionId)).ShouldCompile().ReportedDiagnostics.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ an id taken from another class

    [Fact]
    public void An_id_taken_from_another_class_is_judged_by_the_parent_that_takes_it()
    {
        // The subscription's parent takes any id, so the subscription is fine; the invoice's holds it by value.
        var result = Run(Package(subscriptionAsksOfItsId: AnyId), Application(ClassSubscriptionId));

        result.ShouldHaveExactlyDiagnostics("DDD00053");
        result.ShouldHaveDiagnostic("DDD00053", at: "ShopInvoice").GetMessage().Should().Be(
            "'ShopInvoice' is declared with [Invoice], but 'SubscriptionId' does not meet the parent's constraint on 'TSubscriptionId', which requires a struct");
        Generated(result, "ShopSubscription").Should().BeTrue("its own parent takes the id as it is");
        Generated(result, "ShopInvoice").Should().BeFalse();
        ShouldLeaveTheCompilerNothingToSay(result);
    }

    [Fact]
    public void Every_class_whose_parent_refuses_the_id_says_so_itself()
    {
        // Both parents hold the id by value. Each class is told what its own parent asks, on its own line: the
        // invoice is not left without a base class and without a word because the subscription was reported.
        var result = Run(Package(), Application(ClassSubscriptionId));

        result.ShouldHaveExactlyDiagnostics("DDD00053", "DDD00053");
        result.ShouldHaveDiagnostic("DDD00053", at: "ShopSubscription").GetMessage().Should().StartWith("'ShopSubscription' is declared with [Subscription]");
        result.ShouldHaveDiagnostic("DDD00053", at: "ShopInvoice").GetMessage().Should().StartWith("'ShopInvoice' is declared with [Invoice]");
        ShouldLeaveTheCompilerNothingToSay(result);
    }

    // ------------------------------------------------------------------ what the compilation cannot show yet

    [Fact]
    public void An_id_the_generator_still_completes_is_not_held_to_what_it_does_not_show_yet()
    {
        // IEntityId<Guid> is written by the generator, so the compilation being read does not show it on the
        // id. Reporting it as missing would refuse every id an application declares next to its classes.
        const string GuidId = "struct, IEntityId, IEntityId<Guid>, IEquatable<TSubscriptionId>";

        var result = Run(Package(GuidId, GuidId), Application(StructSubscriptionId));

        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
    }

    [Fact]
    public void An_id_from_a_referenced_project_is_held_to_every_constraint()
    {
        // A contracts project is compiled already: what its id is, it shows, and nothing will be added.
        const string GuidId = "struct, IEntityId, IEntityId<Guid>, IEquatable<TSubscriptionId>";

        static string Contracts(string value) =>
            $$"""
            using DDDToolkit.Abstractions.Attributes;

            namespace Sample;

            [EntityId<{{value}}>]
            public readonly partial record struct SubscriptionId;
            """;

        GeneratorRunOutcome WithContracts(string value)
            => GeneratorTestHost.Create(Package(GuidId, GuidId), "Package.cs")
                .WithSource(Application(string.Empty), "Application.cs")
                .WithReferencedAssembly(Contracts(value), "Sample.Contracts")
                .RunCore();

        var result = WithContracts("int");

        result.ShouldHaveDiagnostic("DDD00053", at: "ShopSubscription").GetMessage().Should().EndWith("which requires 'IEntityId<Guid>'");
        result.ShouldHaveDiagnostic("DDD00053", at: "ShopInvoice").GetMessage().Should().EndWith("which requires 'IEntityId<Guid>'");
        result.ShouldHaveExactlyDiagnostics("DDD00053", "DDD00053");
        ShouldLeaveTheCompilerNothingToSay(result);

        var over = WithContracts("System.Guid");
        over.ReportedDiagnostics.Should().BeEmpty();
        over.ShouldCompile();
    }

    [Theory]
    [InlineData(
        "unmanaged, " + AnyId,
        "public readonly struct SubscriptionId : IEntityId, IEquatable<SubscriptionId> { public string Text { get; init; } public bool Equals(SubscriptionId other) => Text == other.Text; }",
        "an unmanaged type")]
    [InlineData(
        "class, " + AnyId + ", new()",
        "public sealed class SubscriptionId : IEntityId, IEquatable<SubscriptionId> { private SubscriptionId() { } public bool Equals(SubscriptionId? other) => ReferenceEquals(this, other); }",
        "a public parameterless constructor")]
    public void A_type_written_out_in_full_is_held_to_the_other_constraints_too(string asked, string id, string requirement)
    {
        var result = Run(
            Package(asked, AnyId),
            Usings + id +
            """


            [Subscription<SubscriptionId>]
            public sealed partial class ShopSubscription;
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00053");
        result.ShouldHaveDiagnostic("DDD00053", at: "ShopSubscription").GetMessage().Should().EndWith("which requires " + requirement);
        ShouldLeaveTheCompilerNothingToSay(result);
    }

    [Theory]
    [InlineData("public partial struct SubscriptionId;", "DDD00003")]
    [InlineData("public partial class SubscriptionId;", "DDD00003")]
    [InlineData("public readonly record struct SubscriptionId;", "DDD00005")]
    [InlineData("public sealed partial record SubscriptionId;", "DDD00013")]
    public void An_id_the_generator_refuses_is_held_to_every_constraint_because_nothing_will_complete_it(string declaration, string refusal)
    {
        // The id's own diagnostic says what is wrong with it. Nothing is written for it, so IEntityId never
        // comes, and a parent closed over it would add CS0315 in generated code to an error that is clear.
        var result = Run(
            Package(subscriptionAsksOfItsId: AnyId),
            Usings + "[EntityId<Guid>]\n" + declaration +
            """


            [Subscription<SubscriptionId>]
            public sealed partial class ShopSubscription;
            """);

        result.ShouldHaveExactlyDiagnostics(refusal, "DDD00053");
        result.ShouldHaveDiagnostic(refusal, at: "SubscriptionId");
        result.ShouldHaveDiagnostic("DDD00053", at: "ShopSubscription").GetMessage().Should().EndWith("which requires 'IEntityId'");
        Generated(result, "ShopSubscription").Should().BeFalse();
        ShouldLeaveTheCompilerNothingToSay(result);
    }

    // ------------------------------------------------------------------ further type arguments

    [Theory]
    [InlineData("string", "a struct")]
    [InlineData("int", "'Enum'")]
    [InlineData("DayOfWeek?", "a struct")]
    public void A_further_type_argument_of_the_attribute_is_judged_like_the_id(string argument, string requirement)
    {
        const string Tariffs =
            """
            [AggregateRootBase]
            public abstract partial class TariffAggregate<TTariffId, TBillingDay>
                where TTariffId : IEntityId, IEquatable<TTariffId>
                where TBillingDay : struct, Enum;

            [AggregateRootTemplate(typeof(TariffAggregate<,>))]
            public sealed class TariffAttribute<TTariffId, TBillingDay> : Attribute;

            [EntityId<int>]
            public readonly partial record struct TariffId;


            """;

        GeneratorRunOutcome WithBillingDay(string type)
            => GeneratorTestHost.Create(Usings + Tariffs + $"[Tariff<TariffId, {type}>]\npublic sealed partial class ShopTariff;").RunCore();

        var result = WithBillingDay(argument);

        result.ShouldHaveExactlyDiagnostics("DDD00053");
        result.ShouldHaveDiagnostic("DDD00053", at: "ShopTariff").GetMessage()
            .Should().Be($"'ShopTariff' is declared with [Tariff], but '{argument}' does not meet the parent's constraint on 'TBillingDay', which requires {requirement}");
        ShouldLeaveTheCompilerNothingToSay(result);

        var valid = WithBillingDay("DayOfWeek");
        valid.ReportedDiagnostics.Should().BeEmpty();
        valid.ShouldCompile();
    }

    // ------------------------------------------------------------------ next to a class the template takes

    private const string AnyPartId = "IEntityId, IEquatable<TPartId>";

    private const string StructPartId = "struct, " + AnyPartId;

    private const string ClassPartId = "[EntityId<int>]\npublic partial record PartId;";

    /// <summary>
    /// A part, and a holder that takes the part's class and the part's id. Each parent says what it asks of the
    /// part's id, and the holder's what it asks of the part: to be disposable, which no part here is, unless
    /// the test says otherwise.
    /// </summary>
    private static string Holders(
        string partAsksOfItsId = AnyPartId,
        string holderAsksOfThePartId = StructPartId,
        string holderAsksOfThePart = "PartEntity<TPartId>, IDisposable")
        => $$"""
           [EntityBase]
           public abstract partial class PartEntity<TPartId>
               where TPartId : {{partAsksOfItsId}};

           [EntityTemplate(typeof(PartEntity<>))]
           public sealed class PartAttribute<TPartId> : Attribute;

           [AggregateRootBase]
           public abstract partial class HolderAggregate<THolderId, TPart, TPartId>
               where THolderId : IEntityId, IEquatable<THolderId>
               where TPart : {{holderAsksOfThePart}}
               where TPartId : {{holderAsksOfThePartId}};

           [AggregateRootTemplate(typeof(HolderAggregate<,,>))]
           [TemplateArgument(1, typeof(PartAttribute<>), Take = TemplateArgumentKind.Type)]
           [TemplateArgument(2, typeof(PartAttribute<>))]
           public sealed class HolderAttribute<THolderId> : Attribute;

           [EntityId<int>]
           public readonly partial record struct HolderId;

           [Part<PartId>]
           public sealed partial class ShopPart;

           [Holder<HolderId>]
           public sealed partial class ShopHolder;


           """;

    [Fact]
    public void An_id_that_does_not_fit_is_named_before_a_class_that_does_not()
    {
        // The part is not disposable, which is DDD00048, and its id is a class, which the holder's parent
        // refuses. The class is constrained by the id, so the id is the mistake to fix first, and the only one named.
        var result = GeneratorTestHost.Create(Usings + Holders() + ClassPartId).RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00053");
        result.ShouldHaveDiagnostic("DDD00053", at: "ShopHolder").GetMessage().Should().Be(
            "'ShopHolder' is declared with [Holder], but 'PartId' does not meet the parent's constraint on 'TPartId', which requires a struct");
        Generated(result, "ShopPart").Should().BeTrue("the part's own parent takes any id");
        ShouldLeaveTheCompilerNothingToSay(result);

        var withStructId = GeneratorTestHost.Create(Usings + Holders() + "[EntityId<int>]\npublic readonly partial record struct PartId;").RunCore();

        withStructId.ShouldHaveExactlyDiagnostics("DDD00048");
        withStructId.ShouldHaveDiagnostic("DDD00048", at: "ShopHolder").GetMessage().Should().Contain("IDisposable");
    }

    [Fact]
    public void A_class_refused_for_its_id_is_not_taken_and_the_class_that_would_take_it_adds_nothing()
    {
        // Only the part's parent holds the id by value, so the part is the one reported. The holder takes the
        // part itself, which now has no base class to be taken with; the part's diagnostic says why.
        var result = GeneratorTestHost.Create(
            Usings + Holders(partAsksOfItsId: StructPartId, holderAsksOfThePartId: AnyPartId, holderAsksOfThePart: "class") + ClassPartId).RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00053");
        result.ShouldHaveDiagnostic("DDD00053", at: "ShopPart");
        Generated(result, "ShopHolder").Should().BeFalse();
        ShouldLeaveTheCompilerNothingToSay(result);
    }

    [Fact]
    public void A_class_that_takes_a_refused_class_still_hears_what_its_own_parent_asks_of_the_id()
    {
        // Both parents hold the id by value. The holder gets no base class because the part has none, and it is
        // told about the id all the same: fixing the id is what gives both of them their base class back.
        var result = GeneratorTestHost.Create(
            Usings + Holders(partAsksOfItsId: StructPartId, holderAsksOfThePartId: StructPartId) + ClassPartId).RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00053", "DDD00053");
        result.ShouldHaveDiagnostic("DDD00053", at: "ShopPart").GetMessage().Should().StartWith("'ShopPart' is declared with [Part]");
        result.ShouldHaveDiagnostic("DDD00053", at: "ShopHolder").GetMessage().Should().Be(
            "'ShopHolder' is declared with [Holder], but 'PartId' does not meet the parent's constraint on 'TPartId', which requires a struct");
        ShouldLeaveTheCompilerNothingToSay(result);
    }

    [Fact]
    public void Two_classes_that_take_each_other_are_both_told_about_the_id()
    {
        // Each is refused once the other is. If being refused silenced a class, an id neither parent accepts
        // would leave both without a base class and without a diagnostic.
        const string Pair =
            """
            [EntityBase]
            public abstract partial class PartEntity<TPartId, THolder, THolderId>
                where TPartId : IEntityId, IEquatable<TPartId>
                where THolder : class
                where THolderId : struct, IEntityId, IEquatable<THolderId>;

            [EntityTemplate(typeof(PartEntity<,,>))]
            [TemplateArgument(1, typeof(HolderAttribute<>), Take = TemplateArgumentKind.Type)]
            [TemplateArgument(2, typeof(HolderAttribute<>))]
            public sealed class PartAttribute<TPartId> : Attribute;

            [AggregateRootBase]
            public abstract partial class HolderAggregate<THolderId, TPart, TPartId>
                where THolderId : struct, IEntityId, IEquatable<THolderId>
                where TPart : class
                where TPartId : IEntityId, IEquatable<TPartId>;

            [AggregateRootTemplate(typeof(HolderAggregate<,,>))]
            [TemplateArgument(1, typeof(PartAttribute<>), Take = TemplateArgumentKind.Type)]
            [TemplateArgument(2, typeof(PartAttribute<>))]
            public sealed class HolderAttribute<THolderId> : Attribute;

            [EntityId<int>]
            public readonly partial record struct PartId;

            [Part<PartId>]
            public sealed partial class ShopPart;

            [Holder<HolderId>]
            public sealed partial class ShopHolder;


            """;

        var result = GeneratorTestHost.Create(Usings + Pair + "[EntityId<int>]\npublic partial record HolderId;").RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00053", "DDD00053");
        result.ShouldHaveDiagnostic("DDD00053", at: "ShopHolder").GetMessage().Should().Be(
            "'ShopHolder' is declared with [Holder], but 'HolderId' does not meet the parent's constraint on 'THolderId', which requires a struct");
        result.ShouldHaveDiagnostic("DDD00053", at: "ShopPart").GetMessage().Should().Be(
            "'ShopPart' is declared with [Part], but 'HolderId' does not meet the parent's constraint on 'THolderId', which requires a struct");
        ShouldLeaveTheCompilerNothingToSay(result);

        var valid = GeneratorTestHost.Create(Usings + Pair + "[EntityId<int>]\npublic readonly partial record struct HolderId;").RunCore();
        valid.ReportedDiagnostics.Should().BeEmpty();
        valid.ShouldCompile();
    }

    // ------------------------------------------------------------------ next to a registration

    [Fact]
    public void A_registration_that_takes_the_refused_class_is_left_out_without_a_second_report()
    {
        // The registration would be closed over a class that has no base class to meet its constraint with.
        // What is wrong has been said once, on the class.
        const string Registrations =
            """
            using System;
            using System.Collections.Generic;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Abstractions.Interfaces;

            [assembly: TemplateRegistrations(typeof(Sample.SubscriptionRegistrations))]

            namespace Sample;

            public static class SubscriptionRegistrations
            {
                [TemplateRegistration]
                public static List<string> AddSubscriptions<
                    [TemplateType(typeof(SubscriptionAttribute<>), Take = TemplateArgumentKind.Type)] TSubscription,
                    [TemplateType(typeof(SubscriptionAttribute<>))] TSubscriptionId>(this List<string> entries)
                    where TSubscription : SubscriptionAggregate<TSubscriptionId>
                    where TSubscriptionId : struct, IEntityId, IEquatable<TSubscriptionId>
                    => entries;
            }
            """;

        GeneratorRunOutcome WithRegistration(string subscriptionId)
            => GeneratorTestHost.Create(Package(), "Package.cs")
                .WithSource(Registrations, "Registrations.cs")
                .WithSource(Application(subscriptionId), "Application.cs")
                .RunCore();

        var result = WithRegistration(ClassSubscriptionId);

        result.ShouldHaveExactlyDiagnostics("DDD00053", "DDD00053");
        result.HintNames.Should().NotContain("Registration");
        ShouldLeaveTheCompilerNothingToSay(result);

        var valid = WithRegistration(StructSubscriptionId);
        valid.ReportedDiagnostics.Should().BeEmpty();
        valid.ShouldCompile();
        valid.ShouldHaveGenerated("SubscriptionRegistrations.AddSubscriptions.Registration.");
    }

    [Fact]
    public void A_registration_that_takes_only_the_id_says_what_the_method_asks_of_it_too()
    {
        // Taking the id needs no base class, so this registration is not left out for the class's sake. It is
        // closed over the same id, which its own constraint refuses, and DDD00050 says that of the method.
        const string Registrations =
            """
            using System;
            using System.Collections.Generic;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Abstractions.Interfaces;

            [assembly: TemplateRegistrations(typeof(Sample.SubscriptionRegistrations))]

            namespace Sample;

            public static class SubscriptionRegistrations
            {
                [TemplateRegistration]
                public static List<string> AddSubscriptionIds<
                    [TemplateType(typeof(SubscriptionAttribute<>))] TSubscriptionId>(this List<string> entries)
                    where TSubscriptionId : struct, IEntityId, IEquatable<TSubscriptionId>
                    => entries;
            }
            """;

        var result = GeneratorTestHost.Create(Package(), "Package.cs")
            .WithSource(Registrations, "Registrations.cs")
            .WithSource(Application(ClassSubscriptionId), "Application.cs")
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00053", "DDD00053", "DDD00050");
        result.ShouldHaveDiagnostic("DDD00050", at: "ShopSubscription").GetMessage().Should().Contain("'SubscriptionId'").And.Contain("a struct");
        result.HintNames.Should().NotContain("Registration");
        ShouldLeaveTheCompilerNothingToSay(result);
    }

    // ------------------------------------------------------------------ a real package

    [Theory]
    [InlineData("TenantId", "TTenantId", new[] { "Tenant", "Organization", "Seat", "Role" })]
    [InlineData("OrganizationUnitId", "TUnitId", new[] { "Organization", "OrganizationUnit", "Seat" })]
    [InlineData("RoleId", "TRoleId", new[] { "Seat", "Role" })]
    [InlineData("SeatId", "TSeatId", new[] { "Seat" })]
    public void A_Tenancy_template_with_a_class_id_reports_DDD00053(string id, string parameter, string[] closedOverIt)
    {
        // The package as it ships, referenced the way an application references it. Its parents hold ids by
        // value, so every class whose parent is closed over the id says so, and the others are generated.
        var result = Tenancy(classId: id);

        result.ShouldHaveExactlyDiagnostics([.. closedOverIt.Select(static _ => "DDD00053")]);
        foreach (var (type, template) in TenancyClasses)
        {
            if (!closedOverIt.Contains(type))
            {
                Generated(result, type).Should().BeTrue($"the parent of {type} takes no {id}");
                continue;
            }

            result.ShouldHaveDiagnostic("DDD00053", at: type).GetMessage().Should().Be(
                $"'{type}' is declared with [{template}], but '{id}' does not meet the parent's constraint on '{parameter}', which requires a struct");
            Generated(result, type).Should().BeFalse();
        }

        ShouldLeaveTheCompilerNothingToSay(result);
    }

    [Fact]
    public void A_Tenancy_template_with_struct_ids_reports_nothing()
    {
        var result = Tenancy(classId: null);

        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
    }

    private static readonly (string Type, string Template)[] TenancyClasses =
    [
        ("Tenant", "TenantAggregate"),
        ("Organization", "OrganizationAggregate"),
        ("OrganizationUnit", "OrganizationUnit"),
        ("Seat", "SeatAggregate"),
        ("Role", "RoleAggregate"),
    ];

    /// <summary>An application on the Tenancy package, with one of its four ids declared as a record class, or none.</summary>
    private static GeneratorRunOutcome Tenancy(string? classId)
    {
        string Id(string name, string value)
            => $"[EntityId<{value}>]\npublic " + (name == classId ? "partial record " : "readonly partial record struct ") + name + ";";

        return GeneratorTestHost.Create(
                $$"""
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Supporting.Tenancy;

                namespace Sample;

                {{Id("TenantId", "long")}}

                {{Id("SeatId", "Guid")}}

                {{Id("OrganizationUnitId", "Guid")}}

                {{Id("RoleId", "Guid")}}

                [TenantAggregate<TenantId>]
                public sealed partial class Tenant;

                [OrganizationAggregate<TenantId>]
                public sealed partial class Organization;

                [OrganizationUnit<OrganizationUnitId>]
                public sealed partial class OrganizationUnit;

                [SeatAggregate<SeatId>]
                public sealed partial class Seat;

                [RoleAggregate<RoleId>]
                public sealed partial class Role;
                """)
            .WithTenancy()
            .RunCore();
    }
}
