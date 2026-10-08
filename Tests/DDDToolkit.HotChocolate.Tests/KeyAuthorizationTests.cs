using System.Collections.Concurrent;
using System.Text.Json;
using DDDToolkit.Exceptions;
using DDDToolkit.HotChocolate.Authorization;
using DDDToolkit.HotChocolate.Tests.Infrastructure;
using FluentAssertions;
using Gallery.Api;
using Gallery.Application;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// <c>AddDDDToolkitKeyAuthorization()</c>: the policy of HotChocolate's <c>[Authorize]</c> is a permission key, asked
/// of the <see cref="IFieldKeys{TParent}"/> of the object the field belongs to. A caller that holds it gets the
/// field; one that does not gets <see langword="null"/> and the module's refusal; and a rule nobody can answer is
/// refused, never allowed.
/// </summary>
public class KeyAuthorizationTests
{
    [Fact]
    public async Task A_field_whose_key_the_caller_holds_is_answered()
    {
        var bindery = new Bindery().Hold(BinderyKeys.ReadMarginalia);

        var answer = await AskAsync("{ folio { id title marginalia } }", bindery);

        answer.TryGetProperty("errors", out _).Should().BeFalse("got {0}", answer);
        answer.GetProperty("data").GetProperty("folio").GetProperty("marginalia").GetString().Should().Be("See the second proof.");
        bindery.Asked.Should().Equal("FolioKeys: bindery.marginalia.read on folio 1");
    }

    [Fact]
    public async Task A_refused_field_is_null_with_the_modules_refusal_and_the_object_and_its_other_fields_stay()
    {
        var bindery = new Bindery();

        var answer = await AskAsync("{ folio { id title marginalia } }", bindery);

        // The field is null; the folio and what else was asked of it are answered.
        var folio = answer.GetProperty("data").GetProperty("folio");
        folio.GetProperty("id").GetInt32().Should().Be(1);
        folio.GetProperty("title").GetString().Should().Be("Tide Tables");
        folio.GetProperty("marginalia").ValueKind.Should().Be(JsonValueKind.Null);

        // One error, at the field, shaped as a refused query of the module is: its code, its kind, its arguments.
        var error = answer.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("message").GetString().Should().Be("The caller may not read this.");
        error.GetProperty("path").EnumerateArray().Select(segment => segment.ToString()).Should().Equal("folio", "marginalia");
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be(Bindery.NotPermitted);
        error.GetProperty("extensions").GetProperty("kind").GetString().Should().Be("NotPermitted");
        error.GetProperty("extensions").GetProperty("arguments").GetProperty("Key").GetString().Should().Be(BinderyKeys.ReadMarginalia);
        error.GetProperty("extensions").GetProperty("arguments").GetProperty("Folio").GetInt32().Should().Be(1);

        bindery.MarginaliaRead.Should().Be(0, "the key is asked before the resolver, which never ran");
    }

    [Fact]
    public async Task The_refusals_kind_is_spelled_as_the_error_filter_spells_it()
    {
        var answer = await AskAsync(
            "{ folio { marginalia } }",
            new Bindery(),
            configure: builder => builder.AddDDDToolkitErrors(EnumValueSpelling.LowerSnakeCase),
            errors: false);

        Extensions(answer).GetProperty("kind").GetString().Should().Be("not_permitted");
    }

    [Fact]
    public async Task A_parent_type_nobody_answers_for_is_refused_and_the_error_names_it()
    {
        // The caller would hold the key, but no IFieldKeys<FolioRow> is registered to say so.
        var bindery = new Bindery().Hold(BinderyKeys.ReadMarginalia);

        var answer = await AskAsync("{ folio { id marginalia } }", bindery, answersFor: Answers.Nobody);

        var folio = answer.GetProperty("data").GetProperty("folio");
        folio.GetProperty("id").GetInt32().Should().Be(1);
        folio.GetProperty("marginalia").ValueKind.Should().Be(JsonValueKind.Null);

        var error = answer.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("message").GetString().Should()
            .Be("The key 'bindery.marginalia.read' on 'Folio.marginalia' has nobody to answer for it: no IFieldKeys<FolioRow> is registered.");
        error.GetProperty("path").EnumerateArray().Select(segment => segment.ToString()).Should().Equal("folio", "marginalia");
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be(ErrorCodes.Authentication.PolicyNotFound);

        bindery.MarginaliaRead.Should().Be(0);
    }

