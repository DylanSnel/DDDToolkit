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
/// <see cref="AccessRequirement.SignedIn"/> and <see cref="AccessRequirement.Own{TValue}"/>, and then the checks the
/// module added.
/// </para>
/// <para>
/// A requirement that combines cases, <see cref="AccessRequirement.All"/> or <see cref="AccessRequirement.Any"/>, is
/// decided here too: each case by the check that decides it alone, so cases of different owners combine, Tenancy's
/// with Membership's, and every case needs a check before any is asked. All of them, in their order, the first refusal
/// ending it; or the first that lets the caller through, and when none does one refusal,
/// <see cref="Exceptions.ToolkitRefusals.Refused"/>, with each case's own reason.
/// </para>
/// <para>
/// It keeps nothing between two requests and nothing during one, so requests sent side by side within one
/// scope are checked independently, though all go through the one instance the scope has. What it leaves is
/// the flow's: the request is in hand in the flow of work that asked (<see cref="RequestInHand"/>), with the
/// check it passed, for the handler that runs next, the save it ends with, and a refusal of the database later
/// in that flow, which asks the check again.
/// </para>
/// </remarks>
/// <typeparam name="TRequests">The module's request interface, which every command and query of the module implements.</typeparam>
public sealed class AccessChecks<TRequests>
    where TRequests : class, IRequireAccess
{
    private readonly IAccessCheck[] _checks;

    /// <summary>The core's check of who is calling, where the set holds one: asked who is calling before any check is.</summary>
    private readonly CallerAccessCheck? _callers;

    /// <summary>
    /// A set of the given checks, asked in this order. Dependency injection makes one per scope, of the core's
    /// <see cref="CallerAccessCheck"/> and then the checks registered for <typeparamref name="TRequests"/>; a test
    /// makes one by hand, and gives it the <see cref="CallerAccessCheck"/> where its requests require a signed-in
    /// user.
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

        _callers = _checks.OfType<CallerAccessCheck>().FirstOrDefault();
    }

    /// <summary>
    /// Whether a request that declares <paramref name="requirement"/> can pass at all: anyone may send it
    /// (<see cref="AccessRequirement.AllowAnonymous"/>), one of the set's checks decides it, or it combines cases each of
    /// which can pass (<see cref="AccessRequirement.All"/>, <see cref="AccessRequirement.Any"/>). Nothing is read and
    /// nobody is refused, so a test asks it for every requirement the module's requests declare.
    /// </summary>
    /// <param name="requirement">What a request declares.</param>
    /// <exception cref="ArgumentNullException"><paramref name="requirement"/> is null.</exception>
    public bool Decides(AccessRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        return requirement switch
        {
            AccessRequirement.Anyone => true,
            AccessRequirement.Combined combined => combined.Cases.All(Decides),
            _ => CheckFor(requirement) is not null,
        };
    }

    /// <summary>
    /// Holds the caller to what <paramref name="request"/> requires: returns when the caller meets it, and
    /// throws when it does not. Call it before the handler, and only run the handler when it returned.
    /// <para>
    /// It also puts the request in hand (<see cref="RequestInHand"/>) for the flow of work that calls it, from the
    /// moment the checks let it through. So call it in the <see langword="async"/> method that runs the handler
    /// next, as the generated behavior does: the handler, and the save it ends with, then know which request they
    /// serve without being told, and once that method returned its caller has nothing in hand. A method that is not
    /// <see langword="async"/> gives no flow back, so its caller would go on with the request in hand.
    /// </para>
    /// </summary>
    /// <param name="request">The very request the handler is about to be handed.</param>
    /// <param name="cancellationToken">Stops the reading a check does.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="Exceptions.RefusalException">The caller is not who the request requires.</exception>
    /// <exception cref="InvalidOperationException">
    /// The request declares nothing, or a requirement none of the checks registered for
    /// <typeparamref name="TRequests"/> decides.
    /// </exception>
    /// <exception cref="NoCallerException">
    /// Nobody is calling, and the host requires every flow of work to say who it runs as: asked of the set's
    /// <see cref="CallerAccessCheck"/> before any check, whatever the request requires.
    /// </exception>
    public ValueTask RequireAsync(TRequests request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Who is calling, before anything else: in a host that requires every flow of work to say who it runs as, work
        // nobody began a caller for is a mistake in the calling code, and fails here whatever the request requires, a
        // case of a supporting domain's that asks its own caller, never the host's, included.
        _callers?.AskWhoIsCalling();

        // Read once: a request may make its requirement anew each time it is asked. A request that states it with
        // attributes answers it the same way, from the line the generator wrote into its module's interface.
        var requirement = request.RequiredAccess
            ?? throw new InvalidOperationException(
                $"{NameOf(request.GetType())} declares no access requirement. A request that declares nothing lets nobody through: "
                + "state what it requires with an attribute, such as [SignedIn], or answer RequiredAccess with it, and say [AllowAnonymous] where anyone may send it.");

        var check = requirement switch
        {
            AccessRequirement.Anyone => null,

            // Every case has its check before any is asked: a combination with a case nothing decides is stopped as
            // that case would be, whichever case would have let the caller through first.
            AccessRequirement.Combined combined => new CombinedAccessCheck(CheckedCases(request, combined)),
            _ => CheckFor(requirement) ?? throw NothingDecides(request, requirement),
        };

        // In hand from here, with the check that decides it, for the flow that called. This method is not
        // asynchronous, so what it puts in hand reaches the caller, which runs the handler next; it counts once the
        // check let the request through. A request anyone may send is let through by asking nobody.
        return RequestInHand.Take(request, requirement, check, cancellationToken);
    }

    /// <summary>The check of every case of <paramref name="combined"/>, its combinations' cases included, by case.</summary>
    private Dictionary<AccessRequirement, IAccessCheck> CheckedCases(TRequests request, AccessRequirement.Combined combined)
    {
        var found = new Dictionary<AccessRequirement, IAccessCheck>();
        Add(combined);
        return found;

        void Add(AccessRequirement.Combined each)
        {
            foreach (var part in each.Cases)
            {
                if (part is AccessRequirement.Combined nested)
                {
                    Add(nested);
                }
                else if (!found.ContainsKey(part))
                {
                    found.Add(part, CheckFor(part) ?? throw NothingDecides(request, part));
                }
            }
        }
    }

    /// <summary>The failure of a request whose requirement, or one of its cases, none of the set's checks decides.</summary>
    private static InvalidOperationException NothingDecides(TRequests request, AccessRequirement requirement)
        => new(
            $"{NameOf(request.GetType())} declares '{NameOf(requirement.GetType())}', which none of the access checks registered for {NameOf(typeof(TRequests))} decides. "
            + $"A requirement nothing checks lets nobody through: register the check that decides it with {RegistrationOf(requirement.GetType())}.");

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
