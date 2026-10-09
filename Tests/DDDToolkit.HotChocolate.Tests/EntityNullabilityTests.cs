using FluentAssertions;
using Gallery.Api;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Relay;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// <c>AddDDDToolkitEntityNullability()</c>: every field of an object type with a key may be null in the schema,
/// except the key, whatever the type it is declared over says. A gateway that cannot resolve a reference to an
/// entity then answers the key with the other fields null, instead of an error for each field that is never null.
/// </summary>
public class EntityNullabilityTests
{
    [Fact]
    public async Task A_type_class_over_a_record_has_every_field_but_its_key_nullable()
    {
        // The gallery declares Exhibit over the record its application answers, with HotChocolate's generator. The
        // record says a title is never null, and so does every resolver beside it.
        var exhibit = Block(await GalleryAsync(), "type Exhibit");

        exhibit.Should().Contain("id: Int!", "the key stays what the record says");
        exhibit.Should().Contain("title: String\n").And.Contain("year: Int\n", "a property of the record");
        exhibit.Should().Contain("note: String\n", "a property that could be null already is unchanged");
        exhibit.Should().Contain("caption: String\n", "a resolver of the type class");
        exhibit.Should().Contain("curator: Curator\n", "a resolver that answers another entity");
        exhibit.Should().Contain("loans: [Loan!]\n", "a resolver that answers a list: the list may be null, its items may not");
        exhibit.Should().Contain("techniques: [String!]\n", "a list of the record");
        exhibit.Should().Contain("valuation: Int @authorize", "a field keeps its directives");
        NeverNull(exhibit).Should().Equal("id: Int!");
    }

    [Fact]
    public async Task A_type_without_a_key_is_untouched()
    {
        var sdl = await GalleryAsync();

        // A loan has no key, so nothing outside the gallery names one, and no gateway has to leave one empty.
        NeverNull(Block(sdl, "type Loan")).Should().BeEquivalentTo("weeks: Int!", "lender: String!", "days: Int!");
        Block(sdl, "type Query").Should().Contain("exhibits: [Exhibit!]!");
    }

    [Fact]
    public async Task A_type_that_is_only_its_key_is_unchanged()
    {
        // The gallery names a curator and has nothing of one but the key: every source schema can have the
        // convention, also one that only names an entity.
        Lines(Block(await GalleryAsync(), "type Curator")).Should().Equal("id: Int!");
    }

    [Fact]
    public async Task Without_the_convention_the_fields_are_what_the_records_say()
    {
        // The same schema without the call: why a host needs it, and proof that the tests above see its doing.
        var exhibit = Block(await GalleryAsync(nullability: false), "type Exhibit");

        NeverNull(exhibit).Should().BeEquivalentTo(
            "id: Int!", "title: String!", "year: Int!", "caption: String!", "curator: Curator!", "loans: [Loan!]!", "techniques: [String!]!",
            "valuation: Int! @authorize(policy: \"gallery.valuations.view\")");
    }

    [Fact]
    public async Task Fields_a_type_gets_from_its_runtime_type_and_from_an_extension_are_nullable_alike()
    {
        // No generator here: the key is an attribute on the record, HotChocolate infers the fields from its
        // properties and its method, and a type extension adds one more.
        var sdl = await PrintAsync(builder => builder
            .AddQueryType<AnnexQueries>()
            .AddTypeExtension<AlcoveExtension>());

        Lines(Block(sdl, "type Alcove")).Should().BeEquivalentTo(
            "id: Int!",
            "name: String",
            "lamps: [String!]",
            "storey: Storey",
            "signage: String",
            "guide: String");
        NeverNull(Block(sdl, "type Storey")).Should().BeEquivalentTo(["id: Int!", "level: Int!"], "Storey has no key");
    }

    [Fact]
    public async Task Every_field_named_in_the_key_stays_as_declared()
    {
        // A key of two fields, the second reaching into an object: both top fields are the key of this type, and
        // a field called id that is not in the key is a field like any other.
        var sdl = await PrintAsync(builder => builder.AddQueryType<AnnexQueries>());

        var crypt = Block(sdl, "type Crypt");
        crypt.Should().Contain("@key(fields: \"code storey { id }\")");
        Lines(crypt).Should().BeEquivalentTo(
            "id: Int",
            "code: String!",
            "storey: Storey!",
            "keeper: String");
    }

    [Fact]
    public async Task With_several_keys_every_field_named_in_any_of_them_stays_as_declared()
    {
        var sdl = await PrintAsync(builder => builder.AddQueryType<LecternQueries>());

        Lines(Block(sdl, "type Lectern")).Should().BeEquivalentTo(
            "id: Int!",
            "code: String!",
            "wood: String");
    }

