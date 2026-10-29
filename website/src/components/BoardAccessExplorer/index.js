/**
 * The explorer of docs/membership/overview.md, under Who holds a permission on a board: pick a caller, a board and a
 * permission, and see each of the five ways a caller holds a permission on a board, yes or no and why, and the answer
 * the access check gives the tutorial's request for that permission.
 *
 * Everything it shows comes from boards.json, which a test of DDDToolkit.Analyzers.Tests holds to the overview, the
 * tutorial and the code: the component is a way through the tutorial's world, never a second source. The five ways
 * are Membership's (DDDToolkit.Supporting.Membership.EntityFramework, EfMemberQuestions): the owner, a member now, a
 * role now, the permission held at the board's unit or above, and system work in the module's scope. A caller who
 * sees the board as owner, as member, from above with the permission every member holds, or as system work, and holds
 * the permission in none of the five ways, gets not-permitted; a caller who does not see it gets not-found. The page
 * keeps its table of the five ways for GitHub and for the Markdown the site writes for language models.
 *
 * The choices are native radio buttons in fieldsets, so the keyboard moves through them as through any form, and the
 * answer is announced as it changes. The colours are the site's own variables, in light and in dark mode, and every
 * yes and no has a mark and a word beside its colour.
 */
import React, { useId, useState } from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import data from './boards.json';
import styles from './styles.module.css';

const { units, organizationRoles, permissions, boardRoles, callers, boards, refusals, tried, tutorial } = data;

const unitOf = (id) => units.find((unit) => unit.id === id);
const callerOf = (id) => callers.find((caller) => caller.id === id);
const everyMember = permissions.find((permission) => permission.everyMember);

/** Whether a unit is the unit `above` or below it. */
function isAtOrBelow(unitId, aboveId) {
  for (let at = unitOf(unitId); at; at = at.parent ? unitOf(at.parent) : null) {
    if (at.id === aboveId) {
      return true;
    }
  }

  return false;
}

/** "North", "North and North Coast", "A, B and C". */
function listed(names) {
  return names.length < 2 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names.at(-1)}`;
}

/** A unit's name, with "the root" beside it for the root. */
const named = (unitId) => (unitOf(unitId).parent === null ? `${unitOf(unitId).name}, the root` : unitOf(unitId).name);

/** Prose with `code` spans, as the docs write it. */
function Said({ text }) {
  if (!text) {
    return null;
  }

  return text.split('`').map((part, index) => (index % 2 ? <code key={index}>{part}</code> : <React.Fragment key={index}>{part}</React.Fragment>));
}

/** Whether an organization role gives a permission. */
function orgGives(roleName, permission) {
  const role = organizationRoles.find((each) => each.name === roleName);
  return role.every || role.permissions.includes(permission);
}

/** The caller's grants that give a permission at the board's unit or above it. */
function aboveOf(caller, board, permission) {
  return caller.grants.filter((grant) => orgGives(grant.role, permission) && isAtOrBelow(board.unit, grant.unit));
}

