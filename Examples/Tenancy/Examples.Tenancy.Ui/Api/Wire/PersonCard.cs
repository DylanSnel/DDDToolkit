namespace Examples.Tenancy.Ui.Api.Wire;

// The UI's own reading of the API's JSON. None of these references a type of the Host or of a module: ids are
// Guids and statuses are the strings the API writes, so the UI depends on the wire, never on the server's code.
// A field the API adds is ignored here; a field these records name that the API stops sending reads as null.

/// <summary>A person the dev login signs in as (<c>GET /dev/people</c>).</summary>
public sealed record PersonCard(string Key, Guid Id, string Name, string Email, string About);
