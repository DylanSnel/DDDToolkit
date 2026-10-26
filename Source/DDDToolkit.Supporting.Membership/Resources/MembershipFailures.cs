namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// The marker type <c>MembershipFailures.resx</c> and <c>MembershipFailures.nl.resx</c> are named after: the
/// English and Dutch text of every rule <see cref="MembershipRefusals"/> names, each under that name.
/// <para>
/// A resource refuses under its own codes (<see cref="MembershipCodes"/>), so its texts are read under those,
/// once for each resource that has members. An application writes nothing for that: whatever registers a
/// resource offers these texts under that resource's codes, to the localizer of an application that phrases
/// its failures in the reader's language (<c>AddDDDToolkitLocalization</c>).
/// </para>
/// <para>
/// The texts say "member" and "role", never what the resource or its members are called. A resource with words
/// of its own, or a language the package does not ship, is a resx of the application's with that resource's
/// codes as its keys: the application's own texts are asked before the package's.
/// </para>
/// <para>
/// The Dutch texts never use a word for the reader, neither the familiar one nor the formal one: which of the
/// two fits is the application's to say.
/// </para>
/// </summary>
public sealed class MembershipFailures;
