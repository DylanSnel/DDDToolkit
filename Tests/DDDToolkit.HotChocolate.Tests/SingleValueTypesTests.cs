using System.Diagnostics.CodeAnalysis;
using System.Text;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Exceptions;
using DDDToolkit.HotChocolate.Tests.Domain;
using DDDToolkit.HotChocolate.Tests.Harbor.Api;
using DDDToolkit.HotChocolate.Tests.Harbor.Domain;
using DDDToolkit.HotChocolate.Types;
using FluentAssertions;
using HotChocolate.Execution;
using HotChocolate.Types.Relay;
using HotChocolate.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// <c>SingleValueChangeTypeProvider&lt;T, TValue&gt;</c> and <c>SingleValueNodeIdSerializer&lt;T, TValue&gt;</c> on their
/// own: the one provider and the one serializer that work for any generated id, single value object or twin, through
/// <c>ISingleValue</c>. They are what the generated bindings register for the ids of a project that does not
/// reference HotChocolate, and they have to do what the classes nested in an id that binds itself do.
/// </summary>
public class SingleValueTypesTests
{
    // ------------------------------------------------------------------ the provider

    [Fact]
    public void The_provider_converts_a_struct_id_both_ways()
    {
        var provider = new SingleValueChangeTypeProvider<VesselId, Guid>();
        var id = VesselId.Create(HarborData.VesselGuid);

        provider.TryCreateConverter(typeof(VesselId), typeof(Guid), Root, out var toValue).Should().BeTrue();
        toValue!(id).Should().Be(HarborData.VesselGuid);

        provider.TryCreateConverter(typeof(Guid), typeof(VesselId), Root, out var fromValue).Should().BeTrue();
        fromValue!(HarborData.VesselGuid).Should().Be(id);
    }

    [Fact]
    public void The_provider_converts_a_class_id_and_a_provider_of_its_own_converts_the_twin()
    {
        var id = SkipperId.Create(HarborData.SkipperGuid);

        var plain = new SingleValueChangeTypeProvider<SkipperId, Guid>();
        plain.TryCreateConverter(typeof(SkipperId), typeof(Guid), Root, out var toValue).Should().BeTrue();
        toValue!(id).Should().Be(HarborData.SkipperGuid);
        plain.TryCreateConverter(typeof(Guid), typeof(SkipperId), Root, out var fromValue).Should().BeTrue();
        fromValue!(HarborData.SkipperGuid).Should().BeOfType<SkipperId>().And.Be(id);

        // Unlike the provider nested in an id, one generic provider is one pair of types: the twin has its own.
        plain.TryCreateConverter(typeof(ValidSkipperId), typeof(Guid), Root, out _).Should().BeFalse();

        var twin = new SingleValueChangeTypeProvider<ValidSkipperId, Guid>();
        twin.TryCreateConverter(typeof(ValidSkipperId), typeof(Guid), Root, out var twinToValue).Should().BeTrue();
        twinToValue!(id.ToValid()).Should().Be(HarborData.SkipperGuid);
        twin.TryCreateConverter(typeof(Guid), typeof(ValidSkipperId), Root, out var twinFromValue).Should().BeTrue();
        twinFromValue!(HarborData.SkipperGuid).Should().BeOfType<ValidSkipperId>().And.Be(id.ToValid());
    }

    [Fact]
    public void The_provider_reads_the_plain_type_unchecked_and_the_twin_through_its_validating_constructor()
    {
        // As the nested provider does: the plain type is rebuilt as it was sent, and says so when asked; the twin
        // cannot exist invalid.
        new SingleValueChangeTypeProvider<CallSign, string>()
            .TryCreateConverter(typeof(string), typeof(CallSign), Root, out var plain).Should().BeTrue();
        plain!("no").Should().BeOfType<CallSign>().Which.IsValid.Should().BeFalse();

        new SingleValueChangeTypeProvider<ValidCallSign, string>()
            .TryCreateConverter(typeof(string), typeof(ValidCallSign), Root, out var twin).Should().BeTrue();
        twin!(HarborData.Sign).Should().Be(CallSign.Create(HarborData.Sign).ToValid());
        FluentActions.Invoking(() => twin!("no")).Should().Throw<InvalidValueObjectException>();
    }

    [Fact]
    public void The_provider_declines_pairs_it_does_not_know()
    {
        var provider = new SingleValueChangeTypeProvider<VesselId, Guid>();

        provider.TryCreateConverter(typeof(VesselId), typeof(string), Root, out var toText).Should().BeFalse();
        toText.Should().BeNull();
        provider.TryCreateConverter(typeof(Guid), typeof(SkipperId), Root, out var anotherId).Should().BeFalse();
        anotherId.Should().BeNull();
    }

    [Fact]
    public void The_provider_converts_exactly_as_the_provider_nested_in_an_id_does()
    {
        // TicketId is declared in this assembly, which references HotChocolate, so it has both.
        var nested = new TicketId.ChangeTypeProvider();
        var generic = new SingleValueChangeTypeProvider<TicketId, Guid>();

        nested.TryCreateConverter(typeof(TicketId), typeof(Guid), Root, out var nestedToValue).Should().BeTrue();
        generic.TryCreateConverter(typeof(TicketId), typeof(Guid), Root, out var genericToValue).Should().BeTrue();
        genericToValue!(TestData.Ticket).Should().Be(nestedToValue!(TestData.Ticket));

        nested.TryCreateConverter(typeof(Guid), typeof(TicketId), Root, out var nestedFromValue).Should().BeTrue();
        generic.TryCreateConverter(typeof(Guid), typeof(TicketId), Root, out var genericFromValue).Should().BeTrue();
        genericFromValue!(TestData.TicketGuid).Should().Be(nestedFromValue!(TestData.TicketGuid));
    }

