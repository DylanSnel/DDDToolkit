import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useBaseUrl from '@docusaurus/useBaseUrl';
import Layout from '@theme/Layout';
import CodeBlock from '@theme/CodeBlock';
import Heading from '@theme/Heading';

import styles from './index.module.css';

const example = `[AggregateRoot<Guid>("ORD")]
public partial class Order
{
    public Order(OrderId id, CustomerId customer) : base(id)
    {
        Customer = customer;
        RaiseDomainEvent(new OrderPlaced(id, customer));
    }

    public CustomerId Customer { get; private set; }

    public partial IReadOnlyList<OrderLine> Lines { get; }

    public sealed class MustHaveLines : IInvariant<Order>
    {
        public InvariantFailure? Check(Order order)
            => order.Lines.Count == 0
                ? "An order has at least one line."
                : null;
    }
}`;

const generated = [
  ['OrderId', 'an allocation-free identifier with parsing, comparison and JSON'],
  ['Order : AggregateRoot', 'its base class, a concurrency version and a domain event list only it can write to'],
  ['_lines', 'a backing field Entity Framework maps while callers see a read-only list'],
  ['MustHaveLines', 'checked before every save, and reported by name when it breaks'],
];

const blocks = [
  {
    title: 'Identifiers',
    to: '/docs/identifiers',
    text: 'Typed ids from one attribute: no primitive obsession, no hand-written converters for EF, JSON or GraphQL.',
    icon: 'M4 7h16M4 12h10M4 17h7M18 14l3 3-3 3',
  },
  {
    title: 'Value objects',
    to: '/docs/value-objects',
    text: 'Structural equality, validation that names what is wrong, and an always-valid twin the compiler keeps honest.',
    icon: 'M12 3l8 4.5v9L12 21l-8-4.5v-9L12 3zM12 12l8-4.5M12 12v9M12 12L4 7.5',
  },
  {
    title: 'Aggregates and invariants',
    to: '/docs/invariants',
    text: 'Rules as named classes inside the aggregate, run before every save, with a code a client can act on.',
    icon: 'M12 3l7 4v5c0 4.5-3 7.5-7 9-4-1.5-7-4.5-7-9V7l7-4zM9 12l2 2 4-4',
  },
  {
    title: 'Domain events',
    to: '/docs/domain-events',
    text: 'Raised by the aggregate, dispatched with the save, stable names that survive a rename.',
    icon: 'M13 3L4 14h7l-1 7 9-11h-7l1-7z',
  },
  {
    title: 'Modules and integration events',
    to: '/docs/modules',
    text: 'Boundaries the analyzers enforce, an outbox and an inbox per module, contracts that version.',
    icon: 'M4 4h7v7H4zM13 13h7v7h-7zM11 7.5h4.5V13M13 16.5H8.5V11',
  },
  {
    title: 'GraphQL',
    to: '/docs/graphql',
    text: 'Ids as scalars and Relay node ids, rules as errors with codes, one schema over the modules with Fusion.',
    icon: 'M12 3l7.8 4.5v9L12 21l-7.8-4.5v-9L12 3zM12 3v18M4.2 7.5l15.6 9M19.8 7.5l-15.6 9',
  },
];

const packages = [
  ['DDDToolkit', 'Base types and the core generators.'],
  ['DDDToolkit.EntityFramework', 'Mapping, conventions, event dispatch, the outbox and the inbox.'],
  ['DDDToolkit.EntityFramework.Supabase', 'Migrations exported for supabase db push and branching.'],
  ['DDDToolkit.HotChocolate', 'GraphQL bindings, node ids, errors with codes, subscriptions.'],
  ['DDDToolkit.HotChocolate.Fusion.InMemory', 'One schema over a modular monolith, composed in process.'],
  ['DDDToolkit.Messaging.Postgres', 'pgmq: a queue in the database the outbox already writes to.'],
  ['DDDToolkit.Messaging.Wolverine', 'Wolverine as the transport between outbox and inbox.'],
  ['DDDToolkit.Messaging.MassTransit', 'MassTransit 8 as that transport.'],
  ['DDDToolkit.Mediator', 'Domain events dispatched through Mediator.'],
  ['DDDToolkit.FluentValidation', 'A generated validator per value object.'],
  ['DDDToolkit.Localization', 'Failures phrased in the reader’s language.'],
  ['DDDToolkit.Testing', 'Assertions for events, invariants and value objects.'],
];

const hosts = [
  { name: 'Modular monolith', detail: 'Supabase or SQL Server', transport: 'in process' },
  { name: 'Services over pgmq', detail: 'one Postgres, a queue per service', transport: 'pgmq' },
  { name: 'Services over RabbitMQ', detail: 'a database per service', transport: 'Wolverine' },
  { name: 'Services over RabbitMQ', detail: 'SQL Server per service', transport: 'MassTransit' },
];

function Icon({ d }) {
  return (
    <svg className={styles.icon} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d={d} />
    </svg>
  );
}

