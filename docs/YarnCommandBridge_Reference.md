# Yarn Presentation Command Reference

`YarnCommandBridge`가 `DialogueRunner`에 등록하는 모든 연출 커맨드의 사양서.
AI가 시나리오 대본에 연출을 자동으로 붙일 때 참조하는 것을 목적으로 작성됨.

---

## 0. 실행 모델 (반드시 먼저 이해할 것)

### 0.1 두 종류의 커맨드

| 종류 | 동작 | 해당 커맨드 |
|---|---|---|
| **Spec 큐잉형** (대다수) | `Collect(spec)` → `YarnBridgePlaybackDriver._collectedSpecs`에 **쌓이기만** 함. 이후 `PlayCollected()` 시점에 `CommandExecutor.PlaySpecs()`로 **한 배치로 동시 재생** | 아래 대부분 |
| **즉시 실행형** | 큐를 거치지 않고 호출 즉시 부수효과 발생 | `seq`, `debug_log`, `box_hide`, `box_show`, `box_close`, `surface_layout`, `surface_reset`, `box_named`, `box_protagonist`, `box_reset`, `pres_*`, `beat`, `beat_fx` |

> **핵심**: 한 대사 라인 앞에 붙인 커맨드들은 **개별적으로 순차 실행되지 않고, 모여서 하나의 배치로 재생**된다.
> 따라서 커맨드 나열 순서는 "타임라인 순서"가 아니라 "셋업 → 배치" 논리 순서다.
> 시간차를 주려면 `<<Nfr>>` / `<<pause>>`를 명시적으로 끼워 넣어야 한다.

### 0.2 커맨드 등록 범위

- `BindRunnerCommands()` — 메인 러너, 서브(프레젠테이션) 러너 **모두**에 등록
- `BindMainLaneCommands()` — 생성자 인자 `bindMainLaneCommands == true`인 러너에만 등록
  (`pres_*`, `beat`, `beat_fx`, `box_named`, `box_protagonist`, `box_reset`)

### 0.3 인자 생략 규칙

Yarn 커맨드는 **뒤쪽 인자부터만** 생략 가능. 중간 인자만 비울 수 없다.

```
<<slide_in c1 left>>            // OK  (distance, duration 기본값)
<<slide_in c1 left 12u 10fr>>   // OK
<<slide_in c1 12u>>             // 위험: 12u가 direction 자리에 들어가 left로 폴백됨
```

---

## 1. 공통 토큰 문법

### 1.1 시간 토큰 (`YarnDurationParser.Parse`)

| 형식 | 의미 | 예 |
|---|---|---|
| `Nfr` | N 프레임 (**24fps 기준**, N/24초) | `12fr` = 0.5초 |
| `Ns` | N 초 | `1.2s` |
| `N` (단위 없음) | N 초 (하위호환) | `0.4` |
| 빈 문자열 / 파싱 실패 | 호출부별 **fallback 값** (기본 8초, 대부분 호출부에서 개별 지정) | |

- 결과는 `Mathf.Max(0f, ...)`로 클램프 → **음수는 전부 0초(즉시 적용)**.
  - 예: `<<fade_in c1 -1s>>` = 0초 = 즉시 완전 표시.
- **프레임 전용 파서** (`ParseFrames`)는 `left_per` 계열에서만 사용. `12fr`, `12frame`, `12frames`, `12` 모두 허용.

### 1.2 거리 단위 토큰 (`YarnUnitParser`)

- **1u = 40px** (기준 스테이지 폭 1920 / 48).
- 두 가지 파서가 혼용되므로 **음수 허용 여부가 커맨드마다 다르다**. 아래 표로 반드시 구분할 것.

| 파서 | 음수 | 사용하는 커맨드 |
|---|---|---|
| `ParseSignedUnit` (**음수 가능**) | ✅ `-3u` 동작 | `move_by`, `bg_place`, `bg_move`, `shot_to`, `shot_track`, `bg_cutin_in` |
| `YarnUnitParser.Parse` (**음수 → 0으로 클램프**) | ❌ `-3u` = 0 | `left/right/up/down`, `*_per`, `slide_in/out`, `char_move_to`, `bg_slide_in/out`, `bg_jolt`, `bg_idle_tremble`, `bg_idle_breath` |

> 방향이 필요한 경우 음수 대신 **방향 전용 커맨드**(`left`, `slide_in ... right` 등)를 쓸 것.

### 1.3 스테이지 / 뎁스 레이어 키

**스테이지** (`PresentationStageKeyParser`) — 3개의 독립 무대 레이어

| 값 | 허용 토큰 |
|---|---|
| Stage00 | `0`, `00`, `s0`, `slot0`, `slot00`, `stage0`, `stage00`, `a` |
| Stage01 | `1`, `01`, `s1`, `slot1`, `slot01`, `stage1`, `stage01`, `b` |
| Stage02 | `2`, `02`, `s2`, `slot2`, `slot02`, `stage2`, `stage02`, `c` |

**뎁스 레이어** (`PresentationDepthLayerKeyParser`) — 스테이지 내부 앞뒤 순서

| 값 | 허용 토큰 |
|---|---|
| Far | `0`, `far`, `f`, `deep` |
| Back | `1`, `back`, `b`, `bg` |
| Mid | `2`, `mid`, `middle`, `m`, `center` |
| Front | `3`, `front`, `fr`, `fg` |
| Close | `4`, `close`, `near`, `c` |

