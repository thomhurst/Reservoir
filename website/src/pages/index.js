import clsx from 'clsx';
import Link from '@docusaurus/Link';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';
import styles from './index.module.css';

const poolSlots = Array.from({length: 16}, (_, index) => index);

const builtInPools = [
  ['List<T>', '1,024'],
  ['Dictionary<TKey, TValue>', '1,024'],
  ['HashSet<T>', '1,024'],
  ['Queue<T>', '1,024'],
  ['Stack<T>', '1,024'],
  ['StringBuilder', '4,096'],
  ['CancellationTokenSource', 'reset-safe'],
];

function ArrowIcon() {
  return (
    <svg viewBox="0 0 20 20" aria-hidden="true">
      <path d="M4 10h11M11 6l4 4-4 4" />
    </svg>
  );
}

function PoolInstrument() {
  return (
    <div className={styles.instrument} aria-label="An illustrative Reservoir object pool with bounded slots">
      <div className={styles.instrumentHead}>
        <span>POOL / BUFFER</span>
        <span className={styles.warmStatus}><i /> warm path</span>
      </div>

      <div className={styles.dial} aria-hidden="true">
        <div className={styles.orbit}>
          {poolSlots.map((slot) => (
            <i
              className={clsx(styles.slot, slot === 3 || slot === 4 ? styles.slotRented : '')}
              key={slot}
              style={{'--slot': slot}}
            />
          ))}
        </div>
        <div className={styles.dialCore}>
          <strong>0 B</strong>
          <span>rent + return</span>
        </div>
        <span className={styles.rentLabel}>rent</span>
        <span className={styles.returnLabel}>return</span>
      </div>

      <div className={styles.ledger}>
        <div><span>retained</span><strong>14</strong><small>/ 16</small></div>
        <div><span>global locks</span><strong>none</strong></div>
        <div><span>delivery</span><strong>.dll</strong><small> NuGet</small></div>
      </div>
    </div>
  );
}

function InstallCommand() {
  return (
    <div className={styles.installCommand} aria-label="Install Reservoir with the .NET CLI">
      <span aria-hidden="true">$</span>
      <code>dotnet add package Reservoir</code>
    </div>
  );
}

function Hero() {
  return (
    <header className={styles.hero}>
      <div className={styles.heroGrid} aria-hidden="true" />
      <div className={clsx('container', styles.heroLayout)}>
        <div className={styles.heroCopy}>
          <div className={styles.eyebrow}>
            <span>Reservoir / .NET 10+</span>
            <span>runtime package</span>
          </div>
          <Heading as="h1">Stop allocating the same thing <em>twice.</em></Heading>
          <p className={styles.heroLead}>Bounded, thread-safe object pools with a 0 B warm path. One small runtime library works across application and package boundaries.</p>
          <InstallCommand />
          <div className={styles.heroActions}>
            <Link className={styles.primaryButton} to="/docs/quick-start">Start pooling <ArrowIcon /></Link>
            <Link className={styles.textLink} to="/docs/design">Read the design notes <ArrowIcon /></Link>
          </div>
        </div>
        <PoolInstrument />
      </div>

      <div className={styles.proofBar}>
        <div className={clsx('container', styles.proofGrid)}>
          <div><strong>11.83 ns</strong><span>warm rent + return</span></div>
          <div><strong>0 B</strong><span>allocated on every measured warm path</span></div>
          <div><strong>64 slots</strong><span>capacity is explicit and bounded</span></div>
          <small>BenchmarkDotNet · ShortRun · .NET 10 · i7-12700K</small>
        </div>
      </div>
    </header>
  );
}

function CodePanel() {
  return (
    <div className={styles.codePanel}>
      <div className={styles.codePanelHead}>
        <span>BufferPool.cs</span>
        <span>lexical ownership</span>
      </div>
      <pre><code><span className={styles.syntaxKeyword}>var</span> pool = <span className={styles.syntaxKeyword}>new</span>{'\n'}    <span className={styles.syntaxType}>ObjectPool</span>&lt;Buffer, BufferPolicy&gt;(<span className={styles.syntaxNumber}>64</span>);{'\n\n'}<span className={styles.syntaxKeyword}>using var</span> lease = pool.RentScoped({'\n'}    <span className={styles.syntaxKeyword}>out</span> <span className={styles.syntaxType}>Buffer</span> buffer);{'\n\n'}buffer.Write(payload);{'\n'}<span className={styles.syntaxComment}>// reset + return at scope exit</span></code></pre>
      <div className={styles.codeFlow} aria-hidden="true">
        <span>rent</span><i /><span>work</span><i /><span>return</span>
      </div>
    </div>
  );
}

