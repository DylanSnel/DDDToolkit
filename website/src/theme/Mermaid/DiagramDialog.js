/**
 * A diagram full size, in a dialog over the page. The wheel zooms around the pointer, dragging moves the diagram,
 * and the buttons zoom, fit it to the dialog again, or close. Escape, the close button or a click beside the
 * diagram closes it. The page underneath does not scroll while it is open.
 */
import React, {useCallback, useEffect, useLayoutEffect, useRef, useState} from 'react';
import {createPortal} from 'react-dom';
import styles from './styles.module.css';

const MIN_SCALE = 0.2;
const MAX_SCALE = 8;
const STEP = 1.25;

function clamp(scale) {
  return Math.min(MAX_SCALE, Math.max(MIN_SCALE, scale));
}

// The copy gets ids of its own: Mermaid scopes its styles and its arrowheads by the SVG's id, and the diagram on the
// page keeps that id.
function withOwnIds(svg) {
  const match = /<svg[^>]*\sid="([^"]+)"/.exec(svg);
  return match ? svg.split(match[1]).join(`${match[1]}-full`) : svg;
}

export default function DiagramDialog({svg, onClose}) {
  const stage = useRef(null);
  const content = useRef(null);
  const drag = useRef(null);
  const [view, setView] = useState({scale: 1, x: 0, y: 0});

  // Fits the diagram into the dialog, at most at its own size, centred.
  const fit = useCallback(() => {
    const box = stage.current?.getBoundingClientRect();
    const drawing = content.current?.querySelector('svg');
    if (!box || !drawing) {
      return;
    }
    const width = drawing.viewBox.baseVal?.width || drawing.getBBox().width;
    const height = drawing.viewBox.baseVal?.height || drawing.getBBox().height;
    drawing.setAttribute('width', width);
    drawing.setAttribute('height', height);
    drawing.style.maxWidth = 'none';
    const scale = clamp(Math.min(box.width / width, box.height / height) * 0.95);
    setView({scale, x: (box.width - width * scale) / 2, y: (box.height - height * scale) / 2});
  }, []);

  useLayoutEffect(() => {
    content.current.innerHTML = withOwnIds(svg);
    fit();
  }, [svg, fit]);

  useEffect(() => {
    const onKey = (event) => event.key === 'Escape' && onClose();
    const overflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    window.addEventListener('keydown', onKey);
    return () => {
      document.body.style.overflow = overflow;
      window.removeEventListener('keydown', onKey);
    };
  }, [onClose]);

  // Zooms by factor around a point of the stage, so what is under the pointer stays there.
  const zoomAt = useCallback((factor, px, py) => {
    setView((current) => {
      const scale = clamp(current.scale * factor);
      const ratio = scale / current.scale;
      return {scale, x: px - (px - current.x) * ratio, y: py - (py - current.y) * ratio};
    });
  }, []);

  const zoomCentre = (factor) => {
    const box = stage.current.getBoundingClientRect();
    zoomAt(factor, box.width / 2, box.height / 2);
  };

  // A wheel listener of its own, not passive, so the page underneath does not scroll instead.
  useEffect(() => {
    const element = stage.current;
    const onWheel = (event) => {
      event.preventDefault();
      const box = element.getBoundingClientRect();
      zoomAt(event.deltaY < 0 ? STEP : 1 / STEP, event.clientX - box.left, event.clientY - box.top);
    };
    element.addEventListener('wheel', onWheel, {passive: false});
    return () => element.removeEventListener('wheel', onWheel);
  }, [zoomAt]);

  const onPointerDown = (event) => {
    if (event.button !== 0) {
      return;
    }
    // A drag moves the diagram; it does not select its text.
    event.preventDefault();
    event.currentTarget.setPointerCapture(event.pointerId);
    drag.current = {x: event.clientX, y: event.clientY};
  };
  const onPointerMove = (event) => {
    if (!drag.current) {
      return;
    }
    const dx = event.clientX - drag.current.x;
    const dy = event.clientY - drag.current.y;
    drag.current = {x: event.clientX, y: event.clientY};
    setView((current) => ({...current, x: current.x + dx, y: current.y + dy}));
  };
  const onPointerUp = () => {
    drag.current = null;
  };

  return createPortal(
    <div className={styles.backdrop} onClick={onClose} role="presentation">
      <div
        className={styles.dialog}
        role="dialog"
        aria-modal="true"
        aria-label="Diagram, full size"
        onClick={(event) => event.stopPropagation()}>
        <div className={styles.toolbar}>
          <button type="button" onClick={() => zoomCentre(STEP)} title="Zoom in" aria-label="Zoom in">+</button>
          <button type="button" onClick={() => zoomCentre(1 / STEP)} title="Zoom out" aria-label="Zoom out">−</button>
          <button type="button" onClick={fit} title="Fit" aria-label="Fit the diagram to the dialog">Fit</button>
          <button type="button" onClick={onClose} title="Close" aria-label="Close">×</button>
        </div>
        <div
          ref={stage}
          className={styles.stage}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerCancel={onPointerUp}>
          <div
            ref={content}
            className={`docusaurus-mermaid-container ${styles.content}`}
            style={{transform: `translate(${view.x}px, ${view.y}px) scale(${view.scale})`}}
          />
        </div>
      </div>
    </div>,
    document.body,
  );
}
