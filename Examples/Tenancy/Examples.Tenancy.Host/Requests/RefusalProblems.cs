using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;
using DDDToolkit.Localization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Examples.Tenancy.Host.Requests;

/// <summary>
/// Turns what the domain throws into problem+json: the status an HTTP client branches on, and a
/// <c>code</c> it can branch on more finely, translate, and show.
/// </summary>
/// <remarks>
/// The modules' routes decide nothing about responses. A use case refuses with a coded
/// <see cref="RefusalException"/>, an aggregate reports a broken rule with an
/// <see cref="InvariantViolationException"/>, and this one handler answers for all of them, so every route
/// of every module answers a refusal the same way.
/// <list type="table">
/// <listheader><term>Thrown</term><description>Answer</description></listheader>
/// <item><term><see cref="RefusalException"/></term><description>400, 403, 404 or 409 by its kind, with its code and arguments.</description></item>
/// <item><term><see cref="InvalidValueObjectException"/></term><description>400 <c>invalid-value</c>, with every error.</description></item>
/// <item><term><see cref="BadHttpRequestException"/></term><description>its status (400) <c>invalid-request</c>: an id or a body that could not be read. For a body the detail says where, by its path in the body, or that it is missing, and names no type of the server's.</description></item>
/// <item><term><see cref="InvariantViolationException"/></term><description>422 with the first violation's code, and every violation.</description></item>
/// <item><term><see cref="ConcurrencyConflictException"/></term><description>409 <c>concurrency-conflict</c>: somebody changed the same thing at the same moment.</description></item>
/// </list>
/// Anything else is not handled here and stays a 500: it is a bug, not an answer.
/// <para>
/// The title is in the request's language when an <see cref="IFailureLocalizer"/> is registered, and the
/// domain's own words otherwise. The handler names that language itself: the middleware that chose it set the
/// current culture inside its own flow of work, further in than this handler, so by the time an exception
/// arrives here the current culture is the server's again. The host's own three titles are looked up by their
/// codes like any refusal. Refusals are logged at Information: they are answers, not faults.
/// </para>
/// </remarks>
public sealed partial class RefusalProblems(ILogger<RefusalProblems> logger, IFailureLocalizer? localizer = null) : IExceptionHandler
{
    /// <summary>The code of a value that broke its own rules.</summary>
    public const string InvalidValue = "invalid-value";

    /// <summary>The code of a request whose id or body could not be read.</summary>
    public const string InvalidRequest = "invalid-request";

    /// <summary>The code of a change that lost a race with another one.</summary>
    public const string ConcurrencyConflict = "concurrency-conflict";

    /// <summary>The codes the handler answers of its own: what a test holds the host's resource files to.</summary>
    public static IReadOnlyList<string> Codes { get; } = [InvalidValue, InvalidRequest, ConcurrencyConflict];

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // Everything below is phrased in the language the request asked for, numbers and dates included.
        using var language = CultureScope.Use(RequestLanguages.LanguageOf(httpContext));

        var problem = exception switch
        {
            RefusalException refusal => Refused(refusal),
            InvalidValueObjectException invalid => Invalid(invalid),
            BadHttpRequestException badRequest => Unreadable(badRequest, httpContext),
            InvariantViolationException broken => Broken(broken),
            ConcurrencyConflictException => Problem(
                StatusCodes.Status409Conflict,
                ConcurrencyConflict,
                Own(ConcurrencyConflict, RefusalKind.Conflict, "Somebody else changed this at the same moment. Load it again and retry.")),
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        logger.LogInformation(
            "{Method} {Path} answered {Status} {Code}: {Title}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            problem.Status,
            problem.Extensions["code"],
            problem.Title);

        // Through the problem details service, so what AddProblemDetails adds to every problem, such as the
        // trace id, is added to these too.
        await TypedResults.Problem(problem).ExecuteAsync(httpContext);
        return true;
    }