function ContractSection() {
  return (
    <section className={styles.contractSection}>
      <div className="container">
        <div className={styles.sectionHeading}>
          <span className={styles.kicker}>The ownership contract</span>
          <Heading as="h2">Rent. Work. Return.</Heading>
          <p>A small lifecycle that stays explicit, even under contention. The pool owns retention; your policy owns creation, reset, and cleanup.</p>
        </div>

        <div className={styles.contractGrid}>
          <CodePanel />
          <ol className={styles.workflow}>
            <li>
              <span>01 / rent</span>
              <Heading as="h3">Take sole ownership</Heading>
              <p>Rent from a per-thread stripe. A miss asks your policy to create an instance.</p>
            </li>
            <li>
              <span>02 / work</span>
              <Heading as="h3">Use it like it is yours</Heading>
              <p>Because it is—until return. No wrapper sits between your code and the object.</p>
            </li>
            <li>
              <span>03 / return</span>
              <Heading as="h3">Transfer ownership back</Heading>
              <p>The policy resets it. Oversized or invalid objects are destroyed instead of retained.</p>
            </li>
          </ol>
        </div>
      </div>
    </section>
  );
}

function GuaranteesSection() {
  const guarantees = [
    {
      label: 'Bounded by design',
      title: 'A pool, not a leak.',
      text: 'Fixed retention limits keep memory behavior legible. Capacity controls idle objects, never the number of concurrent rentals.',
      detail: 'maxCapacity',
    },
    {
      label: 'Library ready',
      title: 'One package. One identity.',
      text: 'Public types flow transitively through PackageReference graphs while the JIT specializes concrete struct policies across the assembly boundary.',
      detail: 'Reservoir.dll',
    },
    {
      label: 'Scoped ownership',
      title: 'Return by construction.',
      text: 'Stack-only leases return rentals when synchronous work leaves scope, including exceptional control flow.',
      detail: 'PooledLease<T>',
    },
  ];

  return (
    <section className={styles.guaranteesSection}>
      <div className="container">
        <div className={styles.guaranteesHead}>
          <span className={styles.kicker}>Why Reservoir</span>
          <Heading as="h2">Performance with edges.</Heading>
        </div>
        <div className={styles.guaranteeGrid}>
          {guarantees.map((item) => (
            <article key={item.label}>
              <span>{item.label}</span>
              <Heading as="h3">{item.title}</Heading>
              <p>{item.text}</p>
              <code>{item.detail}</code>
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}

function BuiltInsSection() {
  return (
    <section className={styles.builtInsSection}>
      <div className={clsx('container', styles.builtInsLayout)}>
        <div className={styles.builtInsCopy}>
          <span className={styles.kicker}>Useful on install</span>
          <Heading as="h2">Common pools,<br />already primed.</Heading>
          <p>Shared pools arrive ready for collections, text building, and cancellation. Collections return empty; unusually large backing stores do not return at all.</p>
          <Link className={styles.textLink} to="/docs/api/collection-pools">Explore built-in pools <ArrowIcon /></Link>
        </div>

        <div className={styles.poolIndex}>
          <div className={styles.poolIndexHead}><span>pool type</span><span>largest retained</span></div>
          {builtInPools.map(([name, limit]) => (
            <div className={styles.poolRow} key={name}>
              <code>{name}</code>
              <span>{limit}</span>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

function DistributionSection() {
  return (
    <section className={styles.distributionSection}>
      <div className={clsx('container', styles.distributionLayout)}>
        <div className={styles.distributionRoute} aria-label="Reservoir runtime package joins your application">
          <span>NuGet</span><i /><span>Reservoir.dll</span><i /><strong>your application</strong>
        </div>
        <div className={styles.distributionCopy}>
          <span className={styles.kicker}>Conventional PackageReference</span>
          <Heading as="h2">Install once.<br />Share the types.</Heading>
          <p>One runtime dependency, one public type identity, and no source injection into consumer compiler or analyzer settings.</p>
          <div className={styles.distributionActions}>
            <Link className={styles.darkButton} to="/docs/installation">Installation <ArrowIcon /></Link>
            <Link className={styles.darkTextLink} href="https://github.com/thomhurst/Reservoir">View on GitHub <ArrowIcon /></Link>
          </div>
        </div>
      </div>
    </section>
  );
}

export default function Home() {
  return (
    <Layout title="Bounded object pooling for .NET" description="Reservoir provides bounded, thread-safe, zero-allocation object pooling for .NET.">
      <main>
        <Hero />
        <ContractSection />
        <GuaranteesSection />
        <BuiltInsSection />
        <DistributionSection />
      </main>
    </Layout>
  );
}
