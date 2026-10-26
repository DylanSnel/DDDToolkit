using FluentAssertions;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>A host that must not start, and why it did not.</summary>
public static class RefusedStarts
{
    /// <summary>
    /// Starts <paramref name="host"/>, which has to fail, and returns every exception behind the failure: what the
    /// start threw at the test, and what the host itself logged as it gave up, each with the exceptions inside it.
    /// </summary>
    /// <remarks>
    /// What is thrown at the test is not always the reason. The host fails on a thread of its own and is disposed
    /// there; a test that asks the factory for it a moment later is told the host is disposed, and nothing of the
    /// check that stopped it. The host's own log has the reason either way, written before it gave up.
    /// </remarks>
    public static IReadOnlyList<Exception> RefusedStart(this SampleFactory host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var start = () => host.Server;
        var thrown = start.Should().Throw<Exception>("the host does not start").Which;

        return [.. Unwrap(thrown).Concat(host.Errors.Thrown.SelectMany(Unwrap)).Distinct()];
    }

    private static IEnumerable<Exception> Unwrap(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Unwrap))
                {
                    yield return inner;
                }
            }
        }
    }
}