    [Fact]
    public async Task A_parent_is_answered_for_by_its_own_type_and_otherwise_by_the_type_it_derives_from()
    {
        // A gilded folio is a folio. With only the folio's keys registered, they answer for it.
        var inherited = new Bindery().Hold(BinderyKeys.ReadMarginalia);
        var answer = await AskAsync("{ gildedFolio { marginalia } }", inherited);

        answer.TryGetProperty("errors", out _).Should().BeFalse("got {0}", answer);
        inherited.Asked.Should().Equal("FolioKeys: bindery.marginalia.read on folio 2");

        // With keys of its own registered as well, those are asked, and the folio's are not.
        var own = new Bindery().Hold(BinderyKeys.ReadMarginalia);
        await AskAsync("{ gildedFolio { marginalia } }", own, answersFor: Answers.FoliosAndGildedFolios);

        own.Asked.Should().Equal("GildedFolioKeys: bindery.marginalia.read on folio 2");
    }

    [Fact]
    public async Task A_field_without_a_parent_is_refused()
    {
        // A field of a Query type that has no runtime type has no object to ask about, as a [Query] method has none.
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType(descriptor => descriptor.Name("Query").Field("tally").Type<StringType>().Authorize(BinderyKeys.ReadMarginalia).Resolve("Everything."))
            .AddDDDToolkitKeyAuthorization()
            .BuildRequestExecutorAsync(cancellationToken: Cancellation);

        var answer = Parse(await executor.ExecuteAsync("{ tally }", Cancellation));

        answer.GetProperty("data").GetProperty("tally").ValueKind.Should().Be(JsonValueKind.Null);
        var error = answer.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("message").GetString().Should()
            .Be("The key 'bindery.marginalia.read' on 'Query.tally' has no object to be asked about: the field has no parent.");
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be(ErrorCodes.Authentication.PolicyNotFound);
    }

