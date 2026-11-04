using BenchmarkDotNet.Attributes;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Benchmarks.Domain;
using DDDToolkit.Benchmarks.Infrastructure;
using DDDToolkit.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Benchmarks.Access;

/// <summary>
/// One check of a request with <c>[Own]</c> on an aggregate's id, in a scope that already has its context: the caller
/// begun, and the module's checks asked. The courier of the parcel sends it, so the check passes. The check reads the
/// owner's column of one row in one statement. The scope and its context are made once, so their cost is not measured.
/// </summary>
[MemoryDiagnoser]
public class OwnerCheckBenchmarks
{
    private static readonly Guid Courier = Guid.Parse("c0c00000-0000-4000-8000-000000000001");

    private static readonly ParcelId Parcel = new(Guid.Parse("9a7c0000-0000-4000-8000-000000000001"));

    private readonly DeliverParcel _request = new(Parcel);

    private SqliteConnection _connection = null!;
    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private AccessChecks<IParcelRequest> _checks = null!;
    private AccessManager _access = null!;

    /// <summary>Creates the database with a few parcels, and a container with the toolkit and the checks of the parcels' requests.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var collection = new ServiceCollection();
        collection.AddDDDToolkitEntityFramework();
        collection.AddOwnedByCurrentUser<CourierId, CurrentCourier>();
        collection.AddAccessChecks<IParcelRequest>();
        collection.AddDbContext<ParcelContext>((services, options) => options.UseSqlite(_connection).UseDDDToolkit(services));
        _services = collection.BuildServiceProvider();
        _access = _services.GetRequiredService<AccessManager>();

        using var seed = new ParcelContext(new DbContextOptionsBuilder<ParcelContext>().UseSqlite(_connection).Options);
        seed.Database.EnsureCreated();
        seed.Parcels.Add(new Parcel(Parcel, "Books", new CourierId(Courier)));
        for (var i = 0; i < 99; i++)
        {
            seed.Parcels.Add(new Parcel(ParcelId.Create(), "Parcel " + i, CourierId.Create()));
        }

        seed.SaveChanges();

        _scope = _services.CreateAsyncScope();
        _checks = _scope.ServiceProvider.GetRequiredService<AccessChecks<IParcelRequest>>();
    }

    /// <summary>Disposes the scope and the container, and closes the database.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _services.Dispose();
        _connection.Dispose();
    }

    /// <summary>The courier delivers its own parcel.</summary>
    [Benchmark]
    public async Task OwnParcel()
    {
        using var caller = _access.BeginAs(CurrentUserSnapshot.User(Courier));
        await _checks.RequireAsync(_request, CancellationToken.None);
    }
}
