using System.Text.Json;
using DTS.DDD.Results;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace DTS.DDD.HotChocolate.Tests;

/// <summary>
/// <c>AddDDDToolkitErrors()</c> and a resolver that returns a <see cref="Result{T}"/>: the field has the type of the
/// value, a success answers the value, and a failure is a coded GraphQL error with the kind of its error type.
/// </summary>
public sealed class ResultFieldsTests
{
    [Fact]
    public async Task A_field_of_a_result_has_the_type_of_its_value()
    {
        var schema = (await ExecutorAsync()).Schema.ToString();

        schema.Should().Contain("stock(sku: String!): Int!");
        schema.Should().Contain("reserve(sku: String!): Int!");
        schema.Should().Contain("cancel: Boolean!");
        schema.Should().NotContain("ResultOf");
    }

    [Fact]
    public async Task A_success_answers_the_value()
    {
        using var answer = await RunAsync("""{ stock(sku: "tea") reserve(sku: "tea") cancel }""");

        var data = answer.RootElement.GetProperty("data");
        data.GetProperty("stock").GetInt32().Should().Be(7);
        data.GetProperty("reserve").GetInt32().Should().Be(7);
        data.GetProperty("cancel").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_failure_is_a_coded_error_with_its_kind_and_field()
    {
        using var answer = await RunAsync("""{ stock(sku: "gone") }""");

        var error = answer.RootElement.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        var extensions = error.GetProperty("extensions");
        error.GetProperty("message").GetString().Should().Be("No such stock.");
        extensions.GetProperty("code").GetString().Should().Be("stock.unknown");
        extensions.GetProperty("kind").GetString().Should().Be("NOT_FOUND");
        extensions.GetProperty("field").GetString().Should().Be("sku");
    }

    [Fact]
    public async Task An_unexpected_error_has_its_own_kind()
    {
        using var answer = await RunAsync("""{ stock(sku: "down") }""");

        var error = answer.RootElement.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("extensions").GetProperty("kind").GetString().Should().Be("UNEXPECTED");
    }

    [Fact]
    public async Task An_invalid_error_with_failures_is_one_error_per_failure()
    {
        using var answer = await RunAsync("""{ stock(sku: "") }""");

        var errors = answer.RootElement.GetProperty("errors").EnumerateArray().ToList();
        errors.Should().HaveCount(2);
        errors[0].GetProperty("extensions").GetProperty("field").GetString().Should().Be("Sku");
        errors[1].GetProperty("extensions").GetProperty("code").GetString().Should().Be("sku.short");
    }

    private static async Task<IRequestExecutor> ExecutorAsync()
        => await new ServiceCollection()
            .AddGraphQL()
            .AddDDDToolkitErrors()
            .AddQueryType<Stock>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

    internal static async Task<JsonDocument> RunAsync(string query)
    {
        var result = await (await ExecutorAsync()).ExecuteAsync(query, TestContext.Current.CancellationToken);
        return JsonDocument.Parse(result.ToJson());
    }

    public sealed class Stock
    {
        public Result<int> GetStock(string sku) => sku switch
        {
            "gone" => InvalidErrorOrNotFound(),
            "down" => new UnexpectedError("stock.unavailable", "The stock service does not answer."),
            "" => new InvalidError("sku.invalid", "The SKU is not valid.", [new("The SKU is required.", "Sku", "sku.required"), new("The SKU is too short.", "Sku", "sku.short")]),
            _ => 7,
        };

        public ValueTask<Result<int>> ReserveAsync(string sku) => ValueTask.FromResult(GetStock(sku));

        public Result Cancel() => Result.Success();

        private static Result<int> InvalidErrorOrNotFound()
            => new NotFoundError("stock.unknown", "No such stock.", [new(DTS.DDD.Results.Error.FieldArgument, "sku")]);
    }
}