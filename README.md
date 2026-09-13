# TFWRCompilier.NET

C#으로 작성한 코드를 [The Farmer Was Replaced](https://store.steampowered.com/app/2060160/The_Farmer_Was_Replaced/) 인게임 스크립트(DSL, `.py`)로 번역하는 컴파일러 솔루션입니다.

일반 .NET 프로젝트처럼 `TFWR.Api`를 참조해 IntelliSense와 `dotnet build`로 농장 스크립트를 작성하고, `tfwrc` CLI가 Roslyn 기반 `TFWR.Compiler`를 통해 게임에서 실행 가능한 Python 모듈을 생성합니다.

## 아키텍처

```
C# 소스  ──►  TFWR.Api (스텁)  ──►  작성·검증
                │
                ▼
             tfwrc (CLI)
                │
                ▼
          TFWR.Compiler
   Roslyn 파싱 → IR lowering → .py emit
                │
                ▼
     게임 세이브 폴더에 복사·실행
```

## 프로젝트

| 프로젝트 | 설명 |
|----------|------|
| [TFWR.Api](src/TFWR.Api/README.md) | 게임 builtins·enum 스텁. IntelliSense와 컴파일 검증용 (런타임 아님) |
| [TFWR.Compiler](src/TFWR.Compiler/README.md) | Roslyn 파싱 → IR → lowering → Python emit 라이브러리 |
| [tfwrc](src/tfwrc/README.md) | 컴파일러를 호출하는 CLI |
| [TFWR.Compiler.Tests](tests/TFWR.Compiler.Tests/README.md) | emit 결과 스냅샷·회귀 테스트 |

## 요구 사항

- .NET 10 SDK

## 빠른 시작

```bash
dotnet build
dotnet run --project src/tfwrc -- --lang cs --out ./out ./MyFarm/Program.cs
dotnet test
```

상세 옵션·`--toplevel`·게임에 적용하는 방법은 [tfwrc README](src/tfwrc/README.md)를 참고하세요.
