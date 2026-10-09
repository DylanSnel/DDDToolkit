using System.Text.Json;
using DDDToolkit.Exceptions;
using DDDToolkit.HotChocolate.Tests.Domain;
using DDDToolkit.Invariants;
using DDDToolkit.Localization;
using DDDToolkit.Validation;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// <c>AddDDDToolkitErrors()</c>: the toolkit's failure exceptions reach a GraphQL client as one error per
/// failure, each with a code to branch on, in the reader's language when a localizer is registered.
/// </summary>
public class FailureErrorFilterTests
{
    [Fact]
    public async Task An_invalid_value_object_becomes_one_error_per_failure()
    {
        var errors = await ErrorsAsync("{ refuse }");

        errors.Should().HaveCount(2);
        errors[0].GetProperty("message").GetString().Should().Be("A street is at most 40 characters.");
        Extensions(errors[0]).GetProperty("code").GetString().Should().Be("Street.TooLong");
        Extensions(errors[0]).GetProperty("field").GetString().Should().Be("Street");
        Extensions(errors[0]).GetProperty("arguments").GetProperty("MaxLength").GetInt32().Should().Be(40);
        Extensions(errors[1]).GetProperty("code").GetString().Should().Be("City.Required");
        errors[1].GetProperty("path")[0].GetString().Should().Be("refuse", "each error keeps the field it came from");
    }

    [Fact]
    public async Task The_rejected_value_is_never_repeated_back()
    {
        var errors = await ErrorsAsync("{ refuse }");

        errors[0].ToString().Should().NotContain(Failing.RejectedStreet);
    }

    [Fact]
    public async Task A_broken_invariant_names_the_entity_that_reported_it()
    {
        var errors = await ErrorsAsync("{ breakInvariant }");

        var error = errors.Should().ContainSingle().Which;
        Extensions(error).GetProperty("code").GetString().Should().Be("Order.OverCreditLimit");
        Extensions(error).GetProperty("entity").GetString().Should().Be(nameof(Failing));
        Extensions(error).GetProperty("entityId").GetString().Should().Be("ORD_1");
        Extensions(error).GetProperty("arguments").GetProperty("CreditLimit").GetDecimal().Should().Be(1000m);
    }

    [Fact]
    public async Task A_refusal_becomes_one_coded_error_with_kind_and_arguments()
    {
        var errors = await ErrorsAsync("{ refused }");

        var error = errors.Should().ContainSingle().Which;
        error.GetProperty("message").GetString().Should().Be("The gold plan is closed.");
        Extensions(error).GetProperty("code").GetString().Should().Be("subscription.plan-closed");
        Extensions(error).GetProperty("kind").GetString().Should().Be("CONFLICT", "spelled as the schema spells its enum values, HotChocolate's way here");
        Extensions(error).GetProperty("arguments").GetProperty("Plan").GetString().Should().Be("gold");
        Extensions(error).GetProperty("arguments").GetProperty("Seats").GetInt32().Should().Be(3);
        error.GetProperty("path")[0].GetString().Should().Be("refused");
        Extensions(error).TryGetProperty("field", out _).Should().BeFalse("a refusal about the command as a whole names no input");
    }

    [Fact]
    public async Task A_refusal_about_one_input_names_it_where_a_validation_failure_names_its_property()
    {
        var errors = await ErrorsAsync("{ refusedAbout }");

        // A refused query has no payload to carry a RefusalError, so the extension is where a client finds the
        // input: the same extension a validation failure names its property in.
        var error = errors.Should().ContainSingle().Which;
        Extensions(error).GetProperty("code").GetString().Should().Be("subscription.plan-unknown");
        Extensions(error).GetProperty("kind").GetString().Should().Be("INVALID");
        Extensions(error).GetProperty("field").GetString().Should().Be("plan");
        Extensions(error).GetProperty("arguments").GetProperty("Field").GetString().Should().Be("plan", "and the arguments keep it as well");
    }

    [Fact]
    public async Task A_refusal_with_a_field_argument_has_the_field_extension()
    {
        var errors = await ErrorsAsync("{ refusedInput }");

        var error = errors.Should().ContainSingle().Which;
        Extensions(error).GetProperty("code").GetString().Should().Be("subscription.plan-name-invalid");
        Extensions(error).GetProperty("kind").GetString().Should().Be("INVALID");
        Extensions(error).GetProperty("field").GetString().Should().Be("name", "a form puts the message under the input, as it does for a validation failure");
        Extensions(error).GetProperty("arguments").GetProperty("Field").GetString().Should().Be("name", "the argument stays among the arguments");
        Extensions(error).GetProperty("arguments").GetProperty("Max").GetInt32().Should().Be(40);
    }

    [Fact]
    public async Task A_registered_localizer_phrases_every_message()
    {
        var errors = await ErrorsAsync("{ refuse breakInvariant refused }", new Upper());

        errors.Select(e => e.GetProperty("message").GetString())
            .Should().OnlyContain(message => message!.StartsWith("LOCALIZED "));
    }

    [Fact]
    public async Task Any_other_error_passes_through_untouched()
    {
        var errors = await ErrorsAsync("{ crash }");

        var error = errors.Should().ContainSingle().Which;
        error.GetProperty("message").GetString().Should().Be("Unexpected Execution Error");
    }

    private static async Task<JsonElement[]> ErrorsAsync(string query, IFailureLocalizer? localizer = null)
    {
        var services = new ServiceCollection();
        if (localizer is not null)
        {
            services.AddSingleton(localizer);
        }

        var executor = await services
            .AddGraphQL()
            .AddDDDToolkitErrors()
            .AddQueryType<Failing>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await executor.ExecuteAsync(query, TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(result.ToJson());
        return [.. document.RootElement.Clone().GetProperty("errors").EnumerateArray()];
    }

    private static JsonElement Extensions(JsonElement error) => error.GetProperty("extensions");

    /// <summary>Stands in for a real localizer: it only has to prove the filter asked it.</summary>
    private sealed class Upper : IFailureLocalizer
    {
        public string Localize(ValidationError error) => "LOCALIZED " + error.Code;

        public string Localize(InvariantViolation violation) => "LOCALIZED " + violation.Code;

        public string Localize(RefusalException refusal) => "LOCALIZED " + refusal.Code;
    }
}
