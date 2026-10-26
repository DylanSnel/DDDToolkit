/**
 * The walk-through of docs/how-a-request-is-checked.md: pick a request, a caller and a situation, and follow the
 * request through the seven layers the page describes, from the attribute to the row.
 *
 * Everything it shows comes from walkthrough.json, and every line of code in that file is a line of the page, which
 * a test of DDDToolkit.Analyzers.Tests holds it to: the component is a way through the page, never a second source.
 * The page shows its answers as tables too, for GitHub and for the Markdown the site writes for language models,
 * where this component is not drawn.
 *
 * The choices are native radio buttons in fieldsets, so the keyboard moves through them as through any form, and the
 * answer is announced as it changes. The colours are the site's own variables, in light and in dark mode.
 */
import React, { useId, useState } from 'react';
import clsx from 'clsx';
import CodeBlock from '@theme/CodeBlock';
import walkthrough from './walkthrough.json';
import styles from './styles.module.css';

const { callers, layers, sets, scenarios, code } = walkthrough;

/** Prose with `code` spans, as the page writes it. */
function Said({ text }) {
  if (!text) {
    return null;
  }

  return text.split('`').map((part, index) => (index % 2 ? <code key={index}>{part}</code> : <React.Fragment key={index}>{part}</React.Fragment>));
}

/** One snippet of the page's code, drawn as the page draws a code block. */
function Snippet({ id, highlight, title }) {
  const snippet = code[id];
  return (
    <CodeBlock language={snippet.language} metastring={highlight} title={title}>
      {snippet.lines.join('\n')}
    </CodeBlock>
  );
}

/** A snippet, several, or none. */
function Snippets({ ids }) {
  const list = Array.isArray(ids) ? ids : ids ? [ids] : [];
  return list.map((id) => <Snippet key={id} id={id} />);
}

/** The first outcome of the scenario that fits the caller and the situation: the data is written most specific first. */
function outcomeOf(scenario, caller, chosen) {
  return scenario.outcomes.find(
    (outcome) => outcome.when.caller.includes(caller) && Object.entries(outcome.when).every(([key, value]) => key === 'caller' || chosen[key] === value),
  );
}

/** The first value of each of a scenario's situations. */
function defaultsOf(scenario) {
  return Object.fromEntries(scenario.options.map((option) => [option.id, option.values[0].id]));
}