    [Fact]
    public async Task However_a_fields_type_is_written_down_it_becomes_nullable()
    {
        // A type class of HotChocolate's own, with the key from the descriptor, and each field's type said another
        // way: by schema type, in schema syntax with and without a member behind it, as an instance, and inferred.
        var sdl = await PrintAsync(builder => builder
            .AddQueryType<AnnexQueries>()
            .AddType<PlinthType>());

        Lines(Block(sdl, "type Plinth")).Should().BeEquivalentTo(
            "id: Int!",
            "stone: String",
            "finish: String",
            "span: Long",
            "height: Int",
            "depth: Int",
            "girths: [Int!]");
    }

    [Fact]
    public async Task The_convention_goes_by_the_key_the_printed_type_shows()
    {
        // An easel declares no key. All that says it has one is a lookup that answers it, and whether that
        // puts a key on the source schema's type is HotChocolate's to decide: where it does, the type is an
        // entity like any other, and where it does not, the type is left as its record says.
        var sdl = await PrintAsync(builder => builder.AddQueryType<EaselQueries>());

        var easel = Block(sdl, "type Easel");
        if (easel.Contains("@key(fields: \"id\")", StringComparison.Ordinal))
        {
            NeverNull(easel).Should().Equal("id: Int!");
        }
        else
        {
            NeverNull(easel).Should().BeEquivalentTo("id: Int!", "joiner: String!");
        }
    }

    [Fact]
    public async Task An_interface_the_entity_implements_has_to_allow_null_as_well()
    {
        // The convention changes object types. A field an entity shares with an interface that declares it as
        // never null no longer fits the interface, and HotChocolate says so when the schema is built.
        var building = PrintAsync(builder => builder.AddQueryType<EngravedQueries>().AddType<CrateRow>());

        var refused = (await building.Invoking(async task => await task).Should().ThrowAsync<SchemaException>()).Which;
        refused.Errors.Should().ContainSingle().Which.Message.Should().Contain("Field `inscription` must return a type which is equal to or a subtype of");
    }

    [Fact]
    public async Task An_entity_that_is_a_node_keeps_its_id_and_fits_the_Node_interface()
    {
        // Relay's Node interface declares id as never null, and an entity has to keep fitting it. Its id is its
        // key, so it does; a field that carries another node's id is an ID like any other field, and may be null.
        var sdl = await PrintAsync(builder => builder
            .AddGlobalObjectIdentification()
            .AddQueryType<VitrineQueries>());

        var vitrine = Block(sdl, "type Vitrine");
        vitrine.Should().StartWith("type Vitrine implements Node @key(fields: \"id\")");
        Lines(vitrine).Should().BeEquivalentTo(
            "id: ID!",
            "name: String",
            "keeper: ID");
    }

    [Fact]
    public async Task Registering_the_convention_twice_changes_nothing()
    {
        var once = await PrintAsync(builder => builder.AddQueryType<AnnexQueries>());
        var twice = await PrintAsync(builder => builder.AddQueryType<AnnexQueries>().AddDDDToolkitEntityNullability());

        twice.Should().Be(once);
    }

