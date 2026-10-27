// Code in a table may hold zero-width spaces where it is allowed to break (src/rehype/scrollingTables.js). A
// reader who copies a name gets it without them, so it pastes as the name it is.
if (typeof document !== 'undefined') {
  document.addEventListener('copy', (event) => {
    const text = document.getSelection()?.toString() ?? '';
    if (!text.includes('\u200B') || !event.clipboardData) {
      return;
    }

    event.clipboardData.setData('text/plain', text.replace(/\u200B/g, ''));
    event.preventDefault();
  });
}
