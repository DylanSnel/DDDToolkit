// The docs live in ../docs and read well on GitHub too, where a link to ../Examples/README.md or to a
// source file simply opens it. The site only has the docs, so a link that leaves the docs folder is
// turned into a link to that file on GitHub; links between docs are left for Docusaurus to resolve.
const fs = require('fs');
const path = require('path');

const repository = 'https://github.com/DylanSnel/DDDToolkit';
const branch = 'main';

function visit(node, callback) {
  callback(node);
  for (const child of node.children ?? []) {
    visit(child, callback);
  }
}

module.exports = function githubLinks({ docsDir, repoRoot }) {
  return (tree, file) => {
    visit(tree, (node) => {
      if (node.type !== 'link' || !node.url || /^[a-z]+:|^#|^\//i.test(node.url)) {
        return;
      }

      const [target, hash] = node.url.split('#');
      const resolved = path.resolve(path.dirname(file.path), target);
      if (resolved.startsWith(docsDir + path.sep) || resolved === docsDir) {
        return;
      }

      const relative = path.relative(repoRoot, resolved).split(path.sep).join('/');
      const kind = fs.existsSync(resolved) && fs.statSync(resolved).isDirectory() ? 'tree' : 'blob';
      node.url = `${repository}/${kind}/${branch}/${relative}${hash ? `#${hash}` : ''}`;
    });
  };
};