### 1.4 슬롯 키 / 액터 별칭

- 캐릭터 대상은 `slotKey`(= `roleKey`)로 지정. `slot`/`cast` 계열로 먼저 만들어야 한다.
- `@`로 시작하는 문자열은 **별칭(alias)**. `actor`로 등록해야 하며, **대소문자 구분**.
- 별칭 체인은 1단계만 치환된다. 등록 안 된 `@xxx`를 쓰면 경고 후 원문 그대로 통과.

```
<<actor @4 c4>>
<<place_center @4 bust 24fr>>
```

### 1.5 방향 토큰 (`CharRigDirectionParser`)

| 값 | 토큰 |
|---|---|
| Left | `left`, `l` |
| Right | `right`, `r` |
| Up | `up`, `u`, `top`, `t` |
| Down | `down`, `d`, `bottom`, `b` |
| (기타 / 미지정) | **Left로 폴백** |

### 1.6 포커스 프리셋 (`CharacterFocusPresetParser`) — 캐릭터 몸의 어느 지점

| 값 | 토큰 | 기본 오프셋(y) |
|---|---|---|
| Feet | `feet`, `foot`, `base`, `bottom`, `f`, `p4`, `w1` | 0 |
| Body | `body`, `torso`, `mid`, `middle`, `b`, `p3`, `x1` | 400 |
| Bust | `bust`, `chest`, `upper`, `u`, `p2`, `y1` | 600 |
| Face | `face`, `head`, `eye`, `eyes`, `h`, `p1`, `z1` | 850 |
| FaceAura | (enum 이름 직접) | 1000 |
| HandLeft | `hand_left`, `left_hand`, `left`, `lh`, `p5`, `v1` | (-220, 520) |
| HandRight | `hand_right`, `right_hand`, `right`, `rh`, `p6`, `v2` | (220, 520) |

- 실패 시 폴백은 호출부마다 다름 (`Parse`는 Face, `place`는 명시적으로 Bust/Face).

### 1.7 화면 포커스 포인트 (`ScreenFocusPointParser`) — 화면상의 어느 위치

9분할 외곽 그리드(±24% X, ±16% Y) + 삼분할 내곽(±14% X, ±9% Y).

| 값 | 대표 토큰 |
|---|---|
| Center | `center`, `c`, `mid`, `b2`, `5` |
| TopLeft / Top / TopRight | `tl`/`top_left`, `top`/`t`, `tr`/`top_right` |
| Left / Right | `left`, `right` |
| BottomLeft / Bottom / BottomRight | `bl`, `bottom`, `br` |
| ThirdsUpperLeft/Right | `inner_ul`/`thirds_ul`, `inner_ur`/`thirds_ur` |
| ThirdsLowerLeft/Right | `inner_ll`/`thirds_ll`, `inner_lr`/`thirds_lr` |

### 1.8 뎁스 프리셋 (`CharacterDepthPresetParser`)

`size` 계열에서 사용. **숫자를 넣으면 연속 레벨값**, 문자를 넣으면 프리셋으로 해석된다.

| 값 | 토큰 | depthY | depthScale | 스케일 기준 포커스 |
|---|---|---|---|---|
| Far | `far`, `f` | +480 | 0.68 | Feet |
| Back | `back`, `b` | +240 | 0.86 | Bust |
| Mid | `mid`, `middle`, `normal`, `default`, `m` | 0 | 1.00 | Bust |
| Front | `front`, `fore`, `foreground` | -320 | 1.18 | Bust |
| Close | `close`, `near`, `c` | +440 | 1.38 | Face |
| None / Exp1 / Exp2 | `none`/`n`, `exp1`, `exp2` | — | — | — |

---

## 2. 진행 제어 (메인 러너 전용)

| 커맨드 | 시그니처 | 동작 |
|---|---|---|
| `pres_start` | `(string nodeName)` | 서브 프레젠테이션 레인에서 해당 노드 코루틴 시작 |
| `pres_end` | `()` | 프레젠테이션 레인 정지 (코루틴 반환 → 완료까지 대기) |
| `pres_pause` | `()` | 프레젠테이션 레인 일시정지 |
| `pres_resume` | `()` | 재개 |
| `pres_hold` | `(int lines = 1)` | 서브 레인을 N라인 동안 정지. **재호출 시 마지막 값으로 덮어씀** |
| `pres_advance` | `(int steps = 1)` | 이번 라인에서 서브 레인을 N스텝 추가 진행. **재호출 시 누적** |
| `beat` | `(string nodeName)` | One-Shot 노드 재생. **메인 블로킹** (완료까지 대기) |
| `beat_fx` | `(string nodeName)` | One-Shot 노드 재생. **논블로킹** (장식 효과용) |

### 대사창 종류 전환

| 커맨드 | 시그니처 | 동작 |
|---|---|---|
| `box_named` | `(string kind)` | 이름 있는 화자 라인의 박스 종류 지정 |
| `box_protagonist` | `(string kind)` | 주인공(무명) 라인의 박스 종류 지정 |
| `box_reset` | `()` | 기본값 복원 (protagonist=`Surface`, named=`Speaker`) |

`DialogueBoxKind`: `Portrait`(0), `Speaker`(1), `LetterBox`(2), `OnlyText`(3), `BlackBook`(4), `Surface`(5)

> ⚠ `Enum.TryParse` 결과를 검사하지 않으므로 **오타 시 조용히 `Portrait`(0)로 떨어진다.** 정확한 enum 이름을 쓸 것.

