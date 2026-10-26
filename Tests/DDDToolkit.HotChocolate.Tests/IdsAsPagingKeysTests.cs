using System.Reflection;
using System.Text;
using DDDToolkit.HotChocolate.Paging;
using DDDToolkit.HotChocolate.Tests.Domain;
using DDDToolkit.HotChocolate.Tests.GraphQl;
using DDDToolkit.HotChocolate.Tests.Harbor.Api.GraphQl;
using DDDToolkit.HotChocolate.Tests.Harbor.Domain;
using DDDToolkit.Interfaces;
using FluentAssertions;
using GreenDonut.Data;
using GreenDonut.Data.Cursors;
using GreenDonut.Data.Cursors.Serializers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// A generated struct id as a key HotChocolate's paging orders a list by: <c>OrderBy(berthing =&gt; berthing.Id)</c>
/// in front of <c>ToPageAsync</c>, over Entity Framework. The generated
/// <c>Add{Module}GraphQlRuntimeBindings()</c> registers <see cref="SingleValueCursorKeySerializer{T, TValue}"/> for
/// every struct id it binds; without it HotChocolate answers "The key type is not supported".
/// </summary>
public sealed class IdsAsPagingKeysTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly List<string> _statements = [];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        // What a host does when it starts: it gives its schema the bindings generated for its modules. The harbor's
        // ids are declared in a project without HotChocolate; the others in this one.
        new ServiceCollection()
            .AddGraphQL()
            .AddHarborGraphQlRuntimeBindings()
            .AddGraphQlTestsGraphQlRuntimeBindings();

        await _connection.OpenAsync(Cancellation);

        await using var wharf = Wharf();
        await wharf.Database.EnsureCreatedAsync(Cancellation);
        wharf.Berthings.AddRange(Seeded());
        await wharf.SaveChangesAsync(Cancellation);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task A_list_ordered_by_an_id_pages_forward_and_backward_in_the_databases_own_order()
    {
        await using var wharf = Wharf();
        var expected = await wharf.Berthings.OrderBy(berthing => berthing.Id).Select(berthing => berthing.Name).ToListAsync(Cancellation);

        var forward = await WalkAsync(() => wharf.Berthings.OrderBy(berthing => berthing.Id), size: 4);
        var backward = await WalkAsync(() => wharf.Berthings.OrderBy(berthing => berthing.Id), size: 4, backward: true);

        forward.Pages.Should().Be(3, "eleven berthings in pages of four");
        forward.Names.Should().Equal(expected);
        backward.Names.Should().Equal(expected);
    }

    [Fact]
    public async Task An_id_breaks_the_ties_of_the_key_in_front_of_it()
    {
        // Three days for eleven berthings: the day alone does not order them, and no berthing may be skipped or
        // answered twice where a page ends between two of one day.
        await using var wharf = Wharf();
        IQueryable<Berthing> Ordered() => wharf.Berthings.OrderByDescending(berthing => berthing.On).ThenBy(berthing => berthing.Id);
        var expected = await Ordered().Select(berthing => berthing.Name).ToListAsync(Cancellation);

        (await WalkAsync(Ordered, size: 2)).Names.Should().Equal(expected);
        (await WalkAsync(Ordered, size: 2, backward: true)).Names.Should().Equal(expected);
    }

    [Fact]
    public async Task An_id_over_an_int_orders_a_list_as_one_over_a_guid_does()
    {
        await using var wharf = Wharf();
        var expected = await wharf.Berthings.OrderBy(berthing => berthing.Berth).Select(berthing => berthing.Name).ToListAsync(Cancellation);

        (await WalkAsync(() => wharf.Berthings.OrderBy(berthing => berthing.Berth), size: 3)).Names.Should().Equal(expected);
        expected.Should().StartWith(["Berth 1", "Berth 2", "Berth 3"], "the berths are in the order of their numbers, not of their text");
    }

    [Fact]
    public async Task The_page_after_a_cursor_compares_the_column_itself()
    {
        await using var wharf = Wharf();
        var first = await wharf.Berthings.OrderBy(berthing => berthing.Id).ToPageAsync(new PagingArguments(first: 2), Cancellation);

        _statements.Clear();
        await wharf.Berthings.OrderBy(berthing => berthing.Id)
            .ToPageAsync(new PagingArguments(first: 2, after: first.CreateCursor(first.Last!.Value)), Cancellation);

        // The id's own CompareTo is translated to a comparison of the column: no cast, so an index on it serves.
        var statement = _statements.Should().ContainSingle().Which;
        statement.Should().MatchRegex("""WHERE "b"\."Id" > @\w+\s+ORDER BY "b"\."Id"\s""").And.NotContain("CAST");
    }

    [Fact]
    public async Task The_cursor_of_an_id_is_the_cursor_of_its_value()
    {
        // Ordering by the id's value through its own operator needs no registration, and is how a list was paged
        // by an id before. Its cursors and the id's are the same text, so one continues where the other stopped.
        await using var wharf = Wharf();
        var byId = await wharf.Berthings.OrderBy(berthing => berthing.Id).ToPageAsync(new PagingArguments(first: 3), Cancellation);
        var byValue = await wharf.Berthings.OrderBy(berthing => (Guid)berthing.Id).ToPageAsync(new PagingArguments(first: 3), Cancellation);

        var cursor = byId.CreateCursor(byId.Last!.Value);
        cursor.Should().Be(byValue.CreateCursor(byValue.Last!.Value));
        Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Should().Be("{}" + byId.Last!.Value.Item.Id.Value.ToString("D"));

        var next = await wharf.Berthings.OrderBy(berthing => (Guid)berthing.Id).ToPageAsync(new PagingArguments(first: 3, after: cursor), Cancellation);
        var expected = await wharf.Berthings.OrderBy(berthing => berthing.Id).Skip(3).Take(3).Select(berthing => berthing.Name).ToListAsync(Cancellation);
        next.Select(berthing => berthing.Name).Should().Equal(expected);
    }

    [Fact]
    public async Task A_cursor_that_holds_no_id_is_refused_as_HotChocolate_refuses_one_for_the_value()
    {
        await using var wharf = Wharf();
        var notAnId = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}not-an-id"));

        var paging = async () => await wharf.Berthings.OrderBy(berthing => berthing.Id).ToPageAsync(new PagingArguments(first: 2, after: notAnId), Cancellation);

        // HotChocolate's own exception for a key it cannot read: what an application translates, if it wants a
        // refusal of its own for a cursor that is not this list's.
        (await paging.Should().ThrowAsync<FormatException>()).WithMessage("The cursor value is not a valid guid.");
    }

    [Fact]
    public void Every_struct_id_the_generated_bindings_bind_is_a_paging_key()
    {
        Type[] registered =
        [
            typeof(VesselId), typeof(BerthNumber), typeof(QuayCode), typeof(VoyageNumber), typeof(MooringNumber),
            typeof(TicketId), typeof(SeatNumber), typeof(LoginId),
        ];

        foreach (var id in registered)
        {
            CursorKeySerializerRegistration.Find(id).IsSupported(id).Should().BeTrue("{0} is a struct id", id.Name);
        }

        // A key that may be null is the same key.
        CursorKeySerializerRegistration.Find(typeof(VesselId?)).Should().BeOfType<SingleValueCursorKeySerializer<VesselId, Guid>>();

        // A class id does not compare to itself, and no cursor carries a GridCell: neither is registered, and
        // HotChocolate says so when a list is ordered by one.
        foreach (var id in (Type[])[typeof(SkipperId), typeof(PegId)])
        {
            FluentActions.Invoking(() => CursorKeySerializerRegistration.Find(id))
                .Should().Throw<NotSupportedException>().WithMessage("The key type `*` is not supported.");
        }
    }

    [Fact]
    public void An_id_is_registered_once_however_often_its_bindings_are_added()
    {
        // Every source schema of a modular application adds the bindings of the ids it names, so the same id is
        // asked for many times; HotChocolate's list is for the whole process and would only grow.
        for (var again = 0; again < 3; again++)
        {
            new ServiceCollection().AddGraphQL().AddHarborGraphQlRuntimeBindings();
            SingleValueCursorKeySerializer<VesselId, Guid>.Register();
        }

        RegisteredFor(typeof(VesselId)).Should().Be(1);
    }

    [Fact]
    public void An_id_over_a_value_no_cursor_carries_is_refused_when_it_is_registered()
    {
        var registering = SingleValueCursorKeySerializer<PegId, GridCell>.Register;

        // At registration, which is when a host starts, and every time: not at the first page a request asks for.
        registering.Should().Throw<NotSupportedException>().WithMessage("*GridCell*");
        registering.Should().Throw<NotSupportedException>();
        RegisteredFor(typeof(PegId)).Should().Be(0);
    }

    [Fact]
    public void A_key_is_written_as_HotChocolate_writes_its_value_and_read_back_as_the_id()
    {
        RoundTrips<VesselId, Guid>(VesselId.Create(Guid.Parse("0b0e7a52-3c0d-4a51-9a0e-6f4c2f1d9e11")));
        RoundTrips<BerthNumber, int>(BerthNumber.Create(12));
        RoundTrips<QuayCode, string>(QuayCode.Create("north-2"));
        RoundTrips<VoyageNumber, long>(VoyageNumber.Create(9_000_000_001));
        RoundTrips<MooringNumber, short>(MooringNumber.Create(3));
    }

    private static void RoundTrips<T, TValue>(T id)
        where T : ISingleValue<T, TValue>, IComparable<T>
    {
        var serializer = new SingleValueCursorKeySerializer<T, TValue>();

        Span<byte> written = stackalloc byte[64];
        serializer.TryFormat(id, written, out var length).Should().BeTrue();

        Span<byte> expected = stackalloc byte[64];
        CursorKeySerializerRegistration.Find(typeof(TValue)).TryFormat(id.Value!, expected, out var expectedLength).Should().BeTrue();
        Encoding.UTF8.GetString(written[..length]).Should().Be(Encoding.UTF8.GetString(expected[..expectedLength]));

        serializer.Parse(written[..length]).Should().Be(id);
        serializer.TryFormat(id, stackalloc byte[1], out _).Should().Be(typeof(TValue) == typeof(short), "a buffer that is too small is reported, not overrun");

        serializer.IsSupported(typeof(T)).Should().BeTrue();
        serializer.IsSupported(typeof(TValue)).Should().BeFalse("the value keeps HotChocolate's own serializer");
        serializer.GetCompareToMethod(typeof(T)).Should().BeSameAs(typeof(T).GetMethod(nameof(IComparable<T>.CompareTo), [typeof(T)]));
    }

    /// <summary>
    /// How many of HotChocolate's registered serializers answer for a type. The list is private; this reads it, as
    /// nothing public counts it.
    /// </summary>
    private static int RegisteredFor(Type type)
    {
        var list = typeof(CursorKeySerializerRegistration).GetField("s_serializers", BindingFlags.Static | BindingFlags.NonPublic);
        list.Should().NotBeNull("HotChocolate keeps its cursor key serializers in the private list this test counts");

        return ((ICursorKeySerializer[])list!.GetValue(null)!).Count(serializer => serializer.IsSupported(type));
    }

    /// <summary>Walks a list page by page, by the cursor of each page's last entry, or of its first when walking backward.</summary>
    private static async Task<(List<string> Names, int Pages)> WalkAsync(Func<IQueryable<Berthing>> list, int size, bool backward = false)
    {
        var names = new List<string>();
        string? cursor = null;

        for (var pages = 1; ; pages++)
        {
            pages.Should().BeLessThan(50, "a walk through eleven berthings ends");

            var page = await list().ToPageAsync(backward ? new PagingArguments(last: size, before: cursor) : new PagingArguments(first: size, after: cursor), Cancellation);
            var read = page.Select(berthing => berthing.Name);

            if (backward)
            {
                names.InsertRange(0, read);
            }
            else
            {
                names.AddRange(read);
            }

            if (backward ? !page.HasPreviousPage : !page.HasNextPage)
            {
                return (names, pages);
            }

            cursor = page.CreateCursor(backward ? page.First!.Value : page.Last!.Value);
        }
    }

    private WharfContext Wharf()
        => new(new DbContextOptionsBuilder<WharfContext>()
            .UseSqlite(_connection)
            .LogTo(
                message =>
                {
                    lock (_statements)
                    {
                        _statements.Add(message);
                    }
                },
                [DbLoggerCategory.Database.Command.Name],
                LogLevel.Information)
            .Options);

    /// <summary>Eleven berthings on three days, their ids in no order the rows were made in.</summary>
    private static IEnumerable<Berthing> Seeded()
    {
        var random = new Random(4711);
        var days = (DateOnly[])[new(2026, 3, 1), new(2026, 3, 2), new(2026, 3, 3)];

        for (var berth = 1; berth <= 11; berth++)
        {
            var id = new byte[16];
            random.NextBytes(id);

            yield return new Berthing
            {
                Id = VesselId.Create(new Guid(id)),
                Berth = BerthNumber.Create(berth),
                On = days[berth % days.Length],
                Name = "Berth " + berth,
            };
        }
    }

    /// <summary>A vessel's berthing: its key is an id over a <see cref="Guid"/>, and its berth an id over an <see cref="int"/>.</summary>
    public sealed class Berthing
    {
        public VesselId Id { get; set; }

        public BerthNumber Berth { get; set; }

        public DateOnly On { get; set; }

        public string Name { get; set; } = "";
    }

    public sealed class WharfContext(DbContextOptions<WharfContext> options) : DbContext(options)
    {
        public DbSet<Berthing> Berthings => Set<Berthing>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var berthing = modelBuilder.Entity<Berthing>();
            berthing.HasKey(row => row.Id);
            berthing.Property(row => row.Id).HasConversion(id => id.Value, value => VesselId.Create(value));
            berthing.Property(row => row.Berth).HasConversion(berth => berth.Value, value => BerthNumber.Create(value));
            berthing.HasIndex(row => row.Berth).IsUnique();
        }
    }
}
