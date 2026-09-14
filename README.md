# TFWRCompiler.NET

C# 또는 TypeScript로 작성한 코드를 [The Farmer Was Replaced](https://store.steampowered.com/app/2060160/The_Farmer_Was_Replaced/) 인게임 스크립트(DSL, `.py`)로 번역하는 컴파일러 솔루션입니다.

- **C#:** `TFWR.Api`를 참조해 IntelliSense와 `dotnet build`로 작성 → `tfwrc --lang cs`
- **TypeScript:** `tfwr` ambient typings로 작성 → `tfwrc --lang ts` (Node.js 20+ 필요)

## 아키텍처

```
C# (.cs)  ──►  TFWR.Api (스텁)     ──┐
                                      ├──► tfwrc ──► IR ──► .py emit
TS (.ts)  ──►  TFWR.Api.Ts (d.ts)  ──┘         │
                                               │
                                    (ts: Node + TypeScript API)
```

## 프로젝트

| 프로젝트 | 설명 |
|----------|------|
| [TFWR.Api](src/TFWR.Api/README.md) | 게임 builtins·enum C# 스텁 |
| [TFWR.Api.Ts](src/TFWR.Api.Ts/README.md) | 동일 API의 TypeScript ambient (`tfwr`) |
| [TFWR.Compiler](src/TFWR.Compiler/README.md) | IR · lowering · Python emit (+ TsFrontend) |
| [tfwrc](src/tfwrc/README.md) | CLI |
| [TFWR.Compiler.Tests](tests/TFWR.Compiler.Tests/README.md) | emit 스냅샷·회귀 테스트 |

## 요구 사항

- .NET 10 SDK
- `--lang ts` 사용 시: Node.js 20+ (`npm ci` in `src/TFWR.Compiler/TsFrontend`)

## 빠른 시작

```bash
dotnet build
dotnet run --project src/tfwrc -- --lang cs --out ./out ./MyFarm/Program.cs
dotnet run --project src/tfwrc -- --lang ts --out ./out ./MyFarm/main.ts
dotnet test
```

상세 옵션·`--toplevel`·게임에 적용하는 방법은 [tfwrc README](src/tfwrc/README.md)를 참고하세요.
