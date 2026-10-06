using System.Text;
using Examples.Tenancy.AppHost;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

// The Tenancy sample, whole, and one way: Supabase's own Postgres and Auth images and a mail catcher as
// containers, and the API and the UI as two processes. Nothing is configured and no port is chosen here, so all
// it needs is Docker. Run this project, then open the UI from the dashboard.
//
// The files under ../supabase/migrations are applied by the role that owns the database, the login role is given
// a password, and the API logs in as that role, which owns nothing. The demonstration people are made users of
// Auth, so the UI signs them in there with a password as well as through the dev login. The mail Auth sends a
// person who is invited waits in the mail catcher, and its link opens as it is written: it leads to the UI's
// page that accepts the invitation.

var builder = DistributedApplication.CreateBuilder(args);

// The Host: every module, the dev login and the demonstration data. The UI waits until /health answers.
var api = builder.AddProject<Projects.Examples_Tenancy_Host>("api")
    .WithHttpHealthCheck("/health");

// The UI finds the API by the name "api", through service discovery; nothing in it names a port.
var ui = builder.AddProject<Projects.Examples_Tenancy_Ui>("ui")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

// The secret and the issuer of Supabase's local stack, read from where the API has them: its development
// settings, so Auth is given what the API checks with and neither is written down twice. The dev login signs
// with the secret, and Auth reads the service role's token with it. Both are public knowledge, and good for
// a developer's machine only.
var apiSettings = new ConfigurationBuilder()
    .AddJsonFile(Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "Examples.Tenancy.Host", "appsettings.Development.json")))
    .Build();
var jwtSecret = apiSettings["Supabase:JwtSecret"] ?? throw new InvalidOperationException("The API's development settings name no Supabase:JwtSecret.");
var projectUrl = apiSettings["Supabase:Url"] ?? throw new InvalidOperationException("The API's development settings name no Supabase:Url.");

// Passwords of this run alone, made up when nothing sets them (Parameters:<name>): the containers are new
// every run, so nothing has to remember them. Letters and digits only, since two of them go into a URL.
// The dashboard shows each; demo-password is the one the demonstration people sign in with.
var postgresPassword = builder.AddParameter("postgres-password", new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true);
var authPassword = builder.AddParameter("auth-db-password", new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true);
var loginPassword = builder.AddParameter("tenancy-api-password", new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true);
var demoPassword = builder.AddParameter("demo-password", new GenerateParameterDefault { MinLength = 16, Special = false }, secret: true);

// The UI as a browser reaches it. Auth runs in a container, where an endpoint of this machine goes by
// another host name; what it is told here is the address a person's browser opens, so the host is written out.
var uiPort = ui.GetEndpoint("http").Property(EndpointProperty.Port);
var site = ReferenceExpression.Create($"http://localhost:{uiPort}");

var (postgresImage, postgresTag) = ImageAndTag(SupabaseImages.Postgres);
var (authImage, authTag) = ImageAndTag(SupabaseImages.Auth);
var (mailImage, mailTag) = ImageAndTag(SupabaseImages.MailCatcher);

// Supabase's Postgres, started as the image starts itself: its own settings, roles, schemas and extensions.
var db = builder.AddContainer("db", postgresImage, postgresTag)
    .WithEnvironment("POSTGRES_PASSWORD", postgresPassword)
    .WithEndpoint(targetPort: 5432, name: "tcp", scheme: "tcp");
var dbHost = db.GetEndpoint("tcp").Property(EndpointProperty.Host);
var dbPort = db.GetEndpoint("tcp").Property(EndpointProperty.Port);

// What the platform does for a project before Auth can start: the image makes the Auth server's role and
// gives it no password, and only the image's superuser may change a role Supabase reserves. This is the one
// thing done as that superuser.
var roles = builder.AddContainer("roles", postgresImage, postgresTag)
    .WithEntrypoint("bash")
    .WithArgs("-c", Script("""
        set -euo pipefail
        until pg_isready -q -U postgres; do sleep 1; done
        psql -v ON_ERROR_STOP=1 -q -U supabase_admin -d postgres -v password="$AUTH_DB_PASSWORD" <<'SQL'
        ALTER ROLE supabase_auth_admin WITH PASSWORD :'password';
        SQL
        """))
    .WithEnvironment("PGHOST", $"{dbHost}")
    .WithEnvironment("PGPORT", $"{dbPort}")
    .WithEnvironment("PGPASSWORD", postgresPassword)
    .WithEnvironment("AUTH_DB_PASSWORD", authPassword)
    .WaitFor(db);

