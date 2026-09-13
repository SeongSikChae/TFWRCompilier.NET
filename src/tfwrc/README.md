# tfwrc

[The Farmer Was Replaced](https://store.steampowered.com/app/2060160/The_Farmer_Was_Replaced/)용 C# 소스를 인게임 DSL(`.py`)로 컴파일하는 **CLI**입니다.

[`TFWR.Compiler`](../TFWR.Compiler/)를 호출해 Roslyn 파싱 → IR lowering → Python emit을 수행합니다. 게임 API 스텁은 [`TFWR.Api`](../TFWR.Api/)를 사용합니다.

## 요구 사항

- .NET 10 SDK

## 빌드

```bash
dotnet build src/tfwrc
```

single-file publish 시에도 `TFWR.Api.dll`은 exe 옆에 함께 배포됩니다(Roslyn 메타데이터용). 설치 레이아웃 예: `%TFWRC_HOME%\bin\tfwrc.exe` + `%TFWRC_HOME%\bin\TFWR.Api.dll`.

## 사용법

```bash
dotnet run --project src/tfwrc -- --lang cs --out <출력디렉토리> [--toplevel] <입력.cs> [입력.cs ...]
```

또는 빌드된 실행 파일:

```bash
tfwrc --lang cs --out <출력디렉토리> [--toplevel] <입력.cs> [입력.cs ...]
```

### 옵션

| 옵션 | 설명 |
|------|------|
| `--lang cs` | 소스 언어 (v1에서는 `cs`만 지원) |
| `--out <dir>` | 생성될 `.py` 모듈 출력 디렉터리 |
| `--toplevel` / `--top-level` | `Main` / `[TfwrEntry]`를 `def` 없이 최상위 문으로 emit (`Unlocks.Functions` 해금 전용) |
| `-h` / `--help` | 도움말 |

### 예제

```bash
dotnet run --project src/tfwrc -- --lang cs --out ./out ./MyFarm/Program.cs
```

Functions 해금 전(초반)에는 `--toplevel`을 사용합니다.

```bash
dotnet run --project src/tfwrc -- --lang cs --out ./out --toplevel ./MyFarm/Program.cs
```

예: `Game.Harvest();`만 있는 `Main` →

```python
harvest()
```

`--toplevel` 사용 시 같은 모듈에 다른 메서드/생성자가 있으면(추가 `def` 필요) 오류가 납니다.

## 소스 작성

`TFWR.Api`를 참조하고 `using static TFWR.Api.Game;`를 사용합니다.

```csharp
using static TFWR.Api.Game;
using TFWR.Api;

public static class Program
{
    public static void Main()
    {
        while (true)
        {
            if (CanHarvest()) Harvest();
            Move(Direction.North);
        }
    }
}
```

- 진입점: `Main` 또는 `[TfwrEntry]`가 붙은 static 메서드
- 출력: 입력 `.cs` 파일명 → 같은 상대 경로의 `.py` 모듈
- 클래스는 dict 인스턴스 + `TypeName_Method` 프리 함수로 낮춰집니다

## 게임에 적용

생성된 `.py` 파일을 게임 세이브 폴더(`.../TheFarmerWasReplaced/Saves/SaveN/`)에 복사한 뒤, 게임에서 파일 감시를 켜거나 세이브를 다시 로드하세요.

**외부에서 파일을 새로 만들거나 삭제한 경우**에는 세이브 재로드가 필요합니다.

## 관련 프로젝트

| 프로젝트 | 역할 |
|----------|------|
| [`TFWR.Api`](../TFWR.Api/) | 게임 builtins/enum 스텁 (IntelliSense·`dotnet build`용) |
| [`TFWR.Compiler`](../TFWR.Compiler/) | 컴파일러 라이브러리 (이 CLI가 호출) |
