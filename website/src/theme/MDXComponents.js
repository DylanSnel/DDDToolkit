/**
 * The theme's MDX components, with the site's own interactive ones beside them. A page in docs/ is Markdown and
 * imports nothing, so a component it shows on the site has to be one every page can use: src/remark/interactive.js
 * puts it where the page's marker is, by the name it has here.
 *
 * Every docs page loads this file, so a component is named here by a small wrapper that loads the component itself
 * only where a page draws it: each component and its data are a chunk of their own, which no other page downloads.
 * The site's pages are rendered on the server once every lazy part is ready, so the page's HTML holds the
 * component all the same, and the browser keeps that HTML until the chunk has come and the component takes over.
 */
import React, { lazy, Suspense } from 'react';
import MDXComponents from '@theme-original/MDXComponents';

const Walkthrough = lazy(() => import('@site/src/components/RequestWalkthrough'));
const Reach = lazy(() => import('@site/src/components/ReachExplorer'));
const BoardAccess = lazy(() => import('@site/src/components/BoardAccessExplorer'));

/** The walk-through of How a request is checked, loaded with that page alone. */
function RequestWalkthrough(props) {
  return (
    <Suspense fallback={<p>Loading the walk-through…</p>}>
      <Walkthrough {...props} />
    </Suspense>
  );
}

/** The reach explorer of the Tenancy overview, loaded with that page alone. */
function ReachExplorer(props) {
  return (
    <Suspense fallback={<p>Loading the explorer…</p>}>
      <Reach {...props} />
    </Suspense>
  );
}

/** The board access explorer of the Membership overview, loaded with that page alone. */
function BoardAccessExplorer(props) {
  return (
    <Suspense fallback={<p>Loading the explorer…</p>}>
      <BoardAccess {...props} />
    </Suspense>
  );
}

export default {
  ...MDXComponents,
  RequestWalkthrough,
  ReachExplorer,
  BoardAccessExplorer,
};
