namespace DDDToolkit.Access;

/// <summary>
/// The access checks of one module: holds a request to what it declared it requires of its caller
/// (<see cref="IRequireAccess.RequiredAccess"/>), before its handler runs.
/// </summary>
/// <remarks>
/// There is one set of checks per request interface. A module declares the interface, registers the checks
/// that decide the cases its requests declare
/// (<see cref="AccessCheckServiceCollectionExtensions.AddAccessCheck{TRequests, TCheck}"/>), and calls
/// <see cref="RequireAsync"/> in front of every handler: from a pipeline behavior, an endpoint filter or its
/// own dispatcher. Nothing here knows which of those it is.
/// <para>
/// It fails closed. A request that declares nothing, and a requirement that none of the module's checks
/// decides, stop the request with an <see cref="InvalidOperationException"/> rather than letting it through:
/// a case added without its check lets nobody in, and says which check is missing.
/// </para>
/// <para>
/// The checks are asked in the order they were given, and the first that decides a requirement is the one that
/// holds the caller to it. <see cref="AccessRequirement.AllowAnonymous"/> is decided here, by asking nobody. The
/// set dependency injection makes asks the core's <see cref="CallerAccessCheck"/> first, for
/// <see cref="AccessRequirement.SignedIn"/> and <see cref="AccessRequirement.RequiresSystemWork"/>, and then the
/// checks the module added.
/// </para>
/// <para>
/// It keeps nothing between two requests and nothing during one, so requests sent side by side within one
/// scope are checked independently, though all go through the one instance the scope has.
/// </para>
/// </remarks>
/// <typeparam name="TRequests">The module's request interface, which every command and query of the module implements.</typeparam>
public sealed class AccessChecks<TRequests>
    where TRequests : class, IRequireAccess
{
    private readonly IAccessCheck[] _checks;

    /// <summary>
    /// A set of the given checks, asked in this order. Dependency injection makes one per scope, of the core's
    /// <see cref="CallerAccessCheck"/> and then the checks registered for <typeparamref name="TRequests"/>; a test
    /// makes one by hand, and gives it the <see cref="CallerAccessCheck"/> where its requests require a signed-in
    /// user or system work.
    /// </summary>
    /// <param name="checks">The checks of the module, in the order they are asked.</param>
    /// <exception cref="ArgumentNullException"><paramref name="checks"/> is null.</exception>
    /// <exception cref="ArgumentException">One of <paramref name="checks"/> is null.</exception>
    public AccessChecks(IEnumerable<IAccessCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        _checks = checks.ToArray();
        if (_checks.Any(static check => check is null))
        {
            throw new ArgumentException("A set of access checks holds no null.", nameof(checks));
        }
    }

    /// <summary>
    /// Whether a request that declares <paramref name="requirement"/> can pass at all: anyone may send it
    /// (<see cref="AccessRequirement.AllowAnonymous"/>), or one of the set's checks decides it. Nothing is read
    /// and nobody is refused, so a test asks it for every requirement the module's requests declare.
    /// </summary>
    /// <param name="requirement">What a request declares.</param>
    /// <exception cref="ArgumentNullException"><paramref name="requirement"/> is null.</exception>
    public bool Decides(AccessRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        return requirement is AccessRequirement.Anyone || CheckFor(requirement) is not null;
    }

    /// <summary>
    /// Holds the caller to what <paramref name="request"/> requires: returns when the caller meets it, and
    /// throws when it does not. Call it before the handler, and only run the handler when it returned.
    /// </summary>
    /// <param name="request">The very request the handler is about to be handed.</param>
    /// <param name="cancellationToken">Stops the reading a check does.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="Exceptions.RefusalException">The caller is not who the request requires.</exception>
    /// <exception cref="InvalidOperationException">
    /// The request declares nothing, or a requirement none of the checks registered for
    /// <typeparamref name="TRequests"/> decides.
    /// </exception>
    public ValueTask RequireAsync(TRequests request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Read once: a request may make its requirement anew each time it is asked.
        var requirement = request.RequiredAccess
            ?? throw new InvalidOperationException(
                $"{NameOf(request.GetType())} declares no access requirement. A request that declares nothing lets nobody through: "
                + "answer RequiredAccess with what it requires, or with AccessRequirement.AllowAnonymous() where anyone may send it.");

        if (requirement is AccessRequirement.Anyone)
        {
            return ValueTask.CompletedTask;
        }

        var check = CheckFor(requirement)
            ?? throw new InvalidOperationException(
                $"{NameOf(request.GetType())} declares '{NameOf(requirement.GetType())}', which none of the access checks registered for {NameOf(typeof(TRequests))} decides. "
                + $"A requirement nothing checks lets nobody through: register the check that decides it with {RegistrationOf(requirement.GetType())}.");

        return check.RequireAsync(requirement, request, cancellationToken);
    }

    private IAccessCheck? CheckFor(AccessRequirement requirement)
    {
        foreach (var check in _checks)
        {
            if (check.Decides(requirement))
            {
                return check;
            }
        }

        return null;
    }

    /// <summary>
    /// The call that registers the check for a requirement of <paramref name="type"/>: the one its package names
    /// (<see cref="AccessCheckRegistrationAttribute"/>), for this module's request interface, or else the general
    /// one, which a module's own case takes.
    /// </summary>
    private static string RegistrationOf(Type type)
    {
        var requests = NameOf(typeof(TRequests));
        return Attribute.GetCustomAttribute(type, typeof(AccessCheckRegistrationAttribute), inherit: true) is AccessCheckRegistrationAttribute named
            ? named.Registration.Replace("{TRequests}", requests, StringComparison.Ordinal)
            : $"AddAccessCheck<{requests}, TCheck>()";
    }

    /// <summary>A type as its author writes it, for a message a developer reads (<see cref="WrittenTypeNames.Of"/>).</summary>
    private static string NameOf(Type type) => WrittenTypeNames.Of(type);
}
