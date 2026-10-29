using DDDToolkit.Access;

// A request about the caller's own, [OrOwn] or [Own] on a Guid, compares it with the caller's verified identity, the id
// its token names: the core says so without a registration, and says so here for the generator, which reports [OrOwn]
// and [Own] on a type nothing says the caller's own of. Only a signed-in user has a verified identity, so [SignedIn]
// beside [OwnOnly] with [Own] on a Guid says nothing more.
[assembly: OwnedByCurrentUser(typeof(Guid), Implies = [typeof(SignedInAttribute)])]