function Hero() {
  return (
    <header className={styles.hero}>
      <div className={styles.heroGlow} aria-hidden="true" />
      <div className={clsx('container', styles.heroInner)}>
        <div className={styles.heroText}>
          <img className={styles.heroLogo} src={useBaseUrl('/img/logo.svg')} alt="" />
          <span className={styles.badge}>Source generators for .NET 10</span>
          <Heading as="h1" className={styles.heroTitle}>
            Domain-Driven Design,
            <br />
            <span className={styles.gradientText}>without the boilerplate.</span>
          </Heading>
          <p className={styles.heroLead}>
            Declare an aggregate, a value object or an identifier with one attribute. The generator writes the base type,
            equality, persistence mapping and API conversions, and analyzers keep your modules apart.
          </p>
          <div className={styles.install}>
            <span className={styles.prompt}>$</span>
            <code>dotnet add package DDDToolkit</code>
          </div>
          <div className={styles.buttons}>
            <Link className={clsx('button button--lg', styles.primaryButton)} to="/docs/getting-started">
              Get started
            </Link>
            <Link className={clsx('button button--lg', styles.secondaryButton)} href="https://github.com/DylanSnel/DDDToolkit/tree/main/Examples">
              Explore the example shop
            </Link>
          </div>
        </div>
        <div className={styles.heroCode}>
          <div className={styles.window}>
            <div className={styles.windowBar}>
              <span />
              <span />
              <span />
              <em>Order.cs</em>
            </div>
            <CodeBlock language="csharp">{example}</CodeBlock>
          </div>
        </div>
      </div>
    </header>
  );
}

function Generated() {
  return (
    <section className={styles.section}>
      <div className="container">
        <p className={styles.eyebrow}>What that one declaration gives you</p>
        <div className={styles.generatedGrid}>
          {generated.map(([name, text]) => (
            <div key={name} className={styles.generatedItem}>
              <code>{name}</code>
              <p>{text}</p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

function Blocks() {
  return (
    <section className={styles.section}>
      <div className="container">
        <Heading as="h2" className={styles.sectionTitle}>
          The building blocks, generated
        </Heading>
        <p className={styles.sectionLead}>Everything a DDD code base writes by hand, declared once and checked at compile time.</p>
        <div className={styles.cardGrid}>
          {blocks.map((block) => (
            <Link key={block.title} to={block.to} className={styles.card}>
              <Icon d={block.icon} />
              <Heading as="h3">{block.title}</Heading>
              <p>{block.text}</p>
              <span className={styles.cardMore}>Read more →</span>
            </Link>
          ))}
        </div>
      </div>
    </section>
  );
}

function Hosts() {
  return (
    <section className={clsx(styles.section, styles.band)}>
      <div className="container">
        <div className="row">
          <div className="col col--5">
            <Heading as="h2" className={styles.sectionTitle}>
              One domain, hosted any way you like
            </Heading>
            <p className={styles.sectionLead}>
              The example is a shop in five modules: Catalog, Ordering, Inventory, Payments and Shipping. The same modules run as
              one monolith and as three services, over four transports, and the same checkout scenarios pass against every one.
            </p>
            <Link className={clsx('button', styles.secondaryButton)} href="https://github.com/DylanSnel/DDDToolkit/tree/main/Examples">
              See the samples
            </Link>
          </div>
          <div className="col col--7">
            <div className={styles.hostList}>
              {hosts.map((host) => (
                <div key={host.transport} className={styles.host}>
                  <div>
                    <strong>{host.name}</strong>
                    <span>{host.detail}</span>
                  </div>
                  <span className={styles.transport}>{host.transport}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}

function Packages() {
  return (
    <section className={styles.section}>
      <div className="container">
        <Heading as="h2" className={styles.sectionTitle}>
          Take only what you use
        </Heading>
        <p className={styles.sectionLead}>A core package and an integration per tool, each on NuGet.</p>
        <div className={styles.packageGrid}>
          {packages.map(([name, text]) => (
            <a key={name} className={styles.package} href={`https://www.nuget.org/packages/${name}`}>
              <code>{name}</code>
              <span>{text}</span>
            </a>
          ))}
        </div>
      </div>
    </section>
  );
}

function CallToAction() {
  return (
    <section className={styles.cta}>
      <div className="container">
        <Heading as="h2">Write the domain. Let the generator write the rest.</Heading>
        <Link className={clsx('button button--lg', styles.ctaButton)} to="/docs/getting-started">
          Start with the guide
        </Link>
      </div>
    </section>
  );
}

export default function Home() {
  return (
    <Layout title="Domain-Driven Design for .NET" description="Source-generated building blocks for Domain-Driven Design in .NET.">
      <Hero />
      <main>
        <Generated />
        <Blocks />
        <Hosts />
        <Packages />
        <CallToAction />
      </main>
    </Layout>
  );
}
