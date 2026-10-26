using System.Reflection;
using DDDToolkit.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Access;

/// <summary>
/// The start-up check that what asks a module's access checks is in the pipeline. The checks of a request interface
/// hold nobody to anything by themselves: something has to ask them in front of every handler, and where the
/// toolkit wrote that something, the pipeline behavior of an interface marked <c>[AccessRequests]</c>, a host that
/// leaves the behavior out has commands and queries that reach their handlers unchecked, held only by what the
/// database checks, without a word.
/// <para>
/// <c>AddAccessChecks&lt;TRequests&gt;()</c> brings it, and so does everything that calls that: <c>AddAccessCheck</c>,
/// a package's registration of its check, and the generated <c>Add{Module}AccessBehavior()</c> itself. It is
/// brought only for an interface the toolkit wrote a behavior for, so a host that asks the checks in a dispatcher of
/// its own gets nothing to turn off. Once brought, it holds every module of the host, one whose registration was
/// forgotten altogether included. A host runs it with every other check with <c>services.RunStartupChecks()</c>;
/// the method stays for a test that composes the host's services and holds them to it.
/// </para>
/// </summary>
public static class AccessBehaviorChecks
{
    /// <summary>
    /// The start-up check that every message the host can hand to a handler, of a request interface the toolkit wrote
    /// a behavior for, has that behavior in its pipeline (<see cref="EnsureBehaviorsAreRegistered"/>): registered by
    /// the generated call, or listed for the Mediator library, either way. It reads what the host registered and opens
    /// no connection, so it runs with the checks of the services, first.
    /// </summary>
    public const string BehaviorsRegisteredCheck = "access.behaviors-registered";

    /// <summary>How many messages a refusal names before it counts the rest.</summary>
    private const int NamedMessages = 5;

    /// <summary>
    /// Throws when <paramref name="services"/> register a handler of a message whose request interface the toolkit
    /// wrote a behavior for (<see cref="AccessBehaviorAttribute"/>), and leave that behavior out of the message's
    /// pipeline: the message would reach its handler with nothing asking what it requires. A query answered with a
    /// stream passes a pipeline of its own, where the library has one, and needs the behavior written for that one.
    /// <para>
    /// The messages are read from the handlers the library registered, each under its handler interface closed over
    /// its message, <c>ICommandHandler&lt;CloseInvoice, Unit&gt;</c> say. The interfaces held are the ones those
    /// messages implement, whether or not their checks are registered, so a module whose registration was forgotten
    /// altogether is found as well.
    /// </para>
    /// <para>
    /// A behavior counts for a message when it is registered as the pipeline it implements: open, as the generated call
    /// adds it, or closed over that message, as the Mediator library registers what a host lists in its options, once
    /// for each message that meets the behavior's constraints. So a module without a stream query needs nothing in the
    /// pipeline of streams, and one closed registration speaks for its own message only. A behavior registered as
    /// anything else, itself say, with a factory, or under a key, is none the pipeline asks for, and does not count.
    /// </para>
    /// </summary>
    /// <param name="services">The host's services, as registered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// A message is handled with the behavior that asks its checks missing from its pipeline. The message names each
    /// such behavior, its interface, the messages it is missing for where it is there for others, and the line that
    /// puts it in.
    /// </exception>
    public static void EnsureBehaviorsAreRegistered(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var handled = HandledMessages(services);
        var unasked = new List<string>();
        foreach (var (requests, written) in RequestInterfacesOf(handled))
        {
            var library = LibraryOf(written.Behavior);
            var messages = handled
                .Where(each => each.Library == library && requests.IsAssignableFrom(each.Message))
                .DistinctBy(each => each.Message)
                .ToList();

            var behavior = RegistrationsOf(services, written.Behavior);
            var streams = written.StreamBehavior is { } streamBehavior ? RegistrationsOf(services, streamBehavior) : null;
            var missing = behavior.MissingFor(messages.Where(each => !each.Streamed));
            var missingStreams = streams?.MissingFor(messages.Where(each => each.Streamed)) ?? [];

            // A host that lists its behaviors for the library has them registered closed, and is told the one it
            // left out of each list. A host that lists none is told the one line that puts both in.
            var lists = behavior.ClosedOver.Count > 0 || streams?.ClosedOver.Count > 0;
            if (missing.Count > 0)
            {
                unasked.Add(Missing(written, behavior, requests, missing, streamed: false, lists));
            }

            if (streams is not null && missingStreams.Count > 0 && (lists || missing.Count == 0))
            {
                unasked.Add(Missing(written, streams, requests, missingStreams, streamed: true, lists));
            }
        }

        if (unasked.Count == 1)
        {
            throw new InvalidOperationException(unasked[0]);
        }

        if (unasked.Count > 1)
        {
            throw new InvalidOperationException(
                $"The pipeline lacks {unasked.Count} of the behaviors the toolkit wrote to ask the access checks:"
                + string.Concat(unasked.Select(each => Environment.NewLine + "- " + each)));
        }
    }

