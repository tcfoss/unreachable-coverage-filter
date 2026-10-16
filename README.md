# UnreachableCoverageFilter

A .NET 10 CLI that removes explicitly matched, unvisited defensive guards from
Coverlet OpenCover reports before ReportGenerator runs. Assembly metadata and matching portable PDBs supply source
locations for branch destinations.

The CLI logs to the current working directory using the name
`unreachable-coverage-filter.TIMESTAMP.log`. Use `--log-level` with a
logging level (`Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or
`None`); the default is `Information`.


## Usage

Collect coverage in `opencover` format. Preserve the exact assemblies and portable
PDBs used for that run, along with their source files. Coverlet may record only an
assembly filename, in which case supply its test output directory explicitly.

Preview exclusions:

```sh
dotnet run --project src/TcfOss.UnreachableCoverageFilter/TcfOss.UnreachableCoverageFilter.csproj -- \
  'coverage-inputs/**/*.opencover.xml' \
  --assembly-directory path/to/YourProject.Tests/bin/Debug/net10.0 \
  --exclude-regex 'throw new (System\.)?(Diagnostics\.)?UnreachableException\(' \
  --log-level Debug \
  --dry-run
```

Write a separate filtered report:

```sh
dotnet run --project src/TcfOss.UnreachableCoverageFilter/TcfOss.UnreachableCoverageFilter.csproj -- \
  'coverage-inputs/**/*.opencover.xml' \
  'other-results/*.opencover.xml' \
  --output-directory path/to/filtered-coverage \
  --on-collision rename \
  --assembly-directory test/YourProject.Tests/bin/Debug/net10.0 \
  --exclude-regex 'throw new (System\.)?(Diagnostics\.)?UnreachableException\('

reportgenerator \
  '-reports:path/to/filtered-coverage/*.opencover.xml' \
  '-targetdir:path/to/report' \
  '-reporttypes:Html;TextSummary;Cobertura'
```

Pass one or more literal paths or glob patterns as positional arguments. Globs
are resolved from the current working directory; unmatched inputs are errors and
duplicate matches are processed once. Each output keeps its input filename inside
`--output-directory`.
An output path that would overwrite an input report is rejected. If distinct inputs
have the same filename, the selected collision policy determines how their outputs
are handled. The default `rename` policy moves an existing output to `.bak.1`,
incrementing the suffix for later backups; `overwrite`, `skip`, and `error` are also
available. The output directory is created when needed.

Repeat `--exclude-regex` to add patterns, `--assembly-directory` for other module
locations, and `--source-root` for relocated source trees. Source relocation tries
suffixes of the original document path beneath each root and requires exactly one
existing candidate. Assembly search directories are not searched recursively.
Apply the tool to each input report before merging with ReportGenerator; do not
include both raw and filtered reports in the merge.

## Exclusion Rules

- Patterns use .NET regex syntax and match individual physical source lines within
  a PDB sequence point's span. There are no default patterns. Each regex evaluation has a one-second timeout.
- Source bytes must match the PDB document checksum (SHA-256 or legacy SHA-1).
- Only zero-visit sequence points and branch points are removed. A branch is
  removed only when its destination maps to a matching visible sequence point and
  the corresponding report sequence points exist and are all unvisited.
- Missing or ambiguous assemblies, unavailable PDBs, changed sources, unknown
  methods, and hidden destination sequence points are retained and logged.
  Intermediate jumps are not followed. Some defensive branches may consequently
  remain uncovered even when their guard line is excluded.
- An output path that matches any input is rejected. Dry runs perform the same
  analysis without writing output. `EXCLUDE`, `KEEP`, and `SKIP` messages provide
  an audit trail.
- Sequence and branch counts and percentages are recalculated, including each
  sequence point's branch counts. Original complexity and method/class counts are
  retained: excluding a guard does not change the compiled control-flow topology.

The matching portable PDB is verified against the assembly, but OpenCover does not
provide a reliable build identity to verify the assembly against the report. Do
not rebuild or substitute assemblies between collection and filtering. Source
files must be available locally; Source Link downloads and embedded source
extraction are not implemented.

## Tests

```sh
dotnet test --project test/TcfOss.UnreachableCoverageFilter.Tests/TcfOss.UnreachableCoverageFilter.Tests.csproj
```
