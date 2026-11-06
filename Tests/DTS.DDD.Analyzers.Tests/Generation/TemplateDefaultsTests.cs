using Microsoft.CodeAnalysis;

namespace DTS.DDD.Analyzers.Tests.Generation;

/// <summary>
/// A package's switch, <c>[assembly: GenerateTenancyClasses]</c>, has the generator write the package's classes a
/// project leaves out, as the package ships them, and their ids. Nothing is written without it, and whatever the
/// project declares itself wins.
/// <para>
/// A generator never sees what another writes, so the classes and ids written here get everything only because every
/// provider the other generators read hands them on beside the declared ones. These tests run on the real Tenancy
/// package, seen through metadata as an application sees it, and compile every project with what all the generators
/// wrote: a class or an id that misses a part of what a declared one gets fails to compile there. A module split by
/// layer is compiled project by project, each above the one below it, as a build compiles it.
/// </para>
/// </summary>
public partial class TemplateDefaultsTests
{

    /// <summary>The classes the switch writes, and their ids, by name.</summary>
    private static readonly string[] Classes = ["Tenant", "Organization", "OrganizationUnit", "Role", "Seat"];

    private static readonly string[] Ids = ["TenantId", "OrganizationUnitId", "RoleId", "SeatId"];

    /// <summary>The fragment of the hint name of what the switch writes for <paramref name="name"/>.</summary>
    private static string Written(string name) => name + ".TemplateDefault.";

    /// <summary>A type of the compilation after generation, by its metadata name.</summary>
    private static INamedTypeSymbol TypeIn(GeneratorRunOutcome result, string metadataName)
        => result.OutputCompilation.GetTypeByMetadataName(metadataName)
           ?? throw new InvalidOperationException("'" + metadataName + "' is not in the compilation.\n" + result.HintNames);

    /// <summary>The class a type derives from, as C# writes it.</summary>
    private static string ParentOf(GeneratorRunOutcome result, string metadataName) => TypeIn(result, metadataName).BaseType!.ToDisplayString();

    /// <summary>
    /// What the switch writes and what it leaves out compile, and the switch's own error, DDD00066, is the only one: no
    /// generator threw over what it could not write, two ids of one name say, and none wrote code that fails.
    /// </summary>
    private static void CompilesBesidesWhatItSays(GeneratorRunOutcome result)
    {
        result.ShouldNotCrash();
        result.CompilationErrors.Should().BeEmpty();
        result.GeneratorDiagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(static diagnostic => diagnostic.Id)
            .Should().OnlyContain(static id => id == "DDD00066");
    }

    /// <summary>
    /// A package of two templates, the second of which names an id as a later type argument, with a registration and a
    /// class the use cases are named through that take that argument, and a switch that writes the first class and
    /// its id: the shape of a member class whose members are seats, without either supporting package.
    /// </summary>
    private const string Pinboard =
        """
        using System;
        using DTS.DDD.Abstractions.Attributes;
        using DTS.DDD.Abstractions.Interfaces;

        [assembly: TemplateRegistrations(typeof(Acme.Pins.NoteRegistrations))]
        [assembly: TemplateFacade(typeof(Acme.Pins.NoteUseCases<,,>))]

        namespace Acme.Pins;

        [AggregateRootBase]
        public abstract partial class PinAggregate<TPinId>
            where TPinId : IEntityId, IEquatable<TPinId>
        {
            protected PinAggregate(TPinId id) : base(id) { }
        }

        [AggregateRootTemplate(typeof(PinAggregate<>))]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class PinAttribute<TPinId> : Attribute;

        [AggregateRootBase]
        public abstract partial class NoteAggregate<TNoteId, TPinId>
            where TNoteId : IEntityId, IEquatable<TNoteId>
            where TPinId : struct, IEquatable<TPinId>
        {
            protected NoteAggregate(TNoteId id, TPinId pin) : base(id) => Pin = pin;

            public TPinId Pin { get; private set; }
        }

        [AggregateRootTemplate(typeof(NoteAggregate<,>))]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class NoteAttribute<TNoteId, TPinId> : Attribute;

        [TemplateDefaults(typeof(PinAttribute<>))]
        [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
        public sealed class GeneratePinClassesAttribute : Attribute;

        public static class NoteRegistrations
        {
            [TemplateRegistration]
            public static string AddNotes<
                [TemplateType(typeof(NoteAttribute<,>), Take = TemplateArgumentKind.Type)] TNote,
                [TemplateType(typeof(NoteAttribute<,>))] TNoteId,
                [TemplateType(typeof(NoteAttribute<,>), Argument = 1)] TPinId>(this string prefix)
                where TNote : NoteAggregate<TNoteId, TPinId>
                where TNoteId : IEntityId, IEquatable<TNoteId>
                where TPinId : struct, IEquatable<TPinId>
                => prefix + typeof(TPinId).FullName;
        }

        public abstract class NoteUseCases<
            [TemplateType(typeof(NoteAttribute<,>), Take = TemplateArgumentKind.Type)] TNote,
            [TemplateType(typeof(NoteAttribute<,>))] TNoteId,
            [TemplateType(typeof(NoteAttribute<,>), Argument = 1)] TPinId>
            where TNote : NoteAggregate<TNoteId, TPinId>
            where TNoteId : IEntityId, IEquatable<TNoteId>
            where TPinId : struct, IEquatable<TPinId>
        {
            protected NoteUseCases()
            {
            }

            public sealed record Pinned(TNoteId Note, TPinId Pin);
        }
        """;

    [Fact]
    public void A_later_type_argument_that_names_a_written_id_is_closed_into_the_registrations_and_the_class_of_the_use_cases()
    {
        const string source =
            """
            [assembly: Acme.Pins.GeneratePinClasses]
            [assembly: DTS.DDD.Abstractions.Attributes.Module("Board")]

            namespace Shop.Notes;

            using System;
            using Acme.Pins;
            using DTS.DDD.Abstractions.Attributes;

            [EntityId<Guid>]
            public readonly partial record struct NoteId;

            [Note<NoteId, PinId>]
            public sealed partial class Note
            {
                public Note(NoteId id, PinId pin) : base(id, pin)
                {
                }
            }

            public static class Startup
            {
                public static string Register() => "pinned by ".AddNotes();

                public static NoteUseCases.Pinned Pinned(Note note) => new(note.Id, note.Pin);
            }
            """;

        var result = GeneratorTestHost.Create(source).WithBuildProperty("RootNamespace", "Shop").WithReferencedAssembly(Pinboard, "Acme.Pins").RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldHaveGenerated(Written("Pin")).ShouldHaveGenerated(Written("PinId"));
        result.ShouldContain("Note.", ": global::Acme.Pins.NoteAggregate<global::Shop.Notes.NoteId, global::Shop.PinId>");
        result.ShouldContain("NoteUseCases.TemplateFacade", "global::Acme.Pins.NoteUseCases<global::Shop.Notes.Note, global::Shop.Notes.NoteId, global::Shop.PinId>");
        result.ShouldContain("NoteRegistrations.AddNotes.Registration", "AddNotes<global::Shop.Notes.Note, global::Shop.Notes.NoteId, global::Shop.PinId>(prefix)");
    }
}
