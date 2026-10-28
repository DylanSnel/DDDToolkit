namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// What a developer writes to begin system work, for the messages that tell them to. Every <c>Begin</c> an application
/// calls is an instance method of the access manager its code takes, <c>TenancyAccessManager</c>, which the generator
/// writes closed over its ids.
/// </summary>
internal static class SystemWorkAdvice
{
    /// <summary>Where the call is found.</summary>
    private const string Where = "on the TenancyAccessManager your code takes";

    /// <summary>
    /// The sentence that says how to begin system work inside one tenant: "Begin ... <paramref name="purpose"/>, ...".
    /// </summary>
    /// <param name="purpose">What the work is for, such as "for work inside one".</param>
    public static string BeginInATenant(string purpose) => $"Begin access.BeginSystemIn(tenant) {purpose}, {Where}.";

    /// <summary>
    /// The sentence that says how to begin system work outside any tenant: "Begin ... <paramref name="purpose"/>, ...".
    /// </summary>
    /// <param name="purpose">What the work is for, such as "for it".</param>
    public static string BeginOutsideTenants(string purpose) => $"Begin access.BeginSystem() {purpose}, {Where}.";
}
