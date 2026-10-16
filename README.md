# Crystal Code Binaries

**A Crystal Code external tool set that inspects arbitrary files byte by byte.**

This is a native dotnet tool set for [Crystal Code](https://github.com/YELANDAOKONG/CrystalCode). `hexdump` shows a bounded byte window, `strings` extracts printable runs, `hash` computes digests, `binsearch` finds byte patterns, `compare` reports where two files differ, and `binread` reads fixed-width numeric values. Every tool is format-agnostic: it reports bytes and offsets, and the model identifies containers and formats from those bytes.

[Features](#features) · [Tools](#tools) · [Output and limits](#output-and-limits) · [Paths and approval](#paths-and-approval) · [Install](#install) · [Build from source](#build-from-source)

## Features

- **Six generic byte tools.** A window, a run extractor, a digest, a pattern search, a byte comparison, and a fixed-width numeric reader; the names are the classic names for byte-level inspection.
- **Bounded and pageable.** Every result fits its own limit, and truncation comes with a `continue with offset` hint in the style of the built-in `list` and `search` tools.
- **Deterministic text.** Output is plain English with LF separators on every platform, lowercase hex, and no binary bytes; non-printable bytes appear as `.` in the ASCII column.
- **Format-agnostic.** No magic tables and no container parsers: a ZIP file, an ELF binary, and an image are all just bytes, and the model recognizes them.
- **Workspace fence.** `path` and `other` are declared in the manifest's `pathArguments`, so the host resolves every path against the session workspace before invocation and still applies its approval policy.
- **Fits the manifest.** The set joins Plan and Work, declares `approval: always`, and loads all six tools from one assembly in one load context. The production project adds no NuGet packages.

## Tools

| Tool | What it does |
| :--- | :--- |
| `hexdump` | Shows a bounded byte window of a file as hex and ASCII rows |
| `strings` | Extracts printable ASCII, UTF-16LE, or UTF-8 runs with their byte offsets |
| `hash` | Computes a sha256, sha384, sha512, sha1, md5, sha3-256, sha3-384, or sha3-512 digest, optionally compared with an expected value |
| `binsearch` | Finds literal or `??`-wildcard byte patterns in a file and returns their offsets |
| `compare` | Compares two files byte by byte: equality, first difference, and, in `ranges` mode, the differing regions |
| `binread` | Reads fixed-width integers and IEEE 754 floats at a byte offset with an explicit byte order |

### hexdump arguments

| Argument | Default | Meaning |
| :--- | :--- | :--- |
| `path` | Required | Workspace-relative or absolute file path |
| `offset` | 0 | Byte offset to start the window at |
| `length` | 1024 (1–16384) | Bytes to show |

Rows use eight-digit hex offsets and 16 bytes per row, for example `00000000  48 65 6c 6c 6f 20 57 6f  72 6c 64 21 31 32 33 34  |Hello World!1234|`.

### strings arguments

| Argument | Default | Meaning |
| :--- | :--- | :--- |
| `path` | Required | Workspace-relative or absolute file path |
| `minimumLength` | 8 (4–1024) | Minimum characters per run (Unicode scalar values for `utf8`) |
| `encoding` | `ascii` | `ascii` for printable ASCII runs, `utf16le` for UTF-16LE runs, `utf8` for strict UTF-8 text |
| `offset` | 0 | Byte offset to start the scan at |
| `length` | To the end | Bytes to scan from `offset` |
| `limit` | 200 (1–500) | Maximum runs to return |

The first bytes of the file are probed and reported as a `probe:` line (`binary`, `text (no BOM)`, `text with a UTF-8 BOM`, `likely UTF-16LE text`, and so on). Each run is one `offset <byte offset>: <text>` line. With `encoding: utf8`, a run contains only valid UTF-8 and is split at invalid bytes and at control, format, separator, and unassigned characters; a truncated preview never splits a character.

### hash arguments

| Argument | Default | Meaning |
| :--- | :--- | :--- |
| `path` | Required | Workspace-relative or absolute file path |
| `algorithm` | `sha256` | `sha256`, `sha384`, `sha512`, `sha1`, `md5`, `sha3-256`, `sha3-384`, or `sha3-512` |
| `expected` | None | Optional expected digest in hex |

The digest is lowercase hex. With `expected`, the result adds `expected:` and `match: yes` or `match: no`; a mismatch is a result, not a tool error. `sha1` and `md5` exist for matching existing published values.

### binsearch arguments

| Argument | Default | Meaning |
| :--- | :--- | :--- |
| `path` | Required | Workspace-relative or absolute file path |
| `pattern` | Required | A hex string such as `89 50 4E 47`, space-separated hex bytes with `??` wildcards, or literal text for the text encodings |
| `encoding` | `hex` | `hex`, `hex-wildcard`, `ascii`, `utf8`, or `utf16le`; how `pattern` is interpreted |
| `offset` | 0 | Byte offset to start scanning at |
| `length` | To the end | Bytes to scan from `offset` |
| `limit` | 100 (1–1000) | Maximum matches to return |
| `context` | 0 (0–64) | Bytes to show before and after each match |

Each match is one `offset <byte offset>: <matched bytes>` line, followed by `  before:` and `  after:` lines when `context` is set. With `hex-wildcard`, each `??` token matches any byte, the match line reads `wildcard match`, and a `  matched:` line shows the bytes actually found. Overlapping matches are reported. A pattern is at most 256 bytes. No matches answers `No matches.`; an empty file answers `<path>: 0 bytes (empty file)`.

### compare arguments

| Argument | Default | Meaning |
| :--- | :--- | :--- |
| `path` | Required | Workspace-relative or absolute path of the first file |
| `other` | Required | Workspace-relative or absolute path of the second file |
| `mode` | `summary` | `summary` compares whole files; `ranges` lists consecutive differing regions at matching offsets |
| `offset` | 0 | Start byte offset for `ranges` mode |
| `length` | Through the longer file | Bytes to compare in `ranges` mode |
| `limit` | 20 (1–100) | Maximum regions to list in `ranges` mode |

`summary` reports both sizes, `identical: yes` or `identical: no`, the first difference offset with both byte values, the number of differing bytes in the common region, and which file is longer.

`ranges` lists each differing region as an `offset <byte offset>: <n> differing bytes` line with `  first:` and `  second:` previews of up to 16 bytes (a longer region ends with `...`); a side that runs past the end of its file is reported as `<absent>`. When more regions remain, the page ends with `[showing <n> ranges; continue with offset <n>]`. `offset`, `length`, and `limit` apply only in this mode, and regions are aligned at matching offsets: there is no insertion or deletion alignment.

### binread arguments

| Argument | Default | Meaning |
| :--- | :--- | :--- |
| `path` | Required | Workspace-relative or absolute file path |
| `offset` | 0 | Byte offset to start reading at |
| `type` | Required | `u8`, `i8`, `u16`, `i16`, `u32`, `i32`, `u64`, `i64`, `f32`, or `f64` |
| `endianness` | `little` | `little` or `big` |
| `count` | 1 (1–1024) | Number of values to read |

Each value is one `offset <byte offset>: <hex bytes> = <value>` line with the original bytes first, formatted in invariant culture; `f32` and `f64` use round-trip formatting and can report `Infinity` or `NaN`. Every requested value must fit in the file: a read that would extend past the end fails instead of returning partial values.

## Output and limits

- `hexdump` shows 1024 bytes by default and at most 16384 bytes per call; when more bytes remain, the page ends with `[showing <n> of <size> bytes; continue with offset <n>]`.
- `strings` caps each run's text at 200 characters (a longer run ends with `...`) and stops listing at an internal character budget below the host output limit; when the limit or the budget stops the listing, the result ends with `[showing <n> runs; continue with offset <n>]`.
- `binsearch` shows at most 16 bytes of each matched pattern (a longer pattern ends with `...`) and stops listing at the same internal budget; when the limit or the budget stops the listing, the result ends with `[showing <n> matches; continue with offset <n>]`.
- `compare` in `ranges` mode lists at most `limit` regions (default 20) with 16-byte previews, so every page stays small.
- `binread` reads at most 1024 values per call and fails rather than returning partial values.
- `hash` and `compare` stream both files, so size is bounded by the host timeout, not memory.
- The manifest sets `timeoutSeconds` to 120 for every call, and the host truncates any result that exceeds its own tool-output limit.
- No tool writes, deletes, moves, or executes anything; the set is read-only.

## Paths and approval

`path` (and `other` for `compare`) are declared in `pathArguments`, so the host resolves them against the session workspace before invocation. Every resolved path follows symbolic links to its final target. `.ssh`, `.gnupg`, and `.crystal/credentials.json` paths are Forbidden and never fully auto-pass. Paths outside the workspace become OutsideWorkspace: the operator is asked, or the review model decides in Review and Audit.

The floor for any external tool is Write + Workspace, so the manifest declares `approval: always` and works best installed under `~/.crystal/tools`, where the default `externalToolApproval.home = author` lets ordinary workspace-bounded calls pass without prompts and without Review. Installed as a project set under the default `host` policy, every call asks instead. Outside paths still ask or go to Review and Audit either way.

## Install

Publish the set and place the published files, including `tools.json`, in one directory under `~/.crystal/tools`:

```bash
dotnet publish CrystalCode.Tools.Binaries/CrystalCode.Tools.Binaries.csproj -c Release -o ~/.crystal/tools/binaries
```

Restart the session, or run `/tools reload`, to load the set. A project set at `<workspace>/.crystal/tools/binaries/` replaces the home set for that workspace. The directory name is the set identity; this set installs as `binaries`.

## Build from source

You need the .NET 10 SDK and a sibling checkout of [Crystal](https://github.com/YELANDAOKONG/Crystal) at `../Crystal`.

```bash
dotnet build CrystalCode.Tools.Binaries.sln
dotnet test CrystalCode.Tools.Binaries.sln
dotnet publish CrystalCode.Tools.Binaries/CrystalCode.Tools.Binaries.csproj
```

`Crystal.Tools` is referenced as a sibling source project. The production project adds no NuGet packages; the test project uses xUnit and Microsoft.NET.Test.Sdk only. `HexDumpTool`, `StringsTool`, `HashTool`, `BinarySearchTool`, `CompareTool`, and `BinaryReadTool` are public `ITool` types listed in `tools.json`; the host loads this class library in one isolated load context and wraps every call with its own approval, output truncation, and timeout. The byte-level helpers live in `Reading/` and are covered by the test project.
