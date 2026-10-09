using System.Text.Json;
using System.Text.Json.Serialization;
using DDDToolkit.Access;
using DDDToolkit.Startup;
using DDDToolkit.Supporting.Tenancy;
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
// The host serves GraphQL next to the routes: every module registers a source schema of its own, Tenancy its
// administration's as well, and each gets from the host what all of them share, the typed errors, the spelling of
// enum values and the check that the caller has a seat.
var host = builder.AddSampleStorage().WithGraphQL(graphql => graphql.AddSampleGraphQLConventions());

// Who is calling: Supabase access tokens, and in Development the dev login that issues them for the
// demonstration people. Registered before the modules, so the request's caller is the one Tenancy reads.
builder.Services.AddSampleAuthentication(builder.Configuration, builder.Environment);

// The seat policy brings the host's answer to a refusal, which says why with a code. It is registered before the
// GraphQL gateways below, which wrap it: a tool's schema request with the key passes their authorization that way,
// and every other refusal is still answered by it. Registered after them, it would fail the mapping.
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
// it, and the other modules ask Tenancy's answers, so Tenancy comes first. Projects is given the starter project
// roles the application declares for its tenants' crews, which its rules are made with. Inspections asks
// Projects' gate, so it follows Projects.
builder.Services.AddTenantsModule(host, SampleCatalogue.Application);
builder.Services.AddProjectsModule(host, SampleCatalogue.ProjectRoles);
builder.Services.AddInspectionsModule(host);

// Every module's permission keys, in one call that names no module. Each module states its keys once, on the
// list it marks with [TenancyPermissions]; Tenancy's generator finds those lists in the modules this host
// references, and wrote this call into it, in the namespace named after the host's assembly, which the using of
// Examples.Tenancy.Host above brings in. A module that is added reaches the host with no change here. The program
// that exports the policies finds the same marked lists in the modules it references and writes Tenancy's policies
// with them, so the new module's keys reach the export with the reference it gets for the module's migrations.
builder.Services.AddTenancyPermissionsOfModules();

// The check that the database has every migration of every module: the migrations are the Supabase CLI's to apply,
// from the files the exporter writes, and the host applies none. Each module marks its context [SupabaseMigrations],
// which is also what the exporter writes the files from; the Supabase package's generator finds every marked context
// this host references and wrote this call into it, in the same namespace as the one above. A module that is added
// is checked with no change here.
builder.Services.AddSupabaseMigrations();

// Two GraphQL schemas over the modules, one per endpoint, each a gateway that composes the source schemas listed
// for it, in this process, and calls them in memory: the user's, with Tenancy's schema for a seat, and the
// administration's, with Tenancy's administration schema in its place, which has another person's roles besides. A
// module names another's entity by its id, and the gateway asks the owner for the rest. Source schemas that do not
// compose fail the start, with the composer's reason. Each gateway bounds a request as a whole, its depth and its
// number of fields: what a request may cost is estimated per module and per operation the gateway sends, which a
// request that asks one list many times over stays under.
builder.Services.AddInMemoryFusionGateway(
    SampleGateways.User,
    [TenantsModule.SourceSchema, ProjectsModule.SourceSchema, InspectionsModule.SourceSchema],
    options => options.ConfigureGateway = gateway => gateway.AddSampleRequestBounds());
builder.Services.AddInMemoryFusionGateway(
    SampleGateways.Administration,
    [TenantsModule.AdministrationSchema, ProjectsModule.SourceSchema, InspectionsModule.SourceSchema],
    options => options.ConfigureGateway = gateway => gateway.AddSampleRequestBounds());

// Every refusal, broken rule and lost race as problem+json with a code.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<RefusalProblems>();

// Entity Framework logs every failed save as an error with its stack trace, a save that lost a race included,
// before the toolkit turns that race into the ConcurrencyConflictException RefusalProblems answers with 409 and
// logs as an answer. A save that fails for any other reason is still logged, by whoever handles it: the
// exception handler as a 500, the outbox, or the host when the seeding fails. A save the policies refused is
// logged by the toolkit as well, after asking the request's access check again: an information line, with no stack
// trace, when that check now refuses too, since the caller's rights changed between the check and the save, and a
// warning when it still lets the caller through, since C# and the policies disagree. What is asked again is the
// requirement the request declared: what a Tenancy use case asks past it, behind InTenant() say, is not, so a
// refused save of such a request is a warning.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Update", LogLevel.Critical);

// Before anything of the host starts, the server's port and the seeding included: every check the registrations
// above brought. Each module's access behavior is in the pipeline, so no request that is sent passes unchecked; the
// contexts are wired through the toolkit, row level security and Tenancy; the role the host logs in as may become
// every caller; every migration is applied; and that role holds nothing, and the database's policies, functions
// and grants are the ones the modules' code writes, Tenancy's and Membership's among them.
// They run in that order, as the application itself, and the first that finds something wrong stops the start and
// says what puts it right. A module added later brings its own, and no class of the host lists them.
builder.Services.RunStartupChecks();

// Once the host has started, in the background: every tenant's roles made from a pack follow that pack as the
// catalogue has it now. A key a module brings later reaches the flat tenants' Tenant admin, whose pack lists none and
// so holds every key, and a key added to a pack reaches every role made from it; what a tenant changed in a role
// itself stays. It is a call of its own and not a check, since it changes the tenants' roles. Each role whose keys
// change raises RoleFollowedItsPack, which the Tenants module keeps in the access history, where the tenant's
// administrators read it on the History page; the host logs what the run did.
builder.Services.SyncRolePacks();

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

// GraphQL at /graphql, for the same callers as the routes: a token, which the endpoint requires as a route does,
// and a seat, which each module's schema checks in front of its fields, since seatsOfMine and invitationAccept need
// none. The tenant's administration at /admin/graphql: everything a seat is offered at /graphql, and another person's
// roles besides, for a seat in the tenant the request names, as the routes inside a tenant require. That decides who
// is offered the fields; who may read what one answers is its request's to say, as everywhere else. Both run after
// tenant selection, so the caller it resolved is the one a resolver runs as.
//
// A tool that reads a schema, GraphQL Codegen or the Relay compiler, has no token. In Development, where the host runs
// on a developer's machine, it reads the schema without one; elsewhere with the key the host reads at
// GraphQL:SchemaKey, from the environment variable GraphQL__SchemaKey or its secret store, which the tool sends in
// X-GraphQL-Schema-Key. The key reads the schema and nothing else.
app.MapInMemoryFusionGateway(SampleGateways.UserPath, SampleGateways.User).RequireAuthorization();
app.MapInMemoryFusionGateway(SampleGateways.AdministrationPath, SampleGateways.Administration).RequireAuthorization(SamplePolicies.SeatRequired);

app.MapDefaultEndpoints();

app.Run();

/// <summary>The host's entry point; public so the scenario tests can run it in their own process.</summary>
public partial class Program;
