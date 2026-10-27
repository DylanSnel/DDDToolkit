/**
 * The explorer of docs/tenancy/overview.md, under Reach: pick a person and a permission, and the units of the two
 * tenants of the Tenancy tutorial show where the person holds the permission. Pick a unit, and one of the tutorial's
 * requests goes there, with the answer the access check gives.
 *
 * Everything it shows comes from reach.json, which a test of DDDToolkit.Analyzers.Tests holds to the tutorial, the
 * overview and the code: the component is a way through the tutorial's world, never a second source. The page keeps
 * its static diagram inside the marker, for GitHub and for the Markdown the site writes for language models, where
 * this component is not drawn.
 *
 * The choices are native radio buttons in fieldsets, the units of the tree too, so the keyboard moves through them as
 * through any form, and the answer is announced as it changes. The colours are the site's own variables, in light and
 * in dark mode, and every state has a word beside its colour.
 */
import React, { useId, useState } from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import reach from './reach.json';
import styles from './styles.module.css';

const { tenants, roles, people, permissions, refusals, tried, tutorial } = reach;

/** Every unit by its id, with the tenant it belongs to. */
const units = new Map(tenants.flatMap((tenant) => tenant.units.map((unit) => [unit.id, { ...unit, tenant: tenant.id }])));

const tenantOf = (id) => tenants.find((tenant) => tenant.id === id);

const rootOf = (tenant) => units.get(tenant.units.find((unit) => unit.parent === null).id);

/** Whether a unit is the unit `above` or below it. */
function isAtOrBelow(unitId, aboveId) {
  for (let at = units.get(unitId); at; at = at.parent ? units.get(at.parent) : null) {
    if (at.id === aboveId) {
      return true;
    }
  }

  return false;
}

/** Whether a role contains a permission. */
function contains(roleName, permission) {
  const role = roles.find((each) => each.name === roleName);
  return role.every || role.permissions.includes(permission);
}

/** "North", "North and North Coast", "A, B and C". */
function listed(names) {
  return names.length < 2 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names.at(-1)}`;
}

/** Prose with `code` spans, as the docs write it. */
function Said({ text }) {
  if (!text) {
    return null;
  }

  return text.split('`').map((part, index) => (index % 2 ? <code key={index}>{part}</code> : <React.Fragment key={index}>{part}</React.Fragment>));
}

/** The grants that count for a person and a permission: the person's grants whose role contains it, unless they ended. */
function counting(person, permission, ended) {
  return ended ? [] : person.grants.filter((grant) => contains(grant.role, permission));
}

/** What a unit shows for a person and a permission: purple at a grant, green below one, grey elsewhere. */
function stateOf(unit, person, permission, ended) {
  if (unit.tenant !== person.tenant) {
    return { kind: 'muted', note: `${person.name} has no seat here` };
  }

  const here = person.grants.find((grant) => grant.unit === unit.id);
  if (here && ended) {
    return { kind: 'muted', note: `${here.role}, ended yesterday` };
  }

  if (here && !contains(here.role, permission)) {
    return { kind: 'muted', note: `${here.role}, without this permission` };
  }

  if (here) {
    return { kind: 'granted', note: `${here.role} granted here` };
  }

  const from = counting(person, permission, ended).find((grant) => isAtOrBelow(unit.id, grant.unit));
  return from ? { kind: 'reached', note: `reached from ${units.get(from.unit).name}` } : { kind: 'muted', note: 'not reached' };
}

/** The units of the person's tenant where the person holds the permission. */
function reachOf(person, permission, ended) {
  const grants = counting(person, permission, ended);
  return tenantOf(person.tenant).units.filter((unit) => grants.some((grant) => isAtOrBelow(unit.id, grant.unit)));
}