---

## 3. 공통 제어 (모든 러너)

| 커맨드 | 시그니처 | 기본값 | 동작 |
|---|---|---|---|
| `1fr` ~ `48fr` | `()` | — | 프레임 대기 별칭. `<<24fr>>` = 1초 대기. **49프레임 이상은 없음** → `pause` 사용 |
| `pause` | `(float seconds)` | `0.18` | `WaitCommandSpec` 큐잉. **초 단위 float** (`fr` 토큰 아님) |
| `seq` | `(string sequenceKey)` | — | 카탈로그의 오버레이 시퀀스를 **즉시** 재생 (큐 미경유) |
| `ui_patch` | `(string themeId)` | `"default"` | UI 테마 패치 |
| `debug_log` | `(string message)` | — | **즉시** `Debug.Log` |
| `attach_to_bg` | `(string charRigKey, string bgRigKey, string parentTarget)` | `Background_ObjectSlotRoot` | 캐릭터 리그를 배경 리그의 오브젝트 슬롯 아래로 재부모화. `worldPositionStays=false` |
| `actor` | `(string aliasSymbol, string targetKey)` | — | `@alias` → slotKey 별칭 등록 |
| `box_hide` / `box_show` | `()` | — | 현재 대사창 페이드 out/in (**즉시, await**) |
| `box_close` | `()` | — | 대사창을 닫고 **현재 박스 상태 자체를 폐기**. 이후 `box_show`는 무시되고, 다음 대사에서 새로 FadeIn |
| `surface_layout` | `(string presetKey)` | — | 서피스 레이아웃 프리셋 지정. **다음 라인의 `ShowLineAsync` 시점에 적용** (현재 표시 중인 박스에는 즉시 반영 안 됨) |
| `surface_reset` | `()` | — | 서피스 레이아웃 기본값 복원 |

---

## 4. 캐릭터 리그 — 생성 / 캐스팅

| 커맨드 | 시그니처 | 기본값 |
|---|---|---|
| `slot` | `(slotKey, stageKey, layerKey)` | `stage00`, `mid` |
| `slot00` / `slot01` / `slot02` | `(slotKey, layerKey)` | `mid` |
| `slot_tyrant` | `()` | — |
| `cast` | `(slotKey, characterKey, variantKey, emotionKey)` | `a`, `1` |
| `pose` | `(slotKey, variantKey)` | — |
| `face` | `(slotKey, emotionKey)` | — |
| `char_color_to` | `(slotKey, float r, float g, float b, durationToken)` | `8fr` (fallback 0.35s) |
| `mirror` | `(slotKey, directionToken)` | `""` → **toggle** |

**세부 동작**

- `slot` — 지정한 스테이지/레이어에 `CharacterRig` 프리팹으로 리그 루트를 생성하고 `roleKey`로 등록. 오브젝트 이름 접두사는 `roleKey_`.
- `slot_tyrant` — 복합 매크로. 슬롯키 `"tyrant"`로 **주인공 전용 슬롯**에 리그 생성 → `cast tyrant Tyrant a 2` → `fade_in`(0초, 즉시) → `down 4u` → `right 0.6u`.
- `cast` — 4개 스펙을 한 번에 큐잉:
  1. `CastCharacterCommandSpec` (슬롯 ↔ 캐릭터 바인딩)
  2. `pose`(variant 적용)
  3. `face`(emotion 적용)
  4. `SetAnchorCommandSpecCharR` (슬롯/캐릭터 위치 리셋)
  - 재캐스팅 시 기존 바인딩은 해제되지만 **facing(좌우 반전) 상태는 유지**된다.
- 호출 순서 계약: **`slot` → `cast` → `pose`/`face`**. 어기면 `CastRegistry`가 경고 로그를 내고 무시한다.
- `char_color_to` — 포트레이트 이미지 색상 틴트. `r,g,b`는 **0~1 범위**, 알파는 유지(`keepAlpha=true`).
- `mirror` — `CharacterMirrorModeParser`

| 모드 | 토큰 |
|---|---|
| Left(반전) | `left`, `l`, `mirror`, `mirrored`, `true`, `1` |
| Right(원본) | `right`, `r`, `normal`, `default`, `unmirror`, `unmirrored`, `false`, `0` |
| Toggle | `toggle`, `t`, `flip`, `switch`, **그 외 전부 / 미지정** |

---

## 5. 캐릭터 리그 — 스테이징 (슬롯 축 조작)

| 커맨드 | 시그니처 | 기본값 | 타겟 노드 | 성격 |
|---|---|---|---|---|
| `move_by` | `(slot, xToken, yToken, dur)` | `0u`, `0u`, `0.4s` | `CharSlot_Track` | 상대 이동, **음수 가능** |
| `rotate_by` | `(slot, float degree, dur)` | `0.4s` | `CharSlot_SwayPivot` | 상대 회전(Z) |
| `scale_by` | `(slot, float multiplier, dur)` | `0.4s` | `CharSlot_Scale` | 상대 배율 |
| `move_reset` | `(slot, dur)` | `0.4s` | `CharSlot_Track` + `CharSlot_Track_Focus` | 두 축 모두 절대 0으로 |
| `rotate_reset` | `(slot, dur)` | `0.4s` | `CharSlot_SwayPivot` | 절대 0도 |
| `scale_reset` | `(slot, dur)` | `0.4s` | `CharSlot_Scale` | 절대 1배 |
| `sibling_front` | `(slot)` | — | — | 같은 부모 내 최전면으로 |
| `sibling_back` | `(slot)` | — | — | 같은 부모 내 최후면으로 |
| `char_to` | `(slot, stageKey, layerKey)` | `stage00`, `mid` | — | 리그를 다른 스테이지/레이어로 재부모화 (재부모 후 Front 정렬) |
| `char_to_s0` / `s1` / `s2` | `(slot, layerKey)` | `mid` | — | 위의 축약 |

