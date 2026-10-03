using System.Runtime.CompilerServices;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// Tells the assertions that a shared value object is compared as the value it is, wherever an answer carries one.
/// </summary>
/// <remarks>
/// Left to compare an answer member by member, an assertion would walk into a <see cref="DateRange"/> and compare
/// whether each side has been asked yet if it holds, which a value object remembers once asked. Two answers with
/// the same days would then differ by which of them an earlier assertion happened to read. A range is its days,
/// and who changed a row, <see cref="ChangedBy"/>, is a kind and a seat.
/// </remarks>
internal static class ValueEquivalency
{
    /// <summary>Runs once, when the test assembly is loaded, before any test.</summary>
    [ModuleInitializer]
    internal static void CompareSharedValueObjectsByValue()
        => AssertionOptions.AssertEquivalencyUsing(options => options.ComparingByValue<DateRange>().ComparingByValue<ChangedBy>());
}
