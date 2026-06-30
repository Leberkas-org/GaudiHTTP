// Generates a single, self-contained HTML page (Chart.js inlined — no server, no CDN) from a
// BenchmarkDotNet artifacts directory.
//
//   npm run charts -- <artifactsDir> [outFile]
//   # e.g. npm run charts -- ../src/GaudiHTTP.Benchmarks/BenchmarkDotNet.Artifacts/20260630_120000
//
// Data sources (two, deliberately):
//   *-report-full.json   BenchmarkDotNet's own JSON  -> throughput (Req/s) and latency percentiles.
//   *.alloc-by-type.json AllocationByTypeExporter     -> PROCESS-WIDE, sampled allocation total.
//
// The allocation chart is fed ONLY from the EventPipe total in *.alloc-by-type.json — never from
// BenchmarkDotNet's MemoryDiagnoser "Allocated" column, which only measures the calling thread and
// massively under-counts the Akka dispatcher / Task background-thread allocations this code does.

import { readFileSync, writeFileSync, readdirSync, statSync, existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));

function fail(msg) {
    console.error(`generate-charts: ${msg}`);
    process.exit(1);
}

const artifactsDir = process.argv[2];
if (!artifactsDir) {
    fail("missing <artifactsDir>. Usage: npm run charts -- <artifactsDir> [outFile]");
}
if (!existsSync(artifactsDir) || !statSync(artifactsDir).isDirectory()) {
    fail(`artifacts directory not found: ${artifactsDir}`);
}
const outFile = process.argv[3] ?? join(artifactsDir, "charts.html");

function walk(dir) {
    const out = [];
    for (const entry of readdirSync(dir)) {
        const full = join(dir, entry);
        const st = statSync(full);
        if (st.isDirectory()) {
            out.push(...walk(full));
        } else {
            out.push(full);
        }
    }
    return out;
}

const files = walk(artifactsDir);
const fullJsonFiles = files.filter((f) => f.endsWith("-report-full.json"));
const allocJsonFiles = files.filter((f) => f.endsWith(".alloc-by-type.json"));

if (fullJsonFiles.length === 0 && allocJsonFiles.length === 0) {
    fail(`no *-report-full.json or *.alloc-by-type.json found under ${artifactsDir}`);
}

// param extraction helpers (BDN encodes params both as "Name=Value, ..." strings and in folder names)
function httpVersionOf(text) {
    const m = /HttpVersion[=:\s-]*([0-9](?:\.[0-9])?)/i.exec(text ?? "");
    return m ? m[1] : "?";
}
function concurrencyOf(text) {
    const m = /ConcurrencyLevel[=:\s-]*([0-9]+)/i.exec(text ?? "");
    return m ? Number(m[1]) : 1;
}

// throughput + latency from BenchmarkDotNet's own JSON
const perf = [];
for (const file of fullJsonFiles) {
    let doc;
    try {
        doc = JSON.parse(readFileSync(file, "utf8"));
    } catch {
        console.warn(`generate-charts: skipping unreadable ${file}`);
        continue;
    }
    for (const b of doc.Benchmarks ?? []) {
        const stats = b.Statistics;
        if (!stats) {
            continue;
        }
        const meanNs = stats.Mean;
        const params = b.Parameters ?? "";
        const httpVersion = httpVersionOf(params + " " + (b.FullName ?? ""));
        const concurrency = concurrencyOf(params + " " + (b.FullName ?? ""));
        const isConcurrent = /concurrent/i.test(b.Method ?? "");
        const factor = isConcurrent ? concurrency : 1;
        const reqPerSec = meanNs > 0 ? (factor * 1e9) / meanNs : 0;
        const pcts = stats.Percentiles ?? {};
        perf.push({
            type: b.Type ?? "",
            method: b.Method ?? "",
            label: `${b.Type ?? ""}.${b.Method ?? ""}${params ? ` [${params}]` : ""}`,
            shortLabel: `${b.Method ?? ""}${concurrency > 1 ? ` ×${concurrency}` : ""}`,
            httpVersion,
            concurrency,
            reqPerSec,
            p50: pcts.P50 ?? stats.Median ?? meanNs,
            p95: pcts.P95 ?? meanNs,
            p100: pcts.P100 ?? meanNs,
        });
    }
}

// process-wide, sampled allocation from AllocationByTypeExporter
const alloc = [];
for (const file of allocJsonFiles) {
    let doc;
    try {
        doc = JSON.parse(readFileSync(file, "utf8"));
    } catch {
        console.warn(`generate-charts: skipping unreadable ${file}`);
        continue;
    }
    const name = doc.Trace ?? doc.Benchmark ?? "";
    alloc.push({
        benchmark: doc.Benchmark ?? name,
        httpVersion: httpVersionOf(name),
        concurrency: concurrencyOf(name),
        totalMB: (doc.TotalBytes ?? 0) / (1024 * 1024),
        topTypes: (doc.Types ?? []).slice(0, 8).map((t) => ({ type: t.Type, mb: t.Bytes / (1024 * 1024) })),
    });
}

perf.sort((a, b) => a.label.localeCompare(b.label));
alloc.sort((a, b) => b.totalMB - a.totalMB);

