using DDDToolkit.Exceptions;

namespace DDDToolkit.Access;

/// <summary>
/// The request a flow of work is handling, once its access checks let it through, with the requirement it passed:
/// what the handler and the save it ends with serve, found without being handed the request, and the check that
/// can be asked again later in that flow, when the database refuses what the check allowed.
/// </summary>
/// <remarks>
/// <see cref="AccessChecks{TRequests}.RequireAsync"/> puts the request in hand as it starts to ask, and the request
/// counts from the moment the checks let it through: one they refused is in nobody's hand. It follows the flow the
/// way an <see cref="AsyncLocal{T}"/> does: into whatever the method that asked the checks runs after them, the
/// handler and its save among it, into tasks started there, and not back out of that method. So whatever sends a
/// request asks the checks and runs the handler in one <see langword="async"/> method, as the behavior the
/// generator writes does:
/// <code>
/// await checks.RequireAsync(request, cancellationToken);      // the request is in hand from here
/// await handler.HandleAsync(request, cancellationToken);      // and here, down to the save
/// </code>
/// A helper that asks the checks in a method of its own has the request in hand there alone, and hands nothing
/// back: the handler it calls next runs with no request in hand. And only an <see langword="async"/> method gives
/// its caller the flow back as it was: one that is not, that asks the checks and returns the task of the handling
/// it chains on, leaves the request in hand for its caller, and whatever that caller runs next, a handler it calls
/// directly included, is taken for part of the request's handling. A save that runs after the method returned,
/// in a behavior around it or in an endpoint after it sent the request, has nothing in hand.
/// <para>
/// A request is in hand for one handling. Sending it again asks the checks again, and that pass is what is in hand
/// from then on. A request sent from inside the handling of another, through a sender whose behavior asks its
/// checks in a method of its own, is in hand for its own handling, and the other one is in hand again once it
/// returns. A query answered with a stream has its request in hand until its handler hands out the first item:
/// every item after that is asked for by whoever reads the stream, in that reader's flow.
/// </para>
/// <para>
/// What a check keeps for the request it let through (<see cref="Checked{T}.KeepFor"/>) is kept with the request
/// in hand as well, so code that runs in the handling finds it without the request
/// (<see cref="Checked{T}.TryFindInHand"/>): how a package checks at the save what its check read before the
/// handler.
/// </para>
/// <para>
/// And the check the request passed can be asked again, now, as the same caller (<see cref="StillPassesAsync"/>).
/// The toolkit does that when the database refuses a save the application allowed. Under row level security that
/// happens in two ways. The caller's rights changed between the check and the save, a key taken from a role, a
/// grant revoked, so the policies refuse what the check let through a moment earlier, and the check and the
/// policies agreed, each when it was asked. Or the check and the policies hold a rule differently, so the policies
/// refuse what the check still allows: something a developer should look at. Asking the check again tells the two
/// apart, at the cost of one more check, paid only by a save the database refused. What is asked again is the
/// requirement the request declared: a rule a handler or a package's use case checks itself, past that
/// requirement, is not part of it.
/// </para>
/// </remarks>
public sealed class RequestInHand
{
    private static readonly AsyncLocal<RequestInHand?> Ambient = new();

    private static readonly AsyncLocal<bool> AskingAgain = new();

    /// <summary>The check that decided the requirement; <see langword="null"/> for a request anyone may send, which asks nobody.</summary>
    private readonly IAccessCheck? _check;

    private readonly Lock _gate = new();

    private readonly Dictionary<Type, object> _kept = [];

    private volatile bool _passed;

    private RequestInHand(IRequireAccess request, AccessRequirement requirement, IAccessCheck? check)
    {
        Request = request;
        Requirement = requirement;
        _check = check;
    }

    /// <summary>
    /// The request whose access checks this flow of work asked last and that they let through, or
    /// <see langword="null"/>: outside the handling of any request, as in background work or a handler called
    /// directly, and in the handling of one the checks refused or are still asking about.
    /// </summary>
    public static RequestInHand? Current => Ambient.Value is { _passed: true } hand ? hand : null;

    /// <summary>The very request the checks let through.</summary>
    public IRequireAccess Request { get; }

    /// <summary>What the request declared it requires, as it was read from it when it passed.</summary>
    public AccessRequirement Requirement { get; }

    /// <summary>Whether this flow is asking a check again, for <see cref="Checked{T}"/>, which then keeps nothing.</summary>
    internal static bool IsAskingAgain => AskingAgain.Value;

