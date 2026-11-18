# Claude instructions for DTS.DDD

## Writing rules: plain prose

These rules hold for everything written in this repository: docs, code comments, commit messages, PR titles and
bodies, changelog entries and diagrams. The owner calls text that breaks them "Claude prose". Each example is a
pattern, so fix the same pattern everywhere, also where nobody pointed at it.

### Headings

- A heading names its topic in plain words. A reader who scans the table of contents knows what is under it.
- Use no teasers, riddles, puns, metaphors or pronouns in a heading.
- Write headings in sentence case ("Who may give a role").

| Claude prose | Plain |
|---|---|
| Three more words you meet early | Catalogue, starter role and system work |
| The one question that decides it | When to keep containment on |
| On Postgres, a second lock | Row level security on Postgres |
| A context without it fails loudly | Missing filter: start-up error |
| The shortest start: the switch | Quick start |

### Sentences

- Put one fact in each sentence. Start with the subject, use an active verb and concrete nouns.
- Name the mechanism. Each sentence gives an instruction, a fact, a number or a name from the code. "The query
  filter adds `WHERE TenantId = @tenant` to every query of a tenant-owned class" replaces "tenants are kept apart".
- Apply the portability test. A sentence that could stand unchanged in another project's docs says nothing about
  this one. Replace it with a mechanism, a number, or nothing.
- Give every pronoun ("it", "them", "such", "that one") a clear noun in the same sentence.
- Write what the code does. Code does not think or speak, so avoid "Tenancy answers", "the package asks" and
  "the database remembers". Write "the query returns", "the check refuses", "`GrantAsync` writes a row to
  `RoleGrants`".
- Leave out aphorisms, slogans and figurative verbs ("rides along", "stands on", "holds it", "decides nothing",
  "and that is the point").
- Leave out contrasts for effect: "X, not Y", "not X but Y", and lists of "it does not A, it does not B". State the
  fact once. Keep a "does not" only for a real limit that the reader would otherwise assume.
- Use normal word order. Avoid literary forms such as "said explicitly", "held to it", "its own from then on".
- Split a chain of clauses joined by commas, colons and semicolons into sentences or bullets.
- Use a colon only before a list, a code block or an example. A colon that joins two clauses becomes two
  sentences.
- Use no em dashes, and no spaced hyphen in the role of a dash. Use a period or a comma.
- Cut filler and props for weak verbs: "exactly", "whole", "own" (when it adds nothing), "simply", "just",
  "really", "actually", "fully", "quietly", "in short". "Silently" stays only when it is a fact ("fails silently"
  means it writes no log).
- Cut meta-commentary: "this page explains", "as we'll see", "note that", "it is worth noting", "here is why". The
  first lines of a page say what it covers. After that, no announcements.
- Do not make an abstract noun the actor of a sentence ("a reach is a set not yet read"). Say what the code does.
- Do not over-compress either. Keep articles and verbs, and use no arrows in prose. "Parser rejects bad date → exit
  2" becomes "The parser rejects a bad date and exits with code 2."
- Keep "every", "always" and "never" when they state a real rule of the code. Cut them when they only add weight.
- Use the active voice when the actor is known ("your handler calls `GrantAsync`"). The passive is fine when the
  actor does not matter.
- Write for a developer who reads English as a second language and scans. Use short words, and add a concrete
  example where a rule is abstract.
- Use one word per concept, the word the code uses. Explain jargon and abbreviations at first use with a small
  example: "row level security (RLS)", then "RLS".
- State nuances. Say what is configurable, who decides, and what the default is. Do not flatten a choice of the
  application into a rule of the package.
- Keep every fact when you rewrite. Clarity comes first, and shortening is no goal of its own.

| Claude prose | Plain |
|---|---|
| A role every new tenant gets a copy of, its own from then on. | Every new tenant gets a copy of this role. The tenant can change its copy. |
| The way back is closed, because its units would have to go. | A hierarchical tenant cannot become flat again. Its units would have to be deleted. |
| A permission set draws the screen and decides nothing. | Use the permission set to show buttons. The command checks the permission again when it runs. |

Self-check for each section: pick three sentences. Could one of them stand unchanged in another project's docs?
Does one of them need a second read? Does a colon join two clauses? Fix those.

### Commit messages, PR text, code comments, changelog

- The subject line says what changed, with an active verb and the API names: "[OnResource] and [AtUnit] take the
  permission as an argument". A slogan with a personified subject ("A save walks only what it writes") is wrong.
- The body is a bullet list with one fact per bullet.
- A code comment says only what the code does not show. "// Turns a RefusalException into a GraphQL error with the
  refusal code in extensions.code." replaces "// A refusal is an error with its code, as a route answers it."
- To fix the text of a pushed commit, rebuild the change on a new branch and open a new PR. Do not force-push.

