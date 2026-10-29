// A table holds code that cannot wrap, such as a check's class name, so it can be wider than the column. Drawn
// as a table, it then ran over the table of contents beside it. Each table goes in a box of its own that
// scrolls sideways, so a wide one scrolls inside its border and a narrow one still fills the column.
//
// Code in a cell may break after a dot, where a reader expects it (EntityFrameworkChecks. / ToolkitWiredCheck),
// so a column of names stays narrow and the column of sentences keeps room. The break is a zero-width space: the
// code stays one string, which Docusaurus needs to draw it inline (an element inside would make it a code block),
// and src/clientModules/copyWithoutBreaks.js takes the spaces out of what a reader copies.
const BREAK = String.fromCharCode(0x200b);

function breakAfterDots(code) {
  for (const child of code.children ?? []) {
    if (child.type === 'text') {
      child.value = child.value.replace(/\.(?=\S)/g, '.' + BREAK);
    }
  }
}

function inCells(node, inCell) {
  for (const child of node.children ?? []) {
    if (child.type !== 'element') {
      continue;
    }

    if (inCell && child.tagName === 'code') {
      breakAfterDots(child);
    } else {
      inCells(child, inCell || child.tagName === 'td' || child.tagName === 'th');
    }
  }
}

function wrap(node) {
  for (const [index, child] of (node.children ?? []).entries()) {
    if (child.type === 'element' && child.tagName === 'table') {
      inCells(child, false);
      node.children[index] = {
        type: 'element',
        tagName: 'div',
        properties: { className: ['table-scroll'] },
        children: [child],
      };
    } else {
      wrap(child);
    }
  }
}

module.exports = function scrollingTables() {
  return (tree) => wrap(tree);
};
