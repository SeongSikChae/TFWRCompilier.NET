# TFWR.Api

[The Farmer Was Replaced](https://store.steampowered.com/app/2060160/The_Farmer_Was_Replaced/) 게임 builtins을 미러링한 **컴파일 타임 전용** 스텁 라이브러리입니다.

C# 농장 스크립트를 작성할 때 IntelliSense와 `dotnet build`를 쓰기 위한 API 표면을 제공하며, 실제 실행은 [TFWRCompilier.NET](https://github.com/SeongSikChae/TFWRCompilier.NET)의 `tfwrc`가 인게임 DSL(`.py`)로 번역한 뒤 게임에서 이루어집니다.

## 역할

| 구성 | 설명 |
|------|------|
| `Game` | `Harvest`, `Move`, `Plant` 등 게임 내장 함수 스텁 |
| enum | `Direction`, `Entities`, `Grounds`, `Hats`, `Items`, `Leaderboards`, `Unlocks` |
| `Drone` | 드론 핸들 타입 (불투명) |
| `[TfwrEntry]` | 프로그램 진입점 표시 (또는 `Main`) |

메서드 본문은 호출 시 `NotSupportedException`을 던집니다. **런타임 구현이 아니며**, IDE·컴파일 검증용입니다.

## 사용법

프로젝트에서 `TFWR.Api`를 참조하고 `using static TFWR.Api.Game;`를 사용합니다.

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

진입점은 `Main`, 또는 `[TfwrEntry]`가 붙은 static 메서드입니다.

```csharp
[TfwrEntry]
public static void Run()
{
    Till();
    Plant(Entities.Carrot);
}
```

작성한 `.cs`는 `tfwrc`로 컴파일합니다.

```bash
dotnet run --project src/tfwrc -- --lang cs --out ./out ./MyFarm/Program.cs
```

## 패키지 정보

- 대상 프레임워크: `net10.0`
- XML 문서 파일 포함 (`GenerateDocumentationFile`)
