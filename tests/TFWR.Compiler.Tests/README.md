# TFWR.Compiler.Tests

[`TFWR.Compiler`](../../src/TFWR.Compiler/README.md)의 emit 결과를 검증하는 xUnit 테스트 프로젝트입니다.

인메모리 컴파일(`CompileToMemory`)로 C# 입력을 넣고, 생성된 `.py` 문자열에 기대한 builtins·제어 흐름·모듈 규칙이 포함되는지 스냅샷 방식으로 확인합니다.

## 실행

```bash
dotnet test
```

또는 이 프로젝트만:

```bash
dotnet test tests/TFWR.Compiler.Tests
```

## 관련 프로젝트

| 프로젝트 | 역할 |
|----------|------|
| [`TFWR.Compiler`](../../src/TFWR.Compiler/README.md) | 테스트 대상 컴파일러 라이브러리 |
| [`TFWR.Api`](../../src/TFWR.Api/README.md) | 테스트 소스에서 참조하는 게임 API 스텁 |
