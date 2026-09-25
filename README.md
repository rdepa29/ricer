# rdepa29/ricer

My custom Windows ricing tool. Packages are installed with [Scoop](https://scoop.sh); configs come from [rdepa29/config](https://github.com/rdepa29/config).

## Usage

```
ricer install              full bootstrap: ALL apps + env (msys2/fish, Windows Terminal, shims), configs from the base repo
ricer install <selection>  install just the selected apps (no env/config bootstrap)
ricer update               update ALL scoop apps + re-sync configs/shims from the base repo
ricer update <selection>   scoop update the selected apps
ricer uninstall            uninstall ALL managed apps (specials `fish`/`ricer` need explicit names)
ricer uninstall <selection> scoop uninstall the selection
ricer config [repo]        re-clone + re-sync configs from the base repo (default) or [repo]
ricer list                 installed apps + synced configs
ricer status               health checks
ricer help                 this info
```

**Default is `[all]`** — no selection means every managed app.

## Selection (caelestia-style)

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