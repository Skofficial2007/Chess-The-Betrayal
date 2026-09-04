# Render pipeline assets

The game ships to Windows and Android from one scene, and the two platforms need different
rendering. This describes how a setting reaches the screen, so you can change one platform without
touching the other.

Everything lives in `Assets/Settings&Actions/Settings/`.

## The assets

| Asset | Applies to | Holds |
|---|---|---|
| `PC_PipelineAsset.asset` | Windows | shadows, HDR, render scale |
| `PC_Renderer.asset` | Windows | rendering mode, renderer features |
| `Mobile_PipelineAsset.asset` | Android | the same fields, tuned down |
| `Mobile_Renderer.asset` | Android | as above |
| `DefaultVolumeProfile.asset` | both | tonemapping, vignette, bloom |
| `Mobile_VolumeProfile.asset` | Android | one override — bloom off |
| `UniversalRenderPipelineGlobalSettings.asset` | both | the profile every platform starts from |

Each platform has its own renderer. They began as identical copies, so a difference between them is
deliberate rather than inherited.

## How Unity picks one

`ProjectSettings/QualitySettings.asset` defines two levels, `Mobile` and `PC`. Each names a pipeline
asset, and each excludes the platforms it is not for — `Mobile` excludes Standalone, `PC` excludes
Android and iPhone. `m_PerPlatformDefaultQuality` then maps `Android: 0` to `Mobile` and
`Standalone: 1` to `PC`.

`ProjectSettings/GraphicsSettings.asset` names a default pipeline asset, which Unity uses only when a
quality level names none. Both levels name one, so it never applies here.

To confirm which asset a build used, open the Quality settings for that platform rather than reading
this file — the mapping is data, and data drifts from prose.

## How a volume setting reaches the screen

Three layers, applied in order, each overriding the last:

1. The profile in `UniversalRenderPipelineGlobalSettings.asset` — currently `DefaultVolumeProfile`.
2. The pipeline asset's own profile. PC names `DefaultVolumeProfile`; Android names
   `Mobile_VolumeProfile`.
3. Volumes in the scene, blended by priority and weight.

`Mobile_VolumeProfile` contains a single component, Bloom, with its intensity set to zero.
Everything else — tonemapping, vignette, colour grading — still comes from layer 1, so the two
platforms cannot drift apart on anything except the setting that is deliberately different.

Bloom stops entirely at intensity zero rather than running and contributing nothing: URP treats a
bloom override as inactive when its intensity is not above zero.

`Game Scene.unity` contains a `Global Volume` object with no profile assigned. It is the place to
put a scene-specific override, and it applies to both platforms when you give it one. While its
profile is empty it changes nothing.

## Changing one platform only

- A **render setting** — shadows, render scale, HDR: edit that platform's pipeline asset.
- A **renderer setting** — rendering mode, intermediate texture mode, renderer features: edit that
  platform's renderer.
- A **post-processing effect** for Android only: add an override to `Mobile_VolumeProfile`.
- A post-processing effect **for both**: edit `DefaultVolumeProfile`.

The trap is the last two. `DefaultVolumeProfile` is named in the global settings *and* in
`PC_PipelineAsset`, so editing it changes Windows and, for anything `Mobile_VolumeProfile` does not
override, Android too.

## Why bloom is off on Android

On the test device bloom accounted for roughly half the render graph — more than drawing the board,
the pieces, the table and the shadows put together — while producing almost nothing visible, because
the mobile pipeline runs without HDR and bloom has little above its threshold left to gather.

## Shadow settings, and the trap in changing them

`Mobile_PipelineAsset` carries its own shadow distance, bias and soft-shadow quality, tuned apart
from PC's. Two things about that are worth knowing before you touch either.

**The Directional Light itself can override the pipeline asset.** `UniversalAdditionalLightData` on
a light carries its own `Soft Shadow Quality`, and when it is set to anything other than "Use
Pipeline Settings" it wins over whatever the active platform's pipeline asset says. The scene's light
had it pinned to High, which meant Android was silently rendering the same 16-tap shadows as PC
regardless of what `Mobile_PipelineAsset` asked for — tuning the pipeline asset was doing nothing
until this was found and turned back to "Use Pipeline Settings". Check the light before assuming a
pipeline-asset change reached the screen.

**Shadow Distance sets sharpness, not size on screen.** The shadow map is a fixed 2048×2048 texture
regardless of platform, and Unity stretches it to cover whatever distance you give it — a shorter
distance spreads the same texture over less ground, which is a free sharpness win as long as
whatever needs a shadow still falls inside it. Mobile's distance was cut from 40 to 25, which very
nearly clipped the far side of the board; `Cascade Border` was tightened alongside it (0.2 → 0.1) so
the fade-out band that distance introduces starts closer to the edge instead of eating into pieces
still on the board.

**Normal Bias controls how the shadow map treats a mesh's own facets, and the vendored piece meshes
are flat-shaded with two of them not fully closed** (the pawn and the knight each have a handful of
boundary edges where the geometry never welds shut — not something this repository can fix, since
the mesh ships from an Asset Store package). At the mobile pipeline's original bias, that showed up
as visible cracks running through a piece's own shadow — most noticeable on the sixteen pawns.
Dropping `Shadow Normal Bias` from 1 to 0.25 on Mobile closed it. PC never showed the cracks because
its pipeline runs a wider soft-shadow filter that blurs them away on its own; Mobile's cheaper filter
does not have that luxury, which is why the two platforms needed different values here even though
neither mesh problem is platform-specific.

## Anisotropic filtering

`ProjectSettings/QualitySettings.asset` sets `Anisotropic Textures` per quality level. PC has always
forced it on for every texture; Mobile was left at the default of leaving it to each texture's own
import setting, which for the board texture is off. At the camera's viewing angle that showed up as
a faint stairstep in the chequer pattern toward the far ranks — a texture sampling artifact, nothing
to do with shadows, and not something render scale or shadow tuning touches. Forcing it on for
Mobile as well fixed it; measured on device, the cost was not distinguishable from noise across
repeated captures, so there was no trade-off to weigh.

## What is verified and what is not

Measured on device (RMX3998, Android, release build): frame rate and GPU time for each shadow and
render-scale configuration, and bloom's share of the render graph. The device went from roughly 22
FPS with the GPU the clear bottleneck to consistently landing in the high 50s once render scale,
bloom and shadow cost were brought down together — no single change on its own explains that gap,
and the numbers for each step are only trustworthy relative to the same capture method used to
measure the last one, not as absolute figures for any other device.

Not verified: how any of this behaves on a phone other than the one this was tuned against. The
settings here are a starting point for a wider spread of hardware, not a promise about it.