/** What each of the seven layers shows for one request, caller, form and situation. */
function walk(scenario, caller, form, chosen) {
  const outcome = outcomeOf(scenario, caller.id, chosen);
  const set = sets[scenario.set];
  const door = outcome.door;
  const behaviorName = `${set.interface.replace(/^I|Request$/g, '')}AccessBehavior.g.cs`;

  const write = {
    state: 'info',
    label: form === 'attribute' ? 'attribute' : 'property',
    say: scenario.propertyOnly
      ? 'One caller or another: no stack of attributes says it, so the request answers the property `RequiredAccess` itself.'
      : form === 'attribute'
        ? 'The requirement as attributes, above the request: a marker on a member says where a permission is held, and the permission is a constant of the catalogue. This is how the sample writes it.'
        : 'The same requirement written as the property. The sample writes the attribute, and keeps the property for what no attribute says, such as one caller or another.',
    content: <Snippet id={scenario.write[form]} />,
  };

  const generated =
    form === 'attribute'
      ? {
          state: 'info',
          label: 'generated',
          say: `When the module compiles, the generator writes the call the attributes stand for into \`${set.interface}\`, one line per request, as the interface's own \`RequiredAccess\`. The behavior beside it asks the module's checks before every handler, and reads the property.`,
          content: (
            <>
              <Snippet id={scenario.generated.attribute} title={`${behaviorName}, the interface's part, shortened`} />
              <Snippet id="generated.behavior" title={`${behaviorName}, the behavior, shortened`} />
            </>
          ),
        }
      : {
          state: 'info',
          label: 'no line',
          say: `No line for \`${scenario.request}\`: it answers \`RequiredAccess\` itself, and C# takes a class's own over the interface's default. The behavior reads the property, whichever way it was said.`,
          content: <Snippet id="generated.behavior" title={`${behaviorName}, the behavior, shortened`} />,
        };

  const checks = {
    state: 'info',
    label: `${set.checks.length} checks`,
    say: scenario.setSay,
    content: (
      <ol className={styles.checks}>
        {set.checks.map((check, index) => {
          const decides = scenario.decides.includes(index);
          return (
            <li key={check.name} className={clsx(styles.checkRow, decides && styles.decides)}>
              <span className={styles.checkIndex}>{index}</span>
              <span className={styles.checkName}>
                <code>{check.name}</code>
                <small>{check.addedBy}</small>
              </span>
              <span className={styles.checkVerdict}>{decides ? 'decides it' : `decides ${check.decides}`}</span>
            </li>
          );
        })}
      </ol>
    ),
  };

  const check = {
    state: door.pass ? 'pass' : 'refuse',
    label: door.pass ? 'lets through' : 'refuses',
    say: door.say,
    content: <Snippet id={scenario.check.snippet} highlight={scenario.check.highlight} />,
  };

  const statement = outcome.statement.snippet
    ? {
        state: 'info',
        label: 'one statement',
        say: outcome.statement.say,
        content: (
          <>
            <p className={styles.note}>
              First the caller on the connection, as <code>{caller.runsAs}</code>:
            </p>
            <CodeBlock language="sql">{caller.connection}</CodeBlock>
            <Snippet id={outcome.statement.snippet} />
          </>
        ),
      }
    : { state: 'skip', label: 'no statement', say: outcome.statement.say };

  const doorLayer = door.pass
    ? {
        state: 'pass',
        label: 'let through',
        say: 'Let through. The request is in hand for the handler that runs next, and for the save it ends with.',
      }
    : {
        state: 'refuse',
        label: `${door.status} · ${door.code}`,
        say: `Refused with \`${door.code}\`, ${door.status}. The handler does not run, so nothing it would read or write reaches a table.`,
      };

  const shown = outcome.rows;
  let rows;
  if (!door.pass) {
    rows = {
      state: 'skip',
      label: 'not reached',
      say: shown.round ? `The handler does not run. ${shown.say}` : shown.say,
      round: shown.round ? shown.result : null,
      content: <Snippets ids={shown.snippet} />,
    };
  } else if (shown.code) {
    rows = { state: 'refuse', label: `${shown.status} · ${shown.code}`, say: shown.say, content: <Snippets ids={shown.snippet} /> };
  } else {
    rows = { state: shown.warn ? 'warn' : 'pass', label: shown.result, say: shown.say, content: <Snippets ids={shown.snippet} /> };
  }

  const verdict = !door.pass
    ? { state: 'refuse', big: `${door.status} · ${door.code}`, small: 'Refused at the door, before the handler.' }
    : shown.code
      ? { state: 'refuse', big: `${shown.status} · ${shown.code}`, small: 'Let through the door, then refused by the work behind it.' }
      : shown.warn
        ? { state: 'warn', big: shown.result, small: 'Let through the door, then stopped by the database: an error of the set-up, not a refusal.' }
        : { state: 'pass', big: shown.result, small: 'Let through the door, and the rows written or read.' };

  return { views: [write, generated, checks, check, statement, doorLayer, rows], verdict };
}