// Chart.js: inline from node_modules so the output is fully offline (no CDN, no server).
const chartJsPath = join(__dirname, "..", "node_modules", "chart.js", "dist", "chart.umd.js");
let chartJsSource;
if (existsSync(chartJsPath)) {
    chartJsSource = `<script>\n${readFileSync(chartJsPath, "utf8")}\n</script>`;
} else {
    console.warn("generate-charts: chart.js not found in node_modules — run `npm install` in docs/. Falling back to CDN <script>.");
    chartJsSource = `<script src="https://cdn.jsdelivr.net/npm/chart.js@4"></script>`;
}

const data = { perf, alloc, generatedAt: new Date().toISOString(), source: resolve(artifactsDir) };

const html = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>GaudiHTTP Benchmarks</title>
${chartJsSource}
<style>
  :root { color-scheme: light dark; }
  body { font-family: system-ui, sans-serif; margin: 0; padding: 1.5rem; max-width: 1100px; margin-inline: auto; }
  h1 { margin: 0 0 .25rem; } .meta { color: #888; font-size: .85rem; margin-bottom: 1.25rem; }
  .controls { display: flex; gap: 1rem; align-items: center; margin-bottom: 1rem; flex-wrap: wrap; }
  select { padding: .35rem .5rem; }
  .card { border: 1px solid #8884; border-radius: 8px; padding: 1rem; margin-bottom: 1.5rem; }
  .card h2 { margin: 0 0 .25rem; font-size: 1.05rem; } .card p { margin: 0 0 .75rem; color: #888; font-size: .8rem; }
  canvas { max-height: 420px; }
</style>
</head>
<body>
<h1>GaudiHTTP Benchmarks</h1>
<div class="meta" id="meta"></div>
<div class="controls">
  <label>HTTP version: <select id="proto"><option value="all">all</option><option>1.1</option><option>2.0</option><option>3.0</option></select></label>
</div>
<div class="card"><h2>Throughput — Req/s (higher is better)</h2><p>From BenchmarkDotNet Mean; concurrent cases scaled by ConcurrencyLevel.</p><canvas id="throughput"></canvas></div>
<div class="card"><h2>Latency percentiles — ns (lower is better)</h2><p>p50 / p95 / p100 from BenchmarkDotNet statistics.</p><canvas id="latency"></canvas></div>
<div class="card"><h2>Allocation — process-wide, sampled (MB)</h2><p>From the EventPipe GCAllocationTick total (<code>*.alloc-by-type.json</code>), NOT the MemoryDiagnoser column. Sampled (~100 KB/tick): good for relative comparison, approximate in absolute terms.</p><canvas id="allocation"></canvas></div>
<script>
const DATA = ${JSON.stringify(data)};
document.getElementById("meta").textContent =
  "generated " + DATA.generatedAt + " from " + DATA.source + " — " + DATA.perf.length + " perf cases, " + DATA.alloc.length + " allocation traces";

const protoColor = { "1.1": "#36a2eb", "2.0": "#4bc0c0", "3.0": "#ffcd56", "?": "#c9cbcf" };
const charts = {};

function filtered(rows) {
  const p = document.getElementById("proto").value;
  return p === "all" ? rows : rows.filter(r => r.httpVersion === p);
}

function render() {
  const perf = filtered(DATA.perf);
  const alloc = filtered(DATA.alloc);
  Object.values(charts).forEach(c => c.destroy());

  charts.throughput = new Chart(document.getElementById("throughput"), {
    type: "bar",
    data: {
      labels: perf.map(r => r.label),
      datasets: [{
        label: "Req/s",
        data: perf.map(r => r.reqPerSec),
        backgroundColor: perf.map(r => protoColor[r.httpVersion] ?? "#c9cbcf"),
      }],
    },
    options: { indexAxis: "y", plugins: { legend: { display: false } }, scales: { x: { title: { display: true, text: "Req/s" } } } },
  });

  charts.latency = new Chart(document.getElementById("latency"), {
    type: "bar",
    data: {
      labels: perf.map(r => r.label),
      datasets: [
        { label: "p50", data: perf.map(r => r.p50), backgroundColor: "#4bc0c0" },
        { label: "p95", data: perf.map(r => r.p95), backgroundColor: "#ffcd56" },
        { label: "p100", data: perf.map(r => r.p100), backgroundColor: "#ff6384" },
      ],
    },
    options: { indexAxis: "y", scales: { x: { title: { display: true, text: "ns" } } } },
  });

  charts.allocation = new Chart(document.getElementById("allocation"), {
    type: "bar",
    data: {
      labels: alloc.map(r => r.benchmark),
      datasets: [{
        label: "Allocated (MB, process-wide, sampled)",
        data: alloc.map(r => r.totalMB),
        backgroundColor: alloc.map(r => protoColor[r.httpVersion] ?? "#c9cbcf"),
      }],
    },
    options: { indexAxis: "y", plugins: { legend: { display: false } }, scales: { x: { title: { display: true, text: "MB" } } } },
  });
}

document.getElementById("proto").addEventListener("change", render);
render();
</script>
</body>
</html>
`;

writeFileSync(outFile, html);
console.log(`generate-charts: wrote ${outFile} (${perf.length} perf cases, ${alloc.length} allocation traces)`);
