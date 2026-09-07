import {useState} from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';
import CodeBlock from '@theme/CodeBlock';
import styles from './index.module.css';

const collectionTypes = ['List<T>', 'Dictionary<TKey, TValue>', 'HashSet<T>', 'Queue<T>', 'Stack<T>', 'StringBuilder'];
const slots = Array.from({length: 8}, (_, index) => ({
  x: 170 + (index % 4) * 88,
  y: index < 4 ? 235 : 305,
}));

function PoolIllustration() {
  const [rented, setRented] = useState(false);

  return (
    <figure className={styles.poolIllustration}>
      <svg className={styles.basin} viewBox="0 0 640 450" role="img" aria-labelledby="pool-title pool-description">
        <title id="pool-title">An object, back in circulation</title>
        <desc id="pool-description">
          {rented
            ? 'Seven objects remain in the pool. Object 8 is with the caller until it is returned.'
            : 'Eight reusable objects are available in the pool. Rent one to follow its journey.'}
        </desc>
        <g className={styles.pipe}>
          <path d="M434 168V91Q434 60 465 60H532" />
          <path d="m519 51 13 9-13 9" />
          <path d="M546 111V144Q546 175 577 175H587V335Q587 385 537 385H495" />
          <path d="m508 376-13 9 13 9" />
        </g>
        <text x="440" y="35" className={styles.diagramLabel}>Rent</text>
        <text x="516" y="421" className={styles.diagramLabel}>Return</text>
        <rect x="506" y="20" width="80" height="80" rx="24" className={styles.callerSlot} />
        <text x="546" y="-4" textAnchor="middle" className={styles.diagramLabel}>Your code</text>
        <path className={styles.basinWall} d="M94 156H506V235C506 354 427 426 300 426S94 354 94 235Z" />
        <path className={styles.water} d="M108 193C173 172 216 217 285 195S412 174 492 195V235C492 344 420 412 300 412S108 344 108 235Z" />
        <path className={styles.waterLine} d="M108 193C173 172 216 217 285 195S412 174 492 195" />
        <path className={styles.basinLip} d="M82 156H518" />
        <text x="94" y="131" className={styles.diagramLabel}>Shared pool</text>
        <text x="300" y="378" textAnchor="middle" className={styles.capacityLabel}>8 retention slots</text>
        {slots.map(({x, y}, index) => (
          <circle key={index} cx={x} cy={y} r="25" className={styles.emptySlot} />
        ))}
        {slots.map(({x, y}, index) => (
          <g key={index} className={clsx(styles.object, index === 7 && styles.travellingObject, index === 7 && rented && styles.rentedObject)}>
            <circle cx={x} cy={y} r="25" />
            <text x={x} y={y + 6} textAnchor="middle">{index + 1}</text>
          </g>
        ))}
      </svg>
      <figcaption className={styles.poolControls}>
        <div className={styles.poolStatus} aria-live="polite" aria-atomic="true">
          <strong>{rented ? '7 available / 1 in use' : '8 available / 0 in use'}</strong>
          <span>{rented ? 'The caller owns object 8.' : 'Same objects. Ready for more work.'}</span>
        </div>
        <button type="button" className={styles.rentButton} onClick={() => setRented((value) => !value)}>
          {rented ? 'Return object' : 'Rent an object'}
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M19 8a8 8 0 1 0 1 7M19 3v5h-5" /></svg>
        </button>
      </figcaption>
      <p className={styles.illustrationNote}>Try the lifecycle. This illustration starts with a warm pool.</p>
    </figure>
  );
}

function Hero() {
  return (
    <header className={styles.hero}>
      <div className={clsx('container', styles.heroLayout)}>
        <div className={styles.heroCopy}>
          <p className={styles.introduction}>Object pooling for .NET</p>
          <Heading as="h1">Good objects.<br />Back in circulation.</Heading>
          <p className={styles.heroLead}>Create it once. Use it again. Reservoir keeps your objects ready for the next piece of work, with thread-safe pooling and bounded shared retention. Scoped rentals and opt-in manual rentals also retain objects in per-pool thread-local storage.</p>
          <div className={styles.heroActions}>
            <Link className={styles.primaryButton} to="/docs/quick-start">Start pooling</Link>
            <Link className={styles.secondaryLink} to="/docs/design">How Reservoir works</Link>
          </div>
          <div className={styles.installCommand}>
            <code>dotnet add package Reservoir</code>
          </div>
          <p className={styles.compatibility}>.NET Standard 2.0-compatible runtimes, with dedicated .NET 8 and .NET 10 assets. <Link to="/docs/installation">Package compatibility</Link>.</p>
        </div>
        <PoolIllustration />
      </div>
      <div className={clsx('container', styles.heroFoot)}>
        <p>Compare workloads, timings, and allocations.</p>
        <Link to="/docs/benchmarks">See the benchmarks and methodology</Link>
      </div>
    </header>
  );
}

