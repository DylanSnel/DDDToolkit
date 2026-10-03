using System.Diagnostics;
using System.Runtime.CompilerServices;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Domain.Events;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.EntityFramework.Tests.Invariants;
using DDDToolkit.Exceptions;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Saving through a context taken from Entity Framework's context pool. A pool builds its options once,
/// with the application's root services, and hands the same context instance to one renter after another,
/// so two things are held here: the save pipeline works whichever scope rents the context, with that scope's
/// services and no other, and nothing of one renter is left for the next.
/// </summary>
public sealed class PooledContextSaveTests : IDisposable
{
    private readonly SqliteDatabase _db = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => _db.Dispose();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Shelf NewShelf(string name = "Fiction") => new(ShelfId.CreateUnique(), name, UserId.CreateUnique(), CatId.CreateUnique(), null);

    private static Person NewPerson(string first = "Ada") => new(MemberId.CreateUnique(), new PersonName(first, "Lovelace"), null, new ValidDateOfBirth(new DateOnly(1815, 12, 10)));

    private static IReadOnlyList<IDomainEvent> PendingOn(Shelf shelf) => ((IHasDomainEvents)shelf).DomainEvents;

    /// <summary>A host whose events go to an outbox, with the recorder as what the processor delivers to.</summary>
    private PooledTestHost OutboxHost(PooledAs pooledAs, Action<IServiceCollection>? services = null)
        => new(
            _db,
            pooledAs,
            options =>
            {
                options.TimeProvider = _clock;
                options.UseOutbox(outbox => outbox.RegisterEvent<ShelfCreated>().RegisterEvent<ShelfRenamed>().RegisterEvent<BookAdded>());
            },
            dispatchThroughRecorder: true,
            services);

    // ---- The probes: what the rest is built on -----------------------------------------------------

    [Theory]
    [InlineData(PooledAs.Factory)]
    [InlineData(PooledAs.ScopedLease)]
    public void Options_for_a_pool_are_built_from_the_root_services_and_resolve_nothing_scoped(PooledAs pooledAs)
    {
        var built = 0;
        IServiceProvider? handed = null;

        var services = new ServiceCollection();
        services.AddDDDToolkitEntityFramework();
        services.AddPostgresRowLevelSecurity();
        services.AddSingleton<PackageSaveCheck>();
        services.AddScoped<ScopedThing>();
        PooledTestHost.AddPool<LibraryContext>(services, _db, pooledAs, (provider, options) =>
        {
            built++;
            handed = provider;

            // What a host's callback holds next to UseDDDToolkit: row level security, and an interceptor a
            // package registered as a singleton and adds from the provider, as a supporting domain's does.
            options.UsePostgresRowLevelSecurity(provider);
            options.AddInterceptors(provider.GetRequiredService<PackageSaveCheck>());
        });
        services.AddScopedFromPool<LibraryContext>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        Type[] interceptors = [];
        for (var rental = 0; rental < 3; rental++)
        {
            using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LibraryContext>();
            interceptors = [.. context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!.Select(interceptor => interceptor.GetType())];
        }

        built.Should().Be(1, "a pool builds its options once, whoever rents");
        var scoped = () => handed!.GetRequiredService<ScopedThing>();
        scoped.Should().Throw<InvalidOperationException>("the callback was handed the root provider, which resolves nothing scoped where scopes are validated")
            .WithMessage("*from root provider*");
        interceptors.Should().Equal(
            typeof(PublishDomainEventsInterceptor),
            typeof(InvariantInterceptor),
            typeof(AggregateVersionInterceptor),
            typeof(DatabaseRefusalInterceptor),
            typeof(PostgresRowLevelSecurityInterceptor),
            typeof(PackageSaveCheck));
    }

    [Fact]
    public void A_pooled_context_knows_it_is_pooled()
    {
        // AddPooledDbContextFactory: rented from the factory.
        {
            var services = new ServiceCollection();
            services.AddPooledDbContextFactory<LibraryContext>(options => options.UseSqlite(_db.Connection));
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var factory = provider.GetRequiredService<IDbContextFactory<LibraryContext>>();

            LibraryContext first;
            using (first = factory.CreateDbContext())
            {
                first.IsPooled().Should().BeTrue();
                first.ContextId.Lease.Should().Be(1, "the first rental");
            }

            using var again = factory.CreateDbContext();
            again.Should().BeSameAs(first, "the pool hands the same instance out again");
            again.IsPooled().Should().BeTrue();
            again.ContextId.Lease.Should().Be(2, "the lease number grows by one per rental");
        }

        // AddDbContextPool: rented by a scope.
        {
            var services = new ServiceCollection();
            services.AddDbContextPool<LibraryContext>(options => options.UseSqlite(_db.Connection));
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

            LibraryContext first;
            using (var scope = provider.CreateScope())
            {
                first = scope.ServiceProvider.GetRequiredService<LibraryContext>();
                first.IsPooled().Should().BeTrue();
                first.ContextId.Lease.Should().Be(1);
            }

            using var next = provider.CreateScope();
            var again = next.ServiceProvider.GetRequiredService<LibraryContext>();
            again.Should().BeSameAs(first);
            again.ContextId.Lease.Should().Be(2);
        }

        // A pooled factory made by hand, with no services at all.
        {
            var factory = new PooledDbContextFactory<LibraryContext>(_db.Options<LibraryContext>());

            LibraryContext first;
            using (first = factory.CreateDbContext())
            {
                first.IsPooled().Should().BeTrue();
                first.ContextId.Lease.Should().Be(1);
            }

            using var again = factory.CreateDbContext();
            again.Should().BeSameAs(first);
            again.ContextId.Lease.Should().Be(2);
        }

        // Not pooled: AddDbContext, AddDbContextFactory, and a context made with new.
        {
            var services = new ServiceCollection();
            services.AddDbContext<LibraryContext>(options => options.UseSqlite(_db.Connection));
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LibraryContext>();

            context.IsPooled().Should().BeFalse();
            context.ContextId.Lease.Should().Be(0, "a context that is not pooled is never leased");
        }

        {
            var services = new ServiceCollection();
            services.AddDbContextFactory<LibraryContext>(options => options.UseSqlite(_db.Connection));
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using var context = provider.GetRequiredService<IDbContextFactory<LibraryContext>>().CreateDbContext();

            context.IsPooled().Should().BeFalse();
            context.ContextId.Lease.Should().Be(0);
        }

        using var plain = _db.CreateLibraryContext();
        plain.IsPooled().Should().BeFalse();
        plain.ContextId.Lease.Should().Be(0);
    }

