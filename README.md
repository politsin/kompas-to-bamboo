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
