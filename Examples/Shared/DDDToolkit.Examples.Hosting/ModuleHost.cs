using DDDToolkit.EntityFramework.Options;

namespace DDDToolkit.Examples.Hosting;

/// <summary>
/// What a host decides for every module it runs: where the module's tables live, and where the
/// integration events it publishes go. Everything else a module decides for itself.
/// </summary>
/// <param name="Database">Where the module's tables live.</param>
/// <param name="Publish">
/// Where the module's outbox delivers: to the other modules in this process, to a queue, to a broker.
/// Called once per publishing module, on that module's own outbox.
/// </param>
public sealed record ModuleHost(ModuleDatabase Database, Action<OutboxOptions> Publish)
{
    /// <summary>
    /// A modular monolith: every module in one process, each message handed to the other modules through
    /// the module sink.
    /// </summary>
    public static ModuleHost InProcess(ModuleDatabase database) => new(database, outbox => outbox.SendToModules());
}