/** A group of radio buttons drawn as choices. */
function Choices({ legend, name, options, value, onChange, className, describedBy }) {
  return (
    <fieldset className={styles.group} aria-describedby={describedBy}>
      <legend className={styles.legend}>{legend}</legend>
      <div className={className}>
        {options.map((option) => (
          <label key={option.id} className={styles.choice}>
            <input
              type="radio"
              name={name}
              value={option.id}
              checked={value === option.id}
              disabled={option.disabled}
              onChange={() => onChange(option.id)}
            />
            <span className={styles.face}>
              <span className={styles.faceTitle}>{option.title}</span>
              {option.detail ? <span className={styles.faceDetail}>{option.detail}</span> : null}
            </span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

const stateText = { pass: '✓', refuse: '✕', skip: '–', info: '•', warn: '!' };

export default function RequestWalkthrough() {
  const id = useId();
  const [scenarioId, setScenarioId] = useState('add-crew-member');
  const [callerId, setCallerId] = useState('juno');
  const [preferred, setPreferred] = useState('attribute');
  const [situations, setSituations] = useState(() => Object.fromEntries(scenarios.map((scenario) => [scenario.id, defaultsOf(scenario)])));
  const [step, setStep] = useState(3);
  const [everyLayer, setEveryLayer] = useState(false);

  const scenario = scenarios.find((each) => each.id === scenarioId);
  const caller = callers.find((each) => each.id === callerId);
  const form = scenario.propertyOnly ? 'property' : preferred;
  const chosen = situations[scenario.id];
  const { views, verdict } = walk(scenario, caller, form, chosen);
  const shownLayers = everyLayer ? layers.map((layer, index) => index) : [step];

  return (
    <section className={styles.walkthrough} aria-labelledby={`${id}-title`}>
      <div className={styles.header}>
        <p className={styles.eyebrow}>Walk-through</p>
        <p className={styles.title} id={`${id}-title`}>
          From request to row
        </p>
        <p className={styles.lede}>
          Six requests, four of them the sample's own, and six callers in harbor. Pick one of each, set the situation, and step through the seven layers below.
        </p>
      </div>

      <div className={styles.controls}>
        <Choices
          legend="Request"
          name={`${id}-request`}
          className={styles.requests}
          value={scenarioId}
          onChange={setScenarioId}
          options={scenarios.map((each) => ({ id: each.id, title: each.request, detail: each.summary }))}
        />
        <p className={clsx(styles.who, styles.summary)}>
          <Said text={scenario.summary} />
        </p>

        <Choices
          legend="Caller"
          name={`${id}-caller`}
          className={styles.chips}
          value={callerId}
          onChange={setCallerId}
          describedBy={`${id}-who`}
          options={callers.map((each) => ({ id: each.id, title: each.name }))}
        />
        <p className={styles.who} id={`${id}-who`}>
          <Said text={caller.who} /> Runs as <code>{caller.runsAs}</code>.
        </p>

        <div className={styles.situations}>
          <Choices
            legend="Written as"
            name={`${id}-form`}
            className={styles.segments}
            value={form}
            onChange={setPreferred}
            options={[
              { id: 'attribute', title: 'Attribute', disabled: scenario.propertyOnly },
              { id: 'property', title: 'Property' },
            ]}
          />
          {scenario.options.map((option) => (
            <Choices
              key={`${scenario.id}-${option.id}`}
              legend={option.label}
              name={`${id}-${scenario.id}-${option.id}`}
              className={styles.segments}
              value={chosen[option.id]}
              onChange={(value) => setSituations((all) => ({ ...all, [scenario.id]: { ...all[scenario.id], [option.id]: value } }))}
              options={option.values.map((value) => ({ id: value.id, title: value.label }))}
            />
          ))}
        </div>
      </div>

      <div className={clsx(styles.verdict, styles[verdict.state])} role="status">
        <span className={styles.light} aria-hidden="true" />
        <span className={styles.verdictBig}>{verdict.big}</span>
        <span className={styles.verdictSmall}>
          {caller.name} · <code>{scenario.request}</code> · {verdict.small}
        </span>
      </div>

      <nav aria-label="The seven layers">
        <ol className={styles.track}>
          {layers.map((layer, index) => (
            <li key={layer.id}>
              <button
                type="button"
                className={clsx(styles.stop, styles[views[index].state], index === step && styles.current)}
                aria-current={index === step ? 'step' : undefined}
                onClick={() => setStep(index)}>
                <span className={styles.dot} aria-hidden="true">
                  {index + 1}
                </span>
                <span className={styles.visuallyHidden}>Layer {index + 1}: </span>
                <span className={styles.stopTitle}>{layer.short}</span>
                <span className={styles.stopState}>
                  <span aria-hidden="true">{stateText[views[index].state]} </span>
                  {views[index].label}
                </span>
              </button>
            </li>
          ))}
        </ol>
      </nav>

      <div className={styles.stepnav}>
        <button type="button" className={styles.navButton} onClick={() => setStep((at) => Math.max(0, at - 1))} disabled={step === 0}>
          ← Previous layer
        </button>
        <span className={styles.position}>
          Layer {step + 1} of {layers.length}
        </span>
        <button type="button" className={styles.navButton} onClick={() => setStep((at) => Math.min(layers.length - 1, at + 1))} disabled={step === layers.length - 1}>
          Next layer →
        </button>
        <label className={styles.every}>
          <input type="checkbox" checked={everyLayer} onChange={(event) => setEveryLayer(event.target.checked)} /> Show every layer
        </label>
      </div>

      {shownLayers.map((index) => {
        const layer = layers[index];
        const view = views[index];
        return (
          <article key={layer.id} className={clsx(styles.card, styles[view.state], everyLayer && index === step && styles.current)} aria-labelledby={`${id}-${layer.id}`}>
            <div className={styles.cardHead}>
              <h3 className={styles.cardTitle} id={`${id}-${layer.id}`}>
                {index + 1} · {layer.title}
              </h3>
              <span className={styles.where}>{layer.where}</span>
              <span className={styles.pill}>
                <span aria-hidden="true">{stateText[view.state]} </span>
                {view.label}
              </span>
            </div>
            <p className={styles.say}>
              <Said text={view.say} />
            </p>
            {view.round ? (
              <p className={styles.note}>
                Round the door, as <code>{caller.runsAs}</code>, Postgres answers: <strong>{view.round}</strong>.
              </p>
            ) : null}
            {view.content}
          </article>
        );
      })}
    </section>
  );
}
