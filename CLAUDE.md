# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`edl-ng` is a .NET 9 cross-platform toolchain for Qualcomm Emergency Download (EDL) mode. It ships two front-ends — a `System.CommandLine` CLI and an Avalonia 12 GUI — on top of a shared protocol library that speaks Sahara (PBL) and Firehose (APSS) over USB to flash, read, and manage Qualcomm-based devices.

## Build / Format / Publish

- Build: `dotnet build` (solution file is `edl-ng.slnx`). Outputs:
  - CLI: `QCEDL.CLI/bin/<Config>/net9.0/<Platform>/edl-ng`
  - GUI: `QCEDL.GUI/bin/<Config>/net9.0/<rid>/qcedl-gui`
- Format check (matches CI): `dotnet format --verify-no-changes --verbosity diagnostic`. CI fails on any formatting drift — run `dotnet format` before committing.
- Publish self-contained CLI (matches CI): `dotnet publish QCEDL.CLI/QCEDL.CLI.csproj -c Release -r <rid> --self-contained true` where `<rid>` is `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64`.
- Run the GUI locally: `dotnet run --project QCEDL.GUI/QCEDL.GUI.csproj`.
- There is no test project; don't run `dotnet test`.
- `Directory.Build.props` applies to all projects: `TreatWarningsAsErrors=true`, nullable enabled, `AllowUnsafeBlocks=true`, `AnalysisMode=Recommended`, doc generation on. Warnings break the build — fix them, don't suppress. The GUI project locally demotes a small set of IDE style rules (block-vs-expression body etc.) to warnings via `NoWarn` because Avalonia code-behind idiomatically mixes both — see `QCEDL.GUI/QCEDL.GUI.csproj`.

## Solution layout

Four C# projects (referenced in `edl-ng.slnx`) plus a Nix flake:

- `QCEDL.CLI/` — CLI entry point **and** the shared orchestration layer. `Program.cs` wires `System.CommandLine` global options and registers one command per file in `Commands/` (`printgpt`, `read-part`/`write-part`, `read-sector`/`write-sector`/`read-lun`, `erase-part`/`erase-sector`, `rawprogram`, `dump-rawprogram`, `provision`, `upload-loader`, `reset`). Note that `read-lun` lives in `ReadSectorCommand.cs` (`CreateReadLunCommand`), not its own file. The `Core/` folder hosts the **public** protocol orchestration surface (`EdlManager`, `EdlOptions`, `DeviceMode`, `StorageGeometry`, storage backends) — the GUI links against this assembly as a library. Targets `AnyCPU`/`x64`/`ARM64`.
- `QCEDL.NET/` — protocol + transport library. No CLI/UI dependencies. `AnyCPU` only.
- `QCEDL.GUI/` — Avalonia 12 + ReactiveUI desktop app. References `QCEDL.CLI` and drives `EdlManager` directly — it does **not** shell out to the `edl-ng` executable. `AnyCPU` only, self-contained publish to match CLI's `NETSDK1151` constraint.
- `QCEDL.Analyzer/` — Roslyn source generator (`EnumExtensionsGenerator.cs`) consumed at build time.

## Architecture (the parts that need reading multiple files to grasp)

**Connection lifecycle is owned by `QCEDL.CLI/Core/EdlManager.cs`.** Every CLI command and every GUI operation goes through it:

1. `EdlManager` discovers the device (Windows COM port via `ComPortGuid`, or cross-platform via `LibUsbDotNet` matched by VID/PID — defaults `0x05C6` / `0x9008|0x900E`).
2. Opens `QualcommSerial` (`QCEDL.NET/Transport/`).
3. Runs the Sahara handshake via `QualcommSahara` (`QCEDL.NET/Layers/PBL/Sahara/`). The loader file passed via `--loader` can be a raw `.elf`/`.melf` programmer **or** a `qsahara_device_programmer.xml` — `SaharaConfigParser.cs` selects the right ELF for the detected chip (HWID / PK hash in `ChipInfo/`).
4. Switches to Firehose (`QualcommFirehose` in `QCEDL.NET/Layers/APSS/Firehose/`), configures memory type (UFS/eMMC/SPINOR/NVMe/…), reads storage geometry, then hands an `IStorageBackend` to the caller.