    [Fact]
    public async Task AddScopedFromPool_after_AddDbContextPool_binds_the_scope_s_context()
    {
        var recorder = new EventRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddDDDToolkitEntityFramework(options => options.DispatchInProcess((sp, events, ct) => sp.GetRequiredService<EventRecorder>().DispatchAsync(sp, events, ct)));
        PooledTestHost.AddPool<LibraryContext>(services, _db, PooledAs.ScopedLease);

        var registered = services.Single(descriptor => descriptor.ServiceType == typeof(LibraryContext));
        registered.ImplementationFactory.Should().NotBeNull("AddDbContextPool registers the context of a scoped lease through a factory, which is what gets wrapped");

        services.AddScopedFromPool<LibraryContext>();

        var wrapped = services.Single(descriptor => descriptor.ServiceType == typeof(LibraryContext));
        wrapped.Should().NotBeSameAs(registered, "the registration was wrapped");
        wrapped.Lifetime.Should().Be(ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _db.EnsureCreated(() => _db.CreateLibraryContext());
        IServiceProvider? handed = null;
        recorder.OnEvent = (given, _, _) =>
        {
            handed = given;
            return Task.CompletedTask;
        };

        LibraryContext first;
        using (var scope = provider.CreateScope())
        {
            first = scope.ServiceProvider.GetRequiredService<LibraryContext>();
            first.IsPooled().Should().BeTrue();
            first.Shelves.Add(NewShelf());
            await first.SaveChangesAsync(Cancellation);

            handed.Should().BeSameAs(scope.ServiceProvider, "the scope's context is bound to the scope that rented it");
            handed!.GetRequiredService<LibraryContext>().Should().BeSameAs(first);
        }

        using var next = provider.CreateScope();
        next.ServiceProvider.GetRequiredService<LibraryContext>().Should().BeSameAs(first, "the scoped lease still gives the context back with the scope");
    }

    /// <summary>
    /// What Entity Framework itself registers for the context type, which decides what
    /// <c>AddScopedFromPool</c> has to do: wrap a registration that is there, or add one over the factory.
    /// The test asserts what holds on the version it runs on, and writes what it saw to its output.
    /// </summary>
    [Fact]
    public void What_each_pool_registration_registers_for_the_context_is_recorded()
    {
        var output = TestContext.Current.TestOutputHelper;
        var version = FileVersionInfo.GetVersionInfo(typeof(DbContext).Assembly.Location).ProductVersion;
        output?.WriteLine($"Entity Framework Core {version}");

        ServiceDescriptor? ContextAfter(string registration, Action<IServiceCollection> register)
        {
            var services = new ServiceCollection();
            register(services);
            var descriptor = services.LastOrDefault(candidate => !candidate.IsKeyedService && candidate.ServiceType == typeof(LibraryContext));
            var factory = services.LastOrDefault(candidate => !candidate.IsKeyedService && candidate.ServiceType == typeof(IDbContextFactory<LibraryContext>));

            output?.WriteLine(
                $"{registration}: the context is " + (descriptor is null
                    ? "not registered"
                    : $"registered {descriptor.Lifetime}, {(descriptor.ImplementationFactory is not null ? "through an implementation factory" : descriptor.ImplementationType is not null ? "as a type" : "as an instance")}")
                + (factory is null ? "; no factory" : $"; a factory, {factory.Lifetime}"));

            return descriptor;
        }

        var afterFactory = ContextAfter("AddPooledDbContextFactory", services => services.AddPooledDbContextFactory<LibraryContext>(options => options.UseSqlite(_db.Connection)));
        var afterPool = ContextAfter("AddDbContextPool", services => services.AddDbContextPool<LibraryContext>(options => options.UseSqlite(_db.Connection)));

        // For the record only: these two are told from a pool by the context itself, not by their shape.
        ContextAfter("AddDbContextFactory", services => services.AddDbContextFactory<LibraryContext>(options => options.UseSqlite(_db.Connection)));
        var plain = ContextAfter("AddDbContext", services => services.AddDbContext<LibraryContext>(options => options.UseSqlite(_db.Connection)));

        afterPool.Should().NotBeNull("AddDbContextPool registers the context of a scoped lease");
        afterPool!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        afterPool.ImplementationFactory.Should().NotBeNull();

        afterFactory.Should().NotBeNull("Entity Framework 10 registers the context for a pooled factory as well, taken from that factory");
        afterFactory!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        afterFactory.ImplementationFactory.Should().NotBeNull();

        plain.Should().NotBeNull();
        plain!.ImplementationType.Should().Be<LibraryContext>("AddDbContext registers the type, which is how it is refused when it is registered");
    }

    [Theory]
    [InlineData(PooledAs.Factory)]
    [InlineData(PooledAs.ScopedLease)]
    public async Task A_binding_survives_a_collection_while_the_scope_lives(PooledAs pooledAs)
    {
        using var host = new PooledTestHost(_db, pooledAs);
        IServiceProvider? handed = null;
        host.Recorder.OnEvent = (given, _, _) =>
        {
            handed = given;
            return Task.CompletedTask;
        };

        using var scope = host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LibraryContext>();
        context.Shelves.Add(NewShelf());

        // The binding holds the scope weakly; the scope itself is alive for as long as somebody uses it.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await context.SaveChangesAsync(Cancellation);

        handed.Should().BeSameAs(scope.ServiceProvider);
    }

    [Fact]
    public async Task A_binding_survives_a_collection_when_the_container_hands_out_a_wrapper_of_the_scope()
    {
        using var host = new PooledTestHost(_db, PooledAs.Factory);
        IServiceProvider? handed = null;
        host.Recorder.OnEvent = (given, _, _) =>
        {
            handed = given;
            return Task.CompletedTask;
        };

        // Some containers hand a registration a provider of its own, a wrapper of the scope that nothing else
        // refers to. The binding holds what it is given weakly, so the scope has to keep the wrapper itself.
        using var scope = host.CreateScope();
        await using var context = await host.Factory.CreateDbContextAsync(Cancellation);
        BindThroughAWrapper(context, scope.ServiceProvider);
        context.Shelves.Add(NewShelf());

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await context.SaveChangesAsync(Cancellation);

        handed.Should().BeOfType<WrappedScope>().Which.Scope.Should().BeSameAs(scope.ServiceProvider);
        _db.CountRows("Shelves").Should().Be(1);
    }

    /// <summary>Binds in a method of its own, so no local of the test keeps the wrapper alive.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BindThroughAWrapper(LibraryContext context, IServiceProvider scope) => context.BindToScope(new WrappedScope(scope));

    /// <summary>A provider that answers from a scope and is not the scope: what such a container hands a registration.</summary>
    private sealed class WrappedScope(IServiceProvider scope) : IServiceProvider
    {
        public IServiceProvider Scope => scope;

        public object? GetService(Type serviceType) => scope.GetService(serviceType);
    }

    // ---- Saving ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_save_from_a_pooled_context_writes_its_events_to_the_outbox()
    {
        using var host = OutboxHost(PooledAs.Factory);
        var shelf = NewShelf();
        shelf.AddBook("Dune");

        // Rented from the factory, with no scope anywhere: the outbox asks for none.
        await using (var context = await host.Factory.CreateDbContextAsync(Cancellation))
        {
            context.IsPooled().Should().BeTrue();
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync(Cancellation);
        }

        _db.CountRows("Shelves").Should().Be(1);
        host.Recorder.Events.Should().BeEmpty("with an outbox nothing is dispatched at save time");
        PendingOn(shelf).Should().BeEmpty();

        using var check = _db.CreateLibraryContext();
        (await check.Outbox.Select(row => row.EventName).ToListAsync(Cancellation)).Should().BeEquivalentTo(["shelf.created", "book-added"], "the rows were written by the same save");
    }

    [Fact]
    public async Task The_event_log_is_written_from_a_pooled_context_without_a_scope()
    {
        var alice = Callers.FromClaims("""{"sub":"a11ce000-0000-4000-8000-000000000041","role":"authenticated"}""");
        var bob = Callers.FromClaims("""{"sub":"b0b00000-0000-4000-8000-000000000042","role":"authenticated"}""");

        ServiceProvider Build(Action<IServiceCollection> fields)
        {
            var services = new ServiceCollection();
            services.AddDDDToolkitEntityFramework(options =>
            {
                options.TimeProvider = _clock;
                options.UseOutbox<LoggedApiaryContext>(outbox => outbox.RegisterEvent<HiveSettled>().KeepEventLog());
            });
            fields(services);
            services.AddPooledDbContextFactory<LoggedApiaryContext>((provider, options) => options.UseSqlite(_db.Connection).UseDDDToolkit(provider));
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        _db.EnsureCreated(() => new LoggedApiaryContext(_db.Options<LoggedApiaryContext>()));

        // Who acted and what fills the module's own column are singletons that read the flow of work each time
        // they are asked, and the interceptor asks its own provider for them, which under a pool is the root.
        await using var host = Build(services => services.AddSingleton<IEventLogFields, YardOfTheFlow>());
        var factory = host.GetRequiredService<IDbContextFactory<LoggedApiaryContext>>();

        async Task<DbContext> SettleAsAsync(Caller caller, string yard, int number)
        {
            using var running = Callers.Begin(caller);
            using var standing = YardOfTheFlow.Begin(yard);

            // Rented from the factory, with no scope anywhere and none bound: the log asks for none.
            await using var context = await factory.CreateDbContextAsync(Cancellation);
            context.IsPooled().Should().BeTrue();
            context.Hives.Add(new Hive(HiveId.CreateSequential(), number, "By the hedge", caller.UserId, isOpen: true));
            await context.SaveChangesAsync(Cancellation);
            return context;
        }

        var first = await SettleAsAsync(alice, "north", 1);
        var second = await SettleAsAsync(bob, "south", 2);
        second.Should().BeSameAs(first, "the pool hands the same context to the next renter");

        using (var check = new LoggedApiaryContext(_db.Options<LoggedApiaryContext>()))
        {
            var log = await check.EventLog.AsNoTracking()
                .Select(entry => entry.ActedByKind + " " + entry.ActedById + " in " + EF.Property<string?>(entry, LoggedApiaryContext.Yard))
                .ToListAsync(Cancellation);
            log.Should().BeEquivalentTo(
                ["user a11ce000-0000-4000-8000-000000000041 in north", "user b0b00000-0000-4000-8000-000000000042 in south"],
                "each save was written as its own caller, in its own yard: nothing of one renter is read for the next");
            (await check.Outbox.CountAsync(Cancellation)).Should().Be(2, "next to the outbox rows of the same saves");
        }

        // A filler that needs a scope cannot be taken from a pool's options, and the save says so before an event
        // leaves its aggregate.
        await using var scoped = Build(services => services.AddScoped<IEventLogFields, YardOfTheFlow>());
        var hive = new Hive(HiveId.CreateSequential(), number: 3, "By the gate", keeper: null, isOpen: true);
        await using (var context = await scoped.GetRequiredService<IDbContextFactory<LoggedApiaryContext>>().CreateDbContextAsync(Cancellation))
        {
            context.Hives.Add(hive);
            var save = () => context.SaveChangesAsync(Cancellation);

            (await save.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
                "The event log of 'LoggedApiaryContext' could not be written: IActedByAccessor or an IEventLogFields is registered as a scoped service, " +
                "and the options of the context were built with the application's root services, as those of a context pool are. " +
                "Register them as singletons that read what they answer each time they are asked. Nothing was saved.");
        }

        ((IHasDomainEvents)hive).DomainEvents.Should().ContainSingle("the refusal came before anything was dequeued");
        _db.CountRows("Hives").Should().Be(2);
    }

    [Theory]
    [InlineData(PooledAs.Factory)]
    [InlineData(PooledAs.ScopedLease)]
    public async Task A_save_from_the_scope_s_pooled_context_hands_the_handlers_that_scope(PooledAs pooledAs)
    {
        using var host = new PooledTestHost(_db, pooledAs);
        IServiceProvider? handed = null;
        LibraryContext? resolved = null;
        host.Recorder.OnEvent = (given, _, _) =>
        {
            handed = given;
            resolved = given.GetRequiredService<LibraryContext>();
            return Task.CompletedTask;
        };

        using var scope = host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LibraryContext>();
        context.IsPooled().Should().BeTrue();
        context.Shelves.Add(NewShelf());
        await context.SaveChangesAsync(Cancellation);

        handed.Should().BeSameAs(scope.ServiceProvider, "the handlers run with the scope that rented the context, not with the root the options were built from");
        resolved.Should().BeSameAs(context, "so a handler that asks for the context gets the very one being saved");
        _db.CountRows("Shelves").Should().Be(1);
    }

    [Theory]
    [InlineData(PooledAs.Factory)]
    [InlineData(PooledAs.ScopedLease)]
    public async Task A_handler_s_change_on_the_scope_s_pooled_context_rides_the_same_save(PooledAs pooledAs)
    {
        using var host = new PooledTestHost(_db, pooledAs);
        host.Recorder.OnEvent = (services, domainEvent, _) =>
        {
            if (domainEvent is ShelfCreated created)
            {
                var context = services.GetRequiredService<LibraryContext>();
                context.Shelves.Local.Single(shelf => shelf.Id == created.ShelfId).Rename(created.Name + " (renamed by handler)");
                context.People.Add(NewPerson());
            }

            return Task.CompletedTask;
        };

        var shelf = NewShelf("Fiction");
        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync(Cancellation);
        });

        host.Recorder.Events.Select(domainEvent => domainEvent.GetType()).Should().Equal([typeof(ShelfCreated), typeof(ShelfRenamed)], "the event the handler raised is dispatched in the next round of the same save");

        using var check = _db.CreateLibraryContext();
        check.Shelves.Single(row => row.Id == shelf.Id).Name.Should().Be("Fiction (renamed by handler)");
        check.People.Should().ContainSingle();
    }

