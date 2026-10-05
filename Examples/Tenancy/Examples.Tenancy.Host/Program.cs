using System.Text.Json;
using System.Text.Json.Serialization;
using DDDToolkit.Access;
using DDDToolkit.Startup;
using Examples.Tenancy.Host;
using DDDToolkit.HotChocolate.Fusion.InMemory;

var builder = WebApplication.CreateBuilder(args);

// The container checks itself in every environment, not only in Development: when the host is built, that every
// service can be made, and from then on, that nothing scoped is resolved from the root. Handlers, the sender and
// the modules' stores live as long as a request, so a singleton that took one would keep a request's unit of work
// for good; that is a mistake to find at start-up, wherever the host runs.
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

// Traces, metrics and logs to the Aspire dashboard when the AppHost runs this; nothing noticeable when it runs
// on its own.
builder.AddServiceDefaults();

// Where the modules' tables live: Postgres, as Supabase runs it, at ConnectionStrings:Supabase. The host logs in
// as a role that owns nothing and every command runs as its caller under the exported policies. Without that
// setting the host stops here, and says how to get one: the AppHost, or the Supabase CLI's stack.
//
// The host serves GraphQL next to the routes: every module registers a source schema of its own, and gets from
// the host what all of them share, the typed errors, the spelling of enum values and the check that the caller
// has a seat.
var host = builder.AddSampleStorage().WithGraphQL(graphql => graphql.AddSampleGraphQLConventions());

// Who is calling: Supabase access tokens, and in Development the dev login that issues them for the
// demonstration people. Registered before the modules, so the request's caller is the one Tenancy reads.
builder.Services.AddSampleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddSampleSeatPolicy();
builder.Services.AddSampleOperatorPolicy();

// The accounts an invited person signs in with: Supabase Auth's, through its admin client, when the host has the
// key for it, and the dev login's own where that is all there is. The Tenants module asks whichever is here when
// somebody is invited by address.
builder.Services.AddSampleIdentityAccounts(builder.Configuration, builder.Environment);

// A browser application on another origin may call only when Sample:Cors:Origins lists it; with none listed there
// is no CORS at all.
var browserOrigins = BrowserCors.Origins(builder.Configuration);
builder.Services.AddBrowserCors(browserOrigins);

// Every flow of work says who it runs as, and nothing falls back to the application's own power: a request runs as
// its token's user (tenant selection begins it), seeding as system work begun in each tenant, and the outbox
// pollers begin the system caller for their own bookkeeping only. With row level security this is what makes the
// sample fail closed: work that named no caller is refused before its first command, rather than run as the role
// the host logs in as.
builder.Services.RequireExplicitCallers();

// A malformed id or body throws, rather than answering an empty 400, so RefusalProblems answers it with a code
// like every other refusal.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

// A refusal is answered in the language its request asks for, English or Dutch. The host's own texts are added
// here, before the modules: each module adds the texts of its own codes after them.
builder.Services.AddRequestLanguages();

// Enum values travel by name, in lower snake case, in every answer and every body: "closed", "hierarchical", and
// an underscore between the words of a longer name. It is the host's choice, made once here, so no route spells a
// value itself and none reads a number for one.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false)));

// Every use case of a module is a command or a query with a handler of its own, and a route only sends one. The
// mediator is generated into this project, which is the one that references its generator: it finds the handlers
// of every module when the host compiles, and registers them, with the sender, per scope. On its way to its
// handler a request passes the steps registered for it, in the order they were registered: tracing first, so it
// is registered here, before the modules; then the access check of the request's module, which that module
// registers itself when it is added below; then the handler. The host names no module's check.
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddRequestTracing();

// The modules, each through the one entry its API project has. Tenancy brings the application's catalogue with
// it; the other modules add their keys to it, and ask Tenancy's answers, so Tenancy comes first. Projects is
// given the starter project roles the application declares for its tenants' crews, which its rules are made with.
// Inspections asks Projects' gate, so it follows Projects.
builder.Services.AddTenantsModule(host, SampleCatalogue.Application);
builder.Services.AddProjectsModule(host, SampleCatalogue.ProjectRoles);
builder.Services.AddInspectionsModule(host);

