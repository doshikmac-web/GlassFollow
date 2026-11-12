# third_party

Put the upstream files here before building (they are not generated):

```
third_party/correct-liquid-glass/liquid-glass.hlsl   <- from upstream: shaders/liquid-glass.hlsl
third_party/correct-liquid-glass/LICENSE             <- from upstream: LICENSE (unchanged)
```

Upstream: https://github.com/Shram1404/correct-liquid-glass (MIT)

```powershell
git clone https://github.com/Shram1404/correct-liquid-glass $env:TEMP\correct-liquid-glass
Copy-Item $env:TEMP\correct-liquid-glass\shaders\liquid-glass.hlsl third_party\correct-liquid-glass\
Copy-Item $env:TEMP\correct-liquid-glass\LICENSE third_party\correct-liquid-glass\
```

## uwidgets (optional second glass style)

`third_party/uwidgets/` already contains `liquid-glass-uwidgets.hlsl` (adapted from uWidgets) and the uWidgets
`LICENSE` (CC BY-NC-SA 4.0, non-commercial). If you do not want that license in your build, delete the folder:
the project still builds and `--style uwidgets` falls back to the default style.
