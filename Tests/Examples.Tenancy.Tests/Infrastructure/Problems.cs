using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>A problem+json answer, as a client reads it.</summary>
/// <param name="Status">The HTTP status.</param>
/// <param name="ContentType">The media type of the body.</param>
/// <param name="Body">The body.</param>
public sealed record Problem(HttpStatusCode Status, string? ContentType, JsonElement Body)
{
    /// <summary>The <c>code</c> extension, which says which rule answered.</summary>
    public string? Code => Text("code");

    /// <summary>The title: what was refused, in the reader's language.</summary>
    public string? Title => Text("title");

    /// <summary>The argument <paramref name="name"/> of a refusal, as text.</summary>
    public string? Argument(string name)
    {
        if (Body.ValueKind != JsonValueKind.Object || !Body.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var argument in arguments.EnumerateObject())
        {
            if (string.Equals(argument.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return argument.Value.ValueKind == JsonValueKind.String ? argument.Value.GetString() : argument.Value.GetRawText();
            }
        }

        return null;
    }

    /// <summary>
    /// The code of each case's own refusal, in their order, of an <c>access.refused</c> a request that lets one caller or
    /// another through answered: its argument <c>Reasons</c>. Empty for any other refusal.
    /// </summary>
    public IReadOnlyList<string?> Reasons
    {
        get
        {
            if (Body.ValueKind != JsonValueKind.Object || !Body.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var reasons = arguments.EnumerateObject().FirstOrDefault(argument => string.Equals(argument.Name, "Reasons", StringComparison.OrdinalIgnoreCase));
            return reasons.Value.ValueKind == JsonValueKind.Array
                ? [.. reasons.Value.EnumerateArray().Select(static reason => reason.EnumerateObject().FirstOrDefault(property => string.Equals(property.Name, "code", StringComparison.OrdinalIgnoreCase)).Value.GetString())]
                : [];
        }
    }

    private string? Text(string property)
        => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>Reading and asserting problem+json answers.</summary>
public static class Problems
{
    /// <summary>The response as a problem, whatever its status; an empty body reads as an empty object.</summary>
    public static async Task<Problem> ProblemAsync(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var body = string.IsNullOrWhiteSpace(text) ? JsonDocument.Parse("{}").RootElement : JsonDocument.Parse(text).RootElement.Clone();
        return new Problem(response.StatusCode, response.Content.Headers.ContentType?.MediaType, body);
    }

    /// <summary>Asserts the response is a refusal with <paramref name="status"/> and <paramref name="code"/>, and returns it.</summary>
    public static async Task<Problem> ShouldBeRefusedAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var problem = await response.ProblemAsync();

        response.StatusCode.Should().Be(status, "the answer was {0}", problem.Body.GetRawText());
        problem.ContentType.Should().Be("application/problem+json");
        problem.Code.Should().Be(code);
        return problem;
    }
}
