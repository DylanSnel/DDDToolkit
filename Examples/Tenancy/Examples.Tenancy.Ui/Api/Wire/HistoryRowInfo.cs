using System.Text.Json;

namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// One row of the tenant's access history: an event that changed who may do what, when it happened, who made the
/// change, and the event itself with the ids, keys and dates it names.
/// </summary>
public sealed record HistoryRowInfo(Guid Id, string Event, DateTimeOffset OccurredAt, ActorInfo By, JsonElement Details);
