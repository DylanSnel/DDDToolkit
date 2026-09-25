// Writes the docs out for language models, beside the site the build produces:
//
//   llms.txt         an index of every page in the sidebar's order, in the llmstxt.org format
//   llms-full.txt    every page in that order, in one file
//   docs/<page>.md   each page as Markdown, at the page's own URL with .md appended
//
// The pages are docs/ as written, with one change: a relative link is made absolute, to the Markdown of
// another page or to the file on GitHub, because a model reading one file has nothing to resolve a
// relative link against. A page's description in llms.txt is its row in the README's documentation
// table, so the two indexes cannot drift apart: a page in the sidebar without a row fails the build.
const fs = require('fs');
const path = require('path');
const { githubUrl } = require('../remark/githubLinks');

function read(file) {
  return fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
}

/** The sidebar as sections of doc ids: one per category, and one for the pages before the first. */
function sections(sidebar) {
  const result = [];
  let loose = null;
  for (const item of sidebar) {
    if (typeof item === 'string') {
      if (!loose) {
        loose = { label: 'Start here', ids: [] };
        result.push(loose);
      }
      loose.ids.push(item);
    } else if (item.type === 'category' && item.items.every((child) => typeof child === 'string')) {
      loose = null;
      result.push({ label: item.label, ids: item.items });
    } else {
      throw new Error(`llms.txt understands doc ids and categories of doc ids, not ${JSON.stringify(item)}.`);
    }
  }
  return result;
}

/** What the README's documentation table says each page covers, by doc id. */
function descriptions(readme) {
  const result = new Map();
  for (const [, id, description] of readme.matchAll(/^\| \[[^\]]+\]\(docs\/([\w-]+)\.md\) \| (.+?) \|$/gm)) {
    result.set(id, description);
  }
  return result;
}

/** The README's first paragraph, which says what the toolkit is. */
function introduction(readme) {
  return readme.replace(/^# .*\n+/, '').split(/\n\s*\n/)[0].replace(/\s*\n\s*/g, ' ').trim();
}

module.exports = function llms(context, { docsDir, repoRoot, routeBasePath, sidebar }) {
  const siteUrl = `${context.siteConfig.url}${context.siteConfig.baseUrl}`.replace(/\/$/, '');
  const docsUrl = `${siteUrl}/${routeBasePath}`;
  const markdownUrl = (id) => `${docsUrl}/${id}.md`;

  function absolute(url, file) {
    if (/^[a-z][a-z0-9+.-]*:|^\//i.test(url)) {
      return url;
    }
    if (url.startsWith('#')) {
      return markdownUrl(path.basename(file, '.md')) + url;
    }

    const [target, hash] = url.split('#');
    const resolved = path.resolve(path.dirname(file), target);
    if (resolved.startsWith(docsDir + path.sep) && resolved.endsWith('.md')) {
      const id = path.relative(docsDir, resolved).split(path.sep).join('/').replace(/\.md$/, '');
      return markdownUrl(id) + (hash ? `#${hash}` : '');
    }
    return githubUrl(resolved, hash, repoRoot);
  }

  /** The page with every inline link outside a code block made absolute. */
  function page(id) {
    const file = path.join(docsDir, `${id}.md`);
    let fenced = false;
    const markdown = read(file)
      .split('\n')
      .map((line) => {
        if (/^\s*(```|~~~)/.test(line)) {
          fenced = !fenced;
          return line;
        }
        return fenced ? line : line.replace(/\]\(([^)\s]+)\)/g, (_, url) => `](${absolute(url, file)})`);
      })
      .join('\n');
    return { id, title: markdown.match(/^# (.+)$/m)?.[1] ?? id, markdown };
  }

  return {
    name: 'llms-txt',

    async postBuild({ outDir }) {
      const readme = read(path.join(repoRoot, 'README.md'));
      const described = descriptions(readme);
      const groups = sections(sidebar).map(({ label, ids }) => ({ label, pages: ids.map(page) }));
      const pages = groups.flatMap((group) => group.pages);

      const missing = pages.filter(({ id }) => !described.has(id)).map(({ id }) => id);
      if (missing.length > 0) {
        throw new Error(`llms.txt takes each page's description from the README's documentation table, which has no row for: ${missing.join(', ')}.`);
      }

      const header = [`# ${context.siteConfig.title}`, `> ${introduction(readme)}`];

      const index = [
        ...header,
        [
          `Every page below is Markdown at the address given, and [llms-full.txt](${siteUrl}/llms-full.txt) holds all of them in one file, in this order.`,
          `Build errors whose id starts with DDD come from the toolkit's generators and analyzers; [Diagnostics](${markdownUrl('diagnostics')}) says what each one means and how to fix it.`,
          `An AI coding agent can also install the [DDDToolkit skill](${githubUrl(path.join(repoRoot, 'skills', 'dddtoolkit'), null, repoRoot)}), which carries the rules that matter most while writing code.`,
        ].join(' '),
        ...groups.map(({ label, pages: grouped }) => [
          `## ${label}`,
          grouped.map(({ id, title }) => `- [${title}](${markdownUrl(id)}): ${described.get(id)}`).join('\n'),
        ].join('\n\n')),
      ];

      fs.mkdirSync(path.join(outDir, routeBasePath), { recursive: true });
      for (const { id, markdown } of pages) {
        fs.writeFileSync(path.join(outDir, routeBasePath, `${id}.md`), markdown);
      }
      fs.writeFileSync(path.join(outDir, 'llms.txt'), `${index.join('\n\n')}\n`);
      fs.writeFileSync(path.join(outDir, 'llms-full.txt'), `${[...header, ...pages.map(({ markdown }) => markdown.trim())].join('\n\n')}\n`);
    },
  };
};
