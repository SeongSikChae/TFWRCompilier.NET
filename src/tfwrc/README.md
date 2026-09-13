# tfwrc

[The Farmer Was Replaced](https://store.steampowered.com/app/2060160/The_Farmer_Was_Replaced/)용 **C#** 또는 **TypeScript** 소스를 인게임 DSL(`.py`)로 컴파일하는 **CLI**입니다.

[`TFWR.Compiler`](../TFWR.Compiler/)를 호출합니다. C#은 Roslyn, TypeScript는 Node 기반 TsFrontend bridge를 사용합니다.

## 요구 사항

- .NET 10 SDK
- `--lang ts`: Node.js 20+ (빌드 시 `src/TFWR.Compiler/TsFrontend`에서 `npm ci`)

## 빌드

```bash
dotnet build src/tfwrc
```

single-file publish 시에도 `TFWR.Api.dll`과 `TsFrontend/`는 exe 옆에 함께 배포됩니다. 설치 레이아웃 예: `%TFWRC_HOME%\bin\tfwrc.exe` + `%TFWRC_HOME%\bin\TFWR.Api.dll` + `%TFWRC_HOME%\bin\TsFrontend\`.

## 사용법

```bash
dotnet run --project src/tfwrc -- --lang cs --out <출력디렉토리> [--toplevel] <입력.cs> [입력.cs ...]
dotnet run --project src/tfwrc -- --lang ts --out <출력디렉토리> [--toplevel] <입력.ts> [입력.ts ...]
```

또는 빌드된 실행 파일:

```bash
tfwrc --lang cs --out <출력디렉토리> [--toplevel] <입력.cs> ...
tfwrc --lang ts --out <출력디렉토리> [--toplevel] <입력.ts> ...
```

### 옵션

| 옵션 | 설명 |
|------|------|
| `--lang cs\|ts` | 소스 언어 (`cs` = C#/Roslyn, `ts` = TypeScript/Node) |
| `--out <dir>` | 생성될 `.py` 모듈 출력 디렉터리 |
| `--toplevel` / `--top-level` | 진입점을 `def` 없이 최상위 문으로 emit (`Unlocks.Functions` 해금 전용) |
| `-h` / `--help` | 도움말 |

### 예제 (C#)

```bash
dotnet run --project src/tfwrc -- --lang cs --out ./out ./MyFarm/Program.cs
dotnet run --project src/tfwrc -- --lang cs --out ./out --toplevel ./MyFarm/Program.cs
```

### 예제 (TypeScript)

```bash
dotnet run --project src/tfwrc -- --lang ts --out ./out ./MyFarm/main.ts
dotnet run --project src/tfwrc -- --lang ts --out ./out --toplevel ./MyFarm/main.ts
```

`--toplevel` 사용 시 같은 모듈에 다른 함수/생성자가 있으면(추가 `def` 필요) 오류가 납니다.

## 소스 작성 (C#)

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

## 소스 작성 (TypeScript)

[`TFWR.Api.Ts`](../TFWR.Api.Ts/)의 `tfwr` 모듈을 import합니다.

```typescript
import { canHarvest, harvest, move, Direction } from "tfwr";

export function main() {
  while (true) {
    if (canHarvest()) harvest();
    move(Direction.North);
  }
}
```

- 진입점: `main` 또는 JSDoc `/** @tfwrEntry */` 함수
- camelCase builtins → snake_case emit (`canHarvest` → `can_harvest`)

## 게임에 적용

생성된 `.py` 파일을 게임 세이브 폴더(`.../TheFarmerWasReplaced/Saves/SaveN/`)에 복사한 뒤, 게임에서 파일 감시를 켜거나 세이브를 다시 로드하세요.

**외부에서 파일을 새로 만들거나 삭제한 경우**에는 세이브 재로드가 필요합니다.

## 관련 프로젝트

| 프로젝트 | 역할 |
|----------|------|
| [`TFWR.Api`](../TFWR.Api/) | C# 스텁 |
| [`TFWR.Api.Ts`](../TFWR.Api.Ts/) | TypeScript ambient |
| [`TFWR.Compiler`](../TFWR.Compiler/) | 컴파일러 라이브러리 |
