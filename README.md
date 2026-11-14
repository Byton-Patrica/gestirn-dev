# Gestirn dev

<p align="center"><img src="Assets/Branding/GestirnEra-datarot-256.png" width="128" alt="GestirnEra DataRot icon"></p>

Gestirn dev is a modern, lightweight C/C++ IDE for Windows, built from the
Bloodshed/Orwell Dev-C++ source line. It keeps what made Dev-C++ easy (one
window, the bundled MinGW compiler, F9/F11, `.dev` projects and templates) and
adds the editing and debugging comfort of a current tool. The whole program is a
425 KB executable on the .NET Framework that ships with Windows.

## 0.5

### Editor
- A purpose-built code editor: smooth scrolling, syntax colouring, line
  numbers, indent guides, current-line and bracket-match highlights, Chinese
  input (IME) and correct alignment of Chinese characters.
- Suggestions while you type: keywords, standard-library names, snippets
  (`main`, `for`, `cout`, `if`…), headers after `#include <`, and words from
  every open file. Enter or Tab accepts; nothing steals focus.
- Auto-closing brackets and quotes, smart Enter between braces, `}` outdents,
  `Ctrl+/` comment, `Ctrl+D` duplicate, `Alt+↑/↓` move lines, `Ctrl+Shift+K`
  delete line, word-wise undo/redo, find/replace bar with live match count.
- Errors and warnings are underlined in place; hover for the message plus a
  plain-language hint (for example a missing semicolon, or a Chinese `；` typed
  by accident).

### Build, run and debug (Dev-C++ keys)
| Key | Action |
| --- | --- |
| F9 | Compile |
| F10 | Run |
| F11 | Compile & run |
| F12 | Rebuild all |
| F5 | Debug / continue |
| F7 / F8 / Shift+F8 | Step over / into / out |
| F6 | Stop |
| Ctrl+F5 | Toggle breakpoint (or click left of a line number) |

- The g++ bundled with Dev-C++ (MinGW64) is found automatically, as are g++ on
  `PATH` and MSYS2. The C++ standard defaults to the newest one the compiler
  supports.
- Programs run in their own console through `GestirnRun.exe`, which shows the
  return value and running time and waits for a key, like Dev-C++'s pauser.
  Common crash codes (access violation, stack overflow, divide by zero) are
  explained.
- On Chinese Windows, Chinese text in UTF-8 sources prints correctly in the
  console (`-fexec-charset=GBK` is added when needed).
- Debugging uses gdb: breakpoints, the current line in the editor, local
  variables, watch expressions and the call stack.
- Dev-C++ `.dev` projects open, build (in parallel, only what changed) and save
  back in a format Dev-C++ still reads. New projects come from the Dev-C++
  templates. Single files build without a project, as before.
- `Ctrl+Shift+A` formats code with AStyle.

### Seamless by default
- Auto save after you pause typing and when the window loses focus.
- Open files, cursor positions, breakpoints and unsaved untitled buffers come
  back on the next start; a crash backs up your work first.
- Untitled code compiles straight away (it is saved to
  `Documents\Gestirn dev\scratch`); files changed by other programs reload.

### Workspace
- Explorer with a live file tree (new, rename, delete to Recycle Bin, add to
  project), find in files, an outline of functions and classes, and a
  persistent terminal.
- `Ctrl+P` quick open, `Ctrl+Shift+P` command palette (English names also work
  in the Chinese UI), `Ctrl+G` go to line, `Ctrl+Shift+O` go to symbol.
- Chinese and English interface (follows Windows; switch in Settings).
- Sharp at any display scaling, dark theme in the colours of the logo, rounded
  cards and pixel icons, with small animations: gliding tab and rail
  indicators, sliding toasts and panels, a pixel progress strip while building.

### Settings and portability
Settings live in `%APPDATA%\Gestirn dev`. Create a `data` folder next to
`Gestirn-dev.exe` to keep everything beside the program instead (portable
mode), or set `GESTIRN_HOME`.

## Download

The [v0.11 GitHub Release](https://github.com/Byton-Patrica/gestirn-dev/releases/tag/v0.11)
is the last published package. Build 0.5 from source as described below; it
needs a g++ toolchain such as the one bundled with Dev-C++ 5.11.

## Building from source

Run `build.cmd` (or `build.ps1`). It uses the Roslyn C# compiler from Visual
Studio Build Tools and writes a ready-to-run folder to `dist\Gestirn-dev`
containing `Gestirn-dev.exe`, `GestirnRun.exe`, AStyle and the templates.
`GestirnDev/GestirnDev.csproj` builds the same sources with the .NET SDK.

Source layout:

| Folder | Contents |
| --- | --- |
| `GestirnDev/Ui` | Theme, DPI scaling, animation engine, pixel icons, controls |
| `GestirnDev/Editor` | Code editor control, C/C++ tokenizer, completion |
| `GestirnDev/Core` | Settings, toolchain and builds, `.dev` projects, gdb debugger |
| `GestirnDev/Shell` | Main window, panels, palette, dialogs |
| `GestirnRun` | Console runner |
| `Source` | Original Delphi Dev-C++ IDE (needs Delphi 6/7) |

## Earlier versions

- **0.12** — compile and run from the editor, error navigation, tabs.
- **0.11** — code completion with Ctrl+Space and an embedded terminal.

## Branding

The GestirnEra DataRot icon, now with rounded corners, is used for the
application, its shortcuts and this README. The assets are in
`Assets/Branding/`.

## Source and license

Gestirn dev preserves the upstream GPL licensing and attribution of
Bloodshed/Orwell Dev-C++. AStyle and GDB keep their own licenses.
