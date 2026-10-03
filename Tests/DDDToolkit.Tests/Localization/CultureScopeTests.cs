using System.Globalization;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;
using DDDToolkit.Localization;
using DDDToolkit.Tests.Validation;
using DDDToolkit.Validation;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.Tests.Localization;

/// <summary>
/// A text written outside a request, in a language the code that writes it names.
/// <para>
/// DECISION (pinned here): the language of a text outside a request is said by whoever writes it, with
/// <see cref="CultureScope"/> or with an explicit culture on <c>Localize</c>; the domain still never looks at a
/// culture. A scope sets the culture a text is looked up in and the one its values are written in, for its own
/// flow of work only, and puts both back.
/// </para>
/// </summary>
public class CultureScopeTests
{
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

    // ---------------------------------------------------------------- the scope

    [Fact]
    public void A_scope_sets_both_cultures_and_puts_back_the_ones_before()
    {
        // The two differ before the scope, to see each one come back as it was.
        using (CultureScope.Use(British))
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            using (CultureScope.Use(Dutch))
            {
                CultureInfo.CurrentCulture.Should().Be(Dutch, "the values in a text are written the reader's way");
                CultureInfo.CurrentUICulture.Should().Be(Dutch, "the text is looked up in the reader's language");
            }

            CultureInfo.CurrentCulture.Should().Be(British);
            CultureInfo.CurrentUICulture.Name.Should().Be("de-DE");
        }
    }

    [Fact]
    public void A_scope_is_opened_by_a_cultures_name_too()
    {
        using (CultureScope.Use(British))
        {
            using (CultureScope.Use("nl"))
            {
                CultureInfo.CurrentCulture.Name.Should().Be("nl");
                CultureInfo.CurrentUICulture.Name.Should().Be("nl");
            }

            CultureInfo.CurrentUICulture.Should().Be(British);
        }
    }

    [Fact]
    public void The_cultures_come_back_after_an_exception()
    {
        using (CultureScope.Use(British))
        {
            var act = () =>
            {
                using (CultureScope.Use(Dutch))
                {
                    throw new InvalidOperationException("the mail could not be written");
                }
            };

            act.Should().Throw<InvalidOperationException>();
            CultureInfo.CurrentCulture.Should().Be(British);
            CultureInfo.CurrentUICulture.Should().Be(British);
        }
    }

    [Fact]
    public void Scopes_nest_and_each_puts_back_what_it_replaced()
    {
        using (CultureScope.Use(British))
        {
            using (CultureScope.Use(Dutch))
            {
                using (CultureScope.Use("de-DE"))
                {
                    CultureInfo.CurrentUICulture.Name.Should().Be("de-DE");
                }

                CultureInfo.CurrentUICulture.Should().Be(Dutch);
            }

            CultureInfo.CurrentUICulture.Should().Be(British);
        }
    }

    [Fact]
    public void Disposing_a_scope_again_changes_nothing()
    {
        using (CultureScope.Use(British))
        {
            var scope = CultureScope.Use(Dutch);
            scope.Dispose();

            using (CultureScope.Use("de-DE"))
            {
                scope.Dispose();

                CultureInfo.CurrentUICulture.Name.Should().Be("de-DE", "a scope puts its cultures back once");
            }
        }
    }

    [Fact]
    public async Task A_scope_stays_open_across_an_await()
    {
        using (CultureScope.Use(Dutch))
        {
            await Task.Yield();
            await Task.Delay(1, TestContext.Current.CancellationToken);

            CultureInfo.CurrentCulture.Should().Be(Dutch);
            CultureInfo.CurrentUICulture.Should().Be(Dutch);
        }
    }

    [Fact]
    public async Task A_scope_does_not_leak_into_a_parallel_flow()
    {
        using (CultureScope.Use(British))
        {
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // Work that was running already, and looks at its culture while the other flow's scope is open.
            var beside = Task.Run(
                async () =>
                {
                    await opened.Task;
                    return (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
                },
                TestContext.Current.CancellationToken);

            var inside = Task.Run(
                async () =>
                {
                    using (CultureScope.Use(Dutch))
                    {
                        opened.SetResult();
                        await release.Task;
                        return (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
                    }
                },
                TestContext.Current.CancellationToken);

            (await beside).Should().Be((British, British), "another flow of work keeps its own culture");
            CultureInfo.CurrentUICulture.Should().Be(British, "and so does the flow that started the work");

            release.SetResult();
            (await inside).Should().Be((Dutch, Dutch));
            CultureInfo.CurrentCulture.Should().Be(British);
            CultureInfo.CurrentUICulture.Should().Be(British);
        }
    }

    [Fact]
    public async Task A_culture_set_in_an_async_method_does_not_reach_its_caller()
    {
        // Why an exception handler cannot count on the culture a middleware set further in: it is gone when the
        // method that set it returns. The handler names the culture instead.
        using (CultureScope.Use(British))
        {
            await SetWithoutPuttingBackAsync();

            CultureInfo.CurrentUICulture.Should().Be(British);
        }

        static async Task SetWithoutPuttingBackAsync()
        {
            await Task.Yield();
            _ = CultureScope.Use(Dutch);
            CultureInfo.CurrentUICulture.Should().Be(Dutch);
        }
    }

    [Fact]
    public void A_scope_needs_a_culture()
    {
        FluentActions.Invoking(() => CultureScope.Use((CultureInfo)null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => CultureScope.Use((string)null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => CultureScope.Use("no-such-culture-at-all")).Should().Throw<CultureNotFoundException>();
    }

    // ---------------------------------------------------------------- a failure in a culture the caller names

    [Fact]
    public void Localize_with_a_culture_ignores_the_current_ui_culture()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddLocalization()
            .AddDDDToolkitLocalization(options => options.AddResource<TestFailures>())
            .BuildServiceProvider();
        var localizer = provider.GetRequiredService<IFailureLocalizer>();

        var refusal = ToolkitRefusals.Of(ToolkitRefusals.Refused);
        var error = new Mystery().ValidationErrors.Single();
        var till = new Till(TillId.CreateUnique(), limit: 100m);
        till.Deposit(150m);
        var violation = till.GetInvariantViolations().Single();

        using (CultureScope.Use(British))
        {
            localizer.Localize(refusal, Dutch).Should().Be("De database heeft deze wijziging geweigerd.");
            localizer.Localize(error, Dutch).Should().Be("Mystery is niet geldig.");
            localizer.Localize(violation, Dutch).Should().Be(
                "In deze kassa mag hooguit 100,00 zitten, en er zit 150,00 in.",
                "the values are written the reader's way as well");

            CultureInfo.CurrentCulture.Should().Be(British, "the caller's own culture is as it was");
            CultureInfo.CurrentUICulture.Should().Be(British);
        }

        using (CultureScope.Use(Dutch))
        {
            localizer.Localize(refusal, British).Should().Be("The database refused this change.");
            localizer.Localize(error, British).Should().Be("Mystery is not valid.");
            localizer.Localize(violation, British).Should().Be("A till holds at most 100.00; this one holds 150.00.");
        }
    }

    [Fact]
    public void Localize_with_a_culture_asks_a_localizer_of_the_applications_own_in_that_culture()
    {
        IFailureLocalizer localizer = new SaysItsCulture();

        using (CultureScope.Use(British))
        {
            localizer.Localize(new RefusalException("c", RefusalKind.Conflict, "m"), Dutch).Should().Be("nl-NL/nl-NL");
            localizer.Localize(new ValidationError("m", code: "c"), Dutch).Should().Be("nl-NL/nl-NL");
            localizer.Localize(new InvariantViolation("c", "m", typeof(Till), null), Dutch).Should().Be("nl-NL/nl-NL");
        }
    }

    [Fact]
    public void Localize_with_a_culture_needs_all_three()
    {
        var localizer = FailureLocalizer.Default;
        var refusal = new RefusalException("c", RefusalKind.Conflict, "m");
        var error = new ValidationError("m", code: "c");
        var violation = new InvariantViolation("c", "m", typeof(Till), null);

        FluentActions.Invoking(() => ((IFailureLocalizer)null!).Localize(refusal, Dutch)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => localizer.Localize((RefusalException)null!, Dutch)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => localizer.Localize(refusal, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => localizer.Localize((ValidationError)null!, Dutch)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => localizer.Localize(error, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => localizer.Localize((InvariantViolation)null!, Dutch)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => localizer.Localize(violation, null!)).Should().Throw<ArgumentNullException>();
    }

    /// <summary>A localizer that answers with the cultures it was asked in, as one that reads translations from a database would use them.</summary>
    private sealed class SaysItsCulture : IFailureLocalizer
    {
        public string Localize(ValidationError error) => Cultures();

        public string Localize(InvariantViolation violation) => Cultures();

        public string Localize(RefusalException refusal) => Cultures();

        private static string Cultures() => CultureInfo.CurrentCulture.Name + "/" + CultureInfo.CurrentUICulture.Name;
    }
}
