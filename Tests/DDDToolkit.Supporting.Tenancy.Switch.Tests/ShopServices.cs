using DDDToolkit.EntityFramework;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Persistence;

namespace Shop;

/// <summary>
/// The application's registration: the toolkit's Entity Framework integration with an outbox on the context, which
/// Tenancy's domain events go to, Tenancy over the context, and the context.
/// </summary>
public static class ShopServices
{
    /// <summary>Registers the application over <paramref name="connection"/>, a SQLite database held open by the caller.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connection">The database.</param>
    public static IServiceCollection AddShop(this IServiceCollection services, SqliteConnection connection)
    {
        services.AddDDDToolkitEntityFramework(options => options.UseOutbox<ShopContext>(outbox => outbox
            .AddTenancyDomainEvents<TenantId, SeatId, OrganizationUnitId, RoleId>()));

        // Generated for the classes and ids of this project: the context is the one type left to name. The ids are the
        // application's, written or not, and so is how they are made.
        services.AddTenancy<ShopContext>(options =>
        {
            options.NewTenantId = TenantId.CreateSequential;
            options.NewUnitId = OrganizationUnitId.CreateSequential;
            options.NewSeatId = SeatId.CreateSequential;
            options.NewRoleId = RoleId.CreateSequential;
        });

        return services.AddDbContext<ShopContext>((provider, options) => options.UseSqlite(connection).UseDDDToolkit(provider));
    }
}
