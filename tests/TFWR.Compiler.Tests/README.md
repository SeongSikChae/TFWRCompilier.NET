# TFWR.Compiler.Tests

[`TFWR.Compiler`](../../src/TFWR.Compiler/README.md)의 emit 결과를 검증하는 xUnit 테스트 프로젝트입니다.

인메모리 컴파일(`CompileToMemory`)로 C# / TypeScript 입력을 넣고, 생성된 `.py` 문자열을 스냅샷 방식으로 확인합니다.

- `SnapshotTests` — `--lang cs`
- `TsSnapshotTests` — `--lang ts` (Node.js + TsFrontend 필요)

## 실행

```bash
cd src/TFWR.Compiler/TsFrontend && npm ci
dotnet test
```

## 관련 프로젝트

| 프로젝트 | 역할 |
|----------|------|
| [`TFWR.Compiler`](../../src/TFWR.Compiler/README.md) | 테스트 대상 |
| [`TFWR.Api`](../../src/TFWR.Api/README.md) | C# API 스텁 |
| [`TFWR.Api.Ts`](../../src/TFWR.Api.Ts/README.md) | TypeScript ambient |
