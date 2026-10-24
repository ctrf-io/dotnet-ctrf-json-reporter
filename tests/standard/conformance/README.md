# Standard conformance

Conformance uses shared property and report/relationship checks at both unit and
integration levels, against the declared CTRF specification version. These are
coverage families, not an exhaustive set of cases. Existing reporter regressions
remain separate.

```sh
dotnet restore tests/ConformanceHarness.csproj
bash tests/run.sh unit
bash tests/run.sh integration
# Run both, even if unit fails:
bash tests/run.sh conformance
```

Unit tests capture serialized output from controlled TRX inputs through the actual
processor. Integration packs and installs the public .NET tool in temporary
consumers and converts real MSTest, NUnit and xUnit TRX output, including mixed
outcomes, data rows, repeated executions and coordinated shard labels. The source
TRX and fixture inventory provide independent expectations. Repeated test results
are not assumed to represent retries.

The harness requires the .NET SDK supported by the reporter and available NuGet
dependencies. Ordinary regressions also use the existing Node.js verification
runner and its documented CTRF CLI validator. Missing setup fails the run.

Temporary consumers are cleaned automatically. Set `CTRF_STANDARD_ARTIFACTS` to an
absolute external directory to retain assessment CTRF, native streams, source TRX,
expectations, subject reports and diagnostics. Otherwise results are temporary.
`DOTNET_COMMAND` can select an SDK executable outside PATH.

- `shared/catalogue.json`: common family IDs from the reviewed snapshot.
- `shared/Checks.cs`: native schema and specification assertions.
- `shared/ExpectedFailures.cs`: native strict expected-failure engine.
- `dotnet.json`: producer capabilities and reasons for unsupported entries.
- `../../native`: independent source observations, fixtures and runner controls.
- `../shared-files.json`: native hashes and reference/schema provenance.

Unsupported optional features remain skipped with reasons. Supported fields need
positive evidence; every emitted document receives full schema validation.
Mandatory violations fail, recommendations warn, and missing evidence cannot pass.
Temporary expected failures still execute and narrowly match a recorded defect;
different errors and unexpected or partial passes fail CI. Remove markers with
fixes. Expected failures use **Other**, explicit labels and issue metadata, in
separate tracked-defect groups. They are not conformance passes.

Both levels write schema-validated assessment CTRF under `reports/`. CI uses one
built-in summary and folded suite groups distinguishing unit/integration and schema
properties/specification relationships. The Ubuntu reference job maintains one
updating `reporter-conformance` PR comment; every matrix job retains evidence.
Fork PRs retain summaries and artifacts. The top summary separates skipped and
Other; the folded table combines skipped/pending/Other. Expected-failure groups
contain tracked defects only.

`bash tests/run.sh checker` runs independent shared controls in `tests/standard/checker`
and writes only to `checker-reports`, publishing a separate `reporter-checker`
comment. `bash tests/run.sh ordinary` runs existing regressions and native controls
under `reporter-reports`, publishing `reporter-tests`. `bash tests/run.sh standard`
runs checker and both contract levels despite earlier failures; `all` includes
ordinary tests too. Subject reports and diagnostics are outside publication globs.

This is a native C# implementation of shared IDs and assertion semantics, with
recorded parity evidence rather than a claim of JavaScript byte identity. The
harness project isolates validation dependencies and fixture source from the
published reporter tool.
