using System.Collections.Concurrent;
using DDDToolkit.Access;
using DDDToolkit.Startup;
using DDDToolkit.Tests.Access;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// What the toolkit's generator writes beside a behavior, written here by hand: this project uses no library the
// generator writes a behavior for. The garden's has a second behavior for streams and a registration; the pond's
// has neither, as where the project cannot see the service collection.
[assembly: AccessBehavior(
    typeof(IGardenRequest),
    typeof(GardenAccessBehavior<,>),
    StreamBehavior = typeof(GardenAccessStreamBehavior<,>),
    Registration = "services.AddGardenAccessBehavior()")]
[assembly: AccessBehavior(typeof(IPondRequest), typeof(PondAccessBehavior<,>))]

namespace DDDToolkit.Tests.Access;

/// <summary>
/// The start-up check that what asks a module's access checks is in the pipeline: the registration of the checks
/// brings it, for an interface the toolkit wrote a behavior for, and it holds every message the host registered a
/// handler for to having that behavior in its pipeline. A host that runs its start-up checks and leaves the behavior
/// out does not start, and is told the line that puts it in. Without that, the module's requests would reach their
/// handlers with nothing asking what they require, and nothing would say so.
/// </summary>
public sealed class AccessBehaviorCheckTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_host_whose_behaviors_are_registered_starts_and_the_check_ran()
    {
        var logs = new RecordedLogs();
        using var host = Build(
            services => services
                .RunStartupChecks()
                .Handles<PlantTree>()
                .HandlesStreamed<GardenBeds>()
                .AddGardenAccessBehavior(),
            logs);

        await host.StartAsync(Cancellation);

        logs.Messages.Should().ContainSingle(message => message.StartsWith("The start-up checks passed in ", StringComparison.Ordinal))
            .Which.Should().EndWith(" ms: " + AccessBehaviorChecks.BehaviorsRegisteredCheck + ".");
        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task A_host_that_registers_the_checks_and_not_the_behavior_does_not_start_and_is_told_the_line_that_adds_it()
    {
        using var host = Build(services => services
            .RunStartupChecks()
            .Handles<PlantTree>()
            .AddAccessCheck<IGardenRequest, Gardener>());

        var start = () => host.StartAsync(Cancellation);

        var refused = (await start.Should().ThrowAsync<InvalidOperationException>()).Which;
        refused.Message.Should().Be(
            "GardenAccessBehavior<TMessage, TResponse>, the pipeline behavior the toolkit wrote to ask the access checks of IGardenRequest, is not in the pipeline: "
            + "every command and query that implements IGardenRequest reaches its handler with nothing asking what it requires, held only by what the database checks. "
            + "Add services.AddGardenAccessBehavior() where the module registers its checks.");
        refused.Data[StartupChecks.FailedCheckKey].Should().Be(AccessBehaviorChecks.BehaviorsRegisteredCheck);
    }

    [Fact]
    public void A_module_whose_registration_was_forgotten_altogether_is_refused_as_well()
    {
        // The pond registered neither its checks nor its behavior, and its handlers can still be made. The garden's
        // registration brought the check, and the check reads the pond's messages from the handlers the host has.
        var services = new ServiceCollection()
            .Handles<PlantTree>()
            .Handles<FeedFish>()
            .AddGardenAccessBehavior();

        services.GetStartupChecks().Registered.Should().ContainSingle(check => check.Name == AccessBehaviorChecks.BehaviorsRegisteredCheck);
        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(services)).Should().Throw<InvalidOperationException>()
            .WithMessage("PondAccessBehavior<TMessage, TResponse>, the pipeline behavior the toolkit wrote to ask the access checks of IPondRequest, is not in the pipeline:*"
                + "Put PondAccessBehavior<TMessage, TResponse> in the pipeline of the messages that implement IPondRequest.");
    }

    [Fact]
    public void A_host_that_lists_its_behaviors_and_leaves_the_one_for_streams_out_is_told_to_list_it()
    {
        // As the Mediator library registers what a host lists: closed over each message that meets the behavior's
        // constraints. The generated registration would put the first behavior in a second time, so it is not named.
        var services = new ServiceCollection()
            .Handles<PlantTree>()
            .HandlesStreamed<GardenBeds>()
            .AddAccessChecks<IGardenRequest>()
            .AddScoped<IPipe<PlantTree, int>, GardenAccessBehavior<PlantTree, int>>();

        var check = () => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(services);

        check.Should().Throw<InvalidOperationException>().Which.Message.Should().Be(
            "GardenAccessStreamBehavior<TMessage, TResponse>, the behavior the toolkit wrote to ask the access checks of IGardenRequest of a query answered with a stream, is not in the pipeline of streams: "
            + "every such query that implements IGardenRequest is streamed with nothing asking what it requires. "
            + "The host lists its behaviors for the Mediator library: list typeof(GardenAccessStreamBehavior<,>) among its StreamPipelineBehaviors as well.");
    }

    [Fact]
    public void A_module_without_a_stream_query_needs_nothing_in_the_pipeline_of_streams_and_one_without_messages_nothing_at_all()
    {
        // Listed for the library, the behavior for streams is registered over no message where the module has no
        // stream query: there is nothing for it to ask.
        var commandsOnly = new ServiceCollection()
            .Handles<PlantTree>()
            .AddAccessChecks<IGardenRequest>()
            .AddScoped<IPipe<PlantTree, int>, GardenAccessBehavior<PlantTree, int>>();

        var noMessagesYet = new ServiceCollection().AddAccessChecks<IGardenRequest>();

        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(commandsOnly)).Should().NotThrow();
        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(noMessagesYet)).Should().NotThrow();
    }

    [Fact]
    public void A_behavior_counts_as_the_pipeline_it_implements_open_or_closed_over_each_message_and_not_otherwise()
    {
        // Open, as the generated registration adds it; closed over every message, as a library registers what a host
        // lists for it, an instance among them.
        var open = Pond().AddScoped(typeof(IPipe<,>), typeof(PondAccessBehavior<,>));
        var closed = Pond()
            .AddScoped<IPipe<FeedFish, int>, PondAccessBehavior<FeedFish, int>>()
            .AddSingleton<IPipe<CountFish, int>>(new PondAccessBehavior<CountFish, int>());

        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(open)).Should().NotThrow();
        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(closed)).Should().NotThrow();

        // Closed over one message, it says nothing of the other.
        var one = Pond().AddScoped<IPipe<FeedFish, int>, PondAccessBehavior<FeedFish, int>>();
        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(one)).Should().Throw<InvalidOperationException>().Which.Message.Should().Be(
            "PondAccessBehavior<TMessage, TResponse>, the pipeline behavior the toolkit wrote to ask the access checks of IPondRequest, is in the pipeline of some of its messages and not of CountFish: "
            + "that one reaches its handler with nothing asking what it requires, held only by what the database checks. "
            + "The host lists its behaviors for the Mediator library: list typeof(PondAccessBehavior<,>) among its PipelineBehaviors as well.");

        // Under a key, or as itself, it is none the pipeline asks the container for.
        var keyed = Pond().AddKeyedScoped<IPipe<FeedFish, int>, PondAccessBehavior<FeedFish, int>>("pond");
        var asItself = Pond().AddScoped(typeof(PondAccessBehavior<,>));

        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(keyed)).Should().Throw<InvalidOperationException>()
            .WithMessage("*is not in the pipeline:*Put PondAccessBehavior<TMessage, TResponse> in the pipeline of the messages that implement IPondRequest.");
        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(asItself)).Should().Throw<InvalidOperationException>()
            .WithMessage("*is not in the pipeline:*held only by what the database checks. It is registered as PondAccessBehavior<TMessage, TResponse>, which no pipeline asks for. Put *");

        static IServiceCollection Pond() => new ServiceCollection().Handles<FeedFish>().Handles<CountFish>().AddAccessChecks<IPondRequest>();
    }

    [Fact]
    public void Every_behavior_left_out_is_named_in_one_refusal()
    {
        var services = new ServiceCollection()
            .Handles<PlantTree>()
            .Handles<FeedFish>()
            .AddAccessCheck<IPondRequest, Gardener>()
            .AddAccessChecks<IGardenRequest>();

        var check = () => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(services);

        var message = check.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().StartWith("The pipeline lacks 2 of the behaviors the toolkit wrote to ask the access checks:");
        message.Should().Contain(Environment.NewLine + "- GardenAccessBehavior<TMessage, TResponse>, the pipeline behavior the toolkit wrote to ask the access checks of IGardenRequest")
            .And.Contain(Environment.NewLine + "- PondAccessBehavior<TMessage, TResponse>, the pipeline behavior the toolkit wrote to ask the access checks of IPondRequest");
    }

    [Fact]
    public void Checks_of_an_interface_the_toolkit_wrote_no_behavior_for_bring_no_check_and_hold_nothing()
    {
        // A module that asks its checks in a dispatcher of its own: there is nothing of the toolkit's to register.
        var services = new ServiceCollection().Handles<OilHinges>().AddAccessCheck<IShedRequest, Gardener>();

        services.GetStartupChecks().Registered.Should().BeEmpty();
        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(services)).Should().NotThrow();
    }

    [Fact]
    public void One_check_however_many_interfaces_and_registrations_bring_it()
    {
        var services = new ServiceCollection()
            .AddAccessChecks<IGardenRequest>()
            .AddAccessCheck<IGardenRequest, Gardener>()
            .AddAccessChecks<IPondRequest>()
            .AddGardenAccessBehavior();

        services.GetStartupChecks().Registered.Select(check => (check.Name, check.Stage, check.OnByDefault))
            .Should().Equal((AccessBehaviorChecks.BehaviorsRegisteredCheck, StartupCheckStage.Services, false));
    }

    [Fact]
    public async Task A_host_that_does_not_ask_for_its_checks_starts_as_it_did()
    {
        // An application upgrading within 3.x keeps the start-up it had until it calls RunStartupChecks().
        using var host = Build(services => services.Handles<PlantTree>().AddAccessCheck<IGardenRequest, Gardener>());

        await host.StartAsync(Cancellation);
        await host.StopAsync(Cancellation);
    }

    [Fact]
    public void The_check_asks_for_services()
        => FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(null!)).Should().Throw<ArgumentNullException>();

    /// <summary>A host with nothing in it but <paramref name="configure"/>, and what it logs where it is asked.</summary>
    private static IHost Build(Action<IServiceCollection> configure, RecordedLogs? logs = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        if (logs is not null)
        {
            builder.Logging.AddProvider(logs);
        }

        configure(builder.Services);
        return builder.Build();
    }

    /// <summary>Keeps what every logger of the host writes.</summary>
    private sealed class RecordedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyList<string> Messages => [.. _messages];

        public ILogger CreateLogger(string categoryName) => new Recorder(_messages);

        public void Dispose()
        {
        }

        private sealed class Recorder(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Enqueue(formatter(state, exception));
        }
    }
}

