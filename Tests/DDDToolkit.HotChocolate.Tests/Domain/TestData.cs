using DDDToolkit.ExampleLibrary.Common.ValueObjects;

namespace DDDToolkit.HotChocolate.Tests.Domain;

/// <summary>Fixed values so a test can assert on an exact wire representation.</summary>
internal static class TestData
{
    public static readonly Guid CatGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OtherCatGuid = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid PersonGuid = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid TicketGuid = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid EventGuid = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly DateTimeOffset OccurredAt = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    public const string HolderEmail = "ada@example.com";
    public const int SeatValue = 42;

    public static CatId Cat => CatId.Create(CatGuid);

    public static CatId OtherCat => CatId.Create(OtherCatGuid);

    public static PersonId Person => PersonId.Create(PersonGuid);

    public static TicketId Ticket => TicketId.Create(TicketGuid);

    public static SeatNumber Seat => SeatNumber.Create(SeatValue);

    public static LoginId Login => LoginId.Create(HolderEmail);

    public static EmailAddress Holder => EmailAddress.Create(HolderEmail);
}
