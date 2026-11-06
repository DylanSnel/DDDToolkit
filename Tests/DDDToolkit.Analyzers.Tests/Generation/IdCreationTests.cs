namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// Ids are made in code, before the save, by the id itself: <c>TId.Create()</c>. Every id over a <see cref="Guid"/>
/// gets one, time-ordered, and implements <c>ICreatableEntityId&lt;TSelf&gt;</c> with it; an id over anything else says
/// how a new one is made with a <c>Create()</c> of its own, which wins over the generator's for a Guid too. A package
/// that makes the ids of its template's classes, as Tenancy does, says so on the template, and a class declared over
/// an id without one is DDD00067 on the class, while what is closed over the id stands back. Other ids need nothing.
/// </summary>
public partial class IdCreationTests
{
    private const string Preamble = "using DDDToolkit.Abstractions.Attributes;\nusing System;\n\nnamespace Sample;\n\n";

    /// <summary>Whether a type implements <c>ICreatableEntityId</c> closed over itself.</summary>
    private static bool MakesNewOnesOfItself(Type type)
        => type.GetInterfaces().Any(candidate => candidate.IsGenericType
                                                && candidate.GetGenericTypeDefinition().FullName == "DDDToolkit.Abstractions.Interfaces.ICreatableEntityId`1"
                                                && candidate.GetGenericArguments()[0] == type);

    // ------------------------------------------------------------------ what the generator writes

    [Fact]
    public void An_id_over_a_guid_makes_a_new_one_of_itself_in_time_order()
    {
        var result = GeneratorTestHost.Create(Preamble + "[EntityId<Guid>(\"ORD\")]\npublic readonly partial record struct OrderId;").RunCore();

        result.ShouldCompile();
        result.ShouldContain("OrderId", "public static OrderId Create() => CreateSequential();");
        var emitted = result.Emit();
        MakesNewOnesOfItself(emitted.Type("Sample.OrderId")).Should().BeTrue();

        var made = (Guid)emitted.Property(emitted.CallStatic("Sample.OrderId", "Create")!, "Value")!;
        made.Version.Should().Be(7, "Create() makes what CreateSequential() makes");
    }

    [Fact]
    public void A_record_id_over_a_guid_makes_new_ones_and_its_twin_is_one_as_well()
    {
        var result = GeneratorTestHost.Create(Preamble + "[EntityId<Guid>]\npublic partial record CustomerId;").RunCore();

        result.ShouldCompile();
        var emitted = result.Emit();
        MakesNewOnesOfItself(emitted.Type("Sample.CustomerId")).Should().BeTrue();
        emitted.Type("Sample.ValidCustomerId").GetInterfaces().Should().Contain(emitted.Type("Sample.CustomerId").GetInterfaces(), "the twin derives from the id");
        ((Guid)emitted.Property(emitted.CallStatic("Sample.CustomerId", "Create")!, "Value")!).Version.Should().Be(7);
    }

    [Fact]
    public void The_id_the_generator_writes_for_an_aggregate_root_over_a_guid_has_a_create_too()
    {
        var result = GeneratorTestHost.Create(Preamble + "[AggregateRoot<Guid>]\npublic sealed partial class Order;").RunCore();

        result.ShouldCompile();
        MakesNewOnesOfItself(result.Emit().Type("Sample.OrderId")).Should().BeTrue();
    }