// Catches the mails Auth sends; its HTTP endpoint shows them.
var mail = builder.AddContainer("mail", mailImage, mailTag)
    .WithEndpoint(targetPort: 1025, name: "smtp", scheme: "tcp")
    .WithHttpEndpoint(targetPort: 8025, name: "http");

// Supabase's Auth server, with no gateway in front of it. It migrates its own schema when it starts, which
// is also what gives the database the claim functions the policies read. Set up as the sample's
// supabase/config.toml sets up a local stack: nobody signs up, an address is proven before its owner signs
// in, nobody is anonymous, and its site is the UI. It is given a signing key as that stack's Auth is, made
// up for this run: a person's token is signed with it, and the API checks it with the public half Auth
// publishes, where it checks the dev login's with the secret.
var auth = builder.AddContainer("auth", authImage, authTag)
    .WithHttpEndpoint(targetPort: 9999, name: "http")
    .WithHttpHealthCheck("/health");

// The address the link in a mail names: Auth's own, as a browser on this machine reaches it, and the path Auth
// answers itself. In a Supabase project a gateway stands in front of Auth and serves the link under /auth/v1;
// it would only rename the path, since it is Auth's own /verify that proves the address and sends the browser
// on. So no gateway is started, and the link works as it is written. The port is the one this run gave the
// endpoint on this machine, which is why it is asked for by that network: inside its container Auth listens
// on another one. The issuer stays the project's: a token carries it and the API compares it, nobody opens it.
var authPort = auth.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork).Property(EndpointProperty.Port);

// GOTRUE_JWT_DEFAULT_GROUP_NAME is the role a user gets whom the API makes through the admin client, and with
// it the role claim of every token of theirs. Auth logs at each start that it will stop reading the setting,
// and says the same of one nothing here sets; it reads it still, and Supabase's own local stack sets it.
// Without it the demonstration people have no role, and the API finds no seat for a token without one.
auth.WithEnvironment("GOTRUE_API_HOST", "0.0.0.0")
    .WithEnvironment("GOTRUE_API_PORT", "9999")
    .WithEnvironment("GOTRUE_DB_DRIVER", "postgres")
    .WithEnvironment("GOTRUE_DB_DATABASE_URL", $"postgres://supabase_auth_admin:{authPassword.Resource}@{dbHost}:{dbPort}/postgres")
    .WithEnvironment("API_EXTERNAL_URL", ReferenceExpression.Create($"http://localhost:{authPort}"))
    .WithEnvironment("GOTRUE_JWT_ISSUER", projectUrl + "/auth/v1")
    .WithEnvironment("GOTRUE_JWT_SECRET", jwtSecret)
    .WithEnvironment(AuthSigningKeys.Setting, AuthSigningKeys.New())
    .WithEnvironment(AuthSigningKeys.MethodsSetting, AuthSigningKeys.Methods)
    .WithEnvironment("GOTRUE_JWT_AUD", "authenticated")
    .WithEnvironment("GOTRUE_JWT_EXP", "3600")
    .WithEnvironment("GOTRUE_JWT_ADMIN_ROLES", "service_role")
    .WithEnvironment("GOTRUE_JWT_DEFAULT_GROUP_NAME", "authenticated")
    .WithEnvironment("GOTRUE_SITE_URL", site)
    .WithEnvironment("GOTRUE_DISABLE_SIGNUP", "true")
    .WithEnvironment("GOTRUE_EXTERNAL_EMAIL_ENABLED", "true")
    .WithEnvironment("GOTRUE_MAILER_AUTOCONFIRM", "false")
    .WithEnvironment("GOTRUE_EXTERNAL_ANONYMOUS_USERS_ENABLED", "false")
    .WithEnvironment("GOTRUE_SMTP_HOST", $"{mail.GetEndpoint("smtp").Property(EndpointProperty.Host)}")
    .WithEnvironment("GOTRUE_SMTP_PORT", $"{mail.GetEndpoint("smtp").Property(EndpointProperty.Port)}")
    .WithEnvironment("GOTRUE_SMTP_ADMIN_EMAIL", "tenancy@example.test")
    .WithEnvironment("GOTRUE_SMTP_SENDER_NAME", "Tenancy sample")
    .WithEnvironment("GOTRUE_MAILER_URLPATHS_INVITE", "/verify")
    .WithEnvironment("GOTRUE_MAILER_URLPATHS_CONFIRMATION", "/verify")
    .WithEnvironment("GOTRUE_MAILER_URLPATHS_RECOVERY", "/verify")
    .WithEnvironment("GOTRUE_MAILER_URLPATHS_EMAIL_CHANGE", "/verify")
    .WaitFor(mail)
    .WaitForCompletion(roles);

