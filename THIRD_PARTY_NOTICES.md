# Third-party notices

## correct-liquid-glass (MIT)
https://github.com/Shram1404/correct-liquid-glass

GlassFollow is built on this project's Windows live-capture demo: the screen-capture pipeline
(Windows.Graphics.Capture + Direct3D 11), the window-region trick and the refraction pixel shader
(`liquid-glass.hlsl`) come from it. `src/GlassFollow/Program.cs` is a heavily modified derivative of
the demo's `Program.cs`.

The upstream license text and copyright notice must stay with the copied files:
`third_party/correct-liquid-glass/LICENSE` (copy it unchanged from the upstream repository).

## uWidgets (CC BY-NC-SA 4.0): the second glass style
https://github.com/creewick/uWidgets

`third_party/uwidgets/liquid-glass-uwidgets.hlsl` is adapted from the liquid-glass shaders in uWidgets
(`Services/LiquidGlass/GlassGpu.cs`). uWidgets is licensed under
[Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International](https://creativecommons.org/licenses/by-nc-sa/4.0/);
the license text is in `third_party/uwidgets/LICENSE`.

- Changes: the shaders were moved out of C# strings into one file, the local coordinate in `PSGlass` is taken
  relative to the shape inside GlassFollow's padded window, `PSBlur` reads the shared texture/sampler and uses
  constant buffer slot b1, the two full-screen vertex shaders were merged, and the cache pass was dropped.
- **This file is NonCommercial and ShareAlike.** It may be used and shared only for non-commercial purposes, and
  adapted versions of it must stay under the same license. The rest of GlassFollow is MIT. A copy of GlassFollow
  without this file (and without `--style uwidgets`) is MIT-only.

## ryohsuke1231/liquid-glass (MIT)
https://github.com/ryohsuke1231/liquid-glass

The edge lighting math (rim light, specular, sheen) in the uWidgets shader is itself ported from this project.

## Vortice.Windows (MIT)
https://github.com/amerkoleci/Vortice.Windows

Direct3D 11 / DXGI / D3DCompiler bindings, used through NuGet.

## Windhawk
https://windhawk.net

`windhawk/glass-prep.wh.cpp` is a mod written for the Windhawk modding platform. This project is not
affiliated with or endorsed by Windhawk or its author.