### Names in code

- A row access rule says who may do what with which rows: `MembersSeeTheirBoards`, `ACustomerSeesTheirOrders`,
  `RoleManagersWriteBoardRoles`. The name must say something that the attribute does not say. `BoardRead` and
  `SeatsReadTheBoardsTheySee` fail that test.
- A request ends in `Query` or `Command`: `ListBoardQuery`, `EditBoardCommand`. Reading one item is `Get`, never
  `See`. The file takes the feature's name (`Boards/ListBoard/ListBoard.cs`), because it holds the request and its
  handler.
- A GraphQL field is a plain noun (`myPermissions`), never a modal verb such as `may`. A type is a domain noun, a
  reference takes the role (`owner: Seat`), and a list is plural.

### Docs and tutorials

- A tutorial opens with what we build: the tenants, units and people, who has which role where (a diagram and a
  table), and what the reader can try at the end.
- A domain's overview page gives the goal and the scope, then a class diagram of how the pieces connect, then each
  piece in one sentence with the sample's example. Detailed behaviour goes on its own page.
- Each tutorial step opens with where we are, why this step comes now and what it achieves. Then come whole files
  with the new lines highlighted (`{n-m}` after the language), then what those lines do.
- Introduce a concept in plain words with an example before the code that uses it. No concept appears for the
  first time inside a snippet.
- Tutorials are cumulative. A new feature is a later part that continues the same app ("Where we are: ..."). The
  earlier parts do not mention it.
- Put the file path in bold on its own line above a code block. Do not use `title=`, because the site gives such a
  block the "Generated" badge.
- Facts come from the code. When a page and the code disagree, fix the page.
- A new feature also goes into the samples and gets a diagram in the docs, in the same PR.

### Diagrams

Every Mermaid diagram has a visual hierarchy. Mark nodes with these four states, and copy only the classDefs that
the diagram uses:

```
    classDef strong fill:#5b2ec933,stroke:#5b2ec9,stroke-width:2px
    classDef good fill:#1f8a5b26,stroke:#1f8a5b
    classDef muted fill:none,stroke:#9a9ab5,stroke-dasharray:4 3,color:#9a9ab5
    classDef bad fill:#c93c3c24,stroke:#c93c3c
```

- `strong` marks what the diagram is about, `good` what is reached or allowed, `muted` what is left out or not part
  of the package (grey and dashed), and `bad` what is refused.
- A node with a thing and its state puts the name in bold on the first line and the state small on the second:
  `north["<b>North</b><br/><small>Rhea's grant</small>"]`.
- The sentence before the diagram says what it shows and what the colours mean ("purple: the grant; green:
  reached; grey: not reached").
- A class diagram has no multiplicity labels at the line ends (`"1"`, `"many"`). Put the multiplicity in the
  relation label: `Tenant --> Seat : many seats`.
- Keep labels short. Remove a diagram that only repeats the text next to it.
- Check every diagram in the built site. Aim for 10 to 12 px of text on screen, so flowcharts are wide and low and
  sequence messages are short.

### One word per concept

Code identifiers stay as they are. In prose and in comments, use the word in the first column.

| Use | Instead of |
|---|---|
| the access check ("the access check refuses the request") | the door, the gate, the bouncer |
| system work (`Caller.System`, `BeginSystem`) | the application's own work, the application itself |
| row level security (RLS) at first use on a page, then RLS | the second lock, the database's lock |
| starter role (say once per page that the code calls it `RolePack`) | pack |
| permission | key, right (the `SeatRights` table keeps its name) |
| caller | requester, user (when it means the caller) |
| refusal, with its refusal code (`tenancy.not-permitted`) | error code, rejection, denied |
| sign in, sign-in | log in, login (a Postgres login role keeps its name) |
| unit, and the root for the top unit | org unit, node, child unit |
| below, above ("North Coast is below North") | under, beneath, subtree, descendants |
| for the whole tenant, at the tenant | tenant-wide, globally |
| seat | membership, account |
| grant (`RoleGrant`) | assignment, role binding |
| the generator writes ... | magically, for free, automatically |
| diagnostic, with its id (`DDD00077`) | warning code, lint |
| member, members | roster |

### Examples and privacy

- Use the sample's world for examples: Harbor Works with the units North, North Coast and South Bay, and the people
  Ada, Rhea, Leo and Tove.
- Never name a client application, a client or the owner's private design scenarios in this repository. Use the
  sample's world or describe the case generically.
- The site restyles the diagram states in `website/src/css/custom.css`. The "Reach" diagram in
  `docs/tenancy/overview.md` shows the form to copy.
- Some tests read the docs. Before you rename a heading, run `grep -rn "<heading text>" Tests/`. When you rename
  one, fix every link to its anchor.