/// <summary>What every request of the garden implements; the toolkit "wrote" two behaviors for it.</summary>
public interface IGardenRequest : IRequireAccess;

/// <summary>What every request of the pond implements; one behavior, and no registration was written.</summary>
public interface IPondRequest : IRequireAccess;

/// <summary>What every request of the shed implements; nothing was written for it.</summary>
public interface IShedRequest : IRequireAccess;

/// <summary>A command of the garden.</summary>
public sealed record PlantTree : IGardenRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => new AccessRequirement.Open("Anybody may plant a tree.");
}

/// <summary>A query of the garden answered with a stream.</summary>
public sealed record GardenBeds : IGardenRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => new AccessRequirement.Open("The beds are everybody's to see.");
}

/// <summary>A request of the pond.</summary>
public sealed record FeedFish : IPondRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => new AccessRequirement.Open("The fish are everybody's.");
}

/// <summary>Another request of the pond.</summary>
public sealed record CountFish : IPondRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => new AccessRequirement.Open("Anybody may count them.");
}

/// <summary>A request of the shed.</summary>
public sealed record OilHinges : IShedRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => new AccessRequirement.Open("Anybody may oil a hinge.");
}

/// <summary>A pipeline, as a library declares one.</summary>
public interface IPipe<TMessage, TResponse>;

