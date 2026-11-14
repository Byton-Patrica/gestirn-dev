# Gestirn dev

<p align="center"><img src="Assets/Branding/GestirnEra-datarot-256.png" width="128" alt="GestirnEra DataRot icon watermark"></p>

Gestirn dev is a modernisation project built from the Bloodshed/Orwell
Dev-C++ source code. The goal is to keep Dev-C++ lightweight while adding the
workflow features that current C/C++ developers expect.

## 0.12

0.12 turns the Windows editor into a working C/C++ environment:

- **Compile and run**: **F9** compiles the current file with g++ and runs it in
  its own console window; **F11** compiles only; **Ctrl+F2** stops the build and
  any running program. Unsaved files are saved first.
- **Compiler detection**: the bundled MinGW64 of Dev-C++ is found
  automatically, along with g++ on `PATH` and MSYS2. Pick another compiler,
  the C++ standard and extra flags under **Build → Build Options**.
- **Errors you can click**: diagnostics appear in the output panel in colour;
  double-click a line such as `main.cpp:12:5: error:` to jump to that spot.
- **Editing**: several tabs with unsaved-change markers, line numbers, syntax
  colouring, Enter keeps indentation (and indents after `{`), **Ctrl+F** find
  with **F3** for next, and **Ctrl+Space** code hints.
- **Projects**: **File → Open Folder** shows the folder in the workspace tree;
  double-click a file to open it. The last folder is remembered.
- **Shell**: the output panel's command box runs any `cmd` command in the
  current folder.

Limits in this release: each build compiles a single source file (there are no
project files or Makefiles yet), and the program's console is separate from the
editor, so input and output go to that console window.

## 0.11

The first release focuses on two visible improvements:

- **Code completion**: the existing C++ parser completion remains available
  automatically after `.`, `::` and `->`, and can now be opened explicitly
  with **Ctrl+Space**.
- **Embedded terminal**: open **Tools → Embedded terminal** or press
  **Ctrl+`**. Commands run asynchronously through `cmd.exe /C`, stream output
  back into the terminal window, and can invoke Git, build tools, scripts or a
  local AI agent.

The terminal currently runs one command per process;
persistent interactive shell sessions, terminal profiles and agent-specific
integrations are planned for later releases.

## Download

The [v0.11 GitHub Release](https://github.com/Byton-Patrica/gestirn-dev/releases/tag/v0.11)
is the last published Windows package. The 0.12 Windows editor (`Gestirn-dev.exe`)
is built from `GestirnDevPreview/` as described below; it needs a g++ toolchain
such as the one bundled with Dev-C++ 5.11.
The original Delphi source remains the long-term IDE core and still requires a
compatible Delphi 6/7 toolchain for its full native build.

## Branding

The GestirnEra DataRot icon is used as the README watermark. The `.ico` asset
is kept under `Assets/Branding/` and is wired into the application and
file-association icon paths so
the same identity is used by the executable, shortcuts and source files.

## Source and license

Gestirn dev preserves the upstream GPL licensing and attribution. The source
tree is based on the Orwell Dev-C++ line and keeps the original Delphi/VCL
build system for this first milestone.

## Building from source

The legacy project is under `Source/devcpp.dpr`. The original source notes
describe building with Delphi 6 or Delphi 7 and the bundled VCL packages.
Open the project in a compatible Delphi environment, make sure the SynEdit and
ClassBrowsing source paths are available, and build the `devcpp` project.

The Windows editor can be built with the C# compiler (csc.exe, for example the
one in Visual Studio Build Tools) from the files under `GestirnDevPreview/`:

```bash
csc -target:winexe -win32icon:GestirnDevPreview/GestirnEra-datarot.ico -out:Gestirn-dev.exe -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll GestirnDevPreview/*.cs
```

## Direction after 0.12

The next milestones will add Clangd/LSP-based semantic completion, CMake and
Ninja project support, modern compiler profiles, Git integration, and a
persistent terminal session suitable for long-running AI agents.



