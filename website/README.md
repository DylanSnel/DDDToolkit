# DDDToolkit docs site

The Docusaurus site for DDDToolkit, published to https://dylansnel.github.io/DDDToolkit/ by the Docs
workflow on every push to `main`. The pages themselves are the repository's `docs/` folder: write there,
and the site and GitHub show the same text. A link from a doc to a file outside `docs/`, such as
`../Examples/README.md`, becomes a link to that file on GitHub (`src/remark/githubLinks.js`).

```bash
cd website
npm ci
npm start          # http://localhost:3000/DDDToolkit/, reloads as docs/ changes
npm run build      # what the workflow builds; broken links fail it
```

The sidebar's order is `sidebars.js`: a new page in `docs/` has to be added there.

The code on the homepage is compiled, not typed. `sample/` is a small project the generators run over,
and `npm run generate-sample` builds it and writes what they produced to `src/data/generated.json`,
which is committed so the site build needs no .NET SDK. Run it after changing a generator or the
sample.

In the docs, a code block with a title is generated code: ```` ```csharp title="Order.g.cs, shortened" ````
renders with a "Generated" label (`src/css/custom.css`). Give a title to generated files only, and copy
their content from a real build rather than writing it by hand.