/// <summary>The library's pipeline of the messages answered with a stream.</summary>
public interface IStreamPipe<TMessage, TResponse>;

/// <summary>A handler, as the library declares one, which it registers closed over each message it handles.</summary>
public interface IPipeHandler<TMessage, TResponse>;

/// <summary>A handler of a message answered with a stream, as the library declares one.</summary>
public interface IStreamPipeHandler<TMessage, TResponse>;

/// <summary>A handler of any message, for a host that handles it.</summary>
public sealed class Handler<TMessage> : IPipeHandler<TMessage, int>, IStreamPipeHandler<TMessage, int>;

/// <summary>The garden's behavior, in the place of one the generator writes.</summary>
public sealed class GardenAccessBehavior<TMessage, TResponse> : IPipe<TMessage, TResponse>;

/// <summary>The garden's behavior for streams.</summary>
public sealed class GardenAccessStreamBehavior<TMessage, TResponse> : IStreamPipe<TMessage, TResponse>;

/// <summary>The pond's behavior.</summary>
public sealed class PondAccessBehavior<TMessage, TResponse> : IPipe<TMessage, TResponse>;

/// <summary>A check that decides nothing, for a registration that adds one.</summary>
public sealed class Gardener : IAccessCheck
{
    public bool Decides(AccessRequirement requirement) => false;

    public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

/// <summary>The registrations the generator and the library write, by hand.</summary>
public static class GardenAccessBehaviorRegistration
{
    /// <summary>The registration the generator writes for the garden.</summary>
    public static IServiceCollection AddGardenAccessBehavior(this IServiceCollection services)
    {
        services.AddAccessChecks<IGardenRequest>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipe<,>), typeof(GardenAccessBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IStreamPipe<,>), typeof(GardenAccessStreamBehavior<,>)));
        return services;
    }

    /// <summary>A handler of <typeparamref name="TMessage"/>, as the library registers one.</summary>
    public static IServiceCollection Handles<TMessage>(this IServiceCollection services)
        => services.AddScoped<IPipeHandler<TMessage, int>, Handler<TMessage>>();

    /// <summary>A handler of <typeparamref name="TMessage"/>, answered with a stream, as the library registers one.</summary>
    public static IServiceCollection HandlesStreamed<TMessage>(this IServiceCollection services)
        => services.AddScoped<IStreamPipeHandler<TMessage, int>, Handler<TMessage>>();
}