// One GraphQL schema over the modules: the gateway composes the source schemas the modules registered, in this
// process, and calls them in memory. A module names another's entity by its id, and the gateway asks the owner
// for the rest. Source schemas that do not compose fail the start, with the composer's reason. The gateway bounds
// a request as a whole, its depth and its number of fields: what a request may cost is estimated per module and
// per operation the gateway sends, which a request that asks one list many times over stays under.
builder.Services.AddInMemoryFusionGateway(options => options.ConfigureGateway = gateway => gateway.AddSampleRequestBounds());

// Every refusal, broken rule and lost race as problem+json with a code.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<RefusalProblems>();

// Entity Framework logs every failed save as an error with its stack trace, a save that lost a race included,
// before the toolkit turns that race into the ConcurrencyConflictException RefusalProblems answers with 409 and
// logs as an answer. A save that fails for any other reason is still logged, by whoever handles it: the
// exception handler as a 500, the outbox, or the host when the seeding fails.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Update", LogLevel.Critical);

// Before anything of the host starts, the server's port and the seeding included: every check the registrations
// above brought. Each module's access behavior is in the pipeline, so no request that is sent passes unchecked; the
// contexts are wired through the toolkit, row level security and Tenancy; the role the host logs in as may become
// every caller; every migration is applied; and that role holds nothing, and the database's policies, functions
// and grants are the ones the modules' code writes, Tenancy's and Membership's among them.
// They run in that order, as the application itself, and the first that finds something wrong stops the start and
// says what puts it right. A module added later brings its own, and no class of the host lists them.
builder.Services.RunStartupChecks();

// Hosted services start in the order they are added, all of them after the checks above. With
// Sample:SeedAuthUsers on, the demonstration people are made users of Supabase Auth first, under the ids their
// seats are found by, so they sign in there with a password as well as through the dev login.
DemoAuthUsers.AddTo(builder.Services, builder.Configuration, builder.Environment);
builder.Services.AddHostedService<DemoSeeder>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
if (browserOrigins.Length > 0)
{
    app.UseCors(BrowserCors.Policy);
}

app.UseAuthentication();

// The tenant a request works in, from the Tenant header and the caller's own seats. Before authorization, because
// the policy of the routes inside a tenant asks for the seat this resolves.
app.UseTenantSelection();

// The language a request is answered in, from its Accept-Language header: after the caller and the tenant are
// known, and before anything can refuse, the seat policy included.
app.UseRequestLocalization();
app.UseAuthorization();

// Anonymous, and only in Development with the dev login on.
app.MapDevLogin();

// Every other route needs a valid token: none, or an expired one, is a 401. A person's own seats need nothing
// more, since picking a tenant comes before being in one, and neither does accepting an invitation, which is how
// a person comes by a seat; everything else needs a seat in the tenant the request names.
var signedIn = app.MapGroup("").RequireAuthorization();
signedIn.MapTenantsSeatsOfMine();
signedIn.MapTenantsInvitationAcceptance();

var seated = signedIn.MapGroup("").RequireAuthorization(SamplePolicies.SeatRequired);
seated.MapTenantsModule();
seated.MapProjectsModule();
seated.MapInspectionsModule();

// The application's own staff: a token that carries the operators' role, and no seat anywhere. They look across
// tenants, so these routes name a tenant in their path and none in a header. Each module maps what it owns of
// that, and all of it is reading: there is no route here that changes anything.
var operators = signedIn.MapGroup("").RequireAuthorization(SamplePolicies.OperatorRequired);
operators.MapTenantsOperations();
operators.MapProjectsOperations();
operators.MapInspectionsOperations();

// GraphQL at /graphql, for the same callers: a token, checked here because the gateway is a branch of the pipeline
// and not an endpoint that could require it, and a seat, which each module's schema checks in front of its fields.
// Both come after tenant selection, so the caller it resolved is the one a resolver runs as.
app.UseWhen(context => context.Request.Path.StartsWithSegments("/graphql"), branch => branch.UseSignedInOnly());
app.MapInMemoryFusionGateway();

app.MapDefaultEndpoints();

app.Run();

/// <summary>The host's entry point; public so the scenario tests can run it in their own process.</summary>
public partial class Program;
