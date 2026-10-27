/**
 * The chooser of docs/tenancy/keeping-tenants-apart.md, under Which call for which situation: pick one of eight
 * situations, and see which call or attribute starts the work, why it fits, what the work holds, how Tenancy records
 * it, which database role it runs as on Postgres, what to watch out for, and a handler that does it.
 *
 * Everything it shows comes from whoActs.json, which a test of DDDToolkit.Analyzers.Tests holds to the page and the
 * code: the title, the call, what the work holds, how it is recorded and the database role are the cells of the page's
 * table, every call is a member of TenancyAccessManager, every recorded kind is one of TenancyActorKinds, and every link
 * goes to a heading that exists. The page keeps that table inside the marker, for GitHub and for the Markdown the site
 * writes for language models, where this component is not drawn.
 *
 * The situations are native radio buttons in a fieldset, so the keyboard moves through them as through any form, and
 * the call to use is announced as it changes. The colours are the site's own variables, in light and in dark mode. The
 * code example scrolls inside its own box, so the page never scrolls sideways on a phone.
 */
import React, { useId, useState } from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import CodeBlock from '@theme/CodeBlock';
import data from './whoActs.json';
import styles from './styles.module.css';

const { situations } = data;

/** The word a recorded kind is shown with, beside its colour. */
const kinds = {
  seat: 'A seat',
  operator: 'An operator',
  system: 'System work',
  token: 'A link with a token',
};

/** Prose with `code` spans, as the docs write it. */
function Said({ text }) {
  if (!text) {
    return null;
  }

  return text.split('`').map((part, index) => (index % 2 ? <code key={index}>{part}</code> : <React.Fragment key={index}>{part}</React.Fragment>));
}

export default function WhoActs() {
  const id = useId();
  const [situationId, setSituationId] = useState(situations[0].id);
  const situation = situations.find((each) => each.id === situationId);

  const facts = [
    { label: 'Acts in', value: situation.actsIn },
    { label: 'Holds', value: situation.holds },
    { label: 'Recorded as', value: situation.recordedAs, detail: situation.recordedDetail },
    { label: 'On Postgres, runs as', value: situation.postgres, detail: situation.postgresDetail },
  ];

  return (
    <section className={styles.chooser} aria-labelledby={`${id}-title`}>
      <div className={styles.header}>
        <p className={styles.eyebrow}>Chooser</p>
        <p className={styles.title} id={`${id}-title`}>
          Who acts
        </p>
        <p className={styles.lede}>Pick a situation to see which call or attribute starts the work, and what follows from it.</p>
      </div>

      <fieldset className={styles.group}>
        <legend className={styles.legend}>Situation</legend>
        <div className={styles.chips}>
          {situations.map((each) => (
            <label key={each.id} className={styles.choice}>
              <input
                type="radio"
                name={`${id}-situation`}
                value={each.id}
                checked={situationId === each.id}
                onChange={() => setSituationId(each.id)}
              />
              <span className={styles.face}>{each.label}</span>
            </label>
          ))}
        </div>
      </fieldset>

      <div className={clsx(styles.use, styles[situation.kind])} role="status">
        <span className={styles.useLabel}>{situation.title}</span>
        <span className={styles.useCall}>
          <Said text={situation.use} />
        </span>
        <span className={styles.kind}>
          <span className={styles.swatch} aria-hidden="true" /> Recorded as {kinds[situation.kind].toLowerCase()}
        </span>
      </div>

      <div className={styles.body}>
        <p className={styles.story}>
          <Said text={situation.story} />
        </p>

        <div className={styles.part}>
          <p className={styles.partTitle}>Why this one</p>
          <ul className={styles.list}>
            {situation.why.map((line) => (
              <li key={line}>
                <Said text={line} />
              </li>
            ))}
          </ul>
        </div>

        <dl className={styles.facts}>
          {facts.map((fact) => (
            <div key={fact.label} className={styles.fact}>
              <dt>{fact.label}</dt>
              <dd>
                <Said text={fact.value} />
                {fact.detail ? (
                  <span className={styles.detail}>
                    <Said text={fact.detail} />
                  </span>
                ) : null}
              </dd>
            </div>
          ))}
        </dl>

        <div className={styles.part}>
          <p className={styles.partTitle}>Watch out</p>
          <ul className={styles.list}>
            {situation.notes.map((line) => (
              <li key={line}>
                <Said text={line} />
              </li>
            ))}
          </ul>
        </div>

        <div className={styles.code}>
          <CodeBlock language="csharp">{situation.code.join('\n')}</CodeBlock>
        </div>

        {situation.links.length > 0 ? (
          <p className={styles.more}>
            Read more:{' '}
            {situation.links.map((link, index) => (
              <React.Fragment key={link.to}>
                {index > 0 ? ', ' : null}
                <Link to={link.to}>{link.text}</Link>
              </React.Fragment>
            ))}
            .
          </p>
        ) : null}
      </div>
    </section>
  );
}
