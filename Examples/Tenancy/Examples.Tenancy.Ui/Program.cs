using Examples.Tenancy.Ui;
using Examples.Tenancy.Ui.Components;

var builder = WebApplication.CreateBuilder(args);

// Logs and traces to the Aspire dashboard, and service discovery: under the AppHost the name "api" resolves to
// the Host. Run on its own, the settings name the Host instead (appsettings.Development.json, or Api:BaseUrl).
builder.AddServiceDefaults();

// Interactive server rendering: every page runs in the circuit, so the session, its token and its tenant stay
// on the server and never reach the browser's script.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSampleUi();

var app = builder.Build();

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

app.Run();

/// <summary>
/// The UI's entry point. Internal on purpose: the scenario tests reference the Host and the UI together, and two
/// public <c>Program</c> classes in the global namespace would make the Host's ambiguous. Declaring the class also
/// keeps the framework from generating a public one.
/// </summary>
internal partial class Program;
