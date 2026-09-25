using System.Text;
using DDDToolkit.BaseTypes;
using DDDToolkit.Messaging.Postgres;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The pgmq sink and consumer read from a configuration section: every key they know, and a clear failure
/// for a key they do not or a value that does not parse. No database: the options are the whole story,
/// except for <c>Queues</c>, which <see cref="PgmqSinkTests"/> sends through.
/// </summary>
public sealed class PgmqConfigurationTests
{
    private static IConfigurationSection Section(string json, string path)
        => new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build().GetSection(path);

    private static IntegrationEventMessage Message() => new()
    {
        MessageId = Guid.CreateVersion7(),
        Name = "library.shelf-opened",
        Version = 1,
        Payload = "{}",
        OccurredAt = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void Every_consumer_option_is_read_from_its_section()
    {
        var section = Section(
            """
            { "Pgmq": { "Consumer": {
                "VisibilityTimeout": "00:01:00", "BatchSize": 25, "LongPollTimeout": "00:00:10",
                "LongPollInterval": "00:00:00.250", "PollingInterval": "00:00:02", "MaxDeliveries": 3,
                "BindTopics": true, "CheckExtensionOnStart": false } } }
            """,
            "Pgmq:Consumer");

        var options = new PgmqConsumerOptions().ReadFrom(section);

        options.VisibilityTimeout.Should().Be(TimeSpan.FromMinutes(1));
        options.BatchSize.Should().Be(25);
        options.LongPollTimeout.Should().Be(TimeSpan.FromSeconds(10));
        options.LongPollInterval.Should().Be(TimeSpan.FromMilliseconds(250));
        options.PollingInterval.Should().Be(TimeSpan.FromSeconds(2));
        options.MaxDeliveries.Should().Be(3);
        options.BindTopics.Should().BeTrue();
        options.CheckExtensionOnStart.Should().BeFalse();
    }

    [Fact]
    public void Keys_match_in_any_case_and_what_is_not_there_keeps_its_default()
    {
        var options = new PgmqConsumerOptions().ReadFrom(Section("""{ "consumer": { "longpolltimeout": "00:00:00" } }""", "Consumer"));

        options.LongPollTimeout.Should().Be(TimeSpan.Zero, "that turns long polling off");
        options.BatchSize.Should().Be(10);
        options.CheckExtensionOnStart.Should().BeTrue();
    }

    [Fact]
    public void A_key_the_options_do_not_know_fails_by_name_rather_than_being_ignored()
    {
        var read = () => new PgmqConsumerOptions().ReadFrom(Section("""{ "Pgmq": { "Consumer": { "LongPolTimeout": "00:00:10" } } }""", "Pgmq:Consumer"));

        read.Should().Throw<InvalidOperationException>()
            .WithMessage("'Pgmq:Consumer:LongPolTimeout' is not a setting of PgmqConsumerOptions*")
            .WithMessage("*LongPollTimeout*");
    }

    [Fact]
    public void A_value_that_does_not_parse_names_its_key()
    {
        var read = () => new PgmqConsumerOptions().ReadFrom(Section("""{ "Pgmq": { "Consumer": { "BatchSize": "many" } } }""", "Pgmq:Consumer"));

        read.Should().Throw<InvalidOperationException>().WithMessage("'Pgmq:Consumer:BatchSize' is 'many', which is not a whole number.");
    }

    [Fact]
    public void The_sink_reads_its_queue_and_switches()
    {
        var options = new PgmqSinkOptions().ReadFrom(Section(
            """{ "Sink": { "Queue": "shop", "CreateQueueIfMissing": false, "SendHeaders": false, "CheckExtensionOnStart": false } }""",
            "Sink"));

        options.QueueName(Message()).Should().Be("shop");
        options.Topics.Should().BeFalse();
        options.CreateQueueIfMissing.Should().BeFalse();
        options.SendHeaders.Should().BeFalse();
        options.CheckExtensionOnStart.Should().BeFalse();
    }

    [Fact]
    public void The_sink_can_route_by_topic_from_configuration()
    {
        new PgmqSinkOptions().ReadFrom(Section("""{ "Sink": { "Topics": true } }""", "Sink")).Topics.Should().BeTrue();
        new PgmqSinkOptions().ReadFrom(Section("""{ "Sink": { "Topics": false } }""", "Sink")).Topics.Should().BeFalse();
    }

    [Fact]
    public void A_section_that_routes_two_ways_is_refused()
    {
        var read = () => new PgmqSinkOptions().ReadFrom(Section("""{ "Pgmq": { "Sink": { "Queue": "shop", "Topics": true } } }""", "Pgmq:Sink"));

        read.Should().Throw<InvalidOperationException>().WithMessage("'Pgmq:Sink' routes messages more than one way (Queue, Topics)*");
    }

    [Fact]
    public void The_last_routing_call_wins()
    {
        var queue = new PgmqSinkOptions().UseTopics().UseQueue("shop");
        var topics = new PgmqSinkOptions().UseQueue("shop").UseTopics();

        queue.Topics.Should().BeFalse();
        queue.QueueName(Message()).Should().Be("shop");
        topics.Topics.Should().BeTrue();
    }

    [Fact]
    public void Code_has_the_last_word_over_the_section()
    {
        var services = new ServiceCollection();
        var queues = NpgsqlDataSource.Create("Host=localhost;Database=shop");

        services.AddPgmqSink(queues, Section("""{ "Sink": { "Topics": true } }""", "Sink"), pgmq => pgmq.UseQueue("shop"));

        var options = services.BuildServiceProvider().GetRequiredService<PgmqSinkOptions>();
        options.Topics.Should().BeFalse("the code after the section asked for a named queue");
        options.QueueName(Message()).Should().Be("shop");
    }

    [Fact]
    public void A_consumer_section_can_turn_the_start_up_check_off()
    {
        var services = new ServiceCollection();
        var queues = NpgsqlDataSource.Create("Host=localhost;Database=shop");

        services.AddPgmqConsumer(queues, "storefront", Section("""{ "Consumer": { "CheckExtensionOnStart": false } }""", "Consumer"));

        services.Where(service => service.ServiceType == typeof(IHostedService))
            .Should().ContainSingle("the consumer is there and the check is not");
    }
}
