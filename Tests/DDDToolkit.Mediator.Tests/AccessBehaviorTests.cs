using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Mediator.Tests.Domain;
using DDDToolkit.Mediator.Tests.Infrastructure;
using DDDToolkit.Mediator.Tests.Requests;
using FluentAssertions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Mediator.Tests;

/// <summary>
/// The access behavior the toolkit writes for an <c>[AccessRequests]</c> interface, in a project that is its own
/// composition root: the toolkit's generator and Mediator's run in one compilation here, and neither sees what the
/// other writes. So the host does not name the generated behavior in the options Mediator's generator reads; it
/// adds it to the container with the registration written beside it, <c>services.AddBasketAccessBehavior()</c>,
/// and the mediator Mediator's generator wrote takes it from there like any other. Behaviors the host does list
/// for Mediator's generator run as well, in the order of registration. A query answered with a stream passes
/// Mediator's other pipeline, and is held there by the second behavior the same registration adds.
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
}