---

## 6. 캐릭터 리그 — 등장 / 넛지 이동

| 커맨드 | 시그니처 | 기본값 |
|---|---|---|
| `show` | `(slot, faceToken, dur)` | `e1`, `14fr` |
| `left` / `right` / `up` / `down` | `(slot, unitToken, dur)` | dur `8fr` |
| `left_per` / `right_per` / `up_per` / `down_per` | `(slot, frameToken)` | `1fr` |

**세부 동작**

- `show` — 4스펙 묶음: 앵커 리셋 → 표정 스프라이트 세팅 → `RigRoot` 페이드인 → `CharacterPortraitSprite_Root` 페이드인.
  - `faceToken`은 `ShowFaceAliasParser`로 정규화: `e1`→`1`, `emo2`→`2`, `face3`→`3`, `emotion4`→`4`. 미지정 시 `"2"`.
- `left/right/up/down` — `CharSlot_Track_X` / `CharSlot_Track_Y`에 상대 이동, `Ease.OutCubic`.
  - **unitToken은 필수 인자**(기본값 없음). 음수 불가.
- `*_per` — "프레임당 1u" 등속 이동. `frames` 값이 곧 **이동 거리(frames × 1u)이자 지속시간(frames/24초)**. `Ease.Linear`.
  - 예: `<<right_per c1 12fr>>` = 0.5초 동안 오른쪽으로 12u(480px) 등속.

---

## 7. 캐릭터 리그 — 포커스 배치 (`place` 계열)

캐릭터의 **특정 신체 지점(focus)**을 **화면의 특정 지점(screenPoint)**에 맞춘다. VN에서 인물 배치의 기본 수단.

| 커맨드 | 시그니처 | 기본값 |
|---|---|---|
| `place` | `(slot, focus, screenPoint, dur)` | `bust`, `center`, `0fr` |
| `place_left` / `place_center` / `place_right` | `(slot, focus, dur)` | `face`, `0fr` |
| `place_tl` / `place_top` / `place_tr` | `(slot, focus, dur)` | `face`, `0fr` |
| `place_bl` / `place_bottom` / `place_br` | `(slot, focus, dur)` | `face`, `0fr` |
| `place_inner_tl` / `place_inner_tr` | `(slot, focus, dur)` | `face`, `0fr` |
| `place_inner_bl` / `place_inner_br` | `(slot, focus, dur)` | `face`, `0fr` |

- 이동 타겟은 `CharSlot_Track_Focus` (일반 `move_by`와 **다른 축** → 서로 간섭하지 않음).
- `place_*`의 두 번째 인자는 **screenPoint가 아니라 focus**다. screenPoint는 커맨드 이름에 고정되어 있다.
- `inner_*`는 삼분할 구도(rule of thirds)용 내곽 지점.

---

## 8. 캐릭터 리그 — 뎁스 / 크기 (`size` 계열)

원근감을 표현. Y 위치와 스케일을 함께 바꾸되, **지정한 포커스 지점이 화면상에서 유지**되도록 피벗 보정한다.

| 커맨드 | 시그니처 | 기본값 |
|---|---|---|
| `size` | `(slot, depthArg, preserveFocus, dur)` | `bust`, `10fr` |
| `size_far` / `size_back` / `size_mid` / `size_front` / `size_close` | `(slot, preserveFocus, dur)` | `bust`, `10fr` |
| `size_reset` | `(slot, float durationSeconds)` | — |

- `depthArg`가 **숫자로 파싱되면 연속 레벨값**(`useLevel=true`, 0~10 커브 보간), 아니면 프리셋 키.
  - `<<size c1 14 bust -1>>` → 레벨 14, preserveFocus=bust, duration 토큰 `-1` → 0초(즉시).
- ⚠ `size_reset`의 마지막 인자는 **토큰이 아니라 float 초**다. `<<size_reset c1 0.4>>` (○) / `<<size_reset c1 10fr>>` (✗ — 파싱 실패).
- 알 수 없는 프리셋/포커스는 경고 후 각각 `Mid` / `Bust`로 폴백.

---

## 9. 캐릭터 리그 — 프레젠테이션 (표시 / 이동 / 변형)

| 커맨드 | 시그니처 | 기본값 | 타겟 |
|---|---|---|---|
| `fade_in` | `(slot, dur)` | `14fr` | `CharacterPortraitSprite_Root` |
| `fade_out` | `(slot, dur)` | `14fr` | `CharacterPortraitSprite_Root` |
| `face_swap` | `(slot, emotion, dur)` | `10fr` | 표정 **와이프** 전환 |
| `face_crossfade` | `(slot, character, emotion, dur)` | `10fr` | 표정/캐릭터 **크로스페이드** |
| `slide_in` | `(slot, direction, distance, dur)` | `left`, `12u`, `10fr` | 해당 방향에서 들어옴 |
| `slide_out` | `(slot, direction, distance, dur)` | `right`, `12u`, `10fr` | 해당 방향으로 나감 |
| `char_move_to` | `(slot, xToken, yToken, dur)` | dur `10fr` | `CharacterPortrait_Track`, 상대 이동, **음수 불가** |
| `char_scale_to` | `(slot, float xy, dur)` | `10fr` | `CharacterPortrait_ActingScale`, **절대값** |
| `char_rotate_to` | `(slot, int angle, dur)` | `10fr` | `CharacterPortrait_SwayPivot` 기준 회전 |
| `char_flip_horizontal` | `(slot, int angle, dur)` | `6fr` | `CharacterPortrait_Rotation`의 **Y축 오일러** |
| `char_flip_vertical` | `(slot, int angle, dur)` | `6fr` | `CharacterPortrait_Rotation`의 **X축 오일러** |

