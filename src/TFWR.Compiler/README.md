# TFWR.Compiler

[The Farmer Was Replaced](https://store.steampowered.com/app/2060160/The_Farmer_Was_Replaced/)용 C# / TypeScript 소스를 인게임 DSL(`.py`)로 번역하는 컴파일러 라이브러리입니다.

- **C#:** Roslyn 파싱 → IR lowering → Python emit
- **TypeScript:** Node `TsFrontend` bridge → IR JSON → deserialize → 동일 Emitter

CLI는 [`tfwrc`](../tfwrc/)가 이 라이브러리를 호출합니다. C# 스텁은 [`TFWR.Api`](../TFWR.Api/), TS typings는 [`TFWR.Api.Ts`](../TFWR.Api.Ts/)입니다.

## 파이프라인

```
C# (.cs)  →  Roslyn  →  LoweringContext  ─┐
                                           ├─► IR  →  TfwrEmitter  →  .py
TS (.ts)  →  Node bridge → IR JSON ───────┘
```

| 단계 | 구성 | 설명 |
|------|------|------|
| 진입 | `TfwrCompiler` | `--lang cs\|ts` 분기, emit 조율 |
| C# Lowering | `LoweringContext` | C# AST → TFWR IR |
| TS Lowering | `TsFrontend/lower.mjs` | TS AST → IR JSON |
| JSON | `IrJsonDeserializer` | IR JSON → CLR IR |
| Emit | `TfwrEmitter` | IR → Python 소스 |

## 공개 API

```csharp
var result = TfwrCompiler.Compile(new CompileOptions
{
    InputFiles = [@"D:\farm\Program.cs"], // or .ts with Language = "ts"
    OutputDirectory = @"D:\farm\out",
    Language = "cs", // or "ts"
    EmitTopLevelEntry = false
});
```

```csharp
var result = TfwrCompiler.CompileToMemory(new Dictionary<string, string>
{
    ["Program.ts"] = source
}, emitTopLevelEntry: true, language: "ts");
```

`--lang ts`는 Node.js 20+와 `TsFrontend/node_modules`(`npm ci`)가 필요합니다.

## v1 지원 범위

C#과 TS 공통으로 IR 계약을 맞춥니다. 상세는 기존 C# README 범위와 동일합니다(클래스→dict, 제어흐름, 컬렉션, Game API; try/async/lambda 거부).

## 패키지 정보

- 대상 프레임워크: `net10.0`
- 의존성: `Microsoft.CodeAnalysis.CSharp`, `TFWR.Api`, (ts) Node + `typescript`