/** The rows under the tree: which role, granted where, gives the permission, and where it counts. */
function summaryOf(person, permission, ended) {
  const tenant = tenantOf(person.tenant);
  const root = rootOf(tenant);
  const at = (unitId) => (unitId === root.id ? `at the root, ${root.name}` : `at ${units.get(unitId).name}`);

  const granted = listed(person.grants.map((grant) => `${grant.role}, ${at(grant.unit)}${ended ? ', until yesterday' : ''}`));

  const gives = person.grants.map((grant) => {
    const role = roles.find((each) => each.name === grant.role);
    if (role.every) {
      return `Yes. ${role.name} contains every permission.`;
    }

    return role.permissions.includes(permission)
      ? `Yes. ${role.name} contains it.`
      : `No. ${role.name} contains ${listed(role.permissions.map((each) => `\`${each}\``))} only.`;
  });

  const reached = reachOf(person, permission, ended);
  const holds = ended
    ? 'No unit. A grant counts from its start until its end, so a grant that ended yesterday gives nothing today.'
    : reached.length === 0
      ? 'No unit.'
      : reached.length === tenant.units.length
        ? `Every unit of ${tenant.name}. A permission held at the root is held at the tenant.`
        : `${listed(reached.map((unit) => unit.name))}.`;

  const elsewhere = tenants
    .filter((other) => other.id !== tenant.id)
    .map((other) => ({ label: other.name, value: `No seat. ${person.name}'s grants count at no unit of ${other.name}.` }));

  return [
    { label: 'Role, granted at', value: granted },
    { label: `Gives \`${permission}\``, value: gives.join(' ') },
    { label: 'Holds the permission at', value: holds },
    ...elsewhere,
  ];
}

/** One of the tutorial's requests, sent by a person for a unit: what the access check answers, and why. */
function send(person, permission, unit, ended) {
  const tenant = tenantOf(unit.tenant);
  const request = permission.request;
  const what = request.phrase ? `${request.name} ${request.phrase} ${unit.name}` : `${request.name} in ${tenant.name}`;
  const sent = { what, route: request.route, header: `Tenant: ${tenant.slug}` };

  if (person.tenant !== tenant.id) {
    const refusal = refusals.notSeated;
    return {
      ...sent,
      state: 'refuse',
      big: `${refusal.status} · ${refusal.code}`,
      reasons: [
        `${person.name} has no seat in ${tenant.name}.`,
        `The access check refuses the request with \`${refusal.code}\`. A tenant that does not exist gets the same answer, so a caller cannot find out which tenants exist.`,
      ],
    };
  }

  const root = rootOf(tenant);
  const checkedAt = request.at === 'tenant' ? root : unit;
  const looks =
    request.at === 'tenant'
      ? `${request.name} requires \`${permission.name}\` for the whole tenant. The access check looks for it at ${root.name}, the root.`
      : `${request.name} requires \`${permission.name}\` ${request.where}. The access check looks for it at ${unit.name}.`;
  const grants = counting(person, permission.name, ended);
  const holding = grants.find((grant) => isAtOrBelow(checkedAt.id, grant.unit));

  if (holding) {
    const from = units.get(holding.unit);
    return {
      ...sent,
      state: 'pass',
      big: 'Let through',
      reasons: [
        looks,
        from.id === checkedAt.id
          ? `${person.name} holds ${holding.role} at ${from.name}.`
          : `${checkedAt.name} is below ${from.name}, so the grant of ${holding.role} at ${from.name} counts at ${checkedAt.name}.`,
        'The access check lets the request through, and the handler runs.',
      ],
    };
  }

  const refusal = refusals.notPermitted;
  const giving = person.grants.filter((grant) => contains(grant.role, permission.name));
  let why;
  if (ended && giving.length > 0) {
    why = person.grants.map((grant) => `${person.name}'s grant of ${grant.role} at ${units.get(grant.unit).name} ended yesterday. A grant that has ended gives nothing.`);
  } else if (giving.length === 0) {
    why = [
      ...person.grants.map((grant) => `${grant.role} does not contain \`${permission.name}\`.`),
      `${person.name} holds \`${permission.name}\` at no unit.`,
    ];
  } else {
    why = giving.flatMap((grant) => {
      const from = units.get(grant.unit);
      return [`${person.name}'s grant reaches ${from.name} and the units below it.`, `${checkedAt.name} is not below ${from.name}.`];
    });
  }

  return { ...sent, state: 'refuse', big: `${refusal.status} · ${refusal.code}`, reasons: [looks, ...why] };
}