    // ------------------------------------------------------------------ the node id serializer

    [Fact]
    public void The_serializer_reads_back_an_id_over_each_value_a_node_id_can_carry()
    {
        RoundTrip<VesselId, Guid>(VesselId.Create(HarborData.VesselGuid));
        RoundTrip<SkipperId, Guid>(SkipperId.Create(HarborData.SkipperGuid));
        RoundTrip<QuayCode, string>(QuayCode.Create(HarborData.Quay)).Should().Be(HarborData.Quay);
        RoundTrip<BerthNumber, int>(BerthNumber.Create(HarborData.Berth)).Should().Be("12");
        RoundTrip<VoyageNumber, long>(VoyageNumber.Create(HarborData.Voyage)).Should().Be("9000000001");
        RoundTrip<MooringNumber, short>(MooringNumber.Create(HarborData.Mooring)).Should().Be("3");
    }

    [Fact]
    public void The_serializer_writes_what_the_serializer_nested_in_an_id_writes()
    {
        // Ids of this assembly and of the example library, which have a nested serializer to compare with.
        Written(new SingleValueNodeIdSerializer<TicketId, Guid>(), TestData.Ticket)
            .Should().Equal(Written(new TicketId.NodeIdValueSerializer(), TestData.Ticket));
        Written(new SingleValueNodeIdSerializer<PersonId, Guid>(), TestData.Person)
            .Should().Equal(Written(new PersonId.NodeIdValueSerializer(), TestData.Person));
        Written(new SingleValueNodeIdSerializer<LoginId, string>(), TestData.Login)
            .Should().Equal(Written(new LoginId.NodeIdValueSerializer(), TestData.Login));
        Written(new SingleValueNodeIdSerializer<SeatNumber, int>(), TestData.Seat)
            .Should().Equal(Written(new SeatNumber.NodeIdValueSerializer(), TestData.Seat));
    }

    [Fact]
    public void The_serializer_says_a_buffer_is_too_small_rather_than_writing_past_it()
    {
        INodeIdValueSerializer serializer = new SingleValueNodeIdSerializer<QuayCode, string>();

        serializer.Format(new byte[2], QuayCode.Create(HarborData.Quay), out _).Should().Be(NodeIdFormatterResult.BufferTooSmall);
    }

    [Fact]
    public void The_serializer_does_not_read_a_value_of_another_shape()
    {
        INodeIdValueSerializer serializer = new SingleValueNodeIdSerializer<BerthNumber, int>();

        serializer.TryParse("north-2"u8, out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void The_serializer_is_for_its_own_type_only()
    {
        INodeIdValueSerializer serializer = new SingleValueNodeIdSerializer<VesselId, Guid>();

        serializer.IsSupported(typeof(VesselId)).Should().BeTrue();
        serializer.IsSupported(typeof(SkipperId)).Should().BeFalse();
        serializer.IsSupported(typeof(Guid)).Should().BeFalse();
    }

    [Fact]
    public void A_value_no_node_id_can_carry_is_refused_when_the_serializer_is_made()
    {
        // A date of birth is a single value over a DateOnly, which HotChocolate has no node id format for. The
        // generated bindings never register a serializer for one; made by hand, it fails at once and says why.
        FluentActions.Invoking(() => new SingleValueNodeIdSerializer<DateOfBirth, DateOnly>())
            .Should().Throw<NotSupportedException>()
            .WithMessage("*DateOnly*DateOfBirth*");
    }

    [Fact]
    public async Task A_serializer_registered_for_such_a_value_fails_the_schema_and_not_a_request()
    {
        // HotChocolate makes every registered serializer when it creates the schema, so the mistake stops the
        // application from starting instead of failing the first request that writes a node id.
        var failure = await Record.ExceptionAsync(async () => await new ServiceCollection()
            .AddGraphQL()
            .AddHarborSchema()
            .AddNodeIdValueSerializer<SingleValueNodeIdSerializer<DateOfBirth, DateOnly>>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken));

        var causes = new List<Exception>();
        for (var cause = failure; cause is not null; cause = cause.InnerException)
        {
            causes.Add(cause);
        }

        causes.OfType<NotSupportedException>().Should().ContainSingle()
            .Which.Message.Should().Contain("DateOnly").And.Contain("DateOfBirth");
    }

    /// <summary>Writes the id, reads it back, and returns what was written as text (a Guid is written as its bytes).</summary>
    private static string RoundTrip<T, TValue>(T id)
        where T : DDDToolkit.Interfaces.ISingleValue<T, TValue>
    {
        INodeIdValueSerializer serializer = new SingleValueNodeIdSerializer<T, TValue>();
        var written = Written(serializer, id);

        serializer.TryParse(written, out var read).Should().BeTrue();
        read.Should().BeOfType<T>().And.Be(id);

        return Encoding.UTF8.GetString(written);
    }

    private static byte[] Written(INodeIdValueSerializer serializer, object id)
    {
        Span<byte> buffer = stackalloc byte[128];
        serializer.Format(buffer, id, out var written).Should().Be(NodeIdFormatterResult.Success);
        return buffer[..written].ToArray();
    }

    /// <summary>The root provider HotChocolate passes in; these converters never delegate to it.</summary>
    private static bool Root(Type source, Type target, [NotNullWhen(true)] out ChangeType? converter)
    {
        converter = null;
        return false;
    }
}
