using Acme.Press;
using DDDToolkit.Abstractions.Attributes;

// The row access SQL the two packages offer goes into the exported access files because the host lists it: an
// offer alone writes nothing, and one the host does not list is DDD00054 here.
[assembly: UseRowAccessContribution(typeof(PressTenancyPolicies))]
[assembly: UseRowAccessContribution(typeof(ManuscriptMembershipFunctions))]

namespace Acme.Press.Host;

/// <summary>
/// The application. What it serves is beside the point here: when the build's export step starts it, code the
/// Supabase package's generator wrote into it exports before <see cref="Main"/> and ends the process.
/// </summary>
public static class Program
{
    /// <summary>Started any other way, it has nothing to do.</summary>
    public static void Main()
    {
    }
}