- `face_crossfade`는 캐릭터 키까지 지정 가능 → 다른 캐릭터로의 페이드 전환에도 사용.
- `char_flip_*`의 `angle`은 **int**. 180이면 완전 반전, 0이면 원상복귀.
- 슬롯 축(`move_by` 등)과 캐릭터 축(`char_move_to` 등)은 별개 노드다. 리셋 대상이 다르니 섞어 쓸 때 주의.

---

## 10. 캐릭터 리그 — 액팅 (단발 반응)

| 커맨드 | 시그니처 | 튜닝값 |
|---|---|---|
| `dip` | `(slot, direction)` | 기본 `down`. `CharacterPortrait_Track_Move_Y` |
| `hop` | `(slot)` | 1회, height 22, airWidth 0.88, 0.6초 |
| `shake` | `(slot, direction)` | 기본 `right`. strength 44, taps 4, 1.2초. `CharacterPortrait_Shake` |
| `tremble` | `(slot, direction)` | 기본 `right`. strength 8, freq 24Hz, 1.2초 |
| `sway` | `(slot)` | strength 12°, 2사이클, damping 1.9, 1.15초 |

## 11. 캐릭터 리그 — 프리셋 조합

| 커맨드 | 시그니처 | 특성 |
|---|---|---|
| `jolt` | `(slot, direction)` | 기본 `right`. strength 340, taps 3, 0.6초, anticipation -12 |
| `tap` | `(slot, direction)` | 기본 `right`. taps **1**, damping 9 — 가벼운 한 번 |
| `tap_hard` | `(slot, direction)` | 기본 **`down`**. strength **1400**, taps 1, 0.7초 — 강한 충격 |
| `slide_in_sway` | `(slot)` | 페이드인 + 슬라이드(550px) + 딥 + 펀치스케일 + 흔들림 복합 등장 |
| `slide_in_nudge` | `(slot, direction)` | 슬라이드인 + 졸트 조합 |
| `sway_hard` | `(slot)` | 진자형. strength 13°, 1.35초, `wait=false` (논블로킹) |
| `sway_fast` | `(slot)` | strength 6.5°, 3사이클, 0.94초 — 빠르고 잘게 |
| `sway_away` | `(slot)` | strength 15°, 1사이클, 0.74초 — 크게 한 번 젖힘 |

---

## 12. 캐릭터 리그 — 비주얼 합성 (림/딤/실루엣)

| 커맨드 | 시그니처 | 기본 duration |
|---|---|---|
| `char_visual` | `(slot, presetKey, float intensity, dur)` | `6fr` |
| `char_visual_focus` | `(slot, float intensity, dur)` | `10fr` |
| `char_visual_defocus` | `(slot, float intensity, dur)` | `17fr` |
| `char_visual_dim` | `(slot, float intensity, dur)` | `6fr` |
| `char_visual_silhouette` | `(slot, float intensity, dur)` | `6fr` |
| `char_visual_inner_rim` | `(slot, float intensity, dur)` | `6fr` |
| `char_visual_outer_rim` | `(slot, float intensity, dur)` | `6fr` |
| `char_visual_clear` | `(slot, dur)` | `6fr` |

`intensity`는 **0~1** 범위(기본 1).

**프리셋 값** (`CharacterVisualFocusPresetDBSO` 기본 세트)

| key | dim | outerRim | innerRim | 용도 |
|---|---|---|---|---|
| `clear` | 0 | 0 | 0 | 효과 제거 |
| `focus` | 0 | 0.4 | 0.09 | 말하는 인물 강조 |
| `defocus` | 0.45 | 0 | 0 | 듣는 인물 후퇴 |
| `dim` | 0.55 | 0 | 0 | 더 강한 후퇴 |
| `silhouette` | 1.0 (검정) | 0 | 0 | 완전 실루엣 |
| `inner_rim` | 0 | 0 | 0.4 | 안쪽 광 |
| `outer_rim` | 0 | 0.4 | 0 | 외곽 광 |

**키 별칭** (`CharacterVisualFocusPresetKeyParser`): `default`→`focus`, `none`/`off`/`reset`→`clear`, `rim`/`outer`/`outerrim`→`outer_rim`, `inner`/`innerrim`→`inner_rim`, `sil`/`black`/`shadow`→`silhouette`, `de_focus`→`defocus`.

---

## 13. 캐릭터 리그 — 아이들 루프

| 커맨드 | 시그니처 | 동작 |
|---|---|---|
| `idle_bounce` | `(slot)` | 초당 2.5회 통통, height 32, sideSway 0.2 |
| `idle_breathe` | `(slot)` | 초당 0.35회 호흡, height 8 |
| `idle_flinch` | `(slot, direction)` | 1초 간격 펄스형 미세 떨림 (strength 5, 28Hz) |
| `idle_walk` | `(slot)` | 제자리 걸음, 초당 1.9걸음, arc 18 |
| `idle_stop` | `(slot)` | **모든 아이들 정지** |

