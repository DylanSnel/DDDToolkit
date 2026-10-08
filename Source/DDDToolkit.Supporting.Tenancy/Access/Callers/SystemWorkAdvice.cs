namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// What a developer writes to begin system work, for the messages that tell them to. <c>TenancyWork</c>'s methods are
/// generic over the tenant and seat ids, and C# infers no id that is not an argument, so a bare
/// <c>TenancyWork.BeginSystemIn(tenant)</c> does not compile. A project that sees the application's classes calls the
/// same method closed over them, through the class the use cases are named through, <c>TenancyUseCases</c> unless the
/// application named it otherwise; a project that sees only the ids writes them, and the message names them.
/// </summary>
internal static class SystemWorkAdvice
{
    /// <summary>What the generated class is, for a developer who gave it another name.</summary>
    private const string Facade = "TenancyUseCases is the class your use cases are named through";

    /// <summary>
    /// The sentence that says how to begin system work inside one tenant: "Begin ... <paramref name="purpose"/>, or ...".
    /// </summary>
    /// <param name="purpose">What the work is for, such as "for work inside one".</param>
    public static string BeginInATenant<TTenantId, TSeatId>(string purpose)
        => $"Begin TenancyUseCases.BeginSystemIn(tenant) {purpose}, or TenancyWork.BeginSystemIn<{NameOf<TTenantId>()}, {NameOf<TSeatId>()}>(tenant) "
           + $"in a project that sees only the ids ({Facade}).";

    /// <summary>
    /// The sentence that says how to begin system work outside any tenant: "Begin ... <paramref name="purpose"/>, or ...".
    /// </summary>
    /// <param name="purpose">What the work is for, such as "for it".</param>
    public static string BeginOutsideTenants<TTenantId, TSeatId>(string purpose)
        => $"Begin TenancyUseCases.BeginSystem() {purpose}, or TenancyWork.BeginSystem<{NameOf<TTenantId>()}, {NameOf<TSeatId>()}>() "
           + $"in a project that sees only the ids ({Facade}).";

    /// <summary>The id's name as code writes it where its namespace is imported: a nested id with its outer type.</summary>
    private static string NameOf<T>()
        => typeof(T).DeclaringType is { } outer ? outer.Name + "." + typeof(T).Name : typeof(T).Name;
}
