namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// One of the project roles every tenant starts with, as the application declares it: what a tenant's project
/// roles are made from when the tenant is set up, and nothing after that. The tenant renames, re-keys and
/// archives what was made as it likes.
/// </summary>
/// <remarks>
/// The application declares them, not this module, because a crew role gives keys of every module that acts on a
/// project, a surveyor's the key to record an inspection say, and this module knows only its own. They are the
/// application's data next to its role packs, handed to the module where the host adds it.
/// </remarks>
/// <param name="Key">
/// What the role is made from, and found by after that, whatever the tenant has called it since: lower case
/// letters, digits and dashes, such as <c>surveyor</c>.
/// </param>
/// <param name="Name">The name a tenant's role gets when it is made.</param>
/// <param name="Description">What it is for, as a tenant's role is first described.</param>
/// <param name="Keys">The keys it gives on the crew it is held on.</param>
public sealed record StarterProjectRole(string Key, string Name, string Description, IReadOnlyList<string> Keys);
