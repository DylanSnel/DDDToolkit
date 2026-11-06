namespace DDDToolkit.Auth.Supabase.Tests.Infrastructure;

/// <summary>
/// The images these tests run Supabase from: Supabase's own Postgres and its own Auth server, and a mail
/// catcher for the mails Auth sends.
/// </summary>
/// <remarks>
/// <para>
/// These are Supabase's own builds rather than a plain Postgres made to look like one. What the tests
/// lean on is exactly what differs between the two: the roles the image ships and what each of them may
/// do, the extension that guards them, and the <c>auth</c> schema as the Auth server's own migrations
/// leave it. A stand-in would prove the tests against the stand-in.
/// </para>
/// <para>
/// The three tags are the ones Supabase's command line tool starts its local stack with, release 2.119.0
/// of 2026-09-30, so they are a set Supabase tested together. Move them together, from that tool's list of
/// images, and run the stack's tests after: they record what the tests were written against, such as the roles'
/// attributes and the answer Auth gives for an address that has an account.
/// </para>
/// </remarks>
public static class SupabaseImages
{
    /// <summary>Supabase's Postgres 17, with its roles, its schemas and the extensions a project can turn on.</summary>
    public const string Postgres = "supabase/postgres:17.11.0.002";

    /// <summary>Supabase's Auth server, which signs people in, invites them and has the admin API.</summary>
    public const string Auth = "supabase/gotrue:v2.197.0";

    /// <summary>Catches the mails the Auth server sends, and shows them over HTTP.</summary>
    public const string MailCatcher = "axllent/mailpit:v1.31.3";
}
