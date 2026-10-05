using System.Reflection;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Mediator.Tests.Domain;
using DDDToolkit.Mediator.Tests.Infrastructure;
using DDDToolkit.Mediator.Tests.Requests;
using FluentAssertions;
using DDDToolkit.Startup;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

// The second module's behaviors, which the host lists for Mediator's generator, described as the toolkit's generator
// describes the ones it writes, for the tests that the start-up check reads what that generator registered.
[assembly: AccessBehavior(typeof(IStockRequest), typeof(StockAccess<,>), StreamBehavior = typeof(StockStreamAccess<,>))]

namespace DDDToolkit.Mediator.Tests;

/// <summary>
/// The access behavior the toolkit writes for an <c>[AccessRequests]</c> interface, in a project that is its own
/// composition root: the toolkit's generator and Mediator's run in one compilation here, and neither sees what the
/// other writes. So the host does not name the generated behavior in the options Mediator's generator reads; it
/// adds it to the container with the registration written beside it, <c>services.AddBasketAccessBehavior()</c>,
/// and the mediator Mediator's generator wrote takes it from there like any other. Behaviors the host does list
/// for Mediator's generator run as well, in the order of registration. A query answered with a stream passes
/// Mediator's other pipeline, and is held there by the second behavior the same registration adds. And the module's
/// check brings the start-up check that the behavior is there: a host that forgot the line does not start.
/// </summary>
public sealed class AccessBehaviorTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_request_passes_the_listed_behavior_then_its_access_check_then_its_handler()
    {
        using var host = new TestHost();
        var basket = BasketId.CreateUnique();
        host.StepLog.Owned.Add(basket);

        using var scope = host.CreateScope();
        var renamed = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RenameBasket(basket, "Weekend"), Cancellation);

        host.StepLog.InOrder.Should().Equal("listed", "check", "handler");
        renamed.Should().Be(basket, "the handler acts on what the check kept for its request");
    }

    [Fact]
    public async Task The_request_is_in_hand_in_its_handler_through_either_behavior_and_nowhere_after()
    {
        using var host = new TestHost();
        var basket = BasketId.CreateUnique();
        host.StepLog.Owned.Add(basket);
        var command = new RenameBasket(basket, "Weekend");
        var query = new BasketLines(basket);

        using var scope = host.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        await sender.Send(command, Cancellation);
        await foreach (var _ in sender.CreateStream(query, Cancellation))
        {
        }

        // The generated behaviors ask the checks and run the handler in one method, so the handler, and the save it
        // would end with, find the request and what its check kept without being handed either: a stream's handler
        // until it hands out its first line, since each line after is asked for in the flow of whoever reads. And the
        // sender's caller, once it returned, has nothing in hand.
        host.StepLog.InHand.Should().Equal([(command, basket), (query, basket)]);
        RequestInHand.Current.Should().BeNull();
    }

    [Fact]
    public async Task A_refused_request_never_reaches_its_handler()
    {
        using var host = new TestHost();

        using var scope = host.CreateScope();
        var send = async () => await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RenameBasket(BasketId.CreateUnique(), "Weekend"), Cancellation);

        (await send.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(BasketAccessCheck.NotYours);
        host.StepLog.InOrder.Should().Equal("listed", "check");
    }

    [Fact]
    public async Task A_request_anyone_may_send_passes_without_a_check_and_one_nothing_decides_does_not_pass()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        (await sender.Send(new BasketKinds(), Cancellation)).Should().Be(3);
        host.StepLog.InOrder.Should().Equal("listed", "handler");

        var send = async () => await sender.Send(new AuditBaskets(), Cancellation);
        (await send.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*AuditBaskets declares 'AuditBaskets.Audits', which none of the access checks registered for IBasketRequest decides*");
        host.StepLog.InOrder.Should().Equal(["listed", "handler", "listed"], "a requirement nothing checks lets nobody through");
    }

    [Fact]
    public async Task A_query_answered_with_a_stream_passes_its_access_check_before_anything_is_streamed()
    {
        using var host = new TestHost();
        var basket = BasketId.CreateUnique();
        host.StepLog.Owned.Add(basket);

        using var scope = host.CreateScope();
        var stream = scope.ServiceProvider.GetRequiredService<ISender>().CreateStream(new BasketLines(basket), Cancellation);
        host.StepLog.InOrder.Should().BeEmpty("nothing runs until the stream is read");

        var lines = new List<string>();
        await foreach (var line in stream)
        {
            lines.Add(line);
        }

        lines.Should().Equal("bread", "milk");
        host.StepLog.InOrder.Should().Equal(["check", "handler"], "the listed behavior is of the other pipeline, and the check comes before the handler in this one");
    }

    [Fact]
    public async Task A_refused_stream_query_never_reaches_its_handler_and_streams_nothing()
    {
        using var host = new TestHost();

        using var scope = host.CreateScope();
        var lines = new List<string>();
        var read = async () =>
        {
            await foreach (var line in scope.ServiceProvider.GetRequiredService<ISender>().CreateStream(new BasketLines(BasketId.CreateUnique()), Cancellation))
            {
                lines.Add(line);
            }
        };

        (await read.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(BasketAccessCheck.NotYours);
        lines.Should().BeEmpty();
        host.StepLog.InOrder.Should().Equal("check");
    }

    [Fact]
    public async Task A_request_of_no_module_passes_no_access_behavior()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();

        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Ping(), Cancellation);

        host.StepLog.InOrder.Should().Equal("listed", "handler");
        scope.ServiceProvider.GetServices<IPipelineBehavior<Ping, Unit>>().Select(step => step.GetType().GetGenericTypeDefinition())
            .Should().Equal(typeof(Listed<,>));
    }

    [Fact]
    public void The_generated_behavior_is_in_the_container_after_the_ones_the_mediator_generator_registered()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();

        // What Mediator's generator registered from the options it read when this project compiled, then what the
        // toolkit's generated registration added: the order they run in.
        scope.ServiceProvider.GetServices<IPipelineBehavior<RenameBasket, BasketId>>().Select(step => step.GetType().GetGenericTypeDefinition())
            .Should().Equal(typeof(Listed<,>), typeof(BasketAccessBehavior<,>));

        typeof(BasketAccessBehavior<,>).Namespace.Should().Be(typeof(IBasketRequest).Namespace, "the behavior is written beside the interface");
        typeof(BasketAccessBehavior<,>).GetGenericArguments()[0].GetGenericParameterConstraints()
            .Should().BeEquivalentTo([typeof(IBasketRequest), typeof(IMessage)]);
        scope.ServiceProvider.GetRequiredService<AccessChecks<IBasketRequest>>().Decides(new OwnsBasket(BasketId.CreateUnique())).Should().BeTrue();

        // The same registration put the second behavior in the pipeline of the streams, over the same checks.
        scope.ServiceProvider.GetServices<IStreamPipelineBehavior<BasketLines, string>>().Select(step => step.GetType().GetGenericTypeDefinition())
            .Should().Equal(typeof(BasketAccessStreamBehavior<,>));
        typeof(BasketAccessStreamBehavior<,>).GetGenericArguments()[0].GetGenericParameterConstraints()
            .Should().BeEquivalentTo([typeof(IBasketRequest), typeof(IStreamMessage)]);
    }

    [Fact]
    public void The_generator_says_which_behaviors_ask_the_checks_of_the_interface_and_how_they_are_registered()
    {
        var written = typeof(IBasketRequest).Assembly.GetCustomAttributes<AccessBehaviorAttribute>().Single(each => each.Requests == typeof(IBasketRequest));

        written.Behavior.Should().Be(typeof(BasketAccessBehavior<,>));
        written.StreamBehavior.Should().Be(typeof(BasketAccessStreamBehavior<,>));
        written.Registration.Should().Be("services.AddBasketAccessBehavior()");
    }

    [Fact]
    public async Task The_module_s_check_brings_the_start_up_check_of_its_behavior_which_the_host_passes()
    {
        using var host = new TestHost();

        var check = host.Registrations.GetStartupChecks().Registered.Should().ContainSingle(registered => registered.Name == AccessBehaviorChecks.BehaviorsRegisteredCheck).Subject;
        check.Stage.Should().Be(StartupCheckStage.Services);
        await check.RunAsync(host.Services, Cancellation);
    }

    [Fact]
    public async Task A_host_that_left_the_generated_behavior_out_is_refused_with_the_line_that_adds_it()
    {
        using var host = new TestHost(accessBehavior: false);
        var check = host.Registrations.GetStartupChecks().Registered.Single(registered => registered.Name == AccessBehaviorChecks.BehaviorsRegisteredCheck);

        var run = () => check.RunAsync(host.Services, Cancellation);

        (await run.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "BasketAccessBehavior<TMessage, TResponse>, the pipeline behavior the toolkit wrote to ask the access checks of IBasketRequest, is not in the pipeline:*"
            + "Add services.AddBasketAccessBehavior() where the module registers its checks.");

        // What it stops: the pipeline a request about a basket passes holds nothing that asks what it requires.
        using var scope = host.CreateScope();
        scope.ServiceProvider.GetServices<IPipelineBehavior<RenameBasket, BasketId>>().Select(step => step.GetType().GetGenericTypeDefinition())
            .Should().Equal([typeof(Listed<,>)], "nothing of the module's is in the pipeline");
    }

    [Fact]
    public void A_module_whose_behaviors_are_listed_for_Mediator_s_generator_passes_without_a_stream_query()
    {
        // Mediator's generator registers a listed behavior closed over each message that meets its constraints: the
        // second module's over its one command, and the one for streams over nothing, since the module has no query
        // answered with a stream. Nothing of the module is missing from a pipeline a message of it passes.
        using var host = new TestHost();
        var registered = Copy(host.Registrations).AddAccessChecks<IStockRequest>();

        var listed = registered.Should().ContainSingle(descriptor => descriptor.ImplementationType == typeof(StockAccess<TakeStock, Unit>)).Subject;
        listed.ServiceType.Should().Be<IPipelineBehavior<TakeStock, Unit>>();
        registered.Should().NotContain(descriptor => descriptor.ImplementationType != null && descriptor.ImplementationType.IsGenericType
            && descriptor.ImplementationType.GetGenericTypeDefinition() == typeof(StockStreamAccess<,>));
        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(registered)).Should().NotThrow();

        // And it is that registration the check saw: without it, the same host is refused.
        registered.Remove(listed);
        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(registered)).Should().Throw<InvalidOperationException>()
            .WithMessage("StockAccess<TMessage, TResponse>, the pipeline behavior the toolkit wrote to ask the access checks of IStockRequest, is not in the pipeline:*");
    }

    [Fact]
    public void A_module_whose_registration_was_forgotten_altogether_is_refused_from_its_handlers()
    {
        // Neither the module's checks nor its behavior: what is left of it is the handlers Mediator's generator
        // registered, and those are what the check reads.
        using var host = new TestHost(accessBehavior: false);
        var registered = Copy(host.Registrations);
        foreach (var descriptor in registered.Where(descriptor => descriptor.ServiceType.IsConstructedGenericType && descriptor.ServiceType.GenericTypeArguments[0] == typeof(IBasketRequest)).ToList())
        {
            registered.Remove(descriptor);
        }

        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(registered)).Should().Throw<InvalidOperationException>()
            .WithMessage("BasketAccessBehavior<TMessage, TResponse>, the pipeline behavior the toolkit wrote to ask the access checks of IBasketRequest, is not in the pipeline:*"
                + "Add services.AddBasketAccessBehavior() where the module registers its checks.");
    }

    [Fact]
    public void A_behavior_counts_only_as_the_pipeline_and_only_for_the_messages_it_is_registered_for()
    {
        // Registered as itself, the slip of a hand that meant the generated line, the behavior is never asked for.
        using var host = new TestHost(accessBehavior: false);
        var asItself = Copy(host.Registrations);
        asItself.AddScoped(typeof(BasketAccessBehavior<,>));
        asItself.AddScoped(typeof(BasketAccessStreamBehavior<,>));

        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(asItself)).Should().Throw<InvalidOperationException>()
            .WithMessage("*is not in the pipeline:*It is registered as BasketAccessBehavior<TMessage, TResponse>, which no pipeline asks for.*");

        // Closed over one message, it speaks for that one: the others of the module are named.
        var one = Copy(host.Registrations);
        one.AddScoped<IPipelineBehavior<BasketKinds, int>, BasketAccessBehavior<BasketKinds, int>>();
        one.AddScoped<IStreamPipelineBehavior<BasketLines, string>, BasketAccessStreamBehavior<BasketLines, string>>();

        FluentActions.Invoking(() => AccessBehaviorChecks.EnsureBehaviorsAreRegistered(one)).Should().Throw<InvalidOperationException>()
            .WithMessage("BasketAccessBehavior<TMessage, TResponse>, the pipeline behavior the toolkit wrote to ask the access checks of IBasketRequest, is in the pipeline of some of its messages and not of AuditBaskets, RenameBasket: those reach their handlers*");
    }

    /// <summary>A copy of what a host registered, to change without changing the host.</summary>
    private static IServiceCollection Copy(IServiceCollection registrations)
    {
        IServiceCollection copy = new ServiceCollection();
        foreach (var descriptor in registrations)
        {
            copy.Add(descriptor);
        }

        return copy;
    }
}
