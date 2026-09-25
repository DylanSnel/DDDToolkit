using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Domain.Events;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Registration spread over modules: every module calls <c>AddDDDToolkitEntityFramework</c> for its own
/// part, and a context can have an outbox of its own.
/// </summary>
public sealed class ModuleRegistrationTests : IDisposable
{
    private readonly SqliteDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Every_call_configures_the_same_options_so_a_module_can_add_to_what_the_host_set()
    {
        var services = new ServiceCollection()
            .AddDDDToolkitEntityFramework(options => options.DispatchInProcess((_, _, _) => Task.CompletedTask))
            .AddDDDToolkitEntityFramework(options => options.MapIntegrationEvents(contracts => contracts.Register<ShelfOpenedV3>()))
            .AddDDDToolkitEntityFramework(options => options.UseOutbox<LibraryContext>());

        services.Count(d => d.ServiceType == typeof(DDDEntityFrameworkOptions)).Should().Be(1);

        var options = services.BuildServiceProvider().GetRequiredService<DDDEntityFrameworkOptions>();
        options.Dispatcher.Should().NotBeNull("the host's call is not undone by the modules' calls");
        options.Contracts.TryResolve("library.shelf-opened", 3, out _).Should().BeTrue();
        options.ContextOutboxes.Should().ContainKey(typeof(LibraryContext));
    }

    [Fact]
    public void A_second_dispatch_delegate_is_refused_rather_than_silently_replacing_the_first()
    {
        var options = new DDDEntityFrameworkOptions().DispatchInProcess((_, _, _) => Task.CompletedTask);

        var act = () => options.DispatchInProcess((_, _, _) => Task.CompletedTask);

        act.Should().Throw<InvalidOperationException>().WithMessage("*already configured*once, in the host*");
    }

    [Fact]
    public void A_context_gets_its_own_outbox_before_the_shared_one_and_none_when_neither_exists()
    {
        var shared = new DDDEntityFrameworkOptions().UseOutbox<LibraryContext>();
        shared.OutboxFor(typeof(LibraryContext)).Should().BeSameAs(shared.ContextOutboxes[typeof(LibraryContext)]);
        shared.OutboxFor(typeof(RenamedStorageContext)).Should().BeNull("a context without an outbox dispatches in process");

        shared.UseOutbox();
        shared.OutboxFor(typeof(RenamedStorageContext)).Should().BeSameAs(shared.Outbox, "the shared outbox covers every context without its own");
        shared.OutboxFor(typeof(LibraryContext)).Should().NotBeSameAs(shared.Outbox);
    }

    [Fact]
    public void Configuring_a_context_outbox_twice_adds_to_the_same_one()
    {
        var options = new DDDEntityFrameworkOptions()
            .UseOutbox<LibraryContext>(outbox => outbox.RegisterEvent<ShelfCreated>())
            .UseOutbox<LibraryContext>(outbox => outbox.RegisterEvent<BookAdded>());

        options.ContextOutboxes[typeof(LibraryContext)].EventTypes.Names.Should().BeEquivalentTo("shelf.created", "book-added");
    }

    [Fact]
    public async Task A_context_outbox_is_written_by_that_context_and_delivered_by_its_processor()
    {
        using var host = new TestHost(
            _db,
            options => options.UseOutbox<LibraryContext>(outbox => outbox.RegisterEvent<ShelfCreated>().RegisterEvent<BookAdded>()),
            services: services => services.AddOutboxProcessor<LibraryContext>());

        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(new Shelf(ShelfId.CreateUnique(), "Fiction", UserId.CreateUnique(), CatId.CreateUnique(), null));
            await context.SaveChangesAsync();
        });

        host.Recorder.Events.Should().BeEmpty("the context's own outbox takes the event, so nothing is dispatched at save time");

        var processed = await host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());

        processed.Should().Be(1);
        host.Recorder.Events.Should().ContainSingle().Which.Should().BeOfType<ShelfCreated>();
    }

    [Fact]
    public void A_processor_for_a_context_without_an_outbox_names_the_context()
    {
        var options = new DDDEntityFrameworkOptions()
            .DispatchInProcess((_, _, _) => Task.CompletedTask)
            .UseOutbox<RenamedStorageContext>();
        using var context = _db.CreateLibraryContext();

        var act = () => new OutboxProcessor<LibraryContext>(context, new ServiceCollection().BuildServiceProvider(), options);

        act.Should().Throw<InvalidOperationException>().WithMessage("LibraryContext has no outbox*UseOutbox<LibraryContext>*");
    }
}