function LifecycleSection() {
  return (
    <section className={clsx('container', styles.lifecycle)} aria-labelledby="lifecycle-heading">
      <div className={styles.lifecycleCopy}>
        <Heading as="h2" id="lifecycle-heading">A short stay in your code.</Heading>
        <p>Rent an object, do your work, and give it back. For synchronous work, a scoped lease handles the return when you leave scope.</p>
        <ol className={styles.lifecycleSteps}>
          <li><strong>Rent</strong><span>Reuse an available object, or create one on a miss.</span></li>
          <li><strong>Work</strong><span>The object belongs to you until you return it.</span></li>
          <li><strong>Return</strong><span>Reset it for reuse. Discard it if it no longer fits.</span></li>
        </ol>
        <Link className={styles.secondaryLink} to="/docs/ownership-rules">Understand the ownership rules</Link>
      </div>
      <div className={styles.example}>
        <div className={styles.exampleTitle}><span>A list, on loan</span><span>C#</span></div>
        <CodeBlock language="csharp">{`using Reservoir;

using var lease = ListPool<int>.Shared
    .RentScoped(out List<int> numbers);

numbers.Add(42);
Consume(numbers);

// Leaving scope returns the list.
// The next renter gets an empty one.`}</CodeBlock>
        <p>Crossing an <code>await</code>? Use <Link to="/docs/quick-start#shared-collection-pool">Rent / Return with try / finally</Link>.</p>
      </div>
    </section>
  );
}

function PoolsSection() {
  return (
    <section className={styles.poolsSection} aria-labelledby="pools-heading">
      <div className={clsx('container', styles.poolsLayout)}>
        <div>
          <Heading as="h2" id="pools-heading">Your everyday objects.<br />Already covered.</Heading>
          <p>Built-in pools for collections and text. Rent them empty, return them for reuse, and set limits on what stays cached.</p>
          <Link className={styles.secondaryLink} to="/docs/api/collection-pools">Browse collection and text pools</Link>
        </div>
        <div className={styles.poolTypes}>
          {collectionTypes.map((type) => <code key={type}>{type}</code>)}
          <p>Also included: <Link to="/docs/api/cancellation-token-sources">CancellationTokenSource pooling</Link>.</p>
          <p>Something of your own? <Link to="/docs/quick-start">Define a creation and reset policy</Link>.</p>
        </div>
      </div>
    </section>
  );
}

function DocsSection() {
  const guides = [
    {title: 'Build your first pool', text: 'Install the package and start renting.', to: '/docs/quick-start'},
    {title: 'Choose your limits', text: 'Control shared retention and object size.', to: '/docs/configuration'},
    {title: 'Find an API', text: 'Pools, policies, leases, and lifecycle hooks.', to: '/docs/api/object-pools'},
  ];

  return (
    <section className={clsx('container', styles.docsSection)} aria-labelledby="docs-heading">
      <Heading as="h2" id="docs-heading">Make yourself at home.</Heading>
      <div className={styles.docsLinks}>
        {guides.map(({title, text, to}) => (
          <Link to={to} key={to}>
            <Heading as="h3">{title}</Heading>
            <p>{text}</p>
          </Link>
        ))}
      </div>
    </section>
  );
}

export default function Home() {
  return (
    <Layout title="Reusable objects. Clear ownership." description="Thread-safe object pooling for .NET with bounded shared retention and per-pool thread-local storage. Explore Reservoir, try the pooling lifecycle, and get started.">
      <main className={styles.home}>
        <Hero />
        <LifecycleSection />
        <PoolsSection />
        <DocsSection />
      </main>
    </Layout>
  );
}
