import clsx from 'clsx';
import Link from '@docusaurus/Link';
import Heading from '@theme/Heading';
import styles from './styles.module.css';

const features = [
  {
    title: 'Building blocks',
    to: '/docs/value-objects',
    description:
      'Identifiers, value objects with their always-valid twins, entities and aggregates, invariants that name what broke, and domain events, all generated from one attribute.',
  },
  {
    title: 'Modular monoliths',
    to: '/docs/modules',
    description:
      'Modules that publish contracts and never their domain, enforced by analyzers. An outbox and an inbox per module, and integration events that cross in process today and over pgmq, Wolverine or MassTransit tomorrow.',
  },
  {
    title: 'Integrations',
    to: '/docs/entity-framework',
    description:
      'Entity Framework mapping and migrations exported for Supabase, HotChocolate GraphQL with Relay node ids, and one schema over the modules, composed by Fusion in process.',
  },
];

function Feature({ title, to, description }) {
  return (
    <div className={clsx('col col--4')}>
      <div className="padding-horiz--md padding-vert--md">
        <Heading as="h3">
          <Link to={to}>{title}</Link>
        </Heading>
        <p>{description}</p>
      </div>
    </div>
  );
}

export default function HomepageFeatures() {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className="row">
          {features.map((props) => (
            <Feature key={props.title} {...props} />
          ))}
        </div>
      </div>
    </section>
  );
}