    /// <summary>
    /// Brings <see cref="BehaviorsRegisteredCheck"/> to <paramref name="services"/> when the toolkit wrote a behavior
    /// for <typeparamref name="TRequests"/>; once, however many interfaces bring it, since the one check reads them
    /// all. The check reads the very collection it was brought by, which is the one the host is built from.
    /// </summary>
    internal static void BringTo<TRequests>(IServiceCollection services)
        where TRequests : class, IRequireAccess
    {
        if (WrittenFor(typeof(TRequests)) is null)
        {
            return;
        }

        services.AddStartupCheck(new StartupCheck(BehaviorsRegisteredCheck, StartupCheckStage.Services, (_, _) =>
        {
            EnsureBehaviorsAreRegistered(services);
            return Task.CompletedTask;
        }));
    }

    /// <summary>
    /// Every message a library hands to a handler the host registered: a handler interface of the library, closed
    /// over its message first, <c>ICommandHandler&lt;CloseInvoice, Unit&gt;</c>. Read by its shape, as the core
    /// references no library: an interface named for a handler, and of the library's handlers of streams where its
    /// name says so. A notification is no request, and passes no pipeline of the toolkit's.
    /// </summary>
    private static List<HandledMessage> HandledMessages(IServiceCollection services)
        => [.. services
            .Where(static descriptor => !descriptor.IsKeyedService
                && descriptor.ServiceType is { IsInterface: true, IsConstructedGenericType: true } handler
                && NameOf(handler).EndsWith("Handler", StringComparison.Ordinal)
                && !NameOf(handler).Contains("Notification", StringComparison.Ordinal))
            .Select(static descriptor => new HandledMessage(
                descriptor.ServiceType.GenericTypeArguments[0],
                descriptor.ServiceType.Assembly,
                NameOf(descriptor.ServiceType).StartsWith("IStream", StringComparison.Ordinal)))];

    /// <summary>
    /// The request interfaces of <paramref name="handled"/> the toolkit wrote a behavior for, with what it wrote,
    /// ordered by name so a refusal that names several reads the same each time. Only an interface that says what
    /// a request requires can be one, so no other assembly's attributes are read.
    /// </summary>
    private static IEnumerable<(Type Requests, AccessBehaviorAttribute Written)> RequestInterfacesOf(List<HandledMessage> handled)
        => handled
            .Select(static each => each.Message)
            .Distinct()
            .SelectMany(static message => message.GetInterfaces())
            .Distinct()
            .Where(static requests => requests != typeof(IRequireAccess) && typeof(IRequireAccess).IsAssignableFrom(requests))
            .Select(static requests => (Requests: requests, Written: WrittenFor(requests)))
            .Where(static each => each.Written is not null)
            .OrderBy(static each => each.Requests.FullName, StringComparer.Ordinal)
            .Select(static each => (each.Requests, each.Written!));

    /// <summary>
    /// What the toolkit's generator says it wrote for <paramref name="requests"/>, in the assembly that declares the
    /// interface, or null where it wrote nothing: a project without the library asks the checks itself.
    /// </summary>
    private static AccessBehaviorAttribute? WrittenFor(Type requests)
        => requests.Assembly.GetCustomAttributes<AccessBehaviorAttribute>().FirstOrDefault(written => written.Requests == requests);

    /// <summary>The assembly of the library whose pipeline <paramref name="behavior"/> is part of, and whose handlers it stands in front of.</summary>
    private static Assembly? LibraryOf(Type behavior)
        => Definition(behavior).GetInterfaces().FirstOrDefault(static pipeline => pipeline.IsGenericType)?.Assembly;

    /// <summary>How <paramref name="behavior"/> is registered, read in one pass over <paramref name="services"/>.</summary>
    private static BehaviorRegistrations RegistrationsOf(IServiceCollection services, Type behavior)
    {
        var definition = Definition(behavior);
        var pipelines = definition.GetInterfaces().Where(static pipeline => pipeline.IsGenericType).Select(static pipeline => pipeline.GetGenericTypeDefinition()).ToHashSet();

        var open = false;
        var closedOver = new HashSet<Type>();
        Type? elsewhere = null;
        foreach (var descriptor in services)
        {
            if (descriptor.IsKeyedService || (descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType()) is not { } made)
            {
                continue;
            }

            // Only a registration as a pipeline the behavior implements is one the pipeline asks the container for.
            if (made == definition)
            {
                if (descriptor.ServiceType.IsGenericTypeDefinition && pipelines.Contains(descriptor.ServiceType))
                {
                    open = true;
                }
                else
                {
                    elsewhere ??= descriptor.ServiceType;
                }
            }
            else if (made.IsConstructedGenericType && made.GetGenericTypeDefinition() == definition)
            {
                if (made.GetInterfaces().Contains(descriptor.ServiceType))
                {
                    closedOver.Add(made.GenericTypeArguments[0]);
                }
                else
                {
                    elsewhere ??= descriptor.ServiceType;
                }
            }
        }

        return new BehaviorRegistrations(behavior, open, closedOver, elsewhere);
    }

