using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Writes a kind as the word a record keeps it by. An event that names who made a change is stored for good, and
/// a number would change its meaning with the order the members are declared in.
/// <para>
/// The kind of a <see cref="TenancyActor{TSeatId}"/> names this converter itself, so the word does not depend on
/// what the serializer is told about enums in general: an outbox whose options write every enum by its member's
/// name still stores <c>seat</c>, the word the row of an event log keeps next to it. It is public so that a
/// serializer context generated at compile time can name it too.
/// </para>
/// </summary>
public sealed class TenancyActorKindJsonConverter : JsonConverter<TenancyActorKind>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">The value is not one of the four words.</exception>
    public override TenancyActorKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => (reader.TokenType == JsonTokenType.String ? reader.GetString() : null) switch
        {
            TenancyActorKinds.Seat => TenancyActorKind.Seat,
            TenancyActorKinds.Operator => TenancyActorKind.Operator,
            TenancyActorKinds.System => TenancyActorKind.System,
            TenancyActorKinds.Token => TenancyActorKind.Token,
            _ => throw new JsonException("An actor's kind is one of \"seat\", \"operator\", \"system\" and \"token\"."),
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TenancyActorKind value, JsonSerializerOptions options)
        => writer.WriteStringValue(TenancyActorKinds.Of(value));
}
