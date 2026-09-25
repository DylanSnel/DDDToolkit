// GitHub draws a blockquote that opens with [!NOTE] as a note banner; Docusaurus would show the marker
// as text. Each such blockquote becomes the container directive Docusaurus builds its admonitions from,
// so a note in docs/ is a note on the site as well.
const kinds = {
  NOTE: 'note',
  TIP: 'tip',
  IMPORTANT: 'info',
  WARNING: 'warning',
  CAUTION: 'danger',
};

function visit(node, callback) {
  callback(node);
  for (const child of node.children ?? []) {
    visit(child, callback);
  }
}

module.exports = function githubAlerts() {
  return (tree) => {
    visit(tree, (node) => {
      const paragraph = node.type === 'blockquote' ? node.children[0] : undefined;
      const text = paragraph?.type === 'paragraph' ? paragraph.children[0] : undefined;
      const marker = text?.type === 'text' ? /^\[!(\w+)\][ \t]*(?:\r?\n|$)/.exec(text.value) : null;
      const kind = marker && kinds[marker[1].toUpperCase()];
      if (!kind) {
        return;
      }

      text.value = text.value.slice(marker[0].length);
      if (!text.value) {
        paragraph.children.shift();
      }
      if (paragraph.children.length === 0) {
        node.children.shift();
      }

      node.type = 'containerDirective';
      node.name = kind;
      node.attributes = {};
    });
  };
};
