using System.Runtime.ExceptionServices;
using DTS.DDD.Results;
using FluentAssertions;

namespace DTS.DDD.HotChocolate.Tests;

/// <summary>
/// Runs alone, after the other tests: it counts the <see cref="ErrorException"/>s thrown in the whole process, and a test
/// running beside it would add its own.
/// </summary>
[CollectionDefinition(nameof(ResultFieldsWithoutThrowTests), DisableParallelization = true)]
public sealed class ResultFieldsWithoutThrowCollection;

[Collection(nameof(ResultFieldsWithoutThrowTests))]
public sealed class ResultFieldsWithoutThrowTests
{
    [Fact]
    public async Task A_failure_is_reported_without_throwing_an_exception()
    {
        var thrown = 0;
        void Count(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is ErrorException)
            {
                Interlocked.Increment(ref thrown);
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += Count;
        try
        {
            using var answer = await ResultFieldsTests.RunAsync("""{ stock(sku: "gone") }""");
            answer.RootElement.GetProperty("errors").EnumerateArray().Should().ContainSingle();
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= Count;
        }

        thrown.Should().Be(0, "a failed result is reported as an error, and no ErrorException is thrown for it");
    }
}