    [Fact]
    public void A_create_of_the_ids_own_wins_and_the_generator_writes_none()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [EntityId<Guid>]
            public readonly partial record struct OrderId
            {
                public static OrderId Create() => new(Guid.Empty);
            }
            """).RunCore();

        result.ShouldCompile();
        result.Source("OrderId").Should().NotContain("Create() => CreateSequential()");
        var emitted = result.Emit();
        MakesNewOnesOfItself(emitted.Type("Sample.OrderId")).Should().BeTrue("the interface is implemented with the id's own Create()");
        emitted.Property(emitted.CallStatic("Sample.OrderId", "Create")!, "Value").Should().Be(Guid.Empty);
    }

    [Fact]
    public void An_id_over_a_long_has_no_create_until_it_says_how_a_new_one_is_made()
    {
        var without = GeneratorTestHost.Create(Preamble + "[EntityId<long>]\npublic readonly partial record struct TicketNumber;").RunCore();

        without.ShouldCompile();
        without.ReportedDiagnostics.Should().BeEmpty("an id no package makes needs no Create()");
        var plain = without.Emit();
        plain.HasMember("Sample.TicketNumber", "Create").Should().BeFalse("there is no telling how a new long is made");
        MakesNewOnesOfItself(plain.Type("Sample.TicketNumber")).Should().BeFalse();

        var with = GeneratorTestHost.Create(Preamble +
            """
            [EntityId<long>]
            public readonly partial record struct TicketNumber
            {
                private static long _last;

                public static TicketNumber Create() => new(System.Threading.Interlocked.Increment(ref _last));
            }
            """).RunCore();

        with.ShouldCompile();
        var counted = with.Emit();
        MakesNewOnesOfItself(counted.Type("Sample.TicketNumber")).Should().BeTrue();
        counted.Property(counted.CallStatic("Sample.TicketNumber", "Create")!, "Value").Should().Be(1L);
    }

    [Fact]
    public void Something_else_called_create_keeps_the_generators_create_out_and_the_interface_with_it()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [EntityId<Guid>]
            public readonly partial record struct OrderId
            {
                public OrderId Create() => this;
            }
            """).RunCore();

        result.ShouldCompile();
        MakesNewOnesOfItself(result.Emit().Type("Sample.OrderId")).Should().BeFalse("an instance method makes no new id, and two members of one name would not compile");
    }

    private const string LongTenantId = "[EntityId<long>] public readonly partial record struct TenantId;";

    private const string LongTenantIdWithCreate =
        "[EntityId<long>] public readonly partial record struct TenantId { public static TenantId Create() => new(Environment.TickCount64); }";

    // ------------------------------------------------------------------ any package that makes ids

    /// <summary>
    /// A made-up package whose use cases and registration make new ledger ids: <paramref name="marker"/> is what its
    /// template says.
    /// </summary>
    private static string Ledgers(string marker)
        => $$"""
             using System;
             using DDDToolkit.Abstractions.Attributes;
             using DDDToolkit.Abstractions.Interfaces;

             [assembly: TemplateFacade(typeof(Acme.Ledgers.LedgerUseCases<,>))]
             [assembly: TemplateRegistrations(typeof(Acme.Ledgers.LedgerRegistrations))]
             [assembly: Module("Shop")]

             namespace Acme.Ledgers;

             [AggregateRootBase]
             public abstract partial class LedgerAggregate<TLedgerId>
                 where TLedgerId : IEntityId, IEquatable<TLedgerId>
             {
                 protected LedgerAggregate(TLedgerId id) : base(id) { }
             }

             [AggregateRootTemplate(typeof(LedgerAggregate<>){{marker}})]
             [AttributeUsage(AttributeTargets.Class, Inherited = false)]
             public sealed class LedgerAttribute<TLedgerId> : Attribute;

             public abstract class LedgerUseCases<
                 [TemplateType(typeof(LedgerAttribute<>), Take = TemplateArgumentKind.Type)] TLedger,
                 [TemplateType(typeof(LedgerAttribute<>))] TLedgerId>
                 where TLedger : LedgerAggregate<TLedgerId>
                 where TLedgerId : struct, ICreatableEntityId<TLedgerId>, IEquatable<TLedgerId>
             {
                 public static TLedgerId NewLedgerId() => TLedgerId.Create();
             }

             public sealed class Registry;

             public static class LedgerRegistrations
             {
                 [TemplateRegistration]
                 public static Registry AddLedgers<[TemplateType(typeof(LedgerAttribute<>))] TLedgerId>(this Registry registry)
                     where TLedgerId : struct, ICreatableEntityId<TLedgerId>, IEquatable<TLedgerId>
                     => registry;
             }
             """;

    private static string Ledger(string id)
        => $$"""
             using System;
             using DDDToolkit.Abstractions.Attributes;
             using Acme.Ledgers;

             namespace Shop;

             {{id}}

             [Ledger<LedgerNumber>]
             public sealed partial class Ledger
             {
                 public Ledger(LedgerNumber id) : base(id) { }
             }
             """;

    [Fact]
    public void A_package_that_says_its_template_makes_ids_gets_DDD00067_and_not_a_compile_error()
    {
        var result = GeneratorTestHost.Create(Ledgers(", CreatesIds = true"), "Package.cs")
            .WithSource(Ledger("[EntityId<long>] public readonly partial record struct LedgerNumber;"), "Ledger.cs")
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00067");
        result.ShouldHaveDiagnostic("DDD00067", at: "LedgerNumber").GetMessage().Should().Contain("'Ledger' is declared with [Ledger], whose package makes each new LedgerNumber");
        result.CompilationErrors.Should().BeEmpty();
        result.HintNames.Should().NotContain("LedgerUseCases.TemplateFacade").And.NotContain("AddLedgers", "both stand back behind the one error, on the class");
    }

    [Fact]
    public void A_package_that_asks_for_a_create_its_template_does_not_say_it_makes_is_told_where_the_classes_are()
    {
        var result = GeneratorTestHost.Create(Ledgers(string.Empty), "Package.cs")
            .WithSource(Ledger("[EntityId<long>] public readonly partial record struct LedgerNumber;"), "Ledger.cs")
            .RunCore();

        // No DDD00067, since the template does not say its package makes the ids: what is closed over the id says why
        // it is not written, each where it would be, rather than fail in code nobody wrote.
        result.ShouldHaveExactlyDiagnostics("DDD00065", "DDD00050");
        result.ShouldHaveDiagnostic("DDD00065", at: "Ledger").GetMessage().Should().Be(
            "'LedgerUseCases' is not written, so no project can name the types nested in LedgerUseCases through it: it takes 'LedgerNumber' as 'TLedgerId', "
            + "which requires ICreatableEntityId<LedgerNumber>, an id that makes a new one with Create(); 'LedgerNumber' has no public static Create()");
        result.ReportedDiagnostics.Single(diagnostic => diagnostic.Id == "DDD00050").GetMessage().Should().Be(
            "'LedgerRegistrations.AddLedgers' takes 'LedgerNumber' as 'TLedgerId', which requires an id with a public static Create() that makes a new one; "
            + "'LedgerNumber' does not meet it");
        result.CompilationErrors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("[EntityId<Guid>] public readonly partial record struct LedgerNumber;")]
    [InlineData("[EntityId<long>] public readonly partial record struct LedgerNumber { public static LedgerNumber Create() => new(42); }")]
    public void A_package_closes_its_use_cases_over_an_id_that_makes_new_ones(string id)
    {
        var result = GeneratorTestHost.Create(Ledgers(", CreatesIds = true"), "Package.cs")
            .WithSource(Ledger(id), "Ledger.cs")
            .WithSource("namespace Shop;\n\npublic static class Uses\n{\n    public static LedgerNumber Next() => LedgerUseCases.NewLedgerId();\n}\n", "Uses.cs")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.HintNames.Should().Contain("LedgerUseCases.TemplateFacade").And.Contain("LedgerRegistrations.AddLedgers.Registration");
    }
}
