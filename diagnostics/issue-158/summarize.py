"""Validate the bounded A-B-A run and retain individual launch means."""

import json
import os
import re
import statistics
import sys
from collections import defaultdict
from pathlib import Path

root = Path(sys.argv[1])
versions = {"baseline": "baseline", "candidate": "candidate"}
phases = ("A-baseline", "B-candidate", "C-baseline")
expected = set(os.environ.get("DIAG_CASES", "SingleRentReturn,NestedRentReturn,SingleContext,NestedContext,ManualTls,NestedManualTls,Scoped,NestedScoped").split(','))
rows = []
kevlar_hashes = set()
reservoir_hashes = defaultdict(set)
for phase in phases:
    reports = list((root / phase).rglob("*-report-full-compressed.json"))
    assert len(reports) == 1, (phase, reports)
    benchmarks = json.loads(reports[0].read_text(encoding="utf-8-sig"))["Benchmarks"]
    assert len(benchmarks) == len(expected) and {b["Method"] for b in benchmarks} == expected
    log = (root / f"{phase}.log").read_text(encoding="utf-8-sig")
    kevlar_hashes.update(re.findall(r"Loaded Kevlar, .*?SHA256=([A-F0-9]+)", log))
    version = versions["candidate"] if phase == "B-candidate" else versions["baseline"]
    hashes = re.findall(r"Loaded Reservoir, .*?SHA256=([A-F0-9]+)", log)
    assert hashes, phase
    reservoir_hashes[version].update(hashes)
    for benchmark in benchmarks:
        stats = benchmark["Statistics"]
        memory = benchmark["Memory"]
        assert stats is not None, (phase, benchmark["Method"])
        launches = defaultdict(list)
        for measurement in benchmark["Measurements"]:
            if measurement["IterationMode"] == "Workload" and measurement["IterationStage"] == "Result":
                launches[measurement["LaunchIndex"]].append(measurement["Nanoseconds"] / measurement["Operations"])
        assert len(launches) == 3, (phase, benchmark["Method"], launches)
        rows.append({
            "phase": phase,
            "method": benchmark["Method"],
            "mean_ns": stats["Mean"],
            "error_999_ns": stats["ConfidenceInterval"]["Margin"],
            "stddev_ns": stats["StandardDeviation"],
            "allocated": memory["BytesAllocatedPerOperation"],
            "gen0": memory["Gen0Collections"],
            "launches": [{"index": i, "samples": len(values), "mean_ns": statistics.mean(values)}
                         for i, values in launches.items()],
        })
assert len(kevlar_hashes) == 1, kevlar_hashes
assert all(len(hashes) == 1 for hashes in reservoir_hashes.values()), reservoir_hashes
assert reservoir_hashes[versions["baseline"]] != reservoir_hashes[versions["candidate"]]
(root / "summary.json").write_text(json.dumps(rows, indent=2) + "\n")
print("| Method | A Mean ± error (ns) | B Mean ± error (ns) | C Mean ± error (ns) | B/A | B/C | C/A | Allocated A/B/C |")
print("| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |")
for method in sorted(expected):
    a, b, c = [next(row for row in rows if row["phase"] == phase and row["method"] == method) for phase in phases]
    cells = [f"{row['mean_ns']:.3f} ± {row['error_999_ns']:.3f}" for row in (a, b, c)]
    print(f"| {method} | {' | '.join(cells)} | {b['mean_ns']/a['mean_ns']:.3f} | {b['mean_ns']/c['mean_ns']:.3f} | {c['mean_ns']/a['mean_ns']:.3f} | {a['allocated']}/{b['allocated']}/{c['allocated']} B |")
print("\nError is BDN's 99.9% CI half-width. Ratios divide aggregate means. Full JSON retains per-launch results.")
assert all(row["allocated"] == 0 and row["gen0"] == 0 for row in rows), "Allocation or Gen0 regression; inspect summary.json."
