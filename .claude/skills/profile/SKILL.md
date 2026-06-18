---
name: profile
description: Build DxApp in Release mode and run the asynkron-profiler CPU profiler against it
---

Profile DxApp with the asynkron CLI profiler.

## Instructions

1. **Build in Release mode** (profiling debug builds gives misleading results):
   ```powershell
   dotnet build DxApp -c Release
   ```
   Wait for a clean build before proceeding.

2. **Run the profiler**:
   ```powershell
   asynkron-profiler --cpu -- dotnet .\DxApp\bin\Release\net10.0\DxApp.dll $ARGUMENTS
   ```
   - `--cpu` enables CPU sampling
   - Pass any extra arguments the user provided after `--` to the DxApp binary
   - If the user provided no extra arguments, run DxApp with its default GUI mode

3. **Report results**: When profiling completes, report the output path (HTML report or terminal summary) and highlight the top hot-spots by self-time.

## Notes

- The binary path `.\DxApp\bin\Release\net10.0\DxApp.dll` is relative to the repo root — run from there
- For headless/batch profiling add the DxApp CLI flags after the binary path, e.g.:
  ```powershell
  asynkron-profiler --cpu -- dotnet .\DxApp\bin\Release\net10.0\DxApp.dll --export-svg StarMap --format png --output out.png
  ```
- If `asynkron-profiler` is not on PATH, ask the user to install it or provide the full path