    private ProblemDetails Refused(RefusalException refusal)
    {
        var status = refusal.Kind switch
        {
            RefusalKind.Invalid => StatusCodes.Status400BadRequest,
            RefusalKind.NotPermitted => StatusCodes.Status403Forbidden,
            RefusalKind.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict,
        };

        var problem = Problem(status, refusal.Code, localizer?.Localize(refusal) ?? refusal.Message);
        problem.Extensions["arguments"] = refusal.Arguments;
        return problem;
    }

    private ProblemDetails Invalid(InvalidValueObjectException invalid)
    {
        var problem = Problem(StatusCodes.Status400BadRequest, InvalidValue, Own(InvalidValue, RefusalKind.Invalid, "A value in the request is not valid."));
        problem.Extensions["errors"] = invalid.Errors
            .Select(error => new
            {
                property = error.PropertyName,
                code = error.Code,
                message = localizer?.Localize(error) ?? error.Message,
                arguments = error.Arguments,
            })
            .ToArray();
        return problem;
    }

    private ProblemDetails Unreadable(BadHttpRequestException badRequest, HttpContext httpContext)
    {
        var problem = Problem(badRequest.StatusCode, InvalidRequest, Own(InvalidRequest, RefusalKind.Invalid, "The request could not be read."));

        // A body that did not read says where: the path of the value in the body, or the property that is
        // missing. Never the JSON exception's own message, nor the outer one, for a body: both name the type the
        // body was read into, which is the server's business and no client's. The detail stays English: it is for
        // whoever wrote the client, not for its user.
        problem.Detail = badRequest.InnerException switch
        {
            JsonException { Path: { Length: > 0 } path and not "$" } => $"The value at {path} could not be read.",
            JsonException json when MissingProperties().Match(json.Message) is { Success: true } missing
                => $"The body is missing {missing.Groups[1].Value}.",
            JsonException => "The body could not be read as JSON of the shape this route takes.",

            // A body that is not there at all, or is null: the framework says which parameter of the route's
            // handler went without its value, in words meant for whoever wrote the server. The route's own
            // description knows which parameter the body is read into, so a message about that one is answered
            // in the host's words.
            _ when BodyParameterOf(httpContext) is { } body && badRequest.Message.Contains(body + "\"", StringComparison.Ordinal)
                => "The body is missing or null, and this route takes one.",
            _ => badRequest.Message,
        };
        return problem;
    }

    /// <summary>
    /// The name of the parameter the route that failed reads its body into, or <see langword="null"/> for a route
    /// that takes no body. The exception handler has cleared the request's endpoint by the time it asks here, and
    /// kept it in its feature.
    /// </summary>
    private static string? BodyParameterOf(HttpContext httpContext)
    {
        var route = (httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint ?? httpContext.GetEndpoint())?.Metadata;

        return route?.GetMetadata<IAcceptsMetadata>()?.RequestType is { } readInto
            ? route.GetMetadata<MethodInfo>()?.GetParameters().FirstOrDefault(parameter => parameter.ParameterType == readInto)?.Name
            : null;
    }

    private ProblemDetails Broken(InvariantViolationException broken)
    {
        var violations = broken.InvariantViolations;
        var first = violations.Count > 0 ? violations[0] : null;

        var problem = Problem(
            StatusCodes.Status422UnprocessableEntity,
            first?.Code ?? InvariantViolation.SeamCode,
            first is null ? broken.Message : localizer?.Localize(first) ?? first.Message);
        problem.Extensions["violations"] = violations
            .Select(violation => new
            {
                code = violation.Code,
                message = localizer?.Localize(violation) ?? violation.Message,
                arguments = violation.Arguments,
            })
            .ToArray();
        return problem;
    }

    /// <summary>
    /// The title of one of the host's own codes: looked up by the code as a refusal's is, and
    /// <paramref name="text"/> when no localizer is registered.
    /// </summary>
    private string Own(string code, RefusalKind kind, string text)
        => localizer?.Localize(new RefusalException(code, kind, text)) ?? text;

    /// <summary>The names a body left out, as the JSON reader lists them at the end of its message: <c>'unitId', 'seatId'</c>.</summary>
    [GeneratedRegex(@"missing required properties.*?: ('.+')\.?$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex MissingProperties();

    private static ProblemDetails Problem(int status, string code, string title)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;
        return problem;
    }
}