// The database, made by the role that owns it, which on this image is no superuser: every file of
// ../supabase/migrations as it is, in the order of their names, each in a transaction of its own, the way
// the Supabase CLI applies them. Then the one thing no migration carries: the login role is turned on,
// with this run's password. After Auth, whose migrations bring the functions the policies call.
var migrate = builder.AddContainer("migrate", postgresImage, postgresTag)
    .WithBindMount(Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "supabase", "migrations")), "/migrations", isReadOnly: true)
    .WithEntrypoint("bash")
    .WithArgs("-c", Script("""
        set -euo pipefail
        export LC_ALL=C
        for file in /migrations/*.sql; do
          echo "Applying $(basename "$file")"
          psql -v ON_ERROR_STOP=1 -q -U postgres -d postgres --single-transaction -f "$file"
        done
        psql -v ON_ERROR_STOP=1 -q -U postgres -d postgres -v password="$TENANCY_API_PASSWORD" <<'SQL'
        ALTER ROLE tenancy_api WITH LOGIN PASSWORD :'password';
        SQL
        """))
    .WithEnvironment("PGHOST", $"{dbHost}")
    .WithEnvironment("PGPORT", $"{dbPort}")
    .WithEnvironment("PGPASSWORD", postgresPassword)
    .WithEnvironment("TENANCY_API_PASSWORD", loginPassword)
    .WaitFor(auth);

// The API logs in as tenancy_api and as nothing else: it never sees the owner's password. It keeps the project's
// URL from its development settings, which its tokens name, and is told where Auth answers, since no gateway
// serves Auth under that URL here: Supabase:AuthUrl, the one exception to reaching Auth at {Supabase:Url}/auth/v1.
// That is where it fetches the key Auth publishes and reaches the admin API, with a token that carries the
// service role: what a bare Auth server takes where a project has its secret key. With it the API makes the
// demonstration people users there, and has Auth mail a person who is invited. The
// mail's link leads on to the UI's page that accepts, which the API is told here. Auth sends a browser on
// only to an address of its site, and tells the two apart by host name: the page is on the UI's endpoint,
// which is on localhost, as the site above is.
api.WithEnvironment("ConnectionStrings__Supabase", $"Host={dbHost};Port={dbPort};Database=postgres;Username=tenancy_api;Password={loginPassword.Resource}")
    .WithEnvironment("Supabase__AuthUrl", auth.GetEndpoint("http"))
    .WithEnvironment("Supabase__SecretKey", ServiceRoleToken(jwtSecret))
    .WithEnvironment("Sample__SeedAuthUsers", "true")
    .WithEnvironment("Sample__DemoPassword", demoPassword)
    .WithEnvironment("Sample__Invitations__AcceptPage", ReferenceExpression.Create($"{ui.GetEndpoint("http")}/invitations/accept"))
    .WaitFor(auth)
    .WaitForCompletion(migrate);

// The UI is told where Auth is, so its login page offers the sign-in with a password next to the dev login.
ui.WithEnvironment("Supabase__AuthUrl", auth.GetEndpoint("http"));

builder.Build().Run();

// "supabase/postgres:17.11.0.002" as its name and its tag.
static (string Image, string Tag) ImageAndTag(string pinned)
{
    var colon = pinned.LastIndexOf(':');
    return (pinned[..colon], pinned[(colon + 1)..]);
}

// A script for bash, whatever line endings this file was checked out with.
static string Script(string text) => text.ReplaceLineEndings("\n");

// A token with the service role, signed with the local stack's secret (HS256), good for a year.
static string ServiceRoleToken(string secret)
{
    var now = DateTime.UtcNow;
    return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
    {
        Issuer = "supabase",
        IssuedAt = now,
        Expires = now.AddYears(1),
        Claims = new Dictionary<string, object> { ["role"] = "service_role" },
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256),
    });
}
