namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00003 (entity ids must be records), DDD00004 (record struct ids should be readonly — a warning,
/// generation still happens), DDD00005 (must be partial) and DDD00013 (a record class id cannot be
/// sealed, because its always-valid twin derives from it).
/// </summary>
public class EntityIdDiagnosticTests
{
    private const string Usings = "using DDDToolkit.Abstractions.Attributes;\nusing System;\n\nnamespace Sample;\n\n";

    [Fact]
    public void EntityId_on_a_plain_class_reports_DDD00003_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [EntityId<Guid>]
            public partial class OrderId
            {
            }
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00003", at: "OrderId");
        diagnostic.GetMessage().Should().Contain("record");
        result.ShouldNotHaveGeneratedFor("OrderId");
    }

    [Fact]
    public void EntityId_on_a_plain_struct_reports_DDD00003_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [EntityId<Guid>]
            public readonly partial struct OrderId
            {
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00003", at: "OrderId");
        result.ShouldNotHaveGeneratedFor("OrderId");
    }

    [Fact]
    public void EntityId_on_an_interface_reports_DDD00003_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [EntityId<Guid>]
            public partial interface IOrderId
            {
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00003", at: "IOrderId");
        result.ShouldNotHaveGeneratedFor("IOrderId");
    }

    [Fact]
    public void A_non_partial_record_id_reports_DDD00005_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [EntityId<Guid>]
            public record OrderId;
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00005", at: "OrderId");
        diagnostic.GetMessage().Should().Contain("EntityId");
        result.ShouldNotHaveGeneratedFor("OrderId");
    }

    [Fact]
    public void A_non_partial_record_struct_id_reports_DDD00005_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [EntityId<Guid>]
            public readonly record struct OrderId;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00005", at: "OrderId");
        result.ShouldNotHaveGeneratedFor("OrderId");
    }

    [Fact]
    public void A_sealed_record_id_reports_DDD00013_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [EntityId<Guid>]
            public sealed partial record OrderId;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00013", at: "OrderId");
        result.ShouldNotHaveGeneratedFor("OrderId");
    }

    [Fact]
    public void A_record_struct_id_without_readonly_warns_with_DDD00004_but_is_still_generated()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [EntityId<Guid>]
            public partial record struct OrderId;
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00004", at: "OrderId");
        diagnostic.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
            "a mutable id still works, it is just a worse id");
        diagnostic.GetMessage().Should().Contain("readonly");

        // DDD00004 is advice, not a refusal: the id is generated and works.
        result.ShouldHaveGenerated("Sample.OrderId.g.cs");
        result.ShouldContain("Sample.OrderId.g.cs", "public static OrderId CreateUnique()");
        result.ShouldCompile();

        var emitted = result.Emit();
        var id = emitted.CallStatic("Sample.OrderId", "CreateUnique")!;
        emitted.Property(id, "Value").Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void A_readonly_record_struct_id_does_not_warn()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [EntityId<Guid>]
            public readonly partial record struct OrderId;
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00004");
        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
    }

    [Fact]
    public void A_record_class_id_is_not_asked_to_be_readonly()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [EntityId<Guid>]
            public partial record OrderId;
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00004");
        result.ShouldCompile();
    }
}
