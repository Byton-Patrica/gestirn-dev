# Gestirn dev

<p align="center"><img src="Assets/Branding/GestirnEra-datarot-256.png" width="128" alt="GestirnEra DataRot icon watermark"></p>

Gestirn dev is a modernisation project built from the Bloodshed/Orwell
Dev-C++ source code. The goal is to keep Dev-C++ lightweight while adding the
workflow features that current C/C++ developers expect.

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

The Windows download page is the [v0.11 GitHub Release](https://github.com/Byton-Patrica/gestirn-dev/releases/tag/v0.11).
[Download the Windows preview package](https://github.com/Byton-Patrica/gestirn-dev/releases/download/v0.11/Gestirn-dev-0.11-preview.zip).
The release includes a standalone Windows preview executable. It provides the
Gestirn dev editor surface, Ctrl+Space code hints and the embedded terminal.
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

The standalone preview can be built with the Visual Studio C# compiler using
the files under `GestirnDevPreview/`.

## Direction after 0.11

The next milestones will add Clangd/LSP-based semantic completion, CMake and
Ninja project support, modern compiler profiles, Git integration, and a
persistent terminal session suitable for long-running AI agents.



