using DDDToolkit.HotChocolate.Tests.Harbor.Api.GraphQl;
using DDDToolkit.HotChocolate.Tests.Harbor.Domain;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Relay;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Tests.Harbor.Api;

/// <summary>What registers the harbor's schema: the one thing a host calls.</summary>
public static class HarborSchema
{
    /// <summary>
    /// The harbor's schema. <c>AddHarborGraphQlRuntimeBindings()</c> is generated into this project and is the
    /// only thing here that says how an id prints: every id it binds is declared in the domain project.
    /// </summary>
    public static IRequestExecutorBuilder AddHarborSchema(this IRequestExecutorBuilder builder)
        => builder
            .AddDDDToolkitTypes()
            .AddGlobalObjectIdentification()
            .AddHarborGraphQlRuntimeBindings()
            .AddType<VesselType>()
            .AddQueryType<HarborQueries>();
}

/// <summary>Fixed values, so a test can assert on an exact wire representation.</summary>
public static class HarborData
{
    public static readonly Guid VesselGuid = Guid.Parse("66666666-6666-6666-6666-666666666666");
    public static readonly Guid OtherVesselGuid = Guid.Parse("77777777-7777-7777-7777-777777777777");
    public static readonly Guid SkipperGuid = Guid.Parse("88888888-8888-8888-8888-888888888888");

    public const int Berth = 12;
    public const string Quay = "north-2";

    /// <summary>More than an <see cref="int"/> holds, so a <c>Long</c> is what carries it.</summary>
    public const long Voyage = 9_000_000_001;

    public const short Mooring = 3;
    public const string Sign = "TERN7";

    /// <summary>The one vessel the harbor knows.</summary>
    public static readonly Vessel Tern = new(
        VesselId.Create(VesselGuid),
        CallSign.Create(Sign),
        BerthNumber.Create(Berth),
        SkipperId.Create(SkipperGuid));
}

/// <summary>A vessel as the harbor answers it: a Relay node whose id is a <see cref="VesselId"/>.</summary>
public sealed record Vessel(VesselId Id, CallSign CallSign, BerthNumber Berth, SkipperId Skipper);

/// <summary>An input object holding an id of each shape: a struct id, a class id and a twin that validates.</summary>
public sealed class Docking
{
    /// <summary>Who brings the vessel in.</summary>
    public required SkipperId Skipper { get; set; }

    /// <summary>Where it is moored.</summary>
    public BerthNumber Berth { get; set; }

    /// <summary>The vessel's call sign, checked when the input is read.</summary>
    public required ValidCallSign CallSign { get; set; }
}

/// <summary>
/// The query root. Every field is here to pin down one way an id of the domain project crosses the schema: out as a
/// scalar, in as an argument or inside an input object, as a twin, and as a Relay node id.
/// </summary>
public sealed class HarborQueries
{
    /// <summary>A node, whose <c>id</c> is a node id and whose other fields are scalars.</summary>
    public Vessel GetVessel() => HarborData.Tern;

    /// <summary>A struct id over a Guid; binds to <c>UUID</c>.</summary>
    public VesselId GetVesselId() => HarborData.Tern.Id;

    /// <summary>The same id, nullable and absent.</summary>
    public VesselId? GetMissingVesselId() => null;

    /// <summary>A list of struct ids; binds to <c>[UUID!]!</c>.</summary>
    public List<VesselId> GetVesselIds() => [HarborData.Tern.Id, VesselId.Create(HarborData.OtherVesselGuid)];

    /// <summary>A class id; binds to <c>UUID</c> as well.</summary>
    public SkipperId GetSkipper() => HarborData.Tern.Skipper;

    /// <summary>The always-valid twin of the class id, bound to the same scalar.</summary>
    public ValidSkipperId GetValidSkipper() => HarborData.Tern.Skipper.ToValid();

    /// <summary>A field declared as the class id that answers its twin, as a field does that hands on a checked id.</summary>
    public SkipperId GetCheckedSkipper() => HarborData.Tern.Skipper.ToValid();

    /// <summary>An id over an int; binds to <c>Int</c>.</summary>
    public BerthNumber GetBerth() => HarborData.Tern.Berth;

    /// <summary>An id over a string; binds to <c>String</c>.</summary>
    public QuayCode GetQuay() => QuayCode.Create(HarborData.Quay);

    /// <summary>An id over a long; binds to <c>Long</c>.</summary>
    public VoyageNumber GetVoyage() => VoyageNumber.Create(HarborData.Voyage);

    /// <summary>An id over a short; binds to <c>Short</c>.</summary>
    public MooringNumber GetMooring() => MooringNumber.Create(HarborData.Mooring);

    /// <summary>A single value object; binds to <c>String</c>.</summary>
    public CallSign GetCallSign() => HarborData.Tern.CallSign;

    // ---------------------------------------------------------------- arguments

    /// <summary>Returns the id it was given, proving the id survives the round trip.</summary>
    public VesselId EchoVesselId(VesselId id) => id;

    /// <summary>
    /// Returns <c>ToString()</c> of the argument. A Guid or a string would come back without the <c>VSL_</c>
    /// prefix, so this shows the resolver really received a <see cref="VesselId"/>.
    /// </summary>
    public string DescribeVesselId(VesselId id) => id.ToString();

    /// <summary>Takes the twin, which is built through its validating constructor, and returns it.</summary>
    public ValidCallSign EchoCallSign(ValidCallSign callSign) => callSign;

    /// <summary>Returns what was read out of an input object.</summary>
    public string DescribeDocking(Docking docking)
        => $"{docking.Skipper}/{docking.Berth.Value}/{docking.CallSign.Value}";

    /// <summary>Takes a node id and returns the id inside it, in its <c>ToString()</c> form.</summary>
    public string DescribeVessel([ID<Vessel>] VesselId id) => id.ToString();
}

/// <summary>Makes <see cref="Vessel"/> a Relay node, with the identifier itself as the id.</summary>
public sealed class VesselType : ObjectType<Vessel>
{
    protected override void Configure(IObjectTypeDescriptor<Vessel> descriptor)
        => descriptor
            .ImplementsNode()
            .IdField(vessel => vessel.Id)
            .ResolveNode((_, id) => Task.FromResult<Vessel?>(id == HarborData.Tern.Id ? HarborData.Tern : null));
}
