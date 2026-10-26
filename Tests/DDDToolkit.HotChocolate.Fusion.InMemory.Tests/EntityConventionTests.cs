using System.Text.Json;
using DDDToolkit.HotChocolate.Fusion.InMemory.Tests.Infrastructure;
using DDDToolkit.HotChocolate.Tests.Infrastructure;
using FluentAssertions;
using Gallery.Api;
using Gallery.Application;
using Xunit;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests;

/// <summary>
/// What a client of the gateway sees of a module that declares its GraphQL types over its own records, with
/// <c>AddDDDToolkitEntityNullability()</c> and <c>AddDDDToolkitKeyAuthorization()</c> on its source schema: a
/// reference its owner answers nothing for, and a field under a rule. Asked over real HTTP.
/// </summary>
public sealed class EntityConventionTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_reference_the_owner_answers_nothing_for_is_its_key_with_every_other_field_null_and_no_error()
    {
        await using var museum = await GatewayHost.StartAsync(builder =>
        {
            builder.Services.AddGallery();
            builder.Services.AddTours();
        });

        // The stray tour stops at exhibit 9. The gallery's lookup answers nothing for it, as it does for an exhibit
        // the caller may not read. Every field asked here is one the gallery's record declares as never null.
        var body = await museum.PostAsync(
            "{ strayTour { id exhibit { id title year caption techniques loans { lender } curator { id } } } tours { id exhibit { id title caption } } }");

        body.TryGetProperty("errors", out _).Should().BeFalse("the gateway answered {0}", body);

        // The reference is an object: the key Tours gave, and nothing where the owner had nothing to say.
        var stray = body.GetProperty("data").GetProperty("strayTour").GetProperty("exhibit");
        stray.GetProperty("id").GetInt32().Should().Be(9);
        foreach (var field in (string[])["title", "year", "caption", "techniques", "loans", "curator"])
        {
            stray.GetProperty(field).ValueKind.Should().Be(JsonValueKind.Null, "'{0}' has nobody to answer it", field);
        }

        // A reference the owner does answer is whole, in the same response.
        var stops = body.GetProperty("data").GetProperty("tours").EnumerateArray().Select(tour => tour.GetProperty("exhibit")).ToArray();
        stops.Select(exhibit => exhibit.GetProperty("title").GetString()).Should().Equal("Night Ferry", "Tin Orchard");
        stops[0].GetProperty("caption").GetString().Should().Be("Night Ferry (1921)");

        // The composed schema says what a client may count on: the key, and no other field of an exhibit.
        var exhibit = Block(await museum.Schemas.PrintGatewayAsync(Cancellation), "type Exhibit");
        exhibit.Should().Contain("id: Int!").And.Contain("title: String\n").And.Contain("loans: [Loan!]\n");
        exhibit.Split('\n').Where(line => line.TrimEnd().EndsWith('!')).Should().ContainSingle("only the key is never null");
    }

    [Fact]
    public async Task Without_the_convention_the_same_reference_is_null_with_an_error()
    {
        // The gallery's schema as its records say it: a title is never null. This is what the convention is for.
        await using var museum = await GatewayHost.StartAsync(builder =>
        {
            builder.Services.AddGallery(entityNullability: false);
            builder.Services.AddTours();
        });

        var body = await museum.PostAsync("{ strayTour { id exhibit { id title } } }");

        body.GetProperty("data").GetProperty("strayTour").GetProperty("exhibit").ValueKind.Should().Be(JsonValueKind.Null);
        var error = body.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("path").EnumerateArray().Select(segment => segment.ToString()).Should().Equal("strayTour", "exhibit", "title");
    }

    [Fact]
    public async Task A_refused_field_is_null_through_the_gateway_with_the_modules_refusal_and_a_page_costs_one_question()
    {
        // The gallery's loader runs in its source schema, behind the gateway, and that schema's requests take
        // their dispatcher from the application's services. Its batch leaves when it holds the three exhibits,
        // and not when HotChocolate's own dispatcher finds it quiet: what is counted is what a batch costs.
        var batches = new WholeBatches();
        batches.WholeAt(3);
        await using var museum = await GatewayHost.StartAsync(builder =>
        {
            builder.Services.AddGallery();
            builder.Services.AddTours();
            batches.AddTo(builder.Services);
        });

        // The caller holds the key on the first and the third exhibit.
        var desk = museum.Services.GetRequiredService<GalleryDesk>().Hold(GalleryKeys.ViewValuations, 1, 3);

        var body = await museum.PostAsync("{ exhibits { id title valuation } }");

        // The refused field is null, and the exhibit and its other fields are answered.
        var exhibits = body.GetProperty("data").GetProperty("exhibits").EnumerateArray().ToArray();
        exhibits.Select(exhibit => exhibit.GetProperty("title").GetString()).Should().Equal("Night Ferry", "Salt Marsh", "Tin Orchard");
        exhibits[0].GetProperty("valuation").GetInt32().Should().Be(1000);
        exhibits[1].GetProperty("valuation").ValueKind.Should().Be(JsonValueKind.Null);
        exhibits[2].GetProperty("valuation").GetInt32().Should().Be(3000);

        // The module's refusal reaches the client as a refused query's does: at the field, with code, kind and arguments.
        var error = body.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("message").GetString().Should().Be("The caller may not do this here.");
        error.GetProperty("path").EnumerateArray().Select(segment => segment.ToString()).Should().Equal("exhibits", "1", "valuation");
        var extensions = error.GetProperty("extensions");
        extensions.GetProperty("code").GetString().Should().Be(GalleryRefusals.NotPermitted);
        extensions.GetProperty("kind").GetString().Should().Be("not_permitted");
        extensions.GetProperty("arguments").GetProperty("Key").GetString().Should().Be(GalleryKeys.ViewValuations);
        extensions.GetProperty("arguments").GetProperty("Exhibit").GetInt32().Should().Be(2);

        // Three exhibits under the rule, one batch, one question, and the refused exhibit's resolver never ran.
        batches.Sent.Should().Equal([3], "the rules of the three exhibits waited on one batch, sent whole");
        desk.KeyQuestions.Should().ContainSingle().Which.Should().BeEquivalentTo([1, 2, 3]);
        desk.ValuationsRead.Should().Be(2);
    }

    [Fact]
    public async Task The_rule_is_in_the_composed_schema_for_a_client_to_read()
    {
        await using var museum = await GatewayHost.StartAsync(builder =>
        {
            builder.Services.AddGallery();
            builder.Services.AddTours();
        });

        Block(await museum.Schemas.PrintGatewayAsync(Cancellation), "type Exhibit")
            .Should().Contain("valuation: Int @authorize(policy: \"gallery.valuations.view\")");
    }

    /// <summary>One type of a printed schema: from its first line to the brace that closes it.</summary>
    private static string Block(string sdl, string start)
    {
        var text = sdl.ReplaceLineEndings("\n");
        var from = text.IndexOf(start + " ", StringComparison.Ordinal);
        from.Should().BeGreaterThanOrEqualTo(0, "the schema declares '{0}'", start);
        return text[from..(text.IndexOf("\n}", from, StringComparison.Ordinal) + 2)];
    }
}
