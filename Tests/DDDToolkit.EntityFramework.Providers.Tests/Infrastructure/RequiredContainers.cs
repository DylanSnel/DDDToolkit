using System.Globalization;

namespace DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

/// <summary>
/// Whether a missing container is allowed to skip these tests, or has to fail them.
/// <para>
/// On a laptop without Docker a skip is the honest answer: nothing was proven, and saying so is
/// better than a red build somebody learns to ignore. In CI the same skip is a lie by omission,
/// because a green run then says "PostgreSQL and SQL Server pass" when neither server was ever
/// started. Continuous integration is the whole reason these tests exist, so there the container is
/// not optional.
/// </para>
/// <para>
/// The switch is the environment variable <c>DDDTOOLKIT_REQUIRE_CONTAINERS</c>. Set it to <c>1</c>,
/// <c>true</c> or <c>yes</c> and every skip in this project becomes a failure that names what could
/// not be started. The CI workflow sets it; nothing else does.
/// </para>
/// </summary>
public static class RequiredContainers
{
    /// <summary>The environment variable that turns a skip into a failure.</summary>
    public const string Variable = "DDDTOOLKIT_REQUIRE_CONTAINERS";

    /// <summary>
    /// Whether containers are required in this run. Read every time rather than cached, so a test can
    /// prove both answers without the order of the run deciding which one it gets.
    /// </summary>
    public static bool Required => IsSet(Environment.GetEnvironmentVariable(Variable));

    /// <summary>
    /// Whether <paramref name="value"/> asks for containers. Anything that is not an affirmative,
    /// the variable being unset included, means no.
    /// </summary>
    public static bool IsSet(string? value)
        => value is not null
            && (value.Equals("1", StringComparison.Ordinal)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The decision itself: return when the container is there, fail when it is not and was required,
    /// skip when it is not and was not.
    /// <para>
    /// It takes <paramref name="required"/> rather than reading the environment, so a test can ask for
    /// both answers without setting a process-wide variable that the other provider's collection is
    /// running against at the same time.
    /// </para>
    /// </summary>
    public static void EnforceOrSkip(bool available, bool required, string providerName, string? skipReason)
    {
        if (available)
        {
            return;
        }

        if (required)
        {
            Assert.Fail(Explain(providerName, skipReason));
        }

        Assert.Skip(skipReason ?? string.Empty);
    }

    /// <summary>
    /// What to say when the container is missing and required. It names the variable, because the
    /// person reading a CI log needs to know that this failure is about the agent and not about the
    /// toolkit.
    /// </summary>
    public static string Explain(string providerName, string? skipReason)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0} was required by {1} and could not be started, so this test failed rather than skipping. " +
            "A run that skipped here would have reported green without ever touching {0}. {2}",
            providerName,
            Variable,
            skipReason ?? "No reason was recorded.");
}
