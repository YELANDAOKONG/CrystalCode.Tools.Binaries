# Crystal Code Binaries

**A Crystal Code external tool set that inspects arbitrary files byte by byte.**

This is a native dotnet tool set for [Crystal Code](https://github.com/YELANDAOKONG/CrystalCode). `hexdump` shows a bounded byte window, `strings` extracts printable runs, `hash` computes digests, `binsearch` finds byte patterns, and `compare` reports where two files differ. Every tool is format-agnostic: it reports bytes and offsets, and the model identifies containers and formats from those bytes.

[Features](#features) · [Tools](#tools) · [Output and limits](#output-and-limits) · [Paths and approval](#paths-and-approval) · [Install](#install) · [Build from source](#build-from-source)

## Features

- **Five generic byte tools.** A window, a run extractor, a digest, a pattern search, and a byte comparison; the names are the classic names for byte-level inspection.
- **Bounded and pageable.** Every result fits its own limit, and truncation comes with a `continue with offset` hint in the style of the built-in `list` and `search` tools.
- **Deterministic text.** Output is plain English with LF separators on every platform, lowercase hex, and no binary bytes; non-printable bytes appear as `.` in the ASCII column.
- **Format-agnostic.** No magic tables and no container parsers: a ZIP file, an ELF binary, and an image are all just bytes, and the model recognizes them.
- **Workspace fence.** `path` and `other` are declared in the manifest's `pathArguments`, so the host resolves every path against the session workspace before invocation and still applies its approval policy.
- **Fits the manifest.** The set joins Plan and Work, declares `approval: always`, and loads all five tools from one assembly in one load context. The production project adds no NuGet packages.

## Tools

| Tool | What it does |
| :--- | :--- |
| `hexdump` | Shows a bounded byte window of a file as hex and ASCII rows |
| `strings` | Extracts printable ASCII or UTF-16LE runs with their byte offsets |
| `hash` | Computes a sha256, sha512, sha1, or md5 digest, optionally compared with an expected value |
| `binsearch` | Finds literal byte patterns in a file and returns their offsets |
| `compare` | Compares two files byte by byte: equality, first difference, and differing bytes |

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
| `minimumLength` | 8 (4–1024) | Minimum characters per run |
| `encoding` | `ascii` | `ascii` for printable ASCII runs, `utf16le` for UTF-16LE runs |
| `offset` | 0 | Byte offset to start the scan at |
| `length` | To the end | Bytes to scan from `offset` |
| `limit` | 200 (1–500) | Maximum runs to return |

The first bytes of the file are probed and reported as a `probe:` line (`binary`, `text (no BOM)`, `text with a UTF-8 BOM`, `likely UTF-16LE text`, and so on). Each run is one `offset <byte offset>: <text>` line.

### hash arguments

| Argument | Default | Meaning |
| :--- | :--- | :--- |
| `path` | Required | Workspace-relative or absolute file path |
| `algorithm` | `sha256` | `sha256`, `sha512`, `sha1`, or `md5` |
| `expected` | None | Optional expected digest in hex |

The digest is lowercase hex. With `expected`, the result adds `expected:` and `match: yes` or `match: no`; a mismatch is a result, not a tool error. `sha1` and `md5` exist for matching existing published values.

### binsearch arguments

| Argument | Default | Meaning |
| :--- | :--- | :--- |
| `path` | Required | Workspace-relative or absolute file path |
| `pattern` | Required | A hex string such as `89 50 4E 47`, or literal text for the text encodings |
| `encoding` | `hex` | `hex`, `ascii`, `utf8`, or `utf16le`; how `pattern` is interpreted |
| `offset` | 0 | Byte offset to start scanning at |
| `length` | To the end | Bytes to scan from `offset` |
| `limit` | 100 (1–1000) | Maximum matches to return |
| `context` | 0 (0–64) | Bytes to show before and after each match |

Each match is one `offset <byte offset>: <matched bytes>` line, followed by `  before:` and `  after:` lines when `context` is set. Overlapping matches are reported. The pattern is at most 256 bytes. No matches answers `No matches.`.

### compare arguments

| Argument | Default | Meaning |
| :--- | :--- | :--- |
| `path` | Required | Workspace-relative or absolute path of the first file |
| `other` | Required | Workspace-relative or absolute path of the second file |

The result reports both sizes, `identical: yes` or `identical: no`, the first difference offset with both byte values, the number of differing bytes in the common region, and which file is longer.

## Output and limits

- `hexdump` shows 1024 bytes by default and at most 16384 bytes per call; when more bytes remain, the page ends with `[showing <n> of <size> bytes; continue with offset <n>]`.
- `strings` caps each run's text at 200 characters (a longer run ends with `...`); when the limit stops the scan, the result ends with `[showing <n> runs; continue with offset <n>]`.
- `binsearch` shows at most 16 bytes of each matched pattern (a longer pattern ends with `...`); when the limit stops the scan, the result ends with `[showing <n> matches; continue with offset <n>]`.
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

`Crystal.Tools` is referenced as a sibling source project. The production project adds no NuGet packages; the test project uses xUnit and Microsoft.NET.Test.Sdk only. `HexDumpTool`, `StringsTool`, `HashTool`, `BinarySearchTool`, and `CompareTool` are public `ITool` types listed in `tools.json`; the host loads this class library in one isolated load context and wraps every call with its own approval, output truncation, and timeout. The byte-level helpers live in `Reading/` and are covered by the test project.
