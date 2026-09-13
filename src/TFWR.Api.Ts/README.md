# TFWR.Api.Ts

[The Farmer Was Replaced](https://store.steampowered.com/app/2060160/The_Farmer_Was_Replaced/) builtins을 미러링한 **TypeScript ambient** 모듈입니다.

IntelliSense·`tsc` 검증용이며, 실행은 `tfwrc --lang ts`가 인게임 DSL(`.py`)로 번역한 뒤 게임에서 이루어집니다.

## 사용법

```typescript
import { canHarvest, harvest, move, Direction } from "tfwr";

export function main() {
  while (true) {
    if (canHarvest()) harvest();
    move(Direction.North);
  }
}
```

`tsconfig.json`에서 경로를 이 패키지의 `tfwr.d.ts`로 매핑하세요.

```json
{
  "compilerOptions": {
    "strict": true,
    "paths": { "tfwr": ["./node_modules/@tfwr/api/tfwr.d.ts"] }
  }
}
```

또는 저장소의 [`src/TFWR.Api.Ts/tfwr.d.ts`](tfwr.d.ts)를 직접 가리킵니다.

진입점은 `main`, 또는 JSDoc `/** @tfwrEntry */`가 붙은 함수입니다.

```bash
dotnet run --project src/tfwrc -- --lang ts --out ./out ./MyFarm/main.ts
```

**요구 사항:** Node.js 20+ (`--lang ts` 전용).
