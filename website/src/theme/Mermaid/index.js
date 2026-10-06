/**
 * The theme's Mermaid component (@docusaurus/theme-mermaid 3.10.2), swizzled for one change: the diagram's SVG is
 * written into its container by an effect on every render result, rather than through dangerouslySetInnerHTML.
 *
 * Why: the theme renders a diagram again, under the same id, when the colour mode settles, which on a light
 * system is from the default dark to light. mermaid.render first removes the element with that id from the page,
 * which is the SVG React wrote for the first result. The second result is often the same string, and React writes
 * an unchanged dangerouslySetInnerHTML once only, so the container stayed empty: in light mode about half the
 * diagrams were blank boxes, without an error. Writing the SVG for every result puts it back whatever the string.
 *
 * Copyright (c) Facebook, Inc. and its affiliates, for the parts copied from the theme; MIT licensed.
 */
import React, {useLayoutEffect, useRef} from 'react';
import ErrorBoundary from '@docusaurus/ErrorBoundary';
import {ErrorBoundaryErrorMessageFallback} from '@docusaurus/theme-common';
import {
  MermaidContainerClassName,
  useMermaidRenderResult,
} from '@docusaurus/theme-mermaid/client';
import styles from './styles.module.css';

function MermaidRenderResult({renderResult}) {
  const ref = useRef(null);
  useLayoutEffect(() => {
    const div = ref.current;
    div.innerHTML = renderResult.svg;
    renderResult.bindFunctions?.(div);
  }, [renderResult]);
  return <div ref={ref} className={`${MermaidContainerClassName} ${styles.container}`} />;
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
