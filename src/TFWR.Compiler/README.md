# TFWR.Compiler

[The Farmer Was Replaced](https://store.steampowered.com/app/2060160/The_Farmer_Was_Replaced/)용 C# 소스를 인게임 DSL(`.py`)로 번역하는 컴파일러 라이브러리입니다.

Roslyn으로 C#을 파싱·분석한 뒤, TFWR IR로 낮추고 Python 텍스트를 생성합니다. CLI는 [`tfwrc`](../tfwrc/)가 이 라이브러리를 호출합니다. 게임 API 스텁은 [`TFWR.Api`](../TFWR.Api/)를 참조합니다.

메타데이터 참조는 런타임 `Assembly.Location`/TPA 파일 경로가 아니라, 임베디드 net10.0 참조 어셈블리(`Basic.Reference.Assemblies.Net100`)와 디스크의 `TFWR.Api.dll`(exe 옆 또는 `%TFWRC_HOME%\bin`)로 구성합니다. single-file 배포에서도 API DLL은 exe 옆에 둡니다.

## 파이프라인

```
C# (.cs)  →  Roslyn 파싱/바인딩  →  Lowering (IR)  →  Emit (.py)
```

| 단계 | 구성 | 설명 |
|------|------|------|
| 진입 | `TfwrCompiler` | 입력 검증, 컴파일레이션 구성, 모듈별 lowering·emit 조율 |
| Lowering | `LoweringContext` | C# AST → TFWR IR (`TfwrModule` / `TfwrStmt` / `TfwrExpr`) |
| Emit | `TfwrEmitter` | IR → Python 소스 텍스트 |
| IR | `TFWR.Compiler.Ir` | 모듈·함수·문·식 노드 |

## 공개 API

```csharp
var result = TfwrCompiler.Compile(new CompileOptions
{
    InputFiles = [@"D:\farm\Program.cs"],
    OutputDirectory = @"D:\farm\out",
    Language = "cs",
    EmitTopLevelEntry = false // true면 Main/[TfwrEntry]를 def 없이 최상위 문으로 emit
});

if (!result.Success)
{
    foreach (var d in result.Diagnostics)
        Console.Error.WriteLine(d);
    return;
}

foreach (var (path, text) in result.EmittedFiles)
    Console.WriteLine($"{path}: {text.Length} chars");
```

테스트·도구용 인메모리 컴파일:

```csharp
var result = TfwrCompiler.CompileToMemory(new Dictionary<string, string>
{
    ["Program.cs"] = source
}, emitTopLevelEntry: true);
```

| 타입 | 역할 |
|------|------|
| `CompileOptions` | 입력 파일, 출력 디렉터리, 언어, `--toplevel` 대응 옵션 |
| `CompileResult` | 성공 여부, 진단, 상대 경로 → emit 텍스트 |
| `CompileDiagnostic` | 심각도·메시지·위치 |
| `TfwrEmitter` | IR 모듈을 Python 문자열로 변환 (고급/테스트용) |

## 출력 규칙 (요약)

- 파일명 → 모듈명, `.cs` → `.py` (상대 경로 유지)
- `Main` / `[TfwrEntry]` → 엔트리 (`if __name__ == "__main__"` 또는 `--toplevel` 시 최상위 문)
- 인스턴스 타입 → dict + `TypeName_Method` / `TypeName_new` 프리 함수
- `Game.*` → snake_case builtins (`Harvest` → `harvest`)
- `Direction` → 전역 (`North` 등), 기타 게임 enum → `Entities.Carrot` 형태
- 교차 모듈 참조 → `import` + 한정 호출

## v1 지원 범위

**지원:** 클래스·record(단일 상속)·필드·자동 프로퍼티·생성자·메서드, `if`/`while`/`for`/`foreach`/`switch`, 삼항 `?:`(if/else+temp), 게임 API·enum, 문자열 보간, `List`/`Dictionary`/`HashSet`, 다중 파일 `import`

**거부:** `try`/`catch`, `async`/`await`, LINQ, `yield`, `unsafe`, 사용자 제네릭 타입, 대부분의 BCL

`EmitTopLevelEntry`가 켜진 모듈에 다른 함수/생성자가 있으면(추가 `def` 필요) 오류입니다.

## 패키지 정보

- 대상 프레임워크: `net10.0`
- 의존성: `Microsoft.CodeAnalysis.CSharp`, `TFWR.Api`
- XML 문서 파일 포함 (`GenerateDocumentationFile`)
