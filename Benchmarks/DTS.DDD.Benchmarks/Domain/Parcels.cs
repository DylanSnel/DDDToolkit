using DTS.DDD.Abstractions.Access;
using DTS.DDD.Abstractions.Attributes;
using DTS.DDD.Access;

namespace DTS.DDD.Benchmarks.Domain;

/// <summary>The id of a parcel.</summary>
[EntityId<Guid>]
public readonly partial record struct ParcelId;

/// <summary>The id of a courier, the user who carries a parcel.</summary>
[EntityId<Guid>]
public readonly partial record struct CourierId;

/// <summary>A parcel, carried by one courier or by nobody yet. The courier is the parcel's owner.</summary>
[AggregateRoot<ParcelId>]
public partial class Parcel
{
    /// <summary>A parcel with a label, carried by <paramref name="courier"/>.</summary>
    public Parcel(ParcelId id, string label, CourierId? courier) : base(id)
    {
        Label = label;
        Courier = courier;
    }

    /// <summary>What is written on the parcel.</summary>
    public string Label { get; private set; }

    /// <summary>Who carries the parcel, or nobody yet.</summary>
    [Owner]
    public CourierId? Courier { get; private set; }
}

/// <summary>The courier a caller is: the id its token names.</summary>
public sealed class CurrentCourier : IOwnedByCurrentUser<CourierId>
{
    /// <inheritdoc />
    public CourierId? Find(ICurrentUser user) => user.Id is { } id ? new CourierId(id) : null;
}

/// <summary>What every request about parcels implements. The generator writes what each request requires into it.</summary>
[AccessRequests]
public partial interface IParcelRequest : IRequireAccess;

/// <summary>Only the parcel's courier delivers it.</summary>
/// <param name="Parcel">The parcel.</param>
[OwnOnly]
public sealed record DeliverParcel([Own] ParcelId Parcel) : IParcelRequest;