    [Fact]
    public async Task A_rule_that_names_roles_is_refused()
    {
        var bindery = new Bindery().Hold(BinderyKeys.ReadMarginalia);

        var answer = await AskAsync("{ folio { id colophon } }", bindery);

        answer.GetProperty("data").GetProperty("folio").GetProperty("colophon").ValueKind.Should().Be(JsonValueKind.Null);
        Error(answer).GetProperty("message").GetString().Should()
            .Be("The rule on 'Folio.colophon' names roles. A permission key is asked here, and roles are not.");
        Extensions(answer).GetProperty("code").GetString().Should().Be(ErrorCodes.Authentication.PolicyNotFound);
        bindery.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rule_applied_after_the_resolver_is_refused()
    {
        var bindery = new Bindery().Hold(BinderyKeys.ReadMarginalia);

        var answer = await AskAsync("{ folio { id stitching } }", bindery);

        // The resolver ran, as HotChocolate runs it first for such a rule, and what it answered is not passed on.
        answer.GetProperty("data").GetProperty("folio").GetProperty("stitching").ValueKind.Should().Be(JsonValueKind.Null);
        Error(answer).GetProperty("message").GetString().Should().StartWith("The key 'bindery.marginalia.read' on 'Folio.stitching' is not asked before the resolver.");
        bindery.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rule_applied_during_validation_refuses_the_request()
    {
        var bindery = new Bindery().Hold(BinderyKeys.ReadMarginalia);

        var answer = await AskAsync("{ folio { id endpapers } }", bindery);

        // Nothing is resolved: there is no data at all, and the error has no path.
        answer.TryGetProperty("data", out _).Should().BeFalse("got {0}", answer);
        Error(answer).GetProperty("message").GetString().Should().StartWith("The request asks for 'bindery.marginalia.read' during validation.");
        Error(answer).TryGetProperty("path", out _).Should().BeFalse();
        bindery.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rule_without_a_key_is_HotChocolates_missing_default_policy()
    {
        var answer = await AskAsync("{ folio { id pressmark } }", new Bindery().Hold(BinderyKeys.ReadMarginalia));

        answer.GetProperty("data").GetProperty("folio").GetProperty("pressmark").ValueKind.Should().Be(JsonValueKind.Null);
        Extensions(answer).GetProperty("code").GetString().Should().Be(ErrorCodes.Authentication.NoDefaultPolicy);
    }

    [Fact]
    public async Task A_rule_on_a_type_is_asked_about_the_object_of_each_field_that_returns_the_type()
    {
        // HotChocolate applies a rule on a type to every field that returns the type. The key is then asked about
        // the object that field belongs to, not about the object under the rule: for a folio's slipcase, the folio.
        var bindery = new Bindery().Hold(BinderyKeys.ReadMarginalia);

        var answer = await AskAsync("{ folio { slipcase { cloth } } slipcase { cloth } }", bindery);

        answer.GetProperty("data").GetProperty("folio").GetProperty("slipcase").GetProperty("cloth").GetString().Should().Be("linen");
        bindery.Asked.Should().Equal("FolioKeys: bindery.marginalia.read on folio 1");

        // A field of Query that returns the type belongs to an object nobody answers for, so it is refused: a
        // rule on a type never opens what a rule on the field would have closed.
        answer.GetProperty("data").GetProperty("slipcase").ValueKind.Should().Be(JsonValueKind.Null);
        Error(answer).GetProperty("message").GetString().Should()
            .Be("The key 'bindery.marginalia.read' on 'BinderyQueries.slipcase' has nobody to answer for it: no IFieldKeys<BinderyQueries> is registered.");
        Error(answer).GetProperty("path").EnumerateArray().Select(segment => segment.ToString()).Should().Equal("slipcase");
    }

    [Fact]
    public async Task The_parents_of_a_list_are_asked_about_in_one_question()
    {
        // The gallery's rule asks through a data loader keyed by the exhibit. The caller holds the key on the
        // first and the third exhibit.
        var desk = new GalleryDesk().Hold(GalleryKeys.ViewValuations, 1, 3);
        var schema = new ServiceCollection()
            .AddSingleton(desk)
            .AddGalleryServices()
            .AddGraphQL()
            .AddSourceSchemaDefaults()
            .AddGalleryTypes()
            .AddDDDToolkitErrors()
            .AddDDDToolkitEntityNullability()
            .AddDDDToolkitKeyAuthorization();

        // The loader's batch leaves when it holds the three exhibits, however far apart their rules are asked.
        // HotChocolate's own dispatcher sends a batch once it has been quiet for a moment, which on a busy
        // machine makes two of this one: what is counted here is what a batch costs, and not the machine.
        var batches = new WholeBatches();
        batches.WholeAt(3);
        batches.AddTo(schema.Services);
        var executor = await schema.BuildRequestExecutorAsync(cancellationToken: Cancellation);

        var answer = Parse(await executor.ExecuteAsync("{ exhibits { id title valuation } }", Cancellation));

        // Three exhibits, three rules, one batch, one question.
        batches.Sent.Should().Equal([3], "the rules of the three exhibits waited on one batch, sent whole");
        desk.KeyQuestions.Should().ContainSingle().Which.Should().BeEquivalentTo([1, 2, 3]);

        var exhibits = answer.GetProperty("data").GetProperty("exhibits").EnumerateArray().ToArray();
        exhibits.Select(exhibit => exhibit.GetProperty("title").GetString()).Should().Equal("Night Ferry", "Salt Marsh", "Tin Orchard");
        exhibits[0].GetProperty("valuation").GetInt32().Should().Be(1000);
        exhibits[1].GetProperty("valuation").ValueKind.Should().Be(JsonValueKind.Null);
        exhibits[2].GetProperty("valuation").GetInt32().Should().Be(3000);
        desk.ValuationsRead.Should().Be(2, "the refused exhibit's resolver did not run");

        var error = answer.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("path").EnumerateArray().Select(segment => segment.ToString()).Should().Equal("exhibits", "1", "valuation");
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be(GalleryRefusals.NotPermitted);
        error.GetProperty("extensions").GetProperty("arguments").GetProperty("Exhibit").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task It_replaces_another_handler_and_registering_it_again_is_harmless()
    {
        var services = new ServiceCollection().AddSingleton(new Bindery());
        var builder = services
            .AddGraphQL()
            .AddQueryType<BinderyQueries>()
            .AddAuthorizationHandler<AllowsEverything>()
            .AddDDDToolkitKeyAuthorization()
            .AddDDDToolkitKeyAuthorization();

        // An application has one handler: HotChocolate keeps the last one registered.
        services.Where(service => service.ServiceType == typeof(IAuthorizationHandler))
            .Should().ContainSingle().Which.ImplementationType.Should().Be<KeyAuthorizationHandler>();

        var executor = await builder.BuildRequestExecutorAsync(cancellationToken: Cancellation);
        var answer = Parse(await executor.ExecuteAsync("{ folio { marginalia } }", Cancellation));

        Extensions(answer).GetProperty("code").GetString().Should().Be(ErrorCodes.Authentication.PolicyNotFound, "the key is asked, and here nobody answers for a folio");
    }

    [Fact]
    public async Task The_rule_is_in_the_schema_for_a_client_to_read()
    {
        var executor = await Schema(new Bindery()).BuildRequestExecutorAsync(cancellationToken: Cancellation);

        executor.Schema.ToString().Should().Contain("marginalia: String @authorize(policy: \"bindery.marginalia.read\")");
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Who is registered to answer for a key.</summary>
    private enum Answers
    {
        Nobody,
        Folios,
        FoliosAndGildedFolios,
    }

    private static IRequestExecutorBuilder Schema(Bindery bindery, Answers answersFor = Answers.Folios)
    {
        var services = new ServiceCollection().AddSingleton(bindery);

        if (answersFor != Answers.Nobody)
        {
            services.AddScoped<IFieldKeys<FolioRow>, FolioKeys>();
        }

        if (answersFor == Answers.FoliosAndGildedFolios)
        {
            services.AddScoped<IFieldKeys<GildedFolioRow>, GildedFolioKeys>();
        }

        return services
            .AddGraphQL()
            .AddQueryType<BinderyQueries>()
            .AddDDDToolkitKeyAuthorization();
    }

    /// <summary>Asks the bindery's schema and answers the whole response, errors and all.</summary>
    private static async Task<JsonElement> AskAsync(
        string query,
        Bindery bindery,
        Answers answersFor = Answers.Folios,
        Action<IRequestExecutorBuilder>? configure = null,
        bool errors = true)
    {
        var builder = Schema(bindery, answersFor);
        if (errors)
        {
            builder.AddDDDToolkitErrors();
        }

        configure?.Invoke(builder);

        var executor = await builder.BuildRequestExecutorAsync(cancellationToken: Cancellation);
        return Parse(await executor.ExecuteAsync(query, Cancellation));
    }

    private static JsonElement Parse(IExecutionResult result)
    {
        using var document = JsonDocument.Parse(result.ToJson());
        return document.RootElement.Clone();
    }

    private static JsonElement Error(JsonElement answer)
        => answer.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;

    private static JsonElement Extensions(JsonElement answer) => Error(answer).GetProperty("extensions");

    // ------------------------------------------------------------------ a bindery, and its folios

    /// <summary>The key the bindery's fields ask for.</summary>
    public static class BinderyKeys
    {
        public const string ReadMarginalia = "bindery.marginalia.read";
    }

    /// <summary>What stands in for the bindery's own access rules: the keys the caller holds, and a record of what was asked.</summary>
    public sealed class Bindery
    {
        public const string NotPermitted = "bindery.not-permitted";

        private readonly HashSet<string> _held = [];
        private int _marginaliaRead;

        /// <summary>Every question a rule asked, in order: who was asked, for which key, about which folio.</summary>
        public ConcurrentQueue<string> Asked { get; } = new();

        /// <summary>How often the field under the rule was resolved.</summary>
        public int MarginaliaRead => Volatile.Read(ref _marginaliaRead);

        public Bindery Hold(string key)
        {
            _held.Add(key);
            return this;
        }

        public string ReadMarginalia()
        {
            Interlocked.Increment(ref _marginaliaRead);
            return "See the second proof.";
        }

        public RefusalException? Refused(string asked, string key, int folio)
        {
            Asked.Enqueue($"{asked}: {key} on folio {folio}");

            return _held.Contains(key)
                ? null
                : new RefusalException(
                    NotPermitted,
                    RefusalKind.NotPermitted,
                    "The caller may not read this.",
                    new Dictionary<string, object?> { ["Key"] = key, ["Folio"] = folio });
        }
    }

    /// <summary>A folio. Each field under a rule states the rule another way.</summary>
    [GraphQLName("Folio")]
    public record FolioRow(int Id, string Title)
    {
        /// <summary>The rule as a module writes it: the key, asked before the resolver.</summary>
        [Authorize(BinderyKeys.ReadMarginalia)]
        public string? GetMarginalia([Service] Bindery bindery) => bindery.ReadMarginalia();

        /// <summary>A rule that also names roles, which nothing here reads.</summary>
        [Authorize(BinderyKeys.ReadMarginalia, Roles = ["scribe"])]
        public string? Colophon => "Printed in two colors.";

        /// <summary>A rule applied after the resolver.</summary>
        [Authorize(BinderyKeys.ReadMarginalia, ApplyPolicy.AfterResolver)]
        public string? Stitching => "Coptic.";

        /// <summary>A rule applied while the request is validated.</summary>
        [Authorize(BinderyKeys.ReadMarginalia, ApplyPolicy.Validation)]
        public string? Endpapers => "Marbled.";

        /// <summary>A rule that names no key.</summary>
        [Authorize]
        public string? Pressmark => "B.14";

        /// <summary>A field without a rule of its own, which returns a type that carries one.</summary>
        public SlipcaseRow? Slipcase => new("linen");
    }

    /// <summary>A slipcase: the rule is on the type, so HotChocolate applies it to every field that returns one.</summary>
    [GraphQLName("Slipcase")]
    [Authorize(BinderyKeys.ReadMarginalia)]
    public sealed record SlipcaseRow(string Cloth);

    /// <summary>A folio of a kind of its own: another object type, whose runtime type derives from the folio's.</summary>
    [GraphQLName("GildedFolio")]
    public sealed record GildedFolioRow(int Id, string Title) : FolioRow(Id, Title);

    public sealed class BinderyQueries
    {
        public FolioRow GetFolio() => new(1, "Tide Tables");

        public GildedFolioRow GetGildedFolio() => new(2, "Psalter");

        public SlipcaseRow? GetSlipcase() => new("buckram");
    }

    public sealed class FolioKeys(Bindery bindery) : IFieldKeys<FolioRow>
    {
        public ValueTask<RefusalException?> RefusedAsync(FolioRow parent, string key, IResolverContext context, CancellationToken cancellationToken)
            => new(bindery.Refused(nameof(FolioKeys), key, parent.Id));
    }

    public sealed class GildedFolioKeys(Bindery bindery) : IFieldKeys<GildedFolioRow>
    {
        public ValueTask<RefusalException?> RefusedAsync(GildedFolioRow parent, string key, IResolverContext context, CancellationToken cancellationToken)
            => new(bindery.Refused(nameof(GildedFolioKeys), key, parent.Id));
    }

    /// <summary>Another handler, as an application might have registered before: it allows everything.</summary>
    public sealed class AllowsEverything : IAuthorizationHandler
    {
        public ValueTask<AuthorizeResult> AuthorizeAsync(IMiddlewareContext context, AuthorizeDirective directive, CancellationToken cancellationToken = default)
            => new(AuthorizeResult.Allowed);

        public ValueTask<AuthorizeResult> AuthorizeAsync(AuthorizationContext context, IReadOnlyList<AuthorizeDirective> directives, CancellationToken cancellationToken = default)
            => new(AuthorizeResult.Allowed);
    }
}