    [Fact]
    public async Task A_field_that_became_nullable_still_answers_its_value()
    {
        var executor = await Gallery().BuildRequestExecutorAsync(cancellationToken: Cancellation);

        var result = await executor.ExecuteAsync("{ exhibits { id title caption techniques loans { lender weeks } curator { id } } }", Cancellation);

        var json = result.ToJson();
        json.Should().NotContain("\"errors\"");
        json.Should().Contain("\"caption\": \"Night Ferry (1921)\"").And.Contain("\"lender\": \"Harbor Museum\"");
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The gallery's source schema, as its module registers it.</summary>
    private static IRequestExecutorBuilder Gallery(bool nullability = true)
    {
        var builder = new ServiceCollection()
            .AddGalleryServices()
            .AddGraphQL()
            .AddSourceSchemaDefaults()
            .AddGalleryTypes()
            .AddDDDToolkitKeyAuthorization();

        return nullability ? builder.AddDDDToolkitEntityNullability() : builder;
    }

    private static async Task<string> GalleryAsync(bool nullability = true)
        => (await Gallery(nullability).BuildRequestExecutorAsync(cancellationToken: Cancellation)).Schema.ToString();

    private static async Task<string> PrintAsync(Action<IRequestExecutorBuilder> types)
    {
        var builder = new ServiceCollection().AddGraphQL().AddSourceSchemaDefaults().AddDDDToolkitEntityNullability();
        types(builder);
        return (await builder.BuildRequestExecutorAsync(cancellationToken: Cancellation)).Schema.ToString();
    }

    /// <summary>One type of a printed schema: from its first line to the brace that closes it.</summary>
    private static string Block(string sdl, string start)
    {
        var text = sdl.ReplaceLineEndings("\n");
        var from = text.IndexOf(start + " ", StringComparison.Ordinal);
        from.Should().BeGreaterThanOrEqualTo(0, "the schema declares '{0}'", start);
        return text[from..(text.IndexOf("\n}", from, StringComparison.Ordinal) + 2)];
    }

    /// <summary>The fields of a printed type, one per line.</summary>
    private static string[] Lines(string block)
        => [.. block.Split('\n').Skip(1).Select(line => line.Trim()).Where(line => line.Length > 1)];

    private static string[] NeverNull(string block)
        => [.. Lines(block).Where(line => line.Split('@')[0].TrimEnd().EndsWith('!'))];

    // ------------------------------------------------------------------ types declared without a generator

    /// <summary>An alcove: the key as an attribute on the record, the fields inferred from it.</summary>
    [GraphQLName("Alcove")]
    [EntityKey("id")]
    public sealed record AlcoveRow(int Id, string Name, IReadOnlyList<string> Lamps, StoreyRow Storey)
    {
        /// <summary>A method of the runtime type, which HotChocolate publishes as a field.</summary>
        public string GetSignage() => "Alcove " + Name;
    }

    /// <summary>A storey, without a key.</summary>
    [GraphQLName("Storey")]
    public sealed record StoreyRow(int Id, int Level);

    /// <summary>A crypt, known by its code and the storey it is on: a key of two fields, one of them an object.</summary>
    [GraphQLName("Crypt")]
    [EntityKey("code storey { id }")]
    public sealed record CryptRow(int Id, string Code, StoreyRow Storey, string Keeper);

    /// <summary>A lectern with two keys: either finds it.</summary>
    [GraphQLName("Lectern")]
    [EntityKey("id")]
    [EntityKey("code")]
    public sealed record LecternRow(int Id, string Code, string Wood);

    public sealed class LecternQueries
    {
        public LecternRow GetLectern() => new(3, "L-3", "ash");
    }

    /// <summary>A plinth: its type is declared by <see cref="PlinthType"/>.</summary>
    public sealed record PlinthRow(int Id, string Stone, string Finish, IReadOnlyList<int> Girths);

    [ExtendObjectType<AlcoveRow>]
    public sealed class AlcoveExtension
    {
        /// <summary>A resolver an extension adds to the alcove.</summary>
        public string GetGuide([Parent] AlcoveRow alcove) => "Guide to " + alcove.Name;
    }

    public sealed class PlinthType : ObjectType<PlinthRow>
    {
        protected override void Configure(IObjectTypeDescriptor<PlinthRow> descriptor)
        {
            descriptor.Name("Plinth");
            descriptor.Directive(new EntityKey("id"));
            descriptor.Field(plinth => plinth.Stone).Type<NonNullType<StringType>>();
            descriptor.Field(plinth => plinth.Finish).Type("String!");
            descriptor.Field("span").Type(new NonNullType(new LongType())).Resolve(40L);
            descriptor.Field("height").Type<NonNullType<IntType>>().Resolve(120);
            descriptor.Field("depth").Type("Int!").Resolve(30);
        }
    }

    /// <summary>An easel, without a declared key: all that says it has one is a lookup.</summary>
    [GraphQLName("Easel")]
    public sealed record EaselRow(int Id, string Joiner);

    public sealed class EaselQueries
    {
        [Lookup]
        public EaselRow? GetEaselById(int id) => new(id, "Ruvan");
    }

    /// <summary>An interface that declares its field as never null.</summary>
    [InterfaceType("Engraved")]
    public interface IEngraved
    {
        string Inscription { get; }
    }

    /// <summary>An entity that implements it.</summary>
    [GraphQLName("Crate")]
    [EntityKey("id")]
    public sealed record CrateRow(int Id, string Inscription) : IEngraved;

    public sealed class EngravedQueries
    {
        public IEngraved GetEngraved() => new CrateRow(1, "fragile");
    }

    /// <summary>A vitrine: an entity that is also a Relay node, with a field that holds the id of another node.</summary>
    [Node]
    [GraphQLName("Vitrine")]
    [EntityKey("id")]
    public sealed record VitrineRow(int Id, string Name, [property: ID("Keeper")] int Keeper)
    {
        /// <summary>What <c>node(id:)</c> answers a vitrine by.</summary>
        [NodeResolver]
        public static VitrineRow? Find(int id) => new(id, "Strongroom", 4);
    }

    public sealed class VitrineQueries
    {
        public VitrineRow GetVitrine() => VitrineRow.Find(1)!;
    }

    public sealed class AnnexQueries
    {
        private static readonly StoreyRow Ground = new(1, 0);

        public AlcoveRow GetAlcove() => new(1, "Rotunda", ["brass", "glass"], Ground);

        public CryptRow GetCrypt() => new(7, "C-7", Ground, "Imre");
    }
}
