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

Install:

```bat
install-kompas-plugin.bat
```

Run the installer as Administrator. KOMPAS-3D discovers COM libraries through machine-level registration with the `Kompas_Library` marker, so per-user registration is intentionally not used here.
For KOMPAS-3D v24 the installer also writes `C:\ProgramData\ASCON\KOMPAS-3D\24\KompasBambu.kit.config`, because the Applications menu is populated from kit config files.

After install:

1. Restart KOMPAS-3D.
2. Open Applications/Libraries and load `Bambu Studio`.
3. Use `Bambu STEP` from the library menu or press `Ctrl+Shift+S`.

No Windows-global hotkey is installed. `Ctrl+Shift+S` is handled through the KOMPAS application keyboard event while the `Bambu Studio` library is loaded.

Uninstall:

```bat
uninstall-kompas-plugin.bat
```
