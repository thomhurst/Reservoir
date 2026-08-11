import clsx from 'clsx';
import Link from '@docusaurus/Link';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';
import styles from './index.module.css';

const features = [
  {
    number: '01',
    title: 'Own the code',
    text: 'Reservoir ships as C# source and compiles into your assembly. No runtime package, binding conflict, or dependency version to coordinate.',
  },
  {
    number: '02',
    title: 'Keep the hot path cold',
    text: 'Warm rent and return operations allocate 0 B. Struct policies let the JIT specialize creation, reset, and destruction calls.',
  },
  {
    number: '03',
    title: 'Pool with guardrails',
    text: 'Bounded retention, deterministic disposal, stack-only leases, and debug diagnostics make ownership mistakes visible.',
  },
];

const pools = ['ObjectPool', 'List', 'Dictionary', 'HashSet', 'Queue', 'Stack', 'StringBuilder', 'CancellationTokenSource'];

function CodeWindow() {
  return (
    <div className={styles.codeWindow} aria-label="Reservoir quick-start example">
      <div className={styles.codeTopbar}>
        <div className={styles.windowDots}><i /><i /><i /></div>
        <span>RequestHandler.cs</span>
        <span className={styles.live}><i /> 0 B warm</span>
      </div>
      <pre className={styles.code}><code><span className={styles.keyword}>var</span> pool = <span className={styles.keyword}>new</span>{'\n'}    <span className={styles.type}>ObjectPool</span>&lt;Buffer, BufferPolicy&gt;(<span className={styles.number}>64</span>);{'\n\n'}<span className={styles.keyword}>using var</span> lease = pool.RentScoped();{'\n'}<span className={styles.type}>Buffer</span> buffer = lease.Value;{'\n\n'}buffer.Write(payload);</code></pre>
      <div className={styles.pipeline}>
        <span>rent</span><div className={styles.track}><i /><i /><i /></div><span>reset + return</span>
      </div>
    </div>
  );
}

function Hero() {
  return (
    <header className={styles.hero}>
      <div className={styles.rings} aria-hidden="true"><i /><i /><i /></div>
      <div className={clsx('container', styles.heroGrid)}>
        <div className={styles.heroCopy}>
          <div className={styles.eyebrow}><span>.NET 10</span> Source-only object pooling</div>
          <Heading as="h1">Performance<br />you <em>keep.</em></Heading>
          <p>Bounded, thread-safe object pools with a zero-allocation warm path—shipped as source so the optimized code becomes yours.</p>
          <div className={styles.actions}>
            <Link className={styles.primaryButton} to="/docs/quick-start">Fill the pool <span>→</span></Link>
            <Link className={styles.secondaryButton} href="https://github.com/thomhurst/Reservoir">View source</Link>
          </div>
          <div className={styles.trustLine}><span>MIT licensed</span><span>Zero runtime dependencies</span><span>Debug diagnostics</span></div>
        </div>
        <CodeWindow />
      </div>
      <div className={styles.ticker} aria-hidden="true">
        <div>{[...pools, ...pools].map((pool, index) => <span key={`${pool}-${index}`}><i />{pool}</span>)}</div>
      </div>
    </header>
  );
}

function Features() {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className={styles.sectionIntro}>
          <span className={styles.kicker}>Designed for the ownership boundary</span>
          <Heading as="h2">Small API.<br />Sharp guarantees.</Heading>
          <p>Reservoir handles contention and retention. Your policy owns object lifecycle.</p>
        </div>
        <div className={styles.featureGrid}>
          {features.map((feature) => (
            <article className={styles.featureCard} key={feature.number}>
              <span>{feature.number}</span>
              <Heading as="h3">{feature.title}</Heading>
              <p>{feature.text}</p>
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}

function SourceSection() {
  return (
    <section className={styles.sourceSection}>
      <div className={clsx('container', styles.sourceGrid)}>
        <div className={styles.sourceVisual} aria-hidden="true">
          <span>NuGet</span><i>→</i><strong>Your assembly</strong>
          <small>C# source · internal by default</small>
        </div>
        <div className={styles.sourceCopy}>
          <span className={styles.kicker}>A development dependency</span>
          <Heading as="h2">No DLL follows you.</Heading>
          <p>The package contributes C# files at build time. Reservoir types compile into your project and the package stays private from downstream consumers automatically.</p>
          <Link to="/docs/installation">See what gets installed <span>→</span></Link>
        </div>
      </div>
    </section>
  );
}

function FinalCta() {
  return (
    <section className={styles.finalCta}>
      <div className="container">
        <span className={styles.kicker}>One package. One policy.</span>
        <Heading as="h2">Rent. Work. Return.</Heading>
        <p>Start with a shared collection pool or define lifecycle rules for your own type.</p>
        <Link className={styles.primaryButton} to="/docs/quick-start">Read the quick start <span>→</span></Link>
      </div>
    </section>
  );
}

export default function Home() {
  return (
    <Layout title="Source-only object pooling for .NET" description="Reservoir provides bounded, thread-safe, zero-allocation object pooling as source for .NET.">
      <main><Hero /><Features /><SourceSection /><FinalCta /></main>
    </Layout>
  );
}
