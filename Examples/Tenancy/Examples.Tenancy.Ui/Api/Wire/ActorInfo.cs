namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// Who changed a row or made a change, as the API answers it: the kind of actor, <c>seat</c>, <c>operator</c>,
/// <c>system</c> or <c>token</c>, and for a seat its id. The API gives no name; the screen asks the directory what
/// the seat is called.
/// </summary>
public sealed record ActorInfo(string Kind, Guid? SeatId = null);
