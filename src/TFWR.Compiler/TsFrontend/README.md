# TsFrontend

TypeScript → TFWR IR JSON bridge used by `tfwrc --lang ts`.

## Setup

```bash
npm ci
```

Requires Node.js 20+.

## Run (normally via C#)

`TfwrCompiler` / `TsCompiler` invokes:

```bash
node lower.mjs <request.json>
```

Request JSON includes input `.ts` paths, `emitTopLevelEntry`, and `tfwrDtsPath`. stdout is IR JSON consumed by `IrJsonDeserializer`.
