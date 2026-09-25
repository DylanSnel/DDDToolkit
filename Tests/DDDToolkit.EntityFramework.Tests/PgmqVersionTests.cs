using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Messaging.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Which pgmq a database has, and the start-up check that reads it once and fails the start when the
/// extension is missing or too old for topics. Against two real servers: pgmq 1.5.1, what Supabase ships,
/// and 1.13.0, which has topic routing. Skipped without Docker, failed in CI, as the other pgmq tests.
/// </summary>
public sealed class PgmqVersionTests(PgmqVersionTests.Servers servers) : IClassFixture<PgmqVersionTests.Servers>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>pgmq 1.5.1, as on Supabase: named queues only.</summary>
    private PgmqDatabase Supabase => Require(servers.Supabase, PgmqDatabase.SupabaseImage);

    /// <summary>pgmq 1.13.0, with topic routing.</summary>
    private PgmqDatabase Topics => Require(servers.Topics, PgmqDatabase.Image);

    [Fact]
    public async Task The_installed_version_is_read_from_the_database()
    {
        await using var old = await Supabase.OpenAsync(Cancellation);
        await using var current = await Topics.OpenAsync(Cancellation);
        await using var none = new NpgsqlConnection(await servers.WithoutPgmqAsync(Cancellation));
        await none.OpenAsync(Cancellation);

        (await PgmqQueue.InstalledVersionAsync(old, cancellationToken: Cancellation)).Should().Be(new Version(1, 5, 1));
        (await PgmqQueue.InstalledVersionAsync(current, cancellationToken: Cancellation)).Should().Be(new Version(1, 13, 0));
        (await PgmqQueue.InstalledVersionAsync(none, cancellationToken: Cancellation)).Should().BeNull("a database without the extension has no version of it");
    }

    [Fact]
    public async Task Topics_on_a_pgmq_before_1_11_fail_the_start_naming_the_version_and_what_works_instead()
    {
        var queues = NpgsqlDataSource.Create(Supabase.ConnectionString);

        var start = () => StartAsync(services => services.AddPgmqSink(queues, pgmq => pgmq.UseTopics()));

        var failure = (await start.Should().ThrowAsync<PgmqTopicsNotSupportedException>()).Which;
        failure.InstalledVersion.Should().Be(new Version(1, 5, 1));
        failure.RequiredVersion.Should().Be(new Version(1, 11));
        failure.Message.Should()
            .Contain("is version 1.5.1")
            .And.Contain("need pgmq 1.11 or later")
            .And.Contain("Supabase ships pgmq 1.5.1")
            .And.Contain("UseQueue or UseQueues");
    }

    [Fact]
    public async Task Binding_topics_on_a_pgmq_before_1_11_fails_before_the_consumer_starts()
    {
        var queues = NpgsqlDataSource.Create(Supabase.ConnectionString);

        var start = () => StartAsync(services => services.AddPgmqConsumer(queues, "bind_on_old", consumer => consumer.BindTopics = true));

        (await start.Should().ThrowAsync<PgmqTopicsNotSupportedException>())
            .Which.InstalledVersion.Should().Be(new Version(1, 5, 1), "the check read the version; the consumer never got as far as pgmq.bind_topic");

        await using var connection = await Supabase.OpenAsync(Cancellation);
        await using var created = new NpgsqlCommand("SELECT count(*) FROM pgmq.list_queues() WHERE queue_name = 'bind_on_old'", connection);
        ((long)(await created.ExecuteScalarAsync(Cancellation))!).Should().Be(0, "StartingAsync runs before the consumer's StartAsync creates its queue");
    }

    [Fact]
    public async Task Named_queues_start_on_the_pgmq_Supabase_ships()
    {
        var queues = NpgsqlDataSource.Create(Supabase.ConnectionString);

        using var host = await StartAsync(services =>
        {
            services.AddPgmqSink(queues, pgmq => pgmq.UseQueue("named_on_old"));
            services.AddPgmqConsumer(queues, "named_on_old");
        });

        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task Topics_start_on_a_pgmq_that_has_them()
    {
        var queues = NpgsqlDataSource.Create(Topics.ConnectionString);

        using var host = await StartAsync(services =>
        {
            services.AddPgmqSink(queues, pgmq => pgmq.UseTopics());
            services.AddPgmqConsumer(queues, "topics_on_new", consumer => consumer.BindTopics = true);
        });

        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task A_database_without_pgmq_fails_the_start_by_name()
    {
        var queues = NpgsqlDataSource.Create(await servers.WithoutPgmqAsync(Cancellation));

        var start = () => StartAsync(services => services.AddPgmqSink(queues, pgmq => pgmq.UseQueue("anything")));

        (await start.Should().ThrowAsync<PgmqNotInstalledException>())
            .Which.Database.Should().Be(Servers.WithoutPgmq);
    }

    [Fact]
    public async Task The_check_can_be_turned_off()
    {
        var queues = NpgsqlDataSource.Create(await servers.WithoutPgmqAsync(Cancellation));

        using var host = await StartAsync(services => services.AddPgmqSink(queues, pgmq =>
        {
            pgmq.UseQueue("anything");
            pgmq.CheckExtensionOnStart = false;
        }));

        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task A_sink_on_a_context_is_checked_on_the_context_connection()
    {
        var database = Supabase;

        var start = () => StartAsync(services =>
        {
            services.AddDbContext<PgmqContext>(options => options.UseNpgsql(database.ConnectionString));
            services.AddPgmqSink<PgmqContext>(pgmq => pgmq.UseTopics());
        });

        (await start.Should().ThrowAsync<PgmqTopicsNotSupportedException>()).Which.Database.Should().Be("ddd");
    }

    [Fact]
    public void A_sink_and_a_consumer_share_one_check()
    {
        var services = new ServiceCollection();
        var queues = NpgsqlDataSource.Create("Host=localhost;Database=shop");

        services.AddPgmqSink(queues, pgmq => pgmq.UseTopics());
        services.AddPgmqConsumer(queues, "storefront", consumer => consumer.BindTopics = true);
        services.AddPgmqConsumer(queues, "audit");

        services.Where(service => service.ServiceType == typeof(IHostedService) && service.ImplementationType?.Name == "PgmqStartupCheck")
            .Should().ContainSingle("the check groups its requirements by database and reads each one once");
    }

    [Fact]
    public async Task A_topic_function_called_on_a_pgmq_before_1_11_says_what_is_missing()
    {
        await using var connection = await Supabase.OpenAsync(Cancellation);

        var send = () => PgmqQueue.SendTopicAsync(connection, null, "library.shelf-opened", """{"x":1}""", cancellationToken: Cancellation);
        var ensure = () => PgmqQueue.EnsureTopicRoutingAsync(connection, cancellationToken: Cancellation);

        (await send.Should().ThrowAsync<PgmqTopicsNotSupportedException>())
            .Which.InstalledVersion.Should().BeNull("the failing call aborted anything the version could have been read in");
        (await ensure.Should().ThrowAsync<PgmqTopicsNotSupportedException>())
            .Which.InstalledVersion.Should().Be(new Version(1, 5, 1));

        await using var current = await Topics.OpenAsync(Cancellation);
        await PgmqQueue.EnsureTopicRoutingAsync(current, cancellationToken: Cancellation);
    }

    // ------------------------------------------------------------------ the process under test

    /// <summary>Builds a host with nothing in it but <paramref name="configure"/> and starts it.</summary>
    private static async Task<IHost> StartAsync(Action<IServiceCollection> configure)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        configure(builder.Services);

        var host = builder.Build();
        try
        {
            await host.StartAsync(Cancellation);
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    private static PgmqDatabase Require(PgmqDatabase? database, string image)
    {
        RequiredContainers.EnforceOrSkip(
            available: database is not null,
            RequiredContainers.Required,
            "pgmq on PostgreSQL",
            $"No Docker here, so '{image}' could not be started. The pgmq version check is not covered on this machine.");

        return database!;
    }

    /// <summary>Both servers, started side by side, once for the class.</summary>
    public sealed class Servers : IAsyncLifetime
    {
        /// <summary>The database on the 1.5.1 server that has no pgmq at all.</summary>
        public const string WithoutPgmq = "no_pgmq_here";

        private string? _withoutPgmq;

        public PgmqDatabase? Supabase { get; private set; }

        public PgmqDatabase? Topics { get; private set; }

        public async ValueTask InitializeAsync()
        {
            var supabase = PgmqDatabase.StartAsync(PgmqDatabase.SupabaseImage, TestContext.Current.CancellationToken);
            var topics = PgmqDatabase.StartAsync(PgmqDatabase.Image, TestContext.Current.CancellationToken);

            Supabase = await supabase;
            Topics = await topics;
        }

        /// <summary>A database without the extension, on the 1.5.1 server, made the first time it is asked for.</summary>
        public async Task<string> WithoutPgmqAsync(CancellationToken cancellationToken)
            => _withoutPgmq ??= await Require(Supabase, PgmqDatabase.SupabaseImage).CreateDatabaseWithoutPgmqAsync(WithoutPgmq, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (Supabase is not null)
            {
                await Supabase.DisposeAsync();
            }

            if (Topics is not null)
            {
                await Topics.DisposeAsync();
            }
        }
    }
}