    [Fact]
    public async Task Sync_save_from_the_scope_s_pooled_context_has_the_same_semantics()
    {
        using var host = new PooledTestHost(_db, PooledAs.Factory);
        IServiceProvider? handed = null;
        host.Recorder.OnEvent = (given, _, _) =>
        {
            handed = given;
            return Task.CompletedTask;
        };

        await host.InScopeAsync((context, services) =>
        {
            context.Shelves.Add(NewShelf());
            context.SaveChanges();

            handed.Should().BeSameAs(services);
            return Task.FromResult(0);
        });

        _db.CountRows("Shelves").Should().Be(1);
    }

    // ---- The scope of a rental ---------------------------------------------------------------------

    [Fact]
    public async Task An_unbound_pooled_context_refuses_to_dispatch_in_process_and_keeps_its_events()
    {
        using var host = new PooledTestHost(_db, PooledAs.Factory);
        var shelf = NewShelf();

        await using var context = await host.Factory.CreateDbContextAsync(Cancellation);
        context.Shelves.Add(shelf);

        var save = () => context.SaveChangesAsync(Cancellation);
        (await save.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("'LibraryContext' was taken from a context pool and this rental was given no scope*" +
                         "services.AddScopedFromPool<LibraryContext>()*context.BindToScope(scope.ServiceProvider)*UseOutbox<LibraryContext>(...)*" +
                         "Nothing was dispatched and nothing was saved.");

        var saveSync = () => context.SaveChanges();
        saveSync.Should().Throw<InvalidOperationException>().WithMessage("*this rental was given no scope*");

        host.Recorder.Events.Should().BeEmpty();
        _db.CountRows("Shelves").Should().Be(0);
        PendingOn(shelf).Should().ContainSingle("the refusal comes before anything is dequeued").Which.Should().BeOfType<ShelfCreated>();

        // Nothing was lost: given a scope, the same save goes through with the events it kept.
        using var scope = host.CreateScope();
        context.BindToScope(scope.ServiceProvider);
        await context.SaveChangesAsync(Cancellation);

        host.Recorder.Events.Should().ContainSingle().Which.Should().BeOfType<ShelfCreated>();
        _db.CountRows("Shelves").Should().Be(1);
    }

    [Fact]
    public async Task An_unbound_pooled_context_saves_what_raises_no_event()
    {
        using var host = new PooledTestHost(_db, PooledAs.Factory);

        await using var context = await host.Factory.CreateDbContextAsync(Cancellation);
        context.People.Add(NewPerson());
        await context.SaveChangesAsync(Cancellation);

        _db.CountRows("People").Should().Be(1, "only a dispatch needs a scope, and there was nothing to dispatch");
    }

    [Fact]
    public async Task A_factory_context_bound_to_a_scope_dispatches_with_that_scope()
    {
        using var host = new PooledTestHost(_db, PooledAs.Factory);
        IServiceProvider? handed = null;
        host.Recorder.OnEvent = (given, _, _) =>
        {
            handed = given;
            return Task.CompletedTask;
        };

        using var scope = host.CreateScope();
        await using var context = (await host.Factory.CreateDbContextAsync(Cancellation)).BindToScope(scope.ServiceProvider);
        context.Shelves.Add(NewShelf());
        await context.SaveChangesAsync(Cancellation);

        handed.Should().BeSameAs(scope.ServiceProvider);
        scope.ServiceProvider.GetRequiredService<LibraryContext>().Should().NotBeSameAs(context, "binding names the services; the scope's own context is still another one");
        _db.CountRows("Shelves").Should().Be(1);
    }

    [Theory]
    [InlineData(PooledAs.Factory)]
    [InlineData(PooledAs.ScopedLease)]
    public async Task Scopes_that_hold_a_context_at_once_each_hand_their_handlers_their_own_scope(PooledAs pooledAs)
    {
        using var host = new PooledTestHost(_db, pooledAs);
        var handed = new List<(IServiceProvider Services, LibraryContext Context)>();
        host.Recorder.OnEvent = (given, _, _) =>
        {
            handed.Add((given, given.GetRequiredService<LibraryContext>()));
            return Task.CompletedTask;
        };

        static PublishDomainEventsInterceptor InterceptorOf(DbContext context)
            => context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!.OfType<PublishDomainEventsInterceptor>().Single();

        using var one = host.CreateScope();
        using var other = host.CreateScope();
        var contextOfOne = one.ServiceProvider.GetRequiredService<LibraryContext>();
        var contextOfOther = other.ServiceProvider.GetRequiredService<LibraryContext>();
        contextOfOne.Should().NotBeSameAs(contextOfOther, "two scopes that are alive at once rent a context each");
        InterceptorOf(contextOfOne).Should().BeSameAs(InterceptorOf(contextOfOther), "the options are the pool's, so one interceptor serves every context of it");

        // Saves that take turns: which scope a save belongs to is the saving context's, not the last one bound.
        contextOfOne.Shelves.Add(NewShelf("One"));
        contextOfOther.Shelves.Add(NewShelf("Other"));
        await contextOfOne.SaveChangesAsync(Cancellation);
        await contextOfOther.SaveChangesAsync(Cancellation);
        contextOfOne.Shelves.Add(NewShelf("One, again"));
        await contextOfOne.SaveChangesAsync(Cancellation);

        handed.Should().HaveCount(3);
        handed[0].Services.Should().BeSameAs(one.ServiceProvider);
        handed[0].Context.Should().BeSameAs(contextOfOne);
        handed[1].Services.Should().BeSameAs(other.ServiceProvider);
        handed[1].Context.Should().BeSameAs(contextOfOther);
        handed[2].Services.Should().BeSameAs(one.ServiceProvider, "the first scope is still the first context's, after the other was bound and saved");
        handed[2].Context.Should().BeSameAs(contextOfOne);
        _db.CountRows("Shelves").Should().Be(3);
    }

    [Theory]
    [InlineData(PooledAs.Factory)]
    [InlineData(PooledAs.ScopedLease)]
    public async Task A_binding_ends_with_the_rental(PooledAs pooledAs)
    {
        using var host = new PooledTestHost(_db, pooledAs);
        var handed = new List<IServiceProvider>();
        host.Recorder.OnEvent = (given, _, _) =>
        {
            handed.Add(given);
            return Task.CompletedTask;
        };

        LibraryContext first;
        IServiceProvider firstScope;
        using (var scope = host.CreateScope())
        {
            firstScope = scope.ServiceProvider;
            first = scope.ServiceProvider.GetRequiredService<LibraryContext>();
            first.Shelves.Add(NewShelf("First"));
            await first.SaveChangesAsync(Cancellation);
        }

        if (pooledAs == PooledAs.Factory)
        {
            // The same instance, rented with no scope: what the first scope bound is not found.
            await using var unbound = await host.Factory.CreateDbContextAsync(Cancellation);
            unbound.Should().BeSameAs(first);
            unbound.Shelves.Add(NewShelf("Unbound"));

            var save = () => unbound.SaveChangesAsync(Cancellation);
            (await save.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*this rental was given no scope*");
        }

        using (var scope = host.CreateScope())
        {
            var next = scope.ServiceProvider.GetRequiredService<LibraryContext>();
            next.Should().BeSameAs(first, "the pool hands the same instance to the next scope");
            next.Shelves.Add(NewShelf("Second"));
            await next.SaveChangesAsync(Cancellation);

            handed.Should().HaveCount(2);
            handed[0].Should().BeSameAs(firstScope);
            handed[1].Should().BeSameAs(scope.ServiceProvider, "the next scope's handlers get the next scope");
            handed[1].Should().NotBeSameAs(firstScope);
        }

        _db.CountRows("Shelves").Should().Be(2);
    }

    // ---- What goes back to the pool ----------------------------------------------------------------

    [Theory]
    [InlineData(PooledAs.Factory)]
    [InlineData(PooledAs.ScopedLease)]
    public async Task Nothing_tracked_is_carried_to_the_next_renter(PooledAs pooledAs)
    {
        using var host = new PooledTestHost(_db, pooledAs);
        var shelf = NewShelf();

        LibraryContext first;
        using (var scope = host.CreateScope())
        {
            first = scope.ServiceProvider.GetRequiredService<LibraryContext>();
            first.Shelves.Add(shelf);

            // Never saved: the aggregate, and the event pending on it, leave with the change tracker.
        }

        using (var scope = host.CreateScope())
        {
            var next = scope.ServiceProvider.GetRequiredService<LibraryContext>();
            next.Should().BeSameAs(first);
            next.ChangeTracker.Entries().Should().BeEmpty();
            (await next.SaveChangesAsync(Cancellation)).Should().Be(0);
        }

        host.Recorder.Events.Should().BeEmpty("the next renter dispatches nothing of the one before");
        _db.CountRows("Shelves").Should().Be(0);
        PendingOn(shelf).Should().ContainSingle("the event stayed on the aggregate nobody saved");
    }

    [Theory]
    [InlineData(PooledAs.Factory)]
    [InlineData(PooledAs.ScopedLease)]
    public async Task What_a_renter_set_on_the_context_is_put_back(PooledAs pooledAs)
    {
        using var host = new PooledTestHost(_db, pooledAs);
        var runs = 0;

        LibraryContext first;
        using (var scope = host.CreateScope())
        {
            first = scope.ServiceProvider.GetRequiredService<LibraryContext>();
            first.ChangeTracker.AutoDetectChangesEnabled = false;
            first.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            first.Database.SetCommandTimeout(7);
            first.SavingChanges += (_, _) => runs++;
        }

        using (var scope = host.CreateScope())
        {
            var next = scope.ServiceProvider.GetRequiredService<LibraryContext>();
            next.Should().BeSameAs(first);
            next.ChangeTracker.AutoDetectChangesEnabled.Should().BeTrue();
            next.ChangeTracker.QueryTrackingBehavior.Should().Be(QueryTrackingBehavior.TrackAll);
            next.Database.GetCommandTimeout().Should().BeNull();

            next.People.Add(NewPerson());
            await next.SaveChangesAsync(Cancellation);
        }

        runs.Should().Be(0, "the handler the first renter added does not see the next renter's save");
        _db.CountRows("People").Should().Be(1);
    }

    // ---- The rest of the pipeline --------------------------------------------------------------------

    [Theory]
    [InlineData(PooledAs.Factory)]
    [InlineData(PooledAs.ScopedLease)]
    public async Task Invariants_and_versions_are_kept_on_a_pooled_context(PooledAs pooledAs)
    {
        var services = new ServiceCollection();
        services.AddDDDToolkitEntityFramework();
        PooledTestHost.AddPool<TabContext>(services, _db, pooledAs);
        services.AddScopedFromPool<TabContext>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _db.EnsureCreated(() => new TabContext(_db.Options<TabContext>()));

        // A broken invariant refuses the save.
        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TabContext>();
            context.IsPooled().Should().BeTrue();
            var broken = new Tab(TabId.CreateUnique(), limit: 1000);
            broken.Order("Champagne", 9000);
            context.Tabs.Add(broken);

            var save = () => context.SaveChangesAsync(Cancellation);
            (await save.Should().ThrowAsync<InvariantViolationException>()).Which.AggregateType.Should().Be<Tab>();
        }

        _db.CountRows("Tabs").Should().Be(0);

        // A version is bumped once per save, on the instance the broken tab was just tracked on.
        var id = TabId.CreateUnique();
        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TabContext>();
            context.ChangeTracker.Entries().Should().BeEmpty("the refused tab left with the renter that tracked it");
            var tab = new Tab(id, limit: 5000);
            tab.Order("Negroni", 1200);
            context.Tabs.Add(tab);
            await context.SaveChangesAsync(Cancellation);
            tab.Version.Should().Be(1);

            tab.Order("Martini", 1400);
            await context.SaveChangesAsync(Cancellation);
            tab.Version.Should().Be(2);
        }

        // Two contexts of the pool in use at once: the second writer's version is stale.
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<TabContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<TabContext>();
        contextA.Should().NotBeSameAs(contextB);

        var tabA = await contextA.Tabs.SingleAsync(tab => tab.Id == id, Cancellation);
        var tabB = await contextB.Tabs.SingleAsync(tab => tab.Id == id, Cancellation);

        tabA.RaiseLimit(6000);
        await contextA.SaveChangesAsync(Cancellation);
        tabA.Version.Should().Be(3);

        tabB.RaiseLimit(7000);
        var stale = () => contextB.SaveChangesAsync(Cancellation);
        (await stale.Should().ThrowAsync<ConcurrencyConflictException>()).Which.AggregateId.Should().Be(id);
    }