- 전부 타겟이 `CharSlot_Track_Idle` 단일 축 → **동시에 하나만 유효**. 새 아이들을 걸면 이전 것이 대체된다.
- 지속시간이 `99초`로 설정되어 사실상 무한 루프. `wait=false`라 다음 커맨드를 막지 않는다.
- `idle_stop`은 duration `-1f`인 `WalkInPlace` 스펙으로 축을 클레임해 해제하는 방식.

---

## 14. 캐릭터 리그 — 이모지 (기본 조작)

| 커맨드 | 시그니처 | 기본값 |
|---|---|---|
| `emoji_show` | `(slot, emojiKey)` | 즉시 표시(reveal 1) |
| `emoji_hide` | `(slot)` | 0.16초 페이드아웃 |
| `emoji_place` | `(slot, emojiKey)` | 라이브러리 기본 배치값 적용 |
| `emoji_reveal` | `(slot, float toReveal, dur)` | `1`, `8fr` |
| `emoji_scale` | `(slot, float value, dur)` | `8fr` |
| `emoji_rotate` | `(slot, int angle, dur)` | `8fr` |

전부 `EmojiSlot00` 계열 노드를 사용한다.

## 15. 캐릭터 리그 — 이모지 프리셋 연출

모두 시그니처 `(slot, emojiKey)`. 초기화 → reveal → 고유 모션 → **하트비트 아이들**(초기지연 0.9초, 2.05초 간격 더블 펄스) 순으로 큐잉된다.

| 커맨드 | 느낌 |
|---|---|
| `emoji` | 기본 팝 (스케일 업/다운) |
| `emoji_drop` | 위에서 떨어짐 |
| `emoji_shock` | 졸트 + 1.28배 확대 후 복귀 — 충격 |
| `emoji_hop` | reveal 후 튀어오름 (height 54) |
| `emoji_sway` | 좌우 흔들림 (9°, 1사이클) |
| `emoji_tremble` | 미세 떨림 |
| `emoji_spring` | 스프링 등장 |
| `emoji_spit` | 튀어나감 |
| `emoji_pinwheel` | 회전 |
| `emoji_heartfly` | 하트가 종이비행기처럼 날아감 |
| `emoji_chatter` | 재잘거림(위글) |
| `emoji_ellipsis` | 말줄임(…) |

---

## 16. 배경 리그

| 커맨드 | 시그니처 | 기본값 |
|---|---|---|
| `bg_spawn` | `(rigKey, spriteKey)` | Stage00 / Far 고정 |
| `bg_slot00` / `bg_slot01` / `bg_slot02` | `(rigKey, spriteKey, layerKey)` | layer `far` |
| `bg_place` | `(rigKey, xToken, yToken, float rotationZ)` | `0u`, `0u`, `0` |
| `bg_sprite` | `(rigKey, spriteKey, layerKey)` | — |
| `bg_size` | `(rigKey, scaleArg)` | `1` |
| `bg_fade_in` / `bg_fade_out` | `(rigKey, dur)` | `10fr` (fallback 0.4s) |
| `bg_move` | `(rigKey, xToken, yToken, dur)` | `10fr`. **음수 가능**, OutCubic |
| `bg_scale` | `(rigKey, float scale, dur)` | `10fr` |
| `bg_slide_in` | `(rigKey, direction, distance, dur)` | `left`, `12u`, `13fr` |
| `bg_slide_out` | `(rigKey, direction, distance, dur)` | `right`, `12u`, `11fr` |
| `bg_jolt` | `(rigKey, direction, strength, dur)` | `right`, `0.55u`, `21fr`. taps 3 |
| `bg_idle_tremble` | `(rigKey, direction, strength, dur)` | `right`, `0.2u`, `29fr` |
| `bg_idle_breath` | `(rigKey, dur, heightToken, float breathsPerSec)` | `99s`, `0.15u`, `0.2` |
| `bg_slot_cutin` | `(rigKey)` | 고정 프리셋 |
| `bg_cutin_in` | `(rigKey, xToken, yToken, dur)` | `0.18u`, `9.65u`, `12fr` |

**세부**

- `bg_place`는 `Background_Anchor`에 절대 위치 + Z회전을 세팅한다(트윈 없음).
- `bg_sprite`의 세 번째 인자 `layerKey`는 **현재 스펙에 반영되지 않는다**(시그니처에만 존재). 무시해도 무방.
- `bg_size`는 `Background_Size` 노드의 스케일을 절대값으로 덮어쓴다.
- `bg_slot_cutin` — Stage02/Mid에 리그 생성, scale 0.68, 오브젝트 슬롯 -380px, 마스크 스프라이트 `slot3bg`, 이미지 `slot3bg2`를 하드코딩 적용. 컷인 프레임 전용.
- `bg_cutin_in` — 아래 작은 점에서 튀어나와 살짝 오버슛 후 되눌렀다 정착하는 3단 모션. duration을 50%/22%/28%로 분배.

---

## 17. 샷 (카메라)