/** The tutorial's own try of this request, where it has one: the same person, permission and unit, or tenant. */
function triedOf(person, permission, unit) {
  return tried.find(
    (each) =>
      each.person === person.id &&
      each.permission === permission.name &&
      (permission.request.phrase ? each.unit === unit.id : units.get(each.unit).tenant === unit.tenant),
  );
}

/** "Step 9: a role at a unit" as the site's anchor for it. */
const anchorOf = (heading) => heading.toLowerCase().replace(/[^a-z0-9 -]/g, '').replace(/ /g, '-');

/** A group of radio buttons drawn as choices. */
function Choices({ legend, name, options, value, onChange, className, describedBy }) {
  return (
    <fieldset className={styles.group} aria-describedby={describedBy}>
      <legend className={styles.legend}>{legend}</legend>
      <div className={className}>
        {options.map((option) => (
          <label key={option.id} className={styles.choice}>
            <input type="radio" name={name} value={option.id} checked={value === option.id} onChange={() => onChange(option.id)} />
            <span className={styles.face}>
              <span className={styles.faceTitle}>{option.title}</span>
            </span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

/** The units below `parent` in a tenant, each a radio button, with the units below it. */
function Branch({ tenant, parent, name, chosen, onChoose, stateOfUnit }) {
  const below = tenant.units.filter((unit) => unit.parent === parent);
  if (below.length === 0) {
    return null;
  }

  return (
    <ul className={parent ? styles.children : styles.roots}>
      {below.map((each) => {
        const unit = units.get(each.id);
        const state = stateOfUnit(unit);
        return (
          <li key={unit.id} className={clsx(styles.branch, styles[state.kind])}>
            <label className={styles.unit}>
              <input type="radio" name={name} value={unit.id} checked={chosen === unit.id} onChange={() => onChoose(unit.id)} />
              <span className={clsx(styles.node, styles[state.kind])}>
                <span className={styles.nodeName}>{unit.name}</span>
                <span className={styles.nodeNote}>{state.note}</span>
              </span>
              {chosen === unit.id ? (
                <span className={styles.pin} aria-hidden="true">
                  ← request
                </span>
              ) : null}
            </label>
            <Branch tenant={tenant} parent={unit.id} name={name} chosen={chosen} onChoose={onChoose} stateOfUnit={stateOfUnit} />
          </li>
        );
      })}
    </ul>
  );
}

export default function ReachExplorer() {
  const id = useId();
  const [personId, setPersonId] = useState('rhea');
  const [permissionName, setPermissionName] = useState('tenancy.units.manage');
  const [unitId, setUnitId] = useState('south-bay');
  const [endedChosen, setEndedChosen] = useState(false);

  const person = people.find((each) => each.id === personId);
  const permission = permissions.find((each) => each.name === permissionName);
  const unit = units.get(unitId);
  const ended = endedChosen && !person.onlyAdministrator;
  const tenant = tenantOf(person.tenant);
  const grantNames = listed(person.grants.map((grant) => grant.role));

  const summary = summaryOf(person, permission.name, ended);
  const verdict = send(person, permission, unit, ended);
  const tryOf = triedOf(person, permission, unit);

  return (
    <section className={styles.explorer} aria-labelledby={`${id}-title`}>
      <div className={styles.header}>
        <p className={styles.eyebrow}>Explorer</p>
        <p className={styles.title} id={`${id}-title`}>
          Where a grant reaches
        </p>
        <p className={styles.lede}>
          The tenants, units and people are the Tenancy tutorial's, at its end. Pick a person and a permission. The units show where the person
          holds the permission. Pick a unit to send one of the tutorial's requests there.
        </p>
      </div>

      <div className={styles.controls}>
        <Choices
          legend="Person"
          name={`${id}-person`}
          className={styles.chips}
          value={personId}
          onChange={setPersonId}
          describedBy={`${id}-who`}
          options={people.map((each) => ({ id: each.id, title: each.name }))}
        />
        <p className={styles.who} id={`${id}-who`}>
          {person.name} has a seat in {tenant.name}, placed at {units.get(person.placedAt).name}. {person.name} holds{' '}
          {listed(person.grants.map((grant) => `${grant.role} at ${units.get(grant.unit).name}`))}.
        </p>

        <Choices
          legend="Permission"
          name={`${id}-permission`}
          className={clsx(styles.chips, styles.mono)}
          value={permissionName}
          onChange={setPermissionName}
          describedBy={`${id}-what`}
          options={permissions.map((each) => ({ id: each.name, title: each.name }))}
        />
        <p className={styles.who} id={`${id}-what`}>
          <Said text={`${permission.description}. The tutorial's request \`${permission.request.name}\` requires it ${permission.request.where}.`} />
        </p>

        <div className={styles.toggle}>
          <label className={clsx(styles.check, person.onlyAdministrator && styles.disabled)}>
            <input
              type="checkbox"
              checked={ended}
              disabled={person.onlyAdministrator}
              aria-describedby={`${id}-ended`}
              onChange={(event) => setEndedChosen(event.target.checked)}
            />{' '}
            {person.name}'s grant of {grantNames} ended yesterday
          </label>
          <p className={styles.who} id={`${id}-ended`}>
            {person.onlyAdministrator
              ? `${person.name} is the only administrator of ${tenant.name}. The last administrator of a tenant cannot be removed, so the box is off for ${person.name}.`
              : `A grant counts from its start until its end. ${ended ? 'The grant now has an end date in the past.' : 'Tick the box to give the grant an end date in the past.'}`}
          </p>
        </div>
      </div>

      <fieldset className={styles.group}>
        <legend className={styles.legend}>Unit for the request</legend>
        <ul className={styles.key} aria-label="What the colours mean">
          <li className={styles.granted}>
            <span className={styles.swatch} aria-hidden="true" /> granted here
          </li>
          <li className={styles.reached}>
            <span className={styles.swatch} aria-hidden="true" /> reached, below a grant
          </li>
          <li className={styles.muted}>
            <span className={styles.swatch} aria-hidden="true" /> not reached
          </li>
        </ul>
        <div className={styles.tenants}>
          {tenants.map((each) => (
            <div key={each.id} className={styles.tenant}>
              <p className={styles.tenantName}>
                <span className={styles.tenantKind}>Tenant</span> {each.name}
              </p>
              <Branch
                tenant={each}
                parent={null}
                name={`${id}-unit`}
                chosen={unitId}
                onChoose={setUnitId}
                stateOfUnit={(at) => stateOf(at, person, permission.name, ended)}
              />
            </div>
          ))}
        </div>
      </fieldset>

      <dl className={styles.summary}>
        {summary.map((row) => (
          <div key={row.label} className={styles.summaryRow}>
            <dt>
              <Said text={row.label} />
            </dt>
            <dd>
              <Said text={row.value} />
            </dd>
          </div>
        ))}
      </dl>

      <div className={clsx(styles.verdict, styles[verdict.state])} role="status">
        <span className={styles.light} aria-hidden="true" />
        <span className={styles.verdictBig}>{verdict.big}</span>
        <span className={styles.verdictSmall}>
          {person.name} · {verdict.what} · <code>{verdict.route}</code> with <code>{verdict.header}</code>
        </span>
        <div className={styles.reasons}>
          {verdict.reasons.map((reason) => (
            <p key={reason}>
              <Said text={reason} />
            </p>
          ))}
          {!permission.request.phrase ? (
            <p>
              <Said text={`\`${permission.request.name}\` names no unit. Every unit of ${tenantOf(unit.tenant).name} sends the same request.`} />
            </p>
          ) : null}
          {tryOf ? (
            <p className={styles.tried}>
              <Link to={`${tutorial}#${anchorOf(tryOf.step)}`}>{tryOf.step.split(':')[0]} of the tutorial</Link> sends this request.
              {tryOf.status ? (
                <>
                  {' '}
                  The response is <code>{tryOf.status}</code>
                  {tryOf.code ? (
                    <>
                      , <code>{tryOf.code}</code>
                    </>
                  ) : null}
                  .
                </>
              ) : null}
            </p>
          ) : null}
        </div>
      </div>
    </section>
  );
}