    [Theory]
    [InlineData(PooledAs.Factory)]
    [InlineData(PooledAs.ScopedLease)]
    public async Task The_outbox_processor_delivers_from_a_pooled_context(PooledAs pooledAs)
    {
        using var host = OutboxHost(pooledAs, services => services.AddOutboxProcessor<LibraryContext>());
        var shelf = NewShelf();
        shelf.AddBook("Dune");
        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync(Cancellation);
        });

        var handed = new List<IServiceProvider>();
        var resolved = new List<LibraryContext>();
        host.Recorder.OnEvent = (given, _, _) =>
        {
            handed.Add(given);
            resolved.Add(given.GetRequiredService<LibraryContext>());
            return Task.CompletedTask;
        };

        using var scope = host.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor<LibraryContext>>();
        var delivered = await processor.ProcessPendingAsync(cancellationToken: Cancellation);

        delivered.Should().Be(2);
        host.Recorder.Events.Select(domainEvent => domainEvent.GetType()).Should().BeEquivalentTo([typeof(ShelfCreated), typeof(BookAdded)]);
        handed.Should().OnlyContain(given => ReferenceEquals(given, scope.ServiceProvider), "the processor delivers with the scope it was resolved from");
        resolved.Should().OnlyContain(context => ReferenceEquals(context, scope.ServiceProvider.GetRequiredService<LibraryContext>()) && context.IsPooled());

        using var check = _db.CreateLibraryContext();
        (await check.Outbox.ToListAsync(Cancellation)).Should().HaveCount(2).And.OnlyContain(row => row.ProcessedAt != null && row.LastError == null);
    }

    [Theory]
    [InlineData(PooledAs.Factory)]
    [InlineData(PooledAs.ScopedLease)]
    public async Task The_inbox_and_retention_run_on_a_pooled_context(PooledAs pooledAs)
    {
        using var host = new PooledTestHost(
            _db,
            pooledAs,
            options => options.TimeProvider = _clock,
            services: services => services
                .AddDomainEventInbox<LibraryContext>()
                .AddDomainEventRetention<LibraryContext>(retention =>
                {
                    retention.KeepOutboxFor = TimeSpan.FromDays(7);
                    retention.KeepInboxFor = TimeSpan.FromDays(30);
                }));
        var messageId = Guid.CreateVersion7();

        Task<bool> DeliverAsync() => host.InScopeAsync((context, services) => services
            .GetRequiredService<DomainEventInbox<LibraryContext>>()
            .ExecuteOnceAsync(
                messageId,
                "billing.person-projector",
                _ =>
                {
                    context.People.Add(NewPerson());
                    return Task.CompletedTask;
                },
                Cancellation));

        (await DeliverAsync()).Should().BeTrue();
        (await DeliverAsync()).Should().BeFalse("the inbox row the first delivery wrote is found through the context the pool handed out again");
        _db.CountRows("People").Should().Be(1);
        _db.CountRows("InboxMessages").Should().Be(1);

        // An old delivered outbox row next to the inbox row, and time enough for both windows.
        using (var seed = _db.CreateLibraryContext())
        {
            seed.Outbox.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                EventName = "shelf.created",
                Payload = "{}",
                OccurredAt = _clock.GetUtcNow(),
                CreatedAt = _clock.GetUtcNow(),
                ProcessedAt = _clock.GetUtcNow(),
                Attempts = 1,
            });
            await seed.SaveChangesAsync(Cancellation);
        }

        _clock.Advance(TimeSpan.FromDays(31));

        var result = await host.InScopeAsync((_, services) => services.GetRequiredService<DomainEventRetention<LibraryContext>>().DeleteExpiredAsync(Cancellation));

        result.Should().Be(new DomainEventRetentionResult(OutboxMessages: 1, InboxMessages: 1));
        _db.CountRows("OutboxMessages").Should().Be(0);
        _db.CountRows("InboxMessages").Should().Be(0);
    }

    // ---- The outbox poller ---------------------------------------------------------------------------

    [Fact]
    public async Task The_outbox_poller_takes_its_context_as_the_system_caller()
    {
        using var provider = PollerHost(requireExplicitCallers: true, out var rentals, out var recorder);
        var handlerCallers = new List<Caller?>();
        recorder.OnEvent = (_, _, _) =>
        {
            handlerCallers.Add(Callers.Ambient);
            return Task.CompletedTask;
        };
        await QueueAsync(provider);
        rentals.Clear();

        var poller = provider.GetServices<IHostedService>().OfType<OutboxBackgroundService<LibraryContext>>().Single();
        await poller.DrainAsync(Cancellation);

        rentals.Should().Equal([Caller.System, Caller.System], "one rental per batch, the batch that delivered and the one that found nothing, each taken as the system");
        handlerCallers.Should().Equal([null], "the handler still runs with no caller: the system is the poller's own, for its bookkeeping");
        Callers.Ambient.Should().BeNull("the caller is gone again after the round");

        using var check = _db.CreateLibraryContext();
        (await check.Outbox.SingleAsync(Cancellation)).ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task The_outbox_poller_begins_no_caller_where_none_is_required()
    {
        using var provider = PollerHost(requireExplicitCallers: false, out var rentals, out var recorder);
        await QueueAsync(provider);
        rentals.Clear();

        var poller = provider.GetServices<IHostedService>().OfType<OutboxBackgroundService<LibraryContext>>().Single();
        await poller.DrainAsync(Cancellation);

        rentals.Should().Equal([null, null], "a host that does not require explicit callers runs as it always did: nothing is begun for it");
        recorder.Events.Should().ContainSingle();
    }

    /// <summary>
    /// A host whose poller's context comes from a factory the host registered by hand, as one that chooses a
    /// pool by who is calling would be: it writes down who the caller was at every rental.
    /// </summary>
    private ServiceProvider PollerHost(bool requireExplicitCallers, out List<Caller?> rentals, out EventRecorder recorder)
    {
        var seen = new List<Caller?>();
        var events = new EventRecorder();

        var services = new ServiceCollection();
        services.AddSingleton(events);
        services.AddDDDToolkitEntityFramework(options =>
        {
            options.TimeProvider = _clock;
            options.DispatchInProcess((sp, dispatched, ct) => sp.GetRequiredService<EventRecorder>().DispatchAsync(sp, dispatched, ct));
            options.UseOutbox(outbox => outbox.RegisterEvent<ShelfCreated>());
        });

        if (requireExplicitCallers)
        {
            services.RequireExplicitCallers();
        }

        services.AddSingleton<IDbContextFactory<LibraryContext>>(root => new RentalsByCaller(new PooledDbContextFactory<LibraryContext>(PoolOptions(root)), seen));
        services.AddScopedFromPool<LibraryContext>();
        services.AddOutboxBackgroundService<LibraryContext>(TimeSpan.FromMinutes(1));

        _db.EnsureCreated(() => _db.CreateLibraryContext());
        rentals = seen;
        recorder = events;
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>
    /// The options of a pool a host makes by hand: built once, with the root provider, which is also the
    /// application's services as far as the contexts are concerned.
    /// </summary>
    private DbContextOptions<LibraryContext> PoolOptions(IServiceProvider root)
    {
        var options = new DbContextOptionsBuilder<LibraryContext>();
        options.UseSqlite(_db.Connection).UseApplicationServiceProvider(root).UseDDDToolkit(root);
        return options.Options;
    }

    /// <summary>One shelf saved through a context of the pool, so one row waits in the outbox.</summary>
    private static async Task QueueAsync(IServiceProvider provider)
    {
        await using var context = await provider.GetRequiredService<IDbContextFactory<LibraryContext>>().CreateDbContextAsync(Cancellation);
        context.Shelves.Add(NewShelf());
        await context.SaveChangesAsync(Cancellation);
    }

    // ---- Options that are not a pool's ---------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Singleton_options_with_scope_validation_are_refused_at_the_first_dispatch(bool throughFactory)
    {
        var recorder = new EventRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddDDDToolkitEntityFramework(options => options.DispatchInProcess((sp, events, ct) => sp.GetRequiredService<EventRecorder>().DispatchAsync(sp, events, ct)));

        // Both build their options once, with the root provider, and neither is a pool.
        if (throughFactory)
        {
            services.AddDbContextFactory<LibraryContext>((sp, options) => options.UseSqlite(_db.Connection).UseDDDToolkit(sp));
        }
        else
        {
            services.AddDbContext<LibraryContext>((sp, options) => options.UseSqlite(_db.Connection).UseDDDToolkit(sp), contextLifetime: ServiceLifetime.Scoped, optionsLifetime: ServiceLifetime.Singleton);
        }

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _db.EnsureCreated(() => _db.CreateLibraryContext());

        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LibraryContext>();
        context.IsPooled().Should().BeFalse();

        // Building the options resolved nothing scoped, and a save with nothing to dispatch needs no scope.
        context.People.Add(NewPerson());
        await context.SaveChangesAsync(Cancellation);

        var shelf = NewShelf();
        context.Shelves.Add(shelf);
        var save = () => context.SaveChangesAsync(Cancellation);

        var refusal = (await save.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("The options of 'LibraryContext' were built with the application's root services*" +
                         "Leave the options of AddDbContext scoped*AddPooledDbContextFactory with AddScopedFromPool*context.BindToScope(scope.ServiceProvider)*")
            .Which;
        refusal.InnerException.Should().BeOfType<InvalidOperationException>("the container's own refusal is kept").Which.Message.Should().Contain("from root provider");
        recorder.Events.Should().BeEmpty();
        _db.CountRows("Shelves").Should().Be(0);
        PendingOn(shelf).Should().ContainSingle();

        // Refused every time, not only the first.
        await save.Should().ThrowAsync<InvalidOperationException>();

        // Named a scope, the same context dispatches with it.
        IServiceProvider? handed = null;
        recorder.OnEvent = (given, _, _) =>
        {
            handed = given;
            return Task.CompletedTask;
        };
        context.BindToScope(scope.ServiceProvider);
        await context.SaveChangesAsync(Cancellation);

        handed.Should().BeSameAs(scope.ServiceProvider);
        _db.CountRows("Shelves").Should().Be(1);
    }

    [Fact]
    public async Task Singleton_options_without_scope_validation_dispatch_as_they_always_did()
    {
        var recorder = new EventRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddDDDToolkitEntityFramework(options => options.DispatchInProcess((sp, events, ct) => sp.GetRequiredService<EventRecorder>().DispatchAsync(sp, events, ct)));
        services.AddDbContext<LibraryContext>((sp, options) => options.UseSqlite(_db.Connection).UseDDDToolkit(sp), contextLifetime: ServiceLifetime.Scoped, optionsLifetime: ServiceLifetime.Singleton);

        // No validation: the container itself would hand out scoped services from its root, and so does the toolkit.
        using var provider = services.BuildServiceProvider();
        _db.EnsureCreated(() => _db.CreateLibraryContext());

        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LibraryContext>();
        context.Shelves.Add(NewShelf());
        await context.SaveChangesAsync(Cancellation);

        recorder.Events.Should().ContainSingle();
        _db.CountRows("Shelves").Should().Be(1);
    }

    // ---- The registration helper ------------------------------------------------------------------------

    [Fact]
    public void AddScopedFromPool_needs_a_pooled_registration()
    {
        // Nothing registered at all.
        var none = () => new ServiceCollection().AddScopedFromPool<LibraryContext>();
        none.Should().Throw<InvalidOperationException>()
            .WithMessage("AddScopedFromPool<LibraryContext>() found no context pool for LibraryContext. " +
                         "Call services.AddPooledDbContextFactory<LibraryContext>(...) or services.AddDbContextPool<LibraryContext>(...) first.");

        // AddDbContext: refused where it is registered.
        var plain = () => new ServiceCollection()
            .AddDbContext<LibraryContext>(options => options.UseSqlite(_db.Connection))
            .AddScopedFromPool<LibraryContext>();
        plain.Should().Throw<InvalidOperationException>()
            .WithMessage("LibraryContext does not come from a context pool. AddDbContext and AddDbContextFactory give each scope a context of its own already; " +
                         "AddScopedFromPool is for a context registered with AddPooledDbContextFactory or AddDbContextPool.");

        // AddDbContextFactory: Entity Framework registers its context the way it registers a pooled one, so it
        // is refused at the latest when a scope first asks for it.
        var factory = () =>
        {
            using var provider = new ServiceCollection()
                .AddDbContextFactory<LibraryContext>(options => options.UseSqlite(_db.Connection))
                .AddScopedFromPool<LibraryContext>()
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<LibraryContext>();
        };
        factory.Should().Throw<InvalidOperationException>().WithMessage("LibraryContext does not come from a context pool.*");

        // A factory a host registered itself that makes a new context each time is no pool either.
        var byHand = () =>
        {
            using var provider = new ServiceCollection()
                .AddSingleton<IDbContextFactory<LibraryContext>>(new UnpooledFactory(_db))
                .AddScopedFromPool<LibraryContext>()
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<LibraryContext>();
        };
        byHand.Should().Throw<InvalidOperationException>().WithMessage("LibraryContext does not come from a context pool.*");

        var nothing = () => PooledContexts.AddScopedFromPool<LibraryContext>(null!);
        nothing.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(PooledAs.Factory)]
    [InlineData(PooledAs.ScopedLease)]
    public void AddScopedFromPool_twice_registers_one_context(PooledAs pooledAs)
    {
        var services = new ServiceCollection();
        services.AddDDDToolkitEntityFramework();
        PooledTestHost.AddPool<LibraryContext>(services, _db, pooledAs);

        services.AddScopedFromPool<LibraryContext>();
        var once = services.ToList();
        services.AddScopedFromPool<LibraryContext>();

        services.Should().Equal(once, "the second call changes nothing");
        services.Count(descriptor => descriptor.ServiceType == typeof(LibraryContext)).Should().Be(1);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<LibraryContext>().Should().BeSameAs(scope.ServiceProvider.GetRequiredService<LibraryContext>(), "a scope has one context");
    }

    [Fact]
    public async Task AddScopedFromPool_over_a_factory_registered_by_hand_registers_the_scope_s_context()
    {
        var recorder = new EventRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddDDDToolkitEntityFramework(options => options.DispatchInProcess((sp, events, ct) => sp.GetRequiredService<EventRecorder>().DispatchAsync(sp, events, ct)));

        // Only a factory, which a host made itself: nothing registered the context.
        services.AddSingleton<IDbContextFactory<LibraryContext>>(root => new PooledDbContextFactory<LibraryContext>(PoolOptions(root)));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(LibraryContext));

        services.AddScopedFromPool<LibraryContext>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _db.EnsureCreated(() => _db.CreateLibraryContext());
        IServiceProvider? handed = null;
        recorder.OnEvent = (given, _, _) =>
        {
            handed = given;
            return Task.CompletedTask;
        };

        LibraryContext first;
        using (var scope = provider.CreateScope())
        {
            first = scope.ServiceProvider.GetRequiredService<LibraryContext>();
            first.IsPooled().Should().BeTrue();
            first.Shelves.Add(NewShelf());
            await first.SaveChangesAsync(Cancellation);

            handed.Should().BeSameAs(scope.ServiceProvider);
        }

        // The scope disposed it, which gave it back: the factory's next rental is the same instance.
        await using var again = await provider.GetRequiredService<IDbContextFactory<LibraryContext>>().CreateDbContextAsync(Cancellation);
        again.Should().BeSameAs(first);
    }

    [Fact]
    public void The_helpers_refuse_null()
    {
        using var context = _db.CreateLibraryContext();
        using var provider = new ServiceCollection().BuildServiceProvider();

        var noContext = () => PooledContexts.BindToScope<LibraryContext>(null!, provider);
        var noScope = () => context.BindToScope(null!);
        var pooled = () => PooledContexts.IsPooled(null!);

        noContext.Should().Throw<ArgumentNullException>();
        noScope.Should().Throw<ArgumentNullException>();
        pooled.Should().Throw<ArgumentNullException>();
        context.BindToScope(provider).Should().BeSameAs(context, "it returns the context, so it can be chained after CreateDbContext()");
    }

    /// <summary>A singleton interceptor a package registers and a host adds from the provider in its options callback.</summary>
    private sealed class PackageSaveCheck : SaveChangesInterceptor;

    /// <summary>Something only a scope may resolve.</summary>
    private sealed class ScopedThing;

    /// <summary>A factory that makes a new context for every caller, as a host might write one.</summary>
    private sealed class UnpooledFactory(SqliteDatabase database) : IDbContextFactory<LibraryContext>
    {
        public LibraryContext CreateDbContext() => database.CreateLibraryContext();
    }

    /// <summary>A factory over a pooled one that writes down who the caller was at each rental.</summary>
    private sealed class RentalsByCaller(IDbContextFactory<LibraryContext> pool, List<Caller?> rentals) : IDbContextFactory<LibraryContext>
    {
        public LibraryContext CreateDbContext()
        {
            lock (rentals)
            {
                rentals.Add(Callers.Ambient);
            }

            return pool.CreateDbContext();
        }
    }
}