    /// <summary>
    /// Asks the check the request passed again, now, as the caller of this flow, about the same requirement of the
    /// same request: <see langword="true"/> when it lets the caller through still, <see langword="false"/> when it
    /// refuses now, with a <see cref="RefusalException"/>. A request anyone may send
    /// (<see cref="AccessRequirement.AllowAnonymous"/>) passed by asking nobody, and passes again the same way.
    /// </summary>
    /// <remarks>
    /// The check reads what it read the first time, as it is now. Asked again, it keeps nothing for the handler
    /// (<see cref="Checked{T}"/>): the handler took what it acts on when it ran, and nothing of this second
    /// asking stays behind for a later call to find. Anything else the check throws comes out of here as it is,
    /// since it answers neither way: a failure of its reading, and a <see cref="ConcurrencyConflictException"/>
    /// too. A conflict says that what the request is about moved on from the version it named, the handler's own
    /// save among the ways that happens, and not whether the caller may: a check that holds a request to a version
    /// asks it after the caller's rights, as Membership's does.
    /// </remarks>
    /// <param name="cancellationToken">Stops the reading the check does.</param>
    public async ValueTask<bool> StillPassesAsync(CancellationToken cancellationToken = default)
    {
        if (_check is null)
        {
            return true;
        }

        AskingAgain.Value = true;
        try
        {
            await _check.RequireAsync(Requirement, Request, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (RefusalException)
        {
            return false;
        }
    }

    /// <summary>
    /// The request's type and that of its requirement, as their authors write them, <c>CloseInvoice
    /// (BillingAccess.OnInvoice)</c>: what a log line names the request by, so that whoever reads it sees what was
    /// asked again. Nothing of their values, which may say more than a log should.
    /// </summary>
    public override string ToString() => $"{WrittenTypeNames.Of(Request.GetType())} ({WrittenTypeNames.Of(Requirement.GetType())})";

    /// <summary>
    /// Puts <paramref name="request"/> in hand for the rest of this flow of work and asks <paramref name="check"/>
    /// about it, or nobody where it is <see langword="null"/>: the request counts as in hand once the check returned.
    /// Set here, before anything is awaited, and synchronous on purpose: a value set in an asynchronous method stays
    /// in that method, and this one has to reach the flow of the caller of
    /// <see cref="AccessChecks{TRequests}.RequireAsync"/>, which runs the handler next.
    /// </summary>
    /// <param name="request">The very request the checks are asked about.</param>
    /// <param name="requirement">What it declared, read once.</param>
    /// <param name="check">The check that decides <paramref name="requirement"/>; <see langword="null"/> for a request anyone may send.</param>
    /// <param name="cancellationToken">Stops the reading the check does.</param>
    internal static ValueTask Take(IRequireAccess request, AccessRequirement requirement, IAccessCheck? check, CancellationToken cancellationToken)
    {
        var hand = new RequestInHand(request, requirement, check);
        Ambient.Value = hand;

        if (check is null)
        {
            hand._passed = true;
            return ValueTask.CompletedTask;
        }

        var asked = check.RequireAsync(requirement, request, cancellationToken);
        if (!asked.IsCompletedSuccessfully)
        {
            return PassAfterAsync(asked, hand);
        }

        asked.GetAwaiter().GetResult();
        hand._passed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>The hand <paramref name="request"/> is in, in this flow, while its checks run or after: <see langword="null"/> for any other request.</summary>
    /// <param name="request">A request.</param>
    internal static RequestInHand? Of(object request) => Ambient.Value is { } hand && ReferenceEquals(hand.Request, request) ? hand : null;

    /// <summary>Keeps <paramref name="value"/> as what was kept of <typeparamref name="T"/>, in the place of an earlier one. Safe from several tasks of the flow.</summary>
    internal void Keep<T>(T value)
        where T : notnull
    {
        lock (_gate)
        {
            _kept[typeof(T)] = value;
        }
    }

    /// <summary>What was kept of <typeparamref name="T"/>, left where it is.</summary>
    internal bool TryFind<T>(out T value)
        where T : notnull
    {
        lock (_gate)
        {
            if (_kept.TryGetValue(typeof(T), out var kept))
            {
                value = (T)kept;
                return true;
            }
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Awaits the check, then marks the request as let through. Asynchronous, and only the object changes: the flow
    /// that asked holds the same object, so it sees the mark.
    /// </summary>
    private static async ValueTask PassAfterAsync(ValueTask asked, RequestInHand hand)
    {
        await asked.ConfigureAwait(false);
        hand._passed = true;
    }
}
