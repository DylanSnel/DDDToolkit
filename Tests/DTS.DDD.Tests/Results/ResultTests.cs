using DTS.DDD.Results;
using FluentAssertions;

namespace DTS.DDD.Tests.Results;

/// <summary>
/// <see cref="Result"/> and <see cref="Result{T}"/>: a value or an error converts to a result, the combinators pass a
/// failure on, and a result that was never set is a failure.
/// </summary>
public sealed class ResultTests
{
    private static readonly NotFoundError Missing = new("stock.unknown", "No such stock.");

    [Fact]
    public void A_value_and_an_error_convert_to_a_result()
    {
        Result<int> success = 3;
        Result<int> failure = Missing;

        success.IsSuccess.Should().BeTrue();
        success.Value.Should().Be(3);
        failure.IsFailure.Should().BeTrue();
        failure.Error.Should().Be(Missing);
    }

    [Fact]
    public void The_value_of_a_failure_throws_the_error_and_the_error_of_a_success_throws()
    {
        Result<int> failure = Missing;
        Result<int> success = 3;

        FluentActions.Invoking(() => failure.Value).Should().Throw<ErrorException>().Which.Error.Should().Be(Missing);
        FluentActions.Invoking(() => success.Error).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_result_that_was_never_set_is_a_failure()
    {
        var unset = default(Result<int>);

        unset.IsFailure.Should().BeTrue();
        unset.Error.Should().BeOfType<UnexpectedError>().Which.Code.Should().Be(Result<int>.DefaultCode);
        default(Result).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Map_Bind_and_Ensure_run_after_a_success_and_pass_a_failure_on()
    {
        Result<int> three = 3;
        Result<int> failure = Missing;

        three.Map(value => value * 2).Value.Should().Be(6);
        three.Bind(value => value > 2 ? Result<string>.Success("many") : new ConflictError("few", "Few.")).Value.Should().Be("many");
        three.Ensure(value => value > 5, _ => new ConflictError("stock.short", "Too few.")).Error.Code.Should().Be("stock.short");

        var calls = 0;
        failure.Map(value => ++calls).Error.Should().Be(Missing);
        failure.Bind(value => Result<int>.Success(++calls)).Error.Should().Be(Missing);
        calls.Should().Be(0);
    }

    [Fact]
    public void Tap_TapError_and_Match_see_the_side_they_are_for()
    {
        Result<int> three = 3;
        Result<int> failure = Missing;
        var seen = new List<string>();

        three.Tap(value => seen.Add($"value {value}")).TapError(error => seen.Add("error"));
        failure.Tap(value => seen.Add("value")).TapError(error => seen.Add($"error {error.Code}"));

        seen.Should().Equal("value 3", "error stock.unknown");
        three.Match(value => value, _ => -1).Should().Be(3);
        failure.Match(value => value, _ => -1).Should().Be(-1);
    }

    [Fact]
    public void A_result_converts_to_a_result_without_its_value()
    {
        Result fromSuccess = Result<int>.Success(3);
        Result fromFailure = Result<int>.Failure(Missing);

        fromSuccess.IsSuccess.Should().BeTrue();
        fromFailure.Error.Should().Be(Missing);
    }

    [Fact]
    public void Two_errors_are_equal_by_type_code_message_and_arguments()
    {
        new NotFoundError("a", "A.", [new("Id", 1)]).Should().Be(new NotFoundError("a", "A.", [new("Id", 1)]));
        new NotFoundError("a", "A.").Should().NotBe(new ConflictError("a", "A."));
        Result<int>.Failure(Missing).Should().Be(Result<int>.Failure(new NotFoundError("stock.unknown", "No such stock.")));
    }

    [Fact]
    public void An_error_for_a_field_names_the_field()
    {
        var error = InvalidError.ForField("owner-required", "Name the owner.", "ownerEmail");

        error.Field.Should().Be("ownerEmail");
        error.Failures.Should().BeEmpty();
    }
}