/** Each of the five ways, yes or no and why, for one caller, board and permission. */
function waysOf(caller, board, permission) {
  const owner = callerOf(board.owner);
  const isOwner = board.owner === caller.id;
  const membership = board.members.find((member) => member.caller === caller.id);
  const isMember = isOwner || Boolean(membership);
  const p = `\`${permission.name}\``;
  const ways = [];

  // 1. The owner.
  if (caller.system) {
    ways.push({ title: 'Is the owner', yes: false, why: 'System work owns no board.' });
  } else if (isOwner) {
    ways.push({
      title: 'Is the owner',
      yes: permission.owner,
      why: permission.owner
        ? `${caller.name} owns ${board.name}. ${p} is marked for the owner, so the owner holds it.`
        : `${caller.name} owns ${board.name}. ${p} is marked \`Owner = false\`, so owning the board does not give it.`,
    });
  } else {
    ways.push({ title: 'Is the owner', yes: false, why: `${owner.name} owns ${board.name}.` });
  }

  // 2. A member now.
  if (caller.system) {
    ways.push({ title: 'Is a member now', yes: false, why: "System work is nobody's member." });
  } else if (isMember) {
    const as = isOwner ? ' The owner is always a member.' : '';
    ways.push({
      title: 'Is a member now',
      yes: permission.everyMember,
      why: permission.everyMember
        ? `${caller.name} is a member of ${board.name} now.${as} Every member holds ${p}.`
        : `${caller.name} is a member of ${board.name} now.${as} Being a member gives \`${everyMember.name}\` only.`,
    });
  } else {
    ways.push({ title: 'Is a member now', yes: false, why: `${caller.name} is not a member of ${board.name}.` });
  }

  // 3. A role on the board now.
  const roleTitle = 'Holds a role on the board now';
  if (caller.system) {
    ways.push({ title: roleTitle, yes: false, why: 'System work holds no role.' });
  } else if (isOwner) {
    ways.push({ title: roleTitle, yes: false, why: `${caller.name} is the owner. The owner holds no role.` });
  } else if (!membership || membership.roles.length === 0) {
    ways.push({ title: roleTitle, yes: false, why: `${caller.name} holds no role on ${board.name}.` });
  } else {
    const held = membership.roles.map((name) => boardRoles.find((role) => role.name === name));
    const giving = held.filter((role) => role.permissions.includes(permission.name));
    const holds = `${caller.name} holds the role ${listed(held.map((role) => role.name))} on ${board.name}.`;
    const lists = held.map((role) => `The role ${role.name} gives ${listed(role.permissions.map((each) => `\`${each}\``))}.`).join(' ');
    ways.push({
      title: roleTitle,
      yes: permission.inRoles && giving.length > 0,
      why: !permission.inRoles
        ? `${holds} No role gives ${p}. The permission is marked \`InRoles = false\`.`
        : `${holds} ${lists}`,
    });
  }

  // 4. The permission at the board's unit, or above it.
  const aboveTitle = "Holds the permission at the board's unit or above";
  const hangs = `${board.name} hangs at ${named(board.unit)}.`;
  const above = aboveOf(caller, board, permission.name);
  if (caller.system) {
    ways.push({ title: aboveTitle, yes: false, why: 'System work holds no role in the organization.' });
  } else if (!permission.fromAbove) {
    ways.push({ title: aboveTitle, yes: false, why: `${p} is marked \`FromAbove = false\`, so a permission held above does not count.` });
  } else if (above.length > 0) {
    const grant = above[0];
    const role = organizationRoles.find((each) => each.name === grant.role);
    ways.push({
      title: aboveTitle,
      yes: true,
      why: [
        hangs,
        `${caller.name} holds ${grant.role} at ${named(grant.unit)}.`,
        role.every ? `${grant.role} gives every permission.` : `${grant.role} gives ${listed(role.permissions.map((each) => `\`${each}\``))}.`,
        grant.unit === board.unit ? '' : `${unitOf(board.unit).name} is below ${unitOf(grant.unit).name}.`,
      ]
        .filter(Boolean)
        .join(' '),
    });
  } else if (caller.grants.length === 0) {
    ways.push({ title: aboveTitle, yes: false, why: `${hangs} ${caller.name} holds no role in the organization.` });
  } else {
    const giving = caller.grants.filter((grant) => orgGives(grant.role, permission.name));
    ways.push({
      title: aboveTitle,
      yes: false,
      why:
        giving.length === 0
          ? `${hangs} ${caller.name} holds ${listed(caller.grants.map((grant) => `${grant.role} at ${unitOf(grant.unit).name}`))}. None of these roles gives ${p}.`
          : `${hangs} ${caller.name} holds ${p} at ${listed(giving.map((grant) => unitOf(grant.unit).name))}. ${unitOf(board.unit).name} is not below ${listed(giving.map((grant) => unitOf(grant.unit).name))}.`,
    });
  }

  // 5. System work in the module's scope.
  ways.push({
    title: "Is system work in the module's scope",
    yes: Boolean(caller.system),
    why: caller.system
      ? `The rules let the module's own scope, \`${caller.system}\`, through. System work in \`${caller.system}\` holds every permission on every board.`
      : `${caller.name} is a person who signed in.`,
  });

  // Whether the caller sees the board at all: as the owner, as a member, from above with the permission every member
  // holds, or as system work.
  const seesAbove = !caller.system && aboveOf(caller, board, everyMember.name)[0];
  const sees = caller.system
    ? 'System work sees every board.'
    : isOwner
      ? `${caller.name} sees ${board.name} as its owner.`
      : isMember
        ? `${caller.name} sees ${board.name} as a member.`
        : seesAbove
          ? `${caller.name} sees ${board.name} from above, with \`${everyMember.name}\` at ${unitOf(seesAbove.unit).name}.`
          : null;

  const viaMembers = ways[0].yes || ways[1].yes || ways[2].yes;
  const via = caller.system ? 'as system work' : viaMembers ? 'through membership' : ways[3].yes ? 'from above' : null;

  return { ways, sees, via, both: viaMembers && ways[3].yes };
}

/** The answer of the access check, from the five ways. */
function verdictOf(caller, board, permission, { sees, via, both }) {
  if (via) {
    return {
      state: 'pass',
      big: 'Let through',
      reasons: [
        caller.system
          ? `System work in \`${caller.system}\` holds every permission on every board.`
          : `${caller.name} holds \`${permission.name}\` on ${board.name} ${via}.`,
        both ? `${caller.name} also holds it from above. A permission held as a member comes first.` : '',
        'The access check lets the request through, and the handler runs.',
      ].filter(Boolean),
    };
  }

  if (!sees) {
    const refusal = refusals.notFound;
    return {
      state: 'refuse',
      big: `${refusal.status} · ${refusal.code}`,
      reasons: [
        `${caller.name} does not see ${board.name}. ${caller.name} holds the permission in none of the five ways.`,
        `The answer is the same as for a board that does not exist, so nobody learns which boards exist.`,
      ],
    };
  }

  const refusal = refusals.notPermitted;
  return {
    state: 'refuse',
    big: `${refusal.status} · ${refusal.code}`,
    reasons: [sees, `${caller.name} holds \`${permission.name}\` in none of the five ways.`],
  };
}

