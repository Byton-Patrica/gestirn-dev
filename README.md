# Gestirn dev

Gestirn dev is a modernisation project built from the Bloodshed/Orwell
Dev-C++ source code. The goal is to keep Dev-C++ lightweight while adding the
workflow features that current C/C++ developers expect.

## 0.1.0

The first release focuses on two visible improvements:

- **Code completion**: the existing C++ parser completion remains available
  automatically after `.`, `::` and `->`, and can now be opened explicitly
  with **Ctrl+Space**.
- **Embedded terminal**: open **Tools → Embedded terminal** or press
  **Ctrl+`**. Commands run asynchronously through `cmd.exe /C`, stream output
  back into the terminal window, and can invoke Git, build tools, scripts or a
  local AI agent.

The terminal is intentionally small in 0.1. It runs one command per process;
persistent interactive shell sessions, terminal profiles and agent-specific
integrations are planned for later releases.

## Download

The Windows download page is the [v0.1.0 GitHub Release](https://github.com/Byton-Patrica/gestirn-dev/releases/tag/v0.1.0).
The repository currently contains the source release; a compiled Windows
executable will be attached after the legacy Delphi project is built with a
compatible Delphi 6/7 toolchain.

## Branding

The GestirnEra DataRot icon set is kept under `Assets/Branding/`. The `.ico`
asset is also wired into the application and file-association icon paths so
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

## Direction after 0.1

The next milestones will add Clangd/LSP-based semantic completion, CMake and
Ninja project support, modern compiler profiles, Git integration, and a
persistent terminal session suitable for long-running AI agents.

