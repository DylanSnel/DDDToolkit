using DDDToolkit.HotChocolate.Tests.Domain;
using HotChocolate.Types;
using System.Text.RegularExpressions;

[assembly: AssemblyFixture(typeof(DDDToolkit.HotChocolate.Tests.Infrastructure.EmailAddressTypeWarmUp))]

namespace DDDToolkit.HotChocolate.Tests.Infrastructure;

/// <summary>
/// Runs the regular expression of <see cref="EmailAddressType"/> before the first test of the run.
/// </summary>
/// <remarks>
/// HotChocolate gives a match of this expression 200 ms of wall clock time. The first match in a process also waits
/// while the JIT compiles the expression's code. That wait counts toward the 200 ms. On a loaded machine such a match
/// can pass the limit with a valid address. HotChocolate then reports "Unexpected Execution Error" with the code
/// <c>EXEC_INVALID_LEAF_VALUE</c>.
/// <para>
/// A later match takes about a microsecond, so a timeout there is far rarer. This fixture makes the first matches
/// before any test runs. A timeout here is ignored. If every try times out, the tests fail on it as before. Any other
/// exception is not caught.
/// </para>
/// </remarks>
public sealed class EmailAddressTypeWarmUp
{
    /// <summary>How often the expression is matched. The first three matches of a process are slower than the later ones.</summary>
    private const int Tries = 5;

    /// <summary>Matches the address that the tests query. The tests then run code that has run before.</summary>
    public EmailAddressTypeWarmUp()
    {
        var scalar = new EmailAddressType();

        for (var attempt = 0; attempt < Tries; attempt++)
        {
            try
            {
                scalar.ValueToLiteral(TestData.HolderEmail);
            }
            catch (RegexMatchTimeoutException)
            {
                // The next try runs the compiled expression.
            }
        }
    }
}
