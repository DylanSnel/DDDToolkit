/**
 * The theme's Mermaid component (@docusaurus/theme-mermaid 3.10.2), swizzled for two changes.
 *
 * 1. The diagram's SVG is written into its container by an effect on every render result, rather than through
 *    dangerouslySetInnerHTML. Why: the theme renders a diagram again, under the same id, when the colour mode
 *    settles, which on a light system is from the default dark to light. mermaid.render first removes the element
 *    with that id from the page, which is the SVG React wrote for the first result. The second result is often the
 *    same string, and React writes an unchanged dangerouslySetInnerHTML once only, so the container stayed empty: in
 *    light mode about half the diagrams were blank boxes, without an error. Writing the SVG for every result puts it
 *    back whatever the string.
 * 2. A button opens the diagram full size, in a dialog over the page, where it can be zoomed and moved
 *    (see DiagramDialog.js).
 *
 * Copyright (c) Facebook, Inc. and its affiliates, for the parts copied from the theme; MIT licensed.
 */
import React, {useLayoutEffect, useRef, useState} from 'react';
import ErrorBoundary from '@docusaurus/ErrorBoundary';
import {ErrorBoundaryErrorMessageFallback} from '@docusaurus/theme-common';
import {
  MermaidContainerClassName,
  useMermaidRenderResult,
} from '@docusaurus/theme-mermaid/client';
import DiagramDialog from './DiagramDialog';
import styles from './styles.module.css';

function MermaidRenderResult({renderResult}) {
  const ref = useRef(null);
  const [open, setOpen] = useState(false);
  useLayoutEffect(() => {
    const div = ref.current;
    div.innerHTML = renderResult.svg;
    renderResult.bindFunctions?.(div);
  }, [renderResult]);
  return (
    <div className={styles.frame}>
      <button
        type="button"
        className={styles.expand}
        onClick={() => setOpen(true)}
        title="Show full size"
        aria-label="Show the diagram full size">
        <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
          <path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
        </svg>
      </button>
      <div ref={ref} className={`${MermaidContainerClassName} ${styles.container}`} />
      {open && <DiagramDialog svg={renderResult.svg} onClose={() => setOpen(false)} />}
    </div>
  );
}

function MermaidRenderer({value}) {
  const renderResult = useMermaidRenderResult({text: value});
  if (renderResult === null) {
    return null;
  }
  return <MermaidRenderResult renderResult={renderResult} />;
}

export default function Mermaid(props) {
  return (
    <ErrorBoundary
      fallback={(params) => <ErrorBoundaryErrorMessageFallback {...params} />}>
      <MermaidRenderer {...props} />
    </ErrorBoundary>
  );
}