    /// <summary>The refusal for a behavior missing from the pipeline of <paramref name="missing"/>.</summary>
    private static string Missing(AccessBehaviorAttribute written, BehaviorRegistrations behavior, Type requests, List<Type> missing, bool streamed, bool lists)
    {
        var interfaceName = WrittenTypeNames.Of(requests);
        var one = missing.Count == 1;

        var what = streamed
            ? $"{Named(behavior.Behavior)}, the behavior the toolkit wrote to ask the access checks of {interfaceName} of a query answered with a stream, "
            : $"{Named(behavior.Behavior)}, the pipeline behavior the toolkit wrote to ask the access checks of {interfaceName}, ";

        var gap = (behavior.ClosedOver.Count > 0, streamed) switch
        {
            (false, false) => $"is not in the pipeline: every command and query that implements {interfaceName} reaches its handler with nothing asking what it requires, held only by what the database checks.",
            (false, true) => $"is not in the pipeline of streams: every such query that implements {interfaceName} is streamed with nothing asking what it requires.",
            (true, false) => $"is in the pipeline of some of its messages and not of {Listed(missing)}: {(one ? "that one reaches its handler" : "those reach their handlers")} with nothing asking what {(one ? "it requires" : "they require")}, held only by what the database checks.",
            (true, true) => $"is in the pipeline of some of its queries and not of {Listed(missing)}: {(one ? "that one is" : "those are")} streamed with nothing asking what {(one ? "it requires" : "they require")}.",
        };

        var elsewhere = behavior.Elsewhere is { } serviceType
            ? $" It is registered as {Named(serviceType)}, which no pipeline asks for."
            : string.Empty;

        var definition = Definition(behavior.Behavior);
        var putItIn = lists
            ? $" The host lists its behaviors for the Mediator library: list typeof({WrittenTypeNames.Of(definition)}<{new string(',', definition.GetGenericArguments().Length - 1)}>) among its {(streamed ? "StreamPipelineBehaviors" : "PipelineBehaviors")} as well."
            : written.Registration is { Length: > 0 } registration
                ? $" Add {registration} where the module registers its checks."
                : $" Put {Named(behavior.Behavior)} in the pipeline of the messages that implement {interfaceName}.";

        return what + gap + elsewhere + putItIn;
    }

    /// <summary>The first few of <paramref name="messages"/> by name, and how many more there are.</summary>
    private static string Listed(List<Type> messages)
    {
        var named = string.Join(", ", messages.Take(NamedMessages).Select(WrittenTypeNames.Of));
        return messages.Count > NamedMessages ? $"{named} and {messages.Count - NamedMessages} more" : named;
    }

    /// <summary>A behavior as its author would write it: <c>BillingAccessBehavior&lt;TMessage, TResponse&gt;</c>.</summary>
    private static string Named(Type behavior)
    {
        var definition = Definition(behavior);
        return definition.IsGenericTypeDefinition
            ? WrittenTypeNames.Of(definition) + "<" + string.Join(", ", definition.GetGenericArguments().Select(static parameter => parameter.Name)) + ">"
            : WrittenTypeNames.Of(definition);
    }

    /// <summary>A generic type by its definition, any other as it is.</summary>
    private static Type Definition(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition() : type;

    /// <summary>A type's name without its arity: <c>ICommandHandler</c> for <c>ICommandHandler`2</c>.</summary>
    private static string NameOf(Type type) => type.Name.IndexOf('`') is var arity and >= 0 ? type.Name[..arity] : type.Name;

    /// <summary>A message a library hands to a handler the host registered.</summary>
    /// <param name="Message">The message.</param>
    /// <param name="Library">The assembly that declares the handler interface it is registered under.</param>
    /// <param name="Streamed">Whether that interface is one of the library's handlers of streams.</param>
    private sealed record HandledMessage(Type Message, Assembly Library, bool Streamed);

    /// <summary>How one behavior is registered.</summary>
    /// <param name="Behavior">The behavior, as a generic type definition.</param>
    /// <param name="Open">Whether it is registered open, as a pipeline it implements: in front of every message of the pipeline.</param>
    /// <param name="ClosedOver">The messages it is registered closed over, as a pipeline it implements.</param>
    /// <param name="Elsewhere">A service it is registered as that no pipeline asks for, or null.</param>
    private sealed record BehaviorRegistrations(Type Behavior, bool Open, HashSet<Type> ClosedOver, Type? Elsewhere)
    {
        /// <summary>The messages of <paramref name="handled"/> whose pipeline lacks the behavior, ordered by name.</summary>
        public List<Type> MissingFor(IEnumerable<HandledMessage> handled)
            => Open
                ? []
                : [.. handled.Select(static each => each.Message).Where(message => !ClosedOver.Contains(message)).OrderBy(static message => message.FullName, StringComparer.Ordinal)];
    }
}