**`EdlOptions` is the POCO that drives `EdlManager`.** Originally `EdlManager` consumed a `GlobalOptionsBinder` (coupled to `System.CommandLine`); we extracted `EdlOptions` so the GUI can construct a manager directly. The CLI binder is now `BinderBase<EdlOptions>` and projects `System.CommandLine` parse results onto the same POCO.

**Direct-mode bypass:** `--hostdev-as-target <path>` (SPI NOR / file) and `--radxa-wos-platform` skip the USB/Sahara/Firehose stack entirely and use `HostDeviceManager` / `RadxaWoSDeviceManager` as the `IStorageBackend`. Callers should never assume USB is live — check `EdlManager.IsDirectMode` / `IsFirehoseMode` before calling protocol-specific helpers.

**Adding a new CLI command:** create `QCEDL.CLI/Commands/FooCommand.cs` exposing `public static Command Create(GlobalOptionsBinder)`, register it in `Program.cs`. The command's `ExecuteAsync` takes `EdlOptions` (not the binder) — construct `new EdlManager(options)` to obtain a session.

**Adding a GUI screen:** create a `ViewModel` under `QCEDL.GUI/ViewModels/`, a matching `UserControl` under `Views/`, add a nav entry in `ShellViewModel`, and wire a `DataTemplate` in `MainWindow.axaml`. Share sessions via the single `EdlService` in `QCEDL.GUI/Services/` — it guards calls with a semaphore so the UI can't issue overlapping operations.

**Firehose commands vs. XML layer:** `QualcommFirehoseCommands.cs` is the strongly-typed command surface; `Layers/APSS/Firehose/Xml/` contains the on-wire XML element types (serialized via `System.Xml`) and `JSON/StorageInfo/` parses the storage-info blob. `StorageType` enum lives under `Xml/Elements` — this is what `--memory` binds to.

**Partition table:** `QCEDL.NET/PartitionTable/GPT*.cs` is a standalone GPT parser used by `printgpt`, `read-part`, `write-part`, `dump-rawprogram` (CLI) and the Partitions view (GUI). It works off an `IStorageBackend`, so it's reused across Firehose and direct-mode paths.

**Logging is a single sink shared by CLI and GUI.** `QCEDL.CLI/Helpers/Logging.cs` owns both a console writer (toggleable via `Logging.ConsoleSinkEnabled`) and a `LogEmitted` event. The GUI subscribes an `ObservableLogSink` (`QCEDL.GUI/Services/`) to stream entries into the Logs view on the UI thread, and disables the console sink on startup. `QCEDL.NET/Logging/LibraryLogger` forwards protocol-layer logs into the same pipeline via `LibraryLogger.LogAction`.

## GUI design system

The GUI's look is a Claude-inspired warm theme derived directly from `DESIGN.md`. It ships no third-party theme pack.

- `QCEDL.GUI/Themes/Tokens.axaml` — raw palette, semantic brushes, typography families, radii (4→32px scale), spacing scale, and `BoxShadows` for the whisper shadow layer.
- `QCEDL.GUI/Themes/Controls.axaml` — **ControlThemes written from scratch** (no `FluentTheme`) for `Button`, `ToggleButton`, `RepeatButton`, `TextBox`, `CheckBox`, `ComboBox` + `ComboBoxItem`, `ListBox` + `ListBoxItem`, `ItemsControl`, `ContentControl`, `Thumb`, `ScrollBar`, `ScrollViewer`, `ProgressBar`, `Separator`, `ToolTip`. Templates follow Avalonia's `PART_*` contract.
- `QCEDL.GUI/Themes/Styles.axaml` — class-based variants layered on the ControlThemes: typography classes (`display`, `section`, `h1`–`h3`, `feature`, `bodySerif`, `bodyLarge`, `body`, `caption`, `label`, `overline`, `mono`, colour modifiers), button variants (`primary` = Terracotta CTA, `dark`, `ghost`, `danger`, `small`, `link`), card variants (`elevated` with whisper shadow, `hero`, `flat`, `dark`), plus `chip` and `nav`.
- `App.axaml` loads them in order tokens → controls → styles; resource resolution depends on that order.