/** "Step 7: members" as the site's anchor for it. */
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
              {option.detail ? <span className={styles.faceDetail}>{option.detail}</span> : null}
            </span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

/** What the caller picker says about a caller. */
function whoOf(caller) {
  if (caller.system) {
    return `System work is work that no person does, such as writing seed data. This system work runs in the module's own scope, \`${caller.system}\`.`;
  }

  const seat = `${caller.name} has a seat in Harbor Works, placed at ${unitOf(caller.placedAt).name}.`;
  return caller.grants.length === 0
    ? `${seat} ${caller.name} holds no role in the organization.`
    : `${seat} ${caller.name} holds ${listed(caller.grants.map((grant) => `${grant.role} at ${named(grant.unit)}`))}.`;
}

/** What the board picker says about a board. */
function detailOf(board) {
  const members = board.members.map((member) => `${callerOf(member.caller).name} is a member with the role ${listed(member.roles)}.`);
  return [`Hangs at ${unitOf(board.unit).name}.`, `${callerOf(board.owner).name} owns it.`, ...members].join(' ');
}

const mark = { true: '✓', false: '✕' };

export default function BoardAccessExplorer() {
  const id = useId();
  const [callerId, setCallerId] = useState('leo');
  const [boardId, setBoardId] = useState('crane');
  const [permissionName, setPermissionName] = useState('boards.members.manage');

  const caller = callerOf(callerId);
  const board = boards.find((each) => each.id === boardId);
  const permission = permissions.find((each) => each.name === permissionName);
  const found = waysOf(caller, board, permission);
  const verdict = verdictOf(caller, board, permission, found);
  const tryOf = tried.find((each) => each.caller === caller.id && each.board === board.id && each.permission === permission.name);

  return (
    <section className={styles.explorer} aria-labelledby={`${id}-title`}>
      <div className={styles.header}>
        <p className={styles.eyebrow}>Explorer</p>
        <p className={styles.title} id={`${id}-title`}>
          Board access
        </p>
        <p className={styles.lede}>
          The boards and people are the Membership tutorial's, at its end, as the overview draws them. Pick a caller, a board and a permission.
          One query checks all five ways.
        </p>
      </div>

      <div className={styles.controls}>
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
          <Said text={whoOf(caller)} />
        </p>

        <Choices
          legend="Board"
          name={`${id}-board`}
          className={styles.boards}
          value={boardId}
          onChange={setBoardId}
          options={boards.map((each) => ({ id: each.id, title: each.name, detail: detailOf(each) }))}
        />

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
          <Said
            text={`${permission.description}. The tutorial's request \`${permission.request.name}\` requires it on the board. Its route is \`${permission.request.route}\`.`}
          />
        </p>
      </div>

      <ol className={styles.ways} aria-label="The five ways">
        {found.ways.map((way, index) => (
          <li key={way.title} className={clsx(styles.way, way.yes ? styles.yes : styles.no)}>
            <span className={styles.wayIndex}>{index + 1}</span>
            <span className={styles.wayTitle}>{way.title}</span>
            <span className={styles.wayAnswer}>
              <span aria-hidden="true">{mark[way.yes]} </span>
              {way.yes ? 'yes' : 'no'}
            </span>
            <span className={styles.wayWhy}>
              <Said text={way.why} />
            </span>
          </li>
        ))}
      </ol>

      <p className={styles.sees}>
        <span className={styles.seesLabel}>Sees the board</span>{' '}
        <Said
          text={
            found.sees
              ? `Yes. ${found.sees}`
              : `No. ${caller.name} is not the owner or a member of ${board.name}. ${caller.name} does not hold \`${everyMember.name}\` at ${unitOf(board.unit).name} or above it.`
          }
        />
      </p>

      <div className={clsx(styles.verdict, styles[verdict.state])} role="status">
        <span className={styles.light} aria-hidden="true" />
        <span className={styles.verdictBig}>{verdict.big}</span>
        <span className={styles.verdictSmall}>
          {caller.name} · <code>{permission.request.name}</code> on {board.name} · <code>{permission.request.route}</code>
        </span>
        <div className={styles.reasons}>
          {verdict.reasons.map((reason) => (
            <p key={reason}>
              <Said text={reason} />
            </p>
          ))}
          {tryOf ? (
            <p className={styles.tried}>
              <Link to={`${tutorial}#${anchorOf(tryOf.step)}`}>{tryOf.step.split(':')[0]} of the tutorial</Link> sends this request. The response is{' '}
              <code>{tryOf.status}</code>
              {tryOf.code ? (
                <>
                  , <code>{tryOf.code}</code>
                </>
              ) : null}
              .
            </p>
          ) : null}
        </div>
      </div>
    </section>
  );
}
