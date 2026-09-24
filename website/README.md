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
