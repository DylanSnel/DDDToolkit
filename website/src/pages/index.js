import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import CodeBlock from '@theme/CodeBlock';
import Heading from '@theme/Heading';
import HomepageFeatures from '@site/src/components/HomepageFeatures';

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
}`;

function HomepageHeader() {
  const { siteConfig } = useDocusaurusContext();
  return (
    <header className={clsx('hero hero--primary', styles.heroBanner)}>
      <div className="container">
        <Heading as="h1" className="hero__title">
          {siteConfig.title}
        </Heading>
        <p className="hero__subtitle">{siteConfig.tagline}</p>
        <div className={styles.buttons}>
          <Link className="button button--secondary button--lg" to="/docs/getting-started">
            Get started
          </Link>
          <Link className="button button--outline button--secondary button--lg" href="https://github.com/DylanSnel/DDDToolkit/tree/main/Examples">
            See the example shop
          </Link>
        </div>
      </div>
    </header>
  );
}

export default function Home() {
  const { siteConfig } = useDocusaurusContext();
  return (
    <Layout title={siteConfig.title} description={siteConfig.tagline}>
      <HomepageHeader />
      <main>
        <section className={styles.example}>
          <div className="container">
            <div className="row">
              <div className="col col--6">
                <Heading as="h2">Declare the intent, the generator writes the rest</Heading>
                <p>
                  An attribute on a partial class is the whole declaration. <code>OrderId</code> is generated as an
                  allocation-free identifier with parsing, comparison and JSON support. <code>Order</code> gets its base
                  class, an optimistic concurrency version, a domain event list only it can write to, and a backing field
                  Entity Framework maps while the outside world sees a read-only list.
                </p>
                <p>
                  Analyzers keep the rules: an aggregate referenced by id, a module that uses only what another publishes.
                </p>
              </div>
              <div className="col col--6">
                <CodeBlock language="csharp">{example}</CodeBlock>
              </div>
            </div>
          </div>
        </section>
        <HomepageFeatures />
      </main>
    </Layout>
  );
}