| 커맨드 | 시그니처 | 기본값 |
|---|---|---|
| `shot_focus_to` | `(slot, focus, screenPoint, float zoom, dur)` | `body`, `center`, `2.5`, `1.2s` |
| `shot_to` | `(float zoom, xToken, yToken, dur)` | `1`, `2.5u`, `0u`, `0.45s` |
| `shot_zoom` | `(float zoom, dur)` | `1`, `0.45s` |
| `shot_track` | `(xToken, yToken, dur)` | `2.5u`, `0u`, `0.35s` |
| `shot_reset` | `(dur)` | `0.3s` |

- `shot_focus_to`는 지정 캐릭터의 focus 지점을 화면의 screenPoint로 가져오며 zoom을 적용 → **인물 클로즈업의 표준 수단**.
- `shot_to` / `shot_track`의 x, y는 **음수 가능**(`ParseSignedUnit`).
- `shot_zoom`은 팬 없이 배율만, `shot_track`은 배율 없이 팬만.

---

## 18. 트랜지션 (스테이지 마스크 모션)

모두 시그니처 `(string stage, string durationToken)`.

- `stage` 기본값: **대부분 `"01"`**, 단 `tx_daze_*` / `tx_strip_*`은 **`"00"`**.
- `durationToken`이 **빈 문자열이면 프리셋 자체의 duration을 사용**(`durationOverride = -1`). 값을 주면 오버라이드. `0`이면 즉시 커밋.

| 커맨드 | 프리셋 키 | 느낌 |
|---|---|---|
| `tx_slant_in` / `tx_slant_out` | `slant_in` / `slant_out` | 사선 마스크 |
| `tx_hstrip_open` / `tx_hstrip_close` | `hstrip_open` / `hstrip_close` | 가로 스트립 개폐 |
| `tx_hstrip_in` / `tx_hstrip_out` | `hstrip_in` / `hstrip_out` | 가로 스트립 컷 |
| `tx_vstrip_open` / `tx_vstrip_close` | `vstrip_open` / `vstrip_close` | 세로 스트립 개폐 |
| `tx_vstrip_in` / `tx_vstrip_out` | `vstrip_in` / `vstrip_out` | 세로 스트립 컷 |
| `tx_band_in` / `tx_band_out` | `band_in` / `band_out` | 대각 밴드 |
| `tx_iris_in` / `tx_iris_out` | `iris_in` / `iris_out` | 원형 아이리스 |
| `tx_daze_in` / `tx_daze_out` | `daze_close` / `daze_open` | 몽롱함 페이드 (기본 stage `00`) |
| `tx_strip_in` / `tx_strip_out` | `strip_cover` / `strip_clear` | 세로 스트립 커버 (기본 stage `00`) |
| `tx_stage_mask_clear` | — | **Stage00/01/02 전부** 마스크 해제 (`UnmaskedFullVisible`, edge 숨김) |

---

## 19. 오디오

| 커맨드 | 시그니처 | 기본값 |
|---|---|---|
| `bgm` | `(clipKey, fadeDurationToken)` | `1s` |
| `stop_bgm` | `(fadeDurationToken)` | `1s` |
| `sfx` | `(clipKey)` | — |
| `stop_all_sfx` | `()` | — |
| `voice` | `(clipKey)` | — |
| `stop_voice` | `()` | — |

---

## 20. 스크린 이펙트

| 커맨드 | 시그니처 | 기본값 |
|---|---|---|
| `screen_flash` | `(presetKey, float intensity)` | intensity `1` |
| `screen_flash_clear` | `()` | 프리셋 `clear` |
| `screen_vignette` | `(presetKey, float intensity, dur)` | `1`, `0.35s` |
| `screen_vignette_clear` | `(dur)` | `0.35s` |
| `screen_noise` | `(presetKey, float intensity, dur)` | 프리셋 `default`, `1`, `0.35s` |
| `screen_noise_clear` | `(dur)` | `0.35s` |

- `intensity`는 **0~1**.
- 프리셋 키는 정규화됨(소문자, 공백/하이픈 → `_`).
  - Flash 예시 키: `clear`, `default`, `soft`, `hit`, `camera`
  - Vignette 예시 키: `clear`, `focus`(기본), `horror`, `dream`, `letterbox`. 별칭 `lb`/`letter_box` → `letterbox`, `default`/`default_focus` → `focus`
  - Noise 예시 키: `clear`, `default`, `memory`, `horror`, `broadcast`. 별칭 `normal`/`base` → `default`, `rain`/`rainmood` → `rain_mood`
- 실제 사용 가능한 키는 각 `PresetDBSO` 에셋의 엔트리에 따름.

## 21. 스테이지 뎁스 블러

| 커맨드 | 시그니처 | 기본값 |
|---|---|---|
| `screen_blur` | `(layerKey, float blurRadius)` | `mid`, `1` — **Stage00** |
| `screen_blur_s1` | `(layerKey, float blurRadius)` | `mid`, `1` — Stage01 |
| `screen_blur_s2` | `(layerKey, float blurRadius)` | `mid`, `1` — Stage02 |
| `screen_blur_reset` | `()` | 전체 해제 |

- `blurRadius > 0`이면 표시, `0` 이하면 비활성.
- `layerKey` 파싱 실패 시 **커맨드 자체가 조용히 무시**된다(스펙 큐잉 안 됨).

---

## 22. 오버레이 리그

**주의: 오버레이 커맨드의 좌표/크기는 `u` 토큰이 아니라 순수 float 픽셀값이다.**

