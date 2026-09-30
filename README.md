# rdepa29/ricer

My custom Windows ricing tool. Packages are installed with [scoop](https://github.com/ScoopInstaller/scoop); configs come from [rdepa29/config](https://github.com/rdepa29/config).

ricer is a self-contained .NET console app (`Ricer/`). The PowerShell version is
gone - same commands, same selection syntax, but argv now reaches the app
verbatim, so carets and quotes stop being mangled by shims.

## Build

```powershell
dotnet publish .\Ricer\Ricer.csproj -c Release -r win-x64 -o $env:LOCALAPPDATA\ricer\publish
```

The published single-file `ricer.exe` self-installs into `%USERPROFILE%\bin`
(and onto the user PATH) the next time `ricer install` / `ricer update` runs.

## Usage

```
ricer install               :: full bootstrap: ALL apps + env (msys2/fish, Windows Terminal, shims), configs from the base repo
ricer install <selection>   :: install just the selected apps (no env/config bootstrap)
ricer update                :: update ALL scoop apps + re-sync configs/shims from the base repo
ricer update <selection>    :: scoop update the selected apps
ricer uninstall             :: uninstall ALL managed apps (specials `fish`/`ricer` need explicit names)
ricer uninstall <selection> :: scoop uninstall the selection
ricer config [repo]         :: re-clone + re-sync configs from the base repo (default) or [repo]
ricer wm <sub>              :: window manager: start | stop | restart | reload | autostart | status | check
ricer list                  :: installed apps + synced configs
ricer status                :: health checks
ricer selftest              :: run the selection test suite (no side effects)
ricer help                  :: this info
```

**Default is `[all]`** — no selection means every managed app.

## Window manager

`ricer install` / `ricer update` point `KOMOREBI_CONFIG_HOME` at `%USERPROFILE%\.config\komorebi`,
so komorebi reads `komorebi.json`, `komorebi.bar.json` and `applications.json` from the synced
config tree instead of `%USERPROFILE%`.

`ricer wm` drives komorebi plus the `komorebi.ahk` hotkey daemon — **not whkd**, which
komorebi.ahk replaces.

```
ricer wm start           :: komorebic start, then launch ~/.config/komorebi/komorebi.ahk
ricer wm stop            :: stop komorebi and the komorebi.ahk process
ricer wm restart         :: wm stop, then wm start
ricer wm reload          :: komorebic reload-configuration + reload komorebi.ahk
ricer wm autostart on    :: login entry for komorebi (komorebi.lnk) + komorebi.ahk (HKCU Run)
ricer wm autostart off   :: remove both login entries
ricer wm autostart       :: print the current login entries
ricer wm status          :: processes, config paths, KOMOREBI_CONFIG_HOME
ricer wm check           :: komorebic check
```

`wm reload` and `wm start` are safe to re-run: the script is `#SingleInstance Force`, so
launching it again replaces the running instance.

## Selection

Indexes into the numbered MANAGED APPS list shown by `ricer help`.

| syntax  | meaning               |
| ------- | --------------------- |
| `...`   | all packages          |
| `1,3,5` | those                 |
| `1-4`   | range                 |
| `2-`    | from 2 onwards        |
| `-3`    | up to 3               |
| `^2`    | exclude 2             |

App names also work (e.g. `btop`), and `^name` excludes an app. In cmd.exe escape the caret as `^^2`.

## Config repo

`ricer config [repo]` / `ricer install [repo]` accept an optional repo:

- default base repo: `https://github.com/rdepa29/config` (override with `$env:RICER_REPO`)
- `owner/repo` (GitHub shorthand), `https://...` / `git@host:owner/repo` URLs, or a local folder path
- a custom repo must use the **same layout as the base repo**: `accent-theme cava fish komorebi micro wezterm whkd`
- ricer **errors** if the repo is missing a required directory
