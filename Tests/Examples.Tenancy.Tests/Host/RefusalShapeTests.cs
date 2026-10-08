using System.Net;
using System.Text.Json;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// How a refusal reaches a client: problem+json with a <c>code</c> to branch on, a title to show and the
/// arguments to build a message of one's own from. Case by case on the handler that writes it, with no host, so
/// these run in every build; <c>ProbeRouteTests</c> reads the same of a request that was sent over HTTP.
/// </summary>
public sealed class RefusalShapeTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(RefusalKind.Invalid, HttpStatusCode.BadRequest)]
    [InlineData(RefusalKind.NotPermitted, HttpStatusCode.Forbidden)]
    [InlineData(RefusalKind.NotFound, HttpStatusCode.NotFound)]
    [InlineData(RefusalKind.Conflict, HttpStatusCode.Conflict)]
    public async Task Each_kind_of_refusal_has_its_status(RefusalKind kind, HttpStatusCode status)
    {
        var refusal = new RefusalException("sample.test", kind, "Refused for the test.", new Dictionary<string, object?> { ["Why"] = "a test" });

        var (handled, response) = await HandleAsync(refusal);

        handled.Should().BeTrue();
        response.Status.Should().Be(status);
        response.Code.Should().Be("sample.test");
        response.Title.Should().Be("Refused for the test.");
        response.Argument("Why").Should().Be("a test");
    }

    [Fact]
    public async Task An_invalid_value_is_400_invalid_value()
    {
        var invalid = Record.Exception(() => TenantSlug.Create("Not a slug!").ToValid());
        invalid.Should().BeOfType<InvalidValueObjectException>();

        var (handled, response) = await HandleAsync(invalid!);

        handled.Should().BeTrue();
        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be(RefusalProblems.InvalidValue);
        response.Body.GetProperty("errors").GetArrayLength().Should().BePositive();
        response.Body.GetProperty("errors")[0].GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_bad_request_is_400_invalid_request()
    {
        var unreadable = new BadHttpRequestException("Failed to bind parameter \"SeatId seatId\" from \"not-an-id\".", StatusCodes.Status400BadRequest);

        var (handled, response) = await HandleAsync(unreadable);

        handled.Should().BeTrue();
        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be(RefusalProblems.InvalidRequest);
        response.Body.GetProperty("detail").GetString().Should().Contain("not-an-id");
    }

    [Fact]
    public async Task A_concurrency_conflict_is_409()
    {
        var conflict = new ConcurrencyConflictException(typeof(Seat), DemoData.Harbor.Administrator.Id);

        var (handled, response) = await HandleAsync(conflict);

        handled.Should().BeTrue();
        response.Status.Should().Be(HttpStatusCode.Conflict);
        response.Code.Should().Be(RefusalProblems.ConcurrencyConflict);
    }

    [Fact]
    public async Task A_save_the_database_refused_is_403_access_refused()
    {
        // What a save answers when a policy denied its row: a refusal like any other, and no conflict to retry.
        var refused = ToolkitRefusals.Refuse(ToolkitRefusals.Refused, new ConcurrencyConflictException(typeof(Seat), DemoData.Harbor.Administrator.Id));

        var (handled, response) = await HandleAsync(refused);

        handled.Should().BeTrue();
        response.Status.Should().Be(HttpStatusCode.Forbidden);
        response.Code.Should().Be("access.refused");
        response.Title.Should().Be("The database refused this change.");
    }

    [Fact]
    public async Task An_invariant_violation_is_422_with_violations()
    {
        var broken = new InvariantViolationException(typeof(Seat), DemoData.Harbor.Administrator.Id,
        [
            new InvariantViolation(Seat.JobTitleLength.ViolationCode, "A job title is at most 80 characters.").With("Max", 80),
            new InvariantViolation(OrganizationUnit.CostCentreFormat.ViolationCode, "A cost centre is two capitals, a dash and three digits."),
        ]);

        var (handled, response) = await HandleAsync(broken);

        handled.Should().BeTrue();
        response.Status.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Code.Should().Be(Seat.JobTitleLength.ViolationCode, "the first violation names the answer");
        response.Title.Should().Be("A job title is at most 80 characters.");
        response.Body.GetProperty("violations").EnumerateArray().Select(violation => violation.GetProperty("code").GetString())
            .Should().Equal(Seat.JobTitleLength.ViolationCode, OrganizationUnit.CostCentreFormat.ViolationCode);
        response.Body.GetProperty("violations")[0].GetProperty("arguments").GetProperty("Max").GetInt32().Should().Be(80);
    }

    [Fact]
    public async Task Anything_else_is_not_handled()
    {
        var (handled, response) = await HandleAsync(new InvalidOperationException("A bug, not an answer."));

        handled.Should().BeFalse("anything but a refusal, a broken rule or a lost race stays a 500");
        response.Body.ValueKind.Should().Be(JsonValueKind.Object);
        response.Body.EnumerateObject().Should().BeEmpty("nothing was written");
    }

    /// <summary>Runs the handler on a bare request, and reads what it wrote.</summary>
    private static async Task<(bool Handled, Problem Response)> HandleAsync(Exception exception)
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "GET";
        context.Request.Path = "/test";
        context.Response.Body = new MemoryStream();

        var handler = new RefusalProblems(services.GetRequiredService<ILogger<RefusalProblems>>());
        var handled = await handler.TryHandleAsync(context, exception, Cancellation);

        context.Response.Body.Position = 0;
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync(Cancellation);
        var body = JsonDocument.Parse(text.Length == 0 ? "{}" : text).RootElement.Clone();

        return (handled, new Problem((HttpStatusCode)context.Response.StatusCode, context.Response.ContentType, body));
    }
}