| 커맨드 | 시그니처 | 기본값 |
|---|---|---|
| `overlay_rig` | `(overlayKey, rootKindToken)` | `sprite` |
| `sprite_rig` | `(overlayKey)` | Sprite 루트 |
| `text_rig` | `(overlayKey)` | Text 루트 |
| `overlay_move` | `(rigKey, float x, float y, dur)` | `0s` — **절대 좌표** |
| `overlay_move_by` | `(rigKey, float x, float y, dur)` | `8fr` — 상대 |
| `overlay_size` | `(rigKey, float w, float h, dur)` | `0s` — 절대 |
| `overlay_size_by` | `(rigKey, float dw, float dh, dur)` | `8fr` — 상대 |
| `overlay_scale` | `(rigKey, float x, float y, dur)` | `0s` — 절대 |
| `overlay_scale_by` | `(rigKey, float x, float y, dur)` | `8fr` — 상대 |
| `overlay_show` | `(rigKey, dur)` | `8fr` |
| `overlay_hide` | `(rigKey, dur)` | `8fr` |
| `overlay_sprite` | `(rigKey, resourcesPath, setNativeSizeToken)` | `"true"` |
| `overlay_text` | `(rigKey, text)` | — |

- `rootKindToken`: Sprite = `sprite`/`spr`/`image`/`img`/`s`/빈값, Text = `text`/`txt`/`t`.
- `overlay_sprite`의 `setNativeSizeToken`은 문자열 `"false"`일 때만 false, 그 외 전부 true.
- 오버레이 리그 생성 시 루트 알파는 0에서 시작하므로 **`overlay_show`를 호출해야 보인다**.

---

## 23. AI 자동 연출 작성 지침

### 23.1 표준 등장 시퀀스

```
<<slot c1 stage00 mid>>            // 리그 생성
<<cast c1 bandi a 3>>              // 캐스팅 + 포즈 + 표정 + 앵커 리셋
<<actor @1 c1>>                    // (선택) 별칭 등록
<<place_center @1 bust 0fr>>       // 배치 (즉시)
<<size @1 mid bust 0fr>>           // 뎁스 (즉시)
<<fade_in @1 14fr>>                // 표시
```

**순서 계약**: `slot` → `cast` → (`pose`/`face`) → `place`/`size` → `fade_in`.
배치와 뎁스를 `0fr`로 먼저 확정한 뒤 페이드로 드러내는 것이 안전하다.

### 23.2 대화 중 표정/강조

```
<<face_swap c1 5 10fr>>            // 표정 전환
<<char_visual_focus c1 1 10fr>>    // 말하는 쪽 강조
<<char_visual_defocus c2 1 17fr>>  // 듣는 쪽 후퇴
```

### 23.3 감정 반응 (강도순)

| 강도 | 추천 |
|---|---|
| 미세 | `idle_flinch`, `tremble` |
| 가벼움 | `tap`, `dip`, `sway_fast` |
| 보통 | `jolt`, `hop`, `shake`, `sway` |
| 강함 | `tap_hard`, `sway_away`, `screen_flash hit` |

### 23.4 씬 전환 템플릿

```
<<tx_iris_in 01>>
<<24fr>>
<<bg_sprite bg1 room_night>>
<<tx_iris_out 01>>
```

### 23.5 흔한 실패 패턴 (반드시 피할 것)

| 실수 | 결과 | 대신 |
|---|---|---|
| `<<left c1 -3u>>` | 음수가 0으로 클램프되어 **아무 일도 안 일어남** | `<<right c1 3u>>` |
| `<<char_move_to c1 -2u 0u>>` | 동일 (음수 불가) | `<<move_by c1 -2u 0u>>` (슬롯 축, 음수 가능) |
| `<<size_reset c1 10fr>>` | float 파싱 실패 | `<<size_reset c1 0.4>>` |
| `<<pause 12fr>>` | float 파싱 실패 | `<<pause 0.5>>` 또는 `<<12fr>>` |
| `<<60fr>>` | 등록되지 않은 커맨드 | `<<pause 2.5>>` |
| `<<box_named Speeker>>` | 오타 → 조용히 `Portrait`로 | 정확한 enum 이름 |
| `cast` 없이 `pose`/`face` | CastRegistry 경고 후 무시 | `cast` 선행 |
| `idle_bounce` + `idle_walk` 동시 | 같은 축이라 뒤엣것만 남음 | 하나만 |
| `slide_in` 인자 건너뛰기 | 앞 인자 자리로 밀림 | 앞 인자부터 명시 |
| 별칭 대소문자 혼용 (`@A` vs `@a`) | 미등록 경고 후 원문 통과 | 표기 통일 |

### 23.6 타이밍 감각 (24fps 기준)

| 용도 | 권장 |
|---|---|
| 즉시(컷) | `0fr` |
| 순간 반응 | `4fr`~`6fr` (0.17~0.25s) |
| 표준 전환 | `10fr`~`14fr` (0.42~0.58s) |
| 여유 있는 이동 | `20fr`~`24fr` (0.83~1.0s) |
| 씬 전환 / 카메라 | `1.2s` 내외 |

### 23.7 라인당 커맨드 밀도

- 대사 한 라인 앞의 커맨드는 **한 배치로 동시 재생**된다. 5~8개를 넘기면 화면에서 읽히지 않는다.
- 순차 연출이 필요하면 `beat` / `beat_fx`로 One-Shot 노드에 분리하거나, `<<Nfr>>`로 명시적 간격을 넣을 것.
- `beat`는 메인을 막고, `beat_fx`는 막지 않는다. 장식용 효과는 `beat_fx`.
