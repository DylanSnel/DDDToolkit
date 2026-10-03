namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// The marker type <c>TenancyFailures.resx</c> and <c>TenancyFailures.nl.resx</c> are named after: the
/// English and Dutch text of every <c>tenancy.*</c> code in <see cref="TenancyRefusals"/>. Add it to the
/// localizer so refusals reach the reader in their language:
/// <code>
/// services.AddLocalization();
/// services.AddDDDToolkitLocalization(options =&gt; options.AddResource&lt;TenancyFailures&gt;());
/// </code>
/// A language the package does not ship is a resx of your own with the same keys, added before this one.
/// <para>
/// The Dutch texts never use a word for the reader, neither the familiar one nor the formal one: which of the
/// two fits is the application's to say. Another tone, or another word for a tenant or a seat, is a resx of
/// your own as well, with the keys you want to say differently, in every language you support, added before
/// this one.
/// </para>
/// </summary>
public sealed class TenancyFailures;
