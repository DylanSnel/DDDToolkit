namespace Acme.Press.Host;

/// <summary>
/// The application. What it serves is beside the point here: when the build's export step starts it, code the
/// Supabase package's generator wrote into it exports before <see cref="Main"/> and ends the process. Tenancy's and
/// Membership's row access SQL comes with it: both Postgres packages declare themselves contributors, the host
/// references them through the infrastructure project, and the generator makes their classes here from what the
/// domain project marks, the catalogue and the manuscripts' rules. The host lists nothing.
/// </summary>
public static class Program
{
    /// <summary>Started any other way, it has nothing to do.</summary>
    public static void Main()
    {
    }
}
