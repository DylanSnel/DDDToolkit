namespace DDDToolkit.Access;

/// <summary>
/// What a request requires of its caller before its handler runs. A request says which through
/// <see cref="IRequireAccess"/>, and <see cref="AccessChecks{TRequests}"/> holds it to that.
/// </summary>
/// <remarks>
/// The cases are records that derive from this one, each owned by whoever can decide it: a package ships the
/// cases about what it keeps, with the <see cref="IAccessCheck"/> that decides them, and a module adds cases
/// of its own the same way. A record, so two requirements that say the same are equal, and a test can hold
/// every request to the requirement it is meant to declare.
/// <para>
/// A requirement says what is asked, never who may: it carries a key and the ids the request names, and the
/// check reads the rest where it is kept. What a requirement cannot say stays in the handler, in plain sight.
/// </para>
/// <para>
/// The one case declared here is <see cref="Open"/>, because it is the one case nobody has to decide.
/// </para>
/// </remarks>
public abstract record AccessRequirement
{
    /// <summary>For the cases a package or a module declares.</summary>
    protected AccessRequirement()
    {
    }

    /// <summary>
    /// Nothing is required, deliberately: anyone the host lets through may send the request. No check is asked,
    /// so it passes in a module that registered none.
    /// </summary>
    public sealed record Open : AccessRequirement
    {
        /// <summary>A requirement that lets everyone through, and says why.</summary>
        /// <param name="reason">Why nothing is required. A request is never open without one.</param>
        /// <exception cref="ArgumentException"><paramref name="reason"/> is null or blank.</exception>
        public Open(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            Reason = reason;
        }

        /// <summary>Why nothing is required, for whoever reads the request and for a test that lists the open ones.</summary>
        public string Reason { get; }
    }
}