When adding a new control, write its `ControlTheme` in `Controls.axaml` (not views) and only use `DynamicResource` brushes/radii — never inline hex literals. If you need a new visual variant of an existing control, add a class-selector rule to `Styles.axaml`.

## GUI localization, fonts, settings

The GUI is localized and runtime-switchable between English, Simplified Chinese, Traditional Chinese, and Japanese.

- `QCEDL.GUI/Resource/Strings.resx` (+ `.zh-Hans.resx`, `.zh-Hant.resx`, `.ja.resx`) are the string tables. They're auto-included as `EmbeddedResource` by the SDK — don't list them in the csproj. The base resource lives at `QCEDL.GUI.Resource.Strings`.
- `QCEDL.GUI/Services/Localizer.cs` is a singleton (`Localizer.Instance`) that wraps the `ResourceManager` and exposes an indexer `this[key]`. Setting `Culture` raises `PropertyChanged("Item[]")` so every live binding re-resolves, and fires a `CultureChanged` event for imperative consumers (e.g. the font swapper).
- `QCEDL.GUI/Markup/LocalizeExtension.cs` is the XAML side: `Text="{l:Localize Nav_Overview}"` compiles to a `OneWay` `Binding` against `Localizer.Instance` with path `[Nav_Overview]`. Prefer this over ad-hoc `{Binding [Key], Source=…}`.
- `QCEDL.GUI/Services/FontTheme.cs` picks the primary `FontSerif` / `FontSans` / `FontMono` family chain based on the active culture and assigns them to `Application.Current.Resources`. This is needed because Avalonia's per-glyph fallback inside a single `FontFamily` does not pick a CJK *serif* face — the primary family has to be culture-specific. `App.axaml.cs` calls `FontTheme.Apply` on startup and re-runs it on every `CultureChanged`.
- `QCEDL.GUI/Services/GuiSettings.cs` persists preferences (currently `culture`) as JSON under `%APPDATA%/edl-ng/gui-settings.json` (or the platform equivalent). `ResolveStartupCulture` falls back from persisted → OS UI culture (exact match, then two-letter language) → `en`. Load happens in `App.OnFrameworkInitializationCompleted` **before** any view is built so initial bindings see the right language.
- `SettingsViewModel` + `SettingsView` expose the language picker; changing it updates `Localizer.Instance.Culture` and saves via `GuiSettings.Save`.

When adding a new user-facing string: add it to `Strings.resx` with an English value, then to each translated `.resx`, and reference it in XAML via `{l:Localize KeyName}` or in code via `Localizer.Instance["KeyName"]`. Don't hard-code user-visible literals in views.

## GUI implementation tracker

`gui-todos.md` at the repo root is a living tracker — CLI feature inventory, GUI screen mapping, per-capability status, and phased rollout. Update the Status column when finishing work on a capability; don't let it drift behind the code.

## Conventions worth knowing

- Target is `net9.0` everywhere; `ImplicitUsings` is on — don't add redundant `using System;` etc.
- Solution uses the newer `.slnx` format (edit with Rider/VS 2022 17.10+; `dotnet` handles it fine).
- Namespaces follow `Qualcomm.EmergencyDownload.*` in `QCEDL.NET`, `QCEDL.CLI.*` in the CLI, and `QCEDL.GUI.*` in the GUI — match existing neighbors.
- `IDE0058` / `IDE0130` / `CS1591` are downgraded to warnings intentionally; everything else is an error.
- GUI project additionally tolerates `IDE0021`, `IDE0022`, `IDE0044`, `IDE0090`, `IDE0290` locally.
- Avalonia views must use compiled bindings (`x:DataType` on every `DataTemplate` / root). `AvaloniaUseCompiledBindingsByDefault=true` is on in `QCEDL.GUI.csproj`.

## Nix

`flake.nix` exposes `packages.<system>.edl-ng` (derivation in `pkgs/edl-ng/`) and a devShell. Use `nix build` for a reproducible CLI build; the CI workflow `nix-build.yml` gates this on Linux only (macOS runners were removed — see commit `acb89a0`). The GUI is not yet packaged through Nix.
