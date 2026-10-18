# hun0fx (Free) StylizedVFX Fire Pack — 2.0.0

6 free stylized fire effects: campfire, fireball, flame pillar, ground fires and a flame breath.
All effects, textures and meshes are hand-made by hun0fx.

## Quick start

1. Open `Assets/Hun0FX/Demo/Scenes/Demo_Fire_Free` and press Play. ← / → browse, Space replays, the sliders change color, intensity, scale and tempo. Right-drag orbits, the wheel zooms.
2. Prefabs are in `Assets/Hun0FX/FX/FireFX_vol1`. Drop one in a scene and select its **root**: the Scene view's Particle Effect panel plays the whole effect.
3. From code:

```csharp
using Hun0FX;
var fx = HunFX.Spawn(prefab, position, rotation);                      // removed when it ends
var fx2 = HunFX.Spawn(prefab, position, rotation, color: new Color(0.55f, 0.3f, 1f), scale: 1.2f, tempo: 1.1f);
fx.SetIntensity(1.4f);   // brightness
fx.Stop();               // end a looping effect
```

**HunFXController** (on the prefab root, added automatically by `HunFX.Spawn` if missing): `recolor` + `targetColor`, `intensity`, `scale`, `tempo`, `hiddenLayers`. Changes are per instance; shared materials and prefabs are not modified.

## Render pipelines

- **URP**: works out of the box. Turn on HDR in the URP asset and Post Processing on the camera for bloom.
- **Built-in**: works out of the box. Turn on Allow HDR on the camera and add Bloom with the Post Processing package for the intended look. The demo scene is set up for URP: in a Built-in project Unity lists its URP camera / volume components as missing scripts. That is harmless.
- Most particle systems are named after their role (`Slash_Arc_A`, `Embers`, `Smoke_B`…), so layers are easy to find and tweak.

## Folders

- `FX/FireFX_vol1`: the prefabs (and their own materials)
- `FX/BasicMaterial`, `Textures`, `Meshes`, `Shader`: shared by all hun0fx packs (same files in every pack, safe to overwrite)
- `Core/Runtime`: `HunFX.cs`, `HunFXController.cs`
- `Demo`: the demo scene, scripts and capsule character

## Effects (6)

| Prefab | Kind | Element | Length |
|---|---|---|---|
| `FX_Fire_01` | Fire | Fire | loop |
| `FX_Fire_02` | Fire | Fire | loop |
| `FX_Fire_03` | Fire | Fire | loop |
| `FX_Fire_04` | Fire | Fire | loop |
| `FX_Fire_05` | Fire | Fire | loop |
| `FX_Fire_06` | Fire | Fire | loop |

## Updating from 1.x

Effect prefabs, materials and textures keep their file IDs, so importing 2.0 updates them in place. New in 2.0: `Core/`, `Demo/` and `Docs/`. The old demo scene of 1.x stays in your project if you had it; you can delete it.

## More

- **Stylized Combat VFX Mega Bundle**: every hun0fx Unity pack in one package, plus 23 combo skills and 118 element variants (311 effects).
- Store page: https://assetstore.unity.com/publishers/116849
- Contact: X @hun0fx · Instagram @hun0.fx · ArtStation artstation.com/hunyoung04

---

### 한국어 요약

- 데모: `Assets/Hun0FX/Demo/Scenes/Demo_Fire_Free` 열고 플레이 (←/→ 이동, Space 다시 재생, 슬라이더로 색·밝기·크기·속도)
- 프리팹: `Assets/Hun0FX/FX/FireFX_vol1` · 코드: `HunFX.Spawn(prefab, 위치, 회전)`
- Built-in·URP 모두 지원. HDR + Bloom 기준으로 제작. Built-in에서는 데모 씬의 URP 카메라/볼륨 컴포넌트가 missing으로 보이지만 문제없습니다.
- 1.x에서 업데이트하면 기존 파일이 그대로 덮어써지고 Core/Demo/Docs 폴더가 추가됩니다.
