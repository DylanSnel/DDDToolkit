using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework;

/// <summary>
/// What <c>UseDDDToolkitCore</c> leaves in a context's options: that the application asked for the toolkit's base
/// alone, and so meant to leave out what a registered package would have brought. <c>UseDDDToolkit</c> leaves none.
/// </summary>
/// <remarks>
/// The start-up check of row level security reads it, by the name of this type, since that package references
/// this one no more than it references Npgsql. A context on Postgres without row level security passes that check
/// when its options hold this, written by the call that says so, and not because it holds some interceptor of the
/// toolkit's: a context that added the toolkit's interceptors by hand, and forgot row level security, said nothing
/// of the kind. It adds no service and changes nothing a context does.
/// </remarks>
internal sealed class BaseAloneExtension : IDbContextOptionsExtension
{
    /// <summary>The one instance: it holds nothing, so every context's options can share it.</summary>
    public static readonly BaseAloneExtension Instance = new();

    private BaseAloneExtension() => Info = new ExtensionInfo(this);

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info { get; }

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services)
    {
        // Nothing: it is read, not used.
    }

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
        // Nothing to validate: it holds no setting.
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "UseDDDToolkitCore ";

        // It holds nothing, so every context that has it can share the internal services of another that has it.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["DDDToolkit:UseDDDToolkitCore"] = "1";
    }
}
