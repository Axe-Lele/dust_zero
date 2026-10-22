---
paths:
  - "DustZeroUnity/**"
  - "GameData/**"
  - "tools/**"
---
# Project-specific port rules

Read CLAUDE.md and docs/DUSTZERO-PORT-BRIEF.md. Unity code belongs beneath
DustZeroUnity/Assets, not the template's src/ or root Assets/.
Preserve the existing port. uGUI + TMP, JSON tables, ordinary C# simulations.
GameData/tables is authoritative; regenerate GameData/data with GameData/tools/build.py.
Keep original_index.html and original_README.txt as preserved references.
Do not apply unrelated hospital/backend/web framework conventions inherited from outside this project.
