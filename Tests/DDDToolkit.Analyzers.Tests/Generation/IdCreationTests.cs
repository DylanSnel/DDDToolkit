namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// Ids are made in code, before the save, by the id itself: <c>TId.Create()</c>. Every id over a <see cref="Guid"/>
/// gets one, time-ordered, and implements <c>ICreatableEntityId&lt;TSelf&gt;</c> with it; an id over anything else says
/// how a new one is made with a <c>Create()</c> of its own, which wins over the generator's for a Guid too. A package
/// that makes the ids of its template's classes, as Tenancy does, says so on the template, and a class declared over
/// an id without one is DDD00067 on the class, while what is closed over the id stands back. Other ids need nothing.
/// </summary>
public class IdCreationTests
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

    // ------------------------------------------------------------------ DDD00067: Tenancy makes its ids

    /// <summary>An application on the Tenancy package: its four ids, the tenant's over a long, and its five classes.</summary>
    private static string TenancyApplication(string tenantId)
        => $$"""
             using System;
             using DDDToolkit.Abstractions.Attributes;
             using DDDToolkit.Supporting.Tenancy;

             [assembly: Module("Shop")]

             namespace Shop;

             {{tenantId}}

             [EntityId<Guid>] public readonly partial record struct SeatId;
             [EntityId<Guid>] public readonly partial record struct OrganizationUnitId;
             [EntityId<Guid>] public readonly partial record struct RoleId;

             [TenantAggregate<TenantId>] public sealed partial class Tenant;
             [OrganizationAggregate<TenantId>] public sealed partial class Organization;
             [OrganizationUnit<OrganizationUnitId>] public sealed partial class OrganizationUnit;
             [SeatAggregate<SeatId>] public sealed partial class Seat;
             [RoleAggregate<RoleId>] public sealed partial class Role;
             """;

    private const string LongTenantId = "[EntityId<long>] public readonly partial record struct TenantId;";

    private const string LongTenantIdWithCreate =
        "[EntityId<long>] public readonly partial record struct TenantId { public static TenantId Create() => new(Environment.TickCount64); }";

    [Fact]
    public void A_tenancy_class_over_an_id_without_a_create_is_DDD00067_on_the_class_and_nothing_closed_over_it_fails()
    {
        var result = GeneratorTestHost.Create(TenancyApplication(LongTenantId), "Application.cs").WithTenancyOnEntityFramework().RunCore();

        // Once, on the tenant, where the id is named: the organization shares the id, but Tenancy makes no new one for it.
        result.ShouldHaveExactlyDiagnostics("DDD00067");
        var diagnostic = result.ShouldHaveDiagnostic("DDD00067", at: "TenantId");
        diagnostic.GetMessage().Should().Be(
            "'TenantId' has no public static Create(), and 'Tenant' is declared with [TenantAggregate], whose package makes each new TenantId with "
            + "TenantId.Create(), in code and before the save. Declare it in the partial declaration of TenantId, public static TenantId Create() => new(...), "
            + "with a new long made in code: a snowflake, or the next number of a block a HiLo sequence hands out.");
        diagnostic.Properties.Should().Contain("IdMetadataName", "Shop.TenantId");

        // The classes are generated; what Tenancy closes over the id stands back rather than fail in code nobody wrote.
        result.CompilationErrors.Should().BeEmpty();
        result.HintNames.Should().NotContain("ShopTenancy").And.NotContain("TenancyEntityFrameworkServiceCollectionExtensions.AddTenancy.Registration");
        result.HintNames.Should().Contain("Tenant.", "the tenant itself is generated: only what makes its new ids needs the Create()");
    }

    [Fact]
    public void A_tenancy_class_over_an_id_that_says_how_a_new_one_is_made_is_closed_over_and_compiles()
    {
        var result = GeneratorTestHost.Create(TenancyApplication(LongTenantIdWithCreate), "Application.cs").WithTenancyOnEntityFramework().RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain("ShopTenancy.TemplateFacade", "global::Shop.TenantId");
    }

    [Fact]
    public void An_id_of_another_project_without_a_create_is_said_on_the_class_that_uses_it()
    {
        var result = GeneratorTestHost.Create("using Shop.Contracts;\n" + TenancyApplication(string.Empty), "Application.cs")
            .WithTenancy()
            .WithReferencedAssembly("using DDDToolkit.Abstractions.Attributes;\n\nnamespace Shop.Contracts;\n\n" + LongTenantId, "Shop.Contracts")
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00067");
        result.ShouldHaveDiagnostic("DDD00067", at: "TenantId").Properties.Should().Contain("IdMetadataName", "Shop.Contracts.TenantId");
        result.CompilationErrors.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ DDD00067 says what the id lacks

    [Fact]
    public void A_tenancy_class_over_an_id_written_by_hand_is_DDD00067_and_not_an_error_in_generated_code()
    {
        // Partial out of habit, and without [EntityId<T>]: no generator completes a type that shows IEntityId, so
        // it is judged by what it shows, and the use cases closed over it stand back.
        var result = GeneratorTestHost.Create(
                TenancyApplication("public readonly partial record struct TenantId(long Value) : DDDToolkit.Abstractions.Interfaces.IEntityId<long>;"),
                "Application.cs")
            .WithTenancyOnEntityFramework()
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00067");
        var diagnostic = result.ShouldHaveDiagnostic("DDD00067", at: "TenantId");
        diagnostic.GetMessage().Should().Be(
            "'TenantId' has no public static Create(), and 'Tenant' is declared with [TenantAggregate], whose package makes each new TenantId with "
            + "TenantId.Create(), in code and before the save. Declare it on TenantId, public static TenantId Create() => new(...), with a new long "
            + "made in code: a snowflake, or the next number of a block a HiLo sequence hands out, and add ICreatableEntityId<TenantId> to the "
            + "interfaces it implements.");
        diagnostic.Properties.Should().Contain("IdFix", "CreateAndInterface");
        result.CompilationErrors.Should().BeEmpty("nothing closed over the id is written, so there is no CS0315 in code nobody wrote");
        result.HintNames.Should().NotContain("ShopTenancy");
    }

    [Fact]
    public void An_id_written_by_hand_with_a_create_of_its_own_is_told_to_implement_the_interface()
    {
        var result = GeneratorTestHost.Create(
                TenancyApplication(
                    "public readonly record struct TenantId(long Value) : DDDToolkit.Abstractions.Interfaces.IEntityId<long> { public static TenantId Create() => new(1); }"),
                "Application.cs")
            .WithTenancyOnEntityFramework()
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00067");
        var diagnostic = result.ShouldHaveDiagnostic("DDD00067", at: "TenantId");
        diagnostic.GetMessage().Should().Be(
            "'TenantId' has a public static Create() but does not implement ICreatableEntityId<TenantId>, and 'Tenant' is declared with "
            + "[TenantAggregate], whose package makes each new TenantId with TenantId.Create(), in code and before the save. Add "
            + "ICreatableEntityId<TenantId> to the interfaces TenantId implements.");
        diagnostic.Properties.Should().Contain("IdFix", "Interface", "a second Create() would not compile");
        result.CompilationErrors.Should().BeEmpty();
    }

    [Fact]
    public void An_id_of_a_project_that_cannot_see_the_interface_is_told_to_target_net10()
    {
        // What the generator leaves in a project built for net9.0, whose DDDToolkit.Abstractions has no
        // ICreatableEntityId: the id with its Create(), and without the interface.
        var result = GeneratorTestHost.Create("using Shop.Contracts;\n" + TenancyApplication(string.Empty), "Application.cs")
            .WithTenancyOnEntityFramework()
            .WithReferencedAssemblyAsBuilt(
                """
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Abstractions.Interfaces;

                namespace Shop.Contracts;

                [EntityId<long>]
                public readonly record struct TenantId(long Value) : IEntityId<long>
                {
                    public static TenantId Create() => new(1);
                }
                """,
                "Shop.Contracts")
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00067");
        var diagnostic = result.ShouldHaveDiagnostic("DDD00067", at: "TenantId");
        diagnostic.GetMessage().Should()
            .StartWith("'TenantId' has a public static Create() but does not implement ICreatableEntityId<TenantId>, and 'Tenant'")
            .And.EndWith(
                "The generator implements it where the project that declares TenantId can see it, in the net10.0 build of DDDToolkit.Abstractions: "
                + "have that project target net10.0.");
        diagnostic.Properties.Should().Contain("IdFix", string.Empty, "the fix is the other project's target, not a second Create()");
        result.CompilationErrors.Should().BeEmpty();
    }

    [Fact]
    public void An_id_whose_create_is_not_public_and_static_is_told_to_make_it_so_and_gets_no_fix()
    {
        var result = GeneratorTestHost.Create(
                TenancyApplication("[EntityId<long>] public readonly partial record struct TenantId { internal static TenantId Create() => new(1); }"),
                "Application.cs")
            .WithTenancyOnEntityFramework()
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00067");
        var diagnostic = result.ShouldHaveDiagnostic("DDD00067", at: "TenantId");
        diagnostic.GetMessage().Should()
            .StartWith("'TenantId' has a Create() that is not a public static one answering TenantId, and 'Tenant'")
            .And.EndWith("Make it public static TenantId Create() => new(...), with a new long made in code: a snowflake, or the next number of a block a HiLo sequence hands out.");
        diagnostic.Properties.Should().Contain("IdFix", string.Empty, "a second Create() beside it would not compile");
        result.CompilationErrors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("int")]
    [InlineData("uint")]
    public void An_id_over_an_int_is_not_told_to_make_a_snowflake_which_an_int_cannot_hold(string value)
    {
        var result = GeneratorTestHost.Create(TenancyApplication($"[EntityId<{value}>] public readonly partial record struct TenantId;"), "Application.cs")
            .WithTenancyOnEntityFramework()
            .RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00067", at: "TenantId");
        diagnostic.GetMessage().Should()
            .EndWith($"with a new {value} made in code: the next number of a block a HiLo sequence hands out.")
            .And.NotContain("snowflake");
        diagnostic.Properties.Should().Contain("IdExample", "the next number of a block a HiLo sequence hands out");
    }

    [Fact]
    public void An_id_no_package_makes_needs_no_create()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [EntityId<long>]
            public readonly partial record struct TicketNumber;

            [AggregateRoot<TicketNumber>]
            public sealed partial class Ticket;
            """).WithTenancy().RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty("only a package that makes the ids of its classes asks for a Create()");
    }

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

             [assembly: TemplateFacade(typeof(Acme.Ledgers.LedgerUseCases<,>), "{Module}Ledgers")]
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
        result.HintNames.Should().NotContain("ShopLedgers").And.NotContain("AddLedgers", "both stand back behind the one error, on the class");
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
            "'ShopLedgers' is not written, so no project can name the types nested in LedgerUseCases through it: it takes 'LedgerNumber' as 'TLedgerId', "
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
            .WithSource("namespace Shop;\n\npublic static class Uses\n{\n    public static LedgerNumber Next() => ShopLedgers.NewLedgerId();\n}\n", "Uses.cs")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.HintNames.Should().Contain("ShopLedgers").And.Contain("LedgerRegistrations.AddLedgers.Registration");
    }
}
