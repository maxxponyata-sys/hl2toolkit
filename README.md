# HL2 Toolkit

**English** · [Русский](README.ru.md) · [Deutsch](README.de.md)

A beginner-friendly map editor for **Half-Life 2**, inspired by the Far Cry map editors. Drag models into a 3D world, sculpt terrain with brushes, press **F5**, and play your map.

Written in **C#** (WinForms + raw OpenGL). It has no external dependencies and ships as a single Windows x64 executable.

---

## Features

- **Drag and drop.** The library holds about 1,400 Half-Life 2 models with real textured thumbnails, plus NPCs, weapons, items, vehicles, lights and wall blocks. Drag anything into the world, or double-click it to place it in front of the camera.
- **Custom models.** Drop an `.obj` file onto the window. The import wizard converts units, writes the texture (VTF/VMT) and model sources (SMD/QC), and compiles the model with `studiomdl`. It then shows up under **My models**.
- **Far Cry–style controls.** Hold the right mouse button and use WASD to fly. Drag objects across the ground, or use the move, rotate and scale gizmos. Grid snapping and full undo/redo are included.
- **Terrain brushes.** Raise, lower, smooth, flatten, and paint a second texture layer. The terrain is exported as Source displacements.
- **World settings.** Time of day, sun direction, skybox, fog and water.
- **One-click build.** VMF → VBSP → VVIS → VRAD → `hl2/maps`, then the game launches. The map is sealed with a skybox automatically, so leaks are not possible.
- **RTX mode.** If Half-Life 2 RTX (or RTX Remix in the HL2 folder) is installed, the map is copied there and launched with path tracing. RTX Remix compiles its own shaders on first launch.
- **VMF export.** Keep editing the map in Hammer.
- **Smart prop types.** Props that Half-Life 2 only allows as physics objects (barrels, crates, …) are detected automatically.

## Requirements

- Windows 10/11 x64
- **Half-Life 2** on Steam. The compile tools (`vbsp`, `vvis`, `vrad`, `studiomdl`) come with the game.
- Optional: Half-Life 2 RTX and an RTX-capable GPU for RTX mode

## Download

Get `HL2Toolkit.exe` from [**Releases**](../../releases) and run it. No installation and no .NET runtime are needed. The game folder is detected through Steam; if that fails, set it under **Settings → Half-Life 2 folder**.

## Controls

| Action | Input |
|---|---|
| Look around / fly | Hold **RMB** + mouse, **W A S D**, **Q / E** down/up, **Shift** faster |
| Select / drag along ground | **LMB** |
| Move / Rotate / Scale gizmo | **W / E / R** |
| Focus selection / map center | **F** / **Home** |
| Drop to ground | **End** |
| Duplicate / Delete | **Ctrl+D** / **Del** |
| Undo / Redo | **Ctrl+Z** / **Ctrl+Y** |
| Build and play / Build only | **F5** / **F7** |
| Terrain brush, inverted | **LMB**, hold **Ctrl** to invert |

## Building from source

Requires the .NET SDK (the project targets `net11.0-windows`).

```bat
cd src\HL2Toolkit
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ..\..\dist
```

To test the core without the UI, run `HL2Toolkit.exe --selftest`. It reads models, then builds and compiles a test map, and writes the results to `selftest.log`.

## Project layout

```
src/HL2Toolkit/
  Source/   VPK, MDL/VVD/VTX, VTF and VMT readers and writers
  Build/    VMF writer, compile pipeline, OBJ import, studiomdl
  Render/   OpenGL viewport, camera, gizmos, terrain brushes, asset cache
  UI/       Main window, library, properties, import wizard, dark theme
```

## Disclaimer

This is a fan-made tool and is not affiliated with Valve or NVIDIA. You need your own copy of Half-Life 2. No game files are included.

## License

[MIT](LICENSE)
