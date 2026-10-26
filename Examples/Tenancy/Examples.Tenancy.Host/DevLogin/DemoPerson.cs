namespace Examples.Tenancy.Host.DevLogin;

/// <summary>A person of the demonstration, as Supabase Auth would know them.</summary>
/// <param name="Key">What the dev login and the tests call them: <c>rhea</c>.</param>
/// <param name="Id">Their verified identity: the <c>sub</c> of their tokens, which their seats are found by.</param>
/// <param name="Name">The name their seats are shown by.</param>
/// <param name="About">What signing in as them shows.</param>
public sealed record DemoPerson(string Key, Guid Id, string Name, string About)
{
    /// <summary>Their e-mail address, on a domain that can never receive mail.</summary>
    public string Email => Key + "@example.test";
}
