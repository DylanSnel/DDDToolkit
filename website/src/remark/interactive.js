// A page in docs/ is Markdown, which GitHub renders as it is, so it cannot import a React component. Where the
// site shows a component on a page, the page marks the place with a <div> that names it, around what a reader
// gets instead on GitHub and in the Markdown for language models:
//
//   <div data-interactive="RequestWalkthrough">
//
//   > [!TIP]
//   > On the docs site this is a walk-through you can click through. Everything it shows is on this page.
//
//   </div>
//
// On the site the <div>, and everything up to the </div> that closes it, become the component. The component
// comes from the site's MDX components (src/theme/MDXComponents.js), which every page can use. Only a component
// named in this plugin's options may be used, so a typo fails the build rather than leaving a blank place, and so
// does a marker that is never closed. The blank lines inside the <div> are what makes GitHub read the Markdown in it.

/** The line that opens a marker, with the component it names. */
const opening = /^<div data-interactive="([A-Za-z][A-Za-z0-9]*)">$/;

/** The line that closes it. */
const closing = /^<\/div>$/;

/**
 * The page's Markdown with the markers' own lines taken out and what they hold kept: what a reader without the
 * site's components reads. Code blocks are left alone, so a page may show a marker as an example.
 */
function withoutMarkers(markdown) {
  let fenced = false;
  let open = false;
  let dropped = false;
  const kept = [];
  for (const line of markdown.split('\n')) {
    if (/^\s*(```|~~~)/.test(line)) {
      fenced = !fenced;
    }

    const marker = !fenced && (open ? closing.test(line) : opening.test(line));
    if (marker) {
      open = !open;
      dropped = true;
      continue;
    }

    // The blank line after a marker's own line goes with it, where a blank line is kept before it already.
    if (dropped && line === '' && kept.at(-1) === '') {
      dropped = false;
      continue;
    }

    dropped = false;
    kept.push(line);
  }

  return kept.join('\n');
}

module.exports = function interactive({ components }) {
  const allowed = new Set(components);

  return (tree, file) => {
    const children = tree.children;
    for (let index = 0; index < children.length; index++) {
      const node = children[index];
      const match = node.type === 'html' ? opening.exec(node.value.trim()) : null;
      if (!match) {
        continue;
      }

      const name = match[1];
      if (!allowed.has(name)) {
        throw new Error(`${file.path}: <div data-interactive="${name}"> names a component the site does not have; it has ${[...allowed].join(', ')}.`);
      }

      const end = children.findIndex((candidate, at) => at > index && candidate.type === 'html' && closing.test(candidate.value.trim()));
      if (end < 0) {
        throw new Error(`${file.path}: <div data-interactive="${name}"> is never closed by a </div> of its own line.`);
      }

      children.splice(index, end - index + 1, {
        type: 'mdxJsxFlowElement',
        name,
        attributes: [],
        children: [],
      });
    }
  };
};

module.exports.withoutMarkers = withoutMarkers;
