using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// What <c>[EntityId&lt;T&gt;]</c> on a <c>partial record</c> produces. The reference-type id keeps the
/// pre-3.0 shape — it derives from <c>EntityId&lt;T&gt;</c> and gets an always-valid <c>Valid{Name}</c>
/// twin — and gains the parsing and factory members the struct id has.
/// </summary>
public class ClassEntityIdTests
{
    private const string Preamble = "using DDDToolkit.Abstractions.Attributes;\nusing System;\n\nnamespace Sample;\n\n";

    private static GeneratorRunOutcome PersonId()
        => GeneratorTestHost.Create(Preamble +
            """
            [EntityId<Guid>("PRS")]
            public partial record PersonId
            {
                public static PersonId Create(Guid value) => new(value);
            }
            """).RunCore();

    [Fact]
    public void A_record_id_derives_from_EntityId()
    {
        var result = PersonId();
        result.ShouldContain("Sample.PersonId.g.cs", "partial record PersonId : global::DDDToolkit.BaseTypes.EntityId<global::System.Guid>");

        var emitted = result.Emit();
        var idType = emitted.Type("Sample.PersonId");

        idType.BaseType.Should().Be(typeof(EntityId<Guid>));
        typeof(IEntityId<Guid>).IsAssignableFrom(idType).Should().BeTrue();
    }

    [Fact]
    public void The_constructor_is_protected_so_only_the_id_itself_can_make_one()
    {
        var emitted = PersonId().Emit();

        var constructors = emitted.Type("Sample.PersonId")
            .GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Where(constructor => constructor.GetParameters() is not [{ ParameterType.Name: "PersonId" }])
            .ToList();

        constructors.Should().OnlyContain(constructor => constructor.IsFamily, "the value and parameterless constructors are protected");
        constructors.Should().Contain(constructor => constructor.GetParameters().Length == 0, "persistence frameworks need a parameterless constructor");
    }

    [Fact]
    public void ToString_writes_the_prefix_before_the_value()
    {
        var emitted = PersonId().Emit();
        var guid = Guid.NewGuid();

        var id = emitted.CallStatic("Sample.PersonId", "Create", guid)!;

        id.ToString().Should().Be("PRS_" + guid);
    }

    [Fact]
    public void Parse_accepts_the_prefixed_and_the_bare_form()
    {
        var emitted = PersonId().Emit();
        var guid = Guid.NewGuid();

        var prefixed = emitted.CallStatic("Sample.PersonId", "Parse", "PRS_" + guid)!;
        var bare = emitted.CallStatic("Sample.PersonId", "Parse", guid.ToString())!;

        emitted.Property(prefixed, "Value").Should().Be(guid);
        prefixed.Should().Be(bare);
    }

    [Fact]
    public void TryParse_yields_null_and_false_for_garbage()
    {
        var emitted = PersonId().Emit();

        var (success, value) = emitted.TryParse("Sample.PersonId", "nonsense");

        success.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void CreateUnique_and_CreateSequential_make_distinct_ids()
    {
        var emitted = PersonId().Emit();

        var unique = Enumerable.Range(0, 20).Select(_ => emitted.CallStatic("Sample.PersonId", "CreateUnique")!).ToList();
        var sequential = Enumerable.Range(0, 20).Select(_ => emitted.CallStatic("Sample.PersonId", "CreateSequential")!).ToList();

        unique.Distinct().Should().HaveCount(20);
        sequential.Distinct().Should().HaveCount(20);
    }

    [Fact]
    public void Two_ids_over_the_same_value_are_equal()
    {
        var emitted = PersonId().Emit();
        var guid = Guid.NewGuid();

        var left = emitted.CallStatic("Sample.PersonId", "Create", guid)!;
        var right = emitted.CallStatic("Sample.PersonId", "Create", guid)!;

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
        emitted.CallStatic("Sample.PersonId", "Create", Guid.NewGuid()).Should().NotBe(left);
    }

    [Fact]
    public void The_always_valid_twin_derives_from_the_id_and_is_marked_IAlwaysValid()
    {
        var emitted = PersonId().Emit();

        var twin = emitted.Type("Sample.ValidPersonId");

        twin.BaseType.Should().Be(emitted.Type("Sample.PersonId"));
        typeof(IAlwaysValid).IsAssignableFrom(twin).Should().BeTrue();
    }

    [Fact]
    public void ToValid_copies_the_value_into_the_twin()
    {
        var emitted = PersonId().Emit();
        var guid = Guid.NewGuid();
        var id = emitted.CallStatic("Sample.PersonId", "Create", guid)!;

        var valid = emitted.Call(id, "ToValid")!;

        valid.Should().BeOfType(emitted.Type("Sample.ValidPersonId"));
        emitted.Property(valid, "Value").Should().Be(guid);
        emitted.Property(valid, "IsValid").Should().Be(true);
        valid.ToString().Should().Be("PRS_" + guid, "the twin keeps the id's textual form");
    }

    [Fact]
    public void The_twin_can_be_built_straight_from_the_underlying_value()
    {
        var emitted = PersonId().Emit();
        var guid = Guid.NewGuid();

        var valid = emitted.New("Sample.ValidPersonId", guid);

        emitted.Property(valid, "Value").Should().Be(guid);
    }

    [Fact]
    public void A_record_id_over_a_type_without_TryParse_still_gets_its_base_and_twin()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            public readonly record struct Ticket(int Number);

            [EntityId<Ticket>]
            public partial record TicketId
            {
                public static TicketId Create(Ticket value) => new(value);
            }
            """).RunCore();

        result.ShouldCompile();
        result.ShouldNotContain("Sample.TicketId.g.cs", "static TicketId Parse(");

        var emitted = result.Emit();
        emitted.HasType("Sample.ValidTicketId").Should().BeTrue();
        emitted.HasMember("Sample.TicketId", "ToValid").Should().BeTrue();
    }

    [Fact]
    public void Structs_get_no_twin_because_a_struct_cannot_be_derived_from()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [EntityId<Guid>]
            public readonly partial record struct CatId;
            """).RunCore();

        result.ShouldCompile();
        result.Emit().HasType("Sample.ValidCatId").Should().BeFalse();
    }
}
