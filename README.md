# kompas-bambu

Small Windows helper that takes the active KOMPAS-3D part/assembly, exports it for printing, and opens it in Bambu Studio.

Defaults:

- export format: STEP AP203
- exported objects: solid bodies only
- output folder: `print` next to the KOMPAS file
- output file: same base name as the KOMPAS document
- action: open the exported file in Bambu Studio

Example:

```powershell
.\dist\kompas-bambu.exe
```

Other commands:

```powershell
.\dist\kompas-bambu.exe step
.\dist\kompas-bambu.exe stl
.\dist\kompas-bambu.exe step export
.\dist\kompas-bambu.exe step --out-dir print
.\dist\kompas-bambu.exe step --bambu "C:\Program Files\Bambu Studio\bambu-studio.exe"
```

The active KOMPAS document must be saved at least once, because the tool writes to a subfolder next to that file.

## KOMPAS integration

Build outputs are in `dist`:

- `kompas-bambu.exe` - exporter/launcher
- `plugin\KompasBambuPlugin.dll` - KOMPAS COM library
- `kompas-bambu-hotkey.exe` - Ctrl+Shift+S listener

Install:

```bat
install-kompas-plugin.bat
```

The installer first tries normal x64 `RegAsm /codebase`. If it has no administrator rights, it falls back to per-user COM registration under `HKCU\Software\Classes`.

After install:

1. Restart KOMPAS-3D.
2. Open Applications/Libraries and load `Bambu Studio`.
3. Use `Bambu STEP` from the library menu, or press `Ctrl+Shift+S` while a KOMPAS window is active.

Uninstall:

```bat
uninstall-kompas-plugin.bat
```
