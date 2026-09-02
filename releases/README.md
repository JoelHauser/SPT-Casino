# Releases

Packaged builds, laid out so the zip extracts straight into an SPT install.

| Version | Built for | Form | Notes |
| --- | --- | --- | --- |
| 1.1.0 | SPT 4.1.3 | `Blackjack_V1.1.0.zip` | 5 MB. Extract into your SPT folder. Adds the task-bar tab, which had not yet been seen running when it was packed. |
| 1.0.2 | SPT 4.1.3 | `Blackjack_V1.0.2.zip` | 5 MB. Kept as the last build with nothing unproven in it. |
| 1.0.1 | SPT 4.1.3 | removed | Superseded by 1.0.2, which fixed ALL IN. |
| 1.0 | SPT 4.1.3 | removed | Superseded by 1.0.1, which changed only how the controls look. |
| 0.2.0 | SPT 4.1.3 | removed | Server only, no client plugin. Superseded, and removed so there is no wrong one to pick. |
| 0.1.0 | SPT 4.1.3 | removed | Wrong layout and three money-path bugs. |

## The installer

No installer ships with 1.0.2. The mod page describes the zip only, and a 40 MB
executable nobody is pointed at is a stale copy waiting to be found -- so the
1.0.1 one was removed rather than left beside a newer zip. It remains in git
history, and `tools/Blackjack.Installer/` still builds.

Built, it carries the mod inside it and writes it into an SPT folder: run it, and
either point it at the folder or drop it in there first and let it find itself.

It looks for `SPT_Runtime\SPT.Server.exe` before writing anything, and asks
before proceeding if it cannot find one. Extracting into the wrong folder is the
failure that looks exactly like the mod not working, so it is worth one question.

It is around 40 MB, which is almost entirely a .NET runtime: SPT ships its own
rather than installing one system-wide, so assuming a shared runtime is the
assumption that fails on somebody else's machine. The mod itself is 5 MB of that,
mostly the table photograph and 52 card faces. That ratio is most of why it is not
worth shipping.

Build it with:

```
python tools/build-installer.py
dotnet publish tools/Blackjack.Installer/Blackjack.Installer.csproj -c Release -o dist-installer
```

The first step builds both halves and stages `payload.zip`; the second embeds it.
That zip is generated and not committed, so the publish step alone will not work
from a fresh clone.

## The layout matters

4.1.x keeps the server under `SPT_Runtime\`, and `user\mods\` sits **inside** it,
beside `SPT.Server.exe`. A zip laid out as a bare `user/mods/` extracts one level
too high, and the server never scans it -- the mod is simply absent, with nothing
in the log to say so.

This was checked against a real install: all 40 mods there live under
`SPT_Runtime\user\mods\`, and the two folders sitting in that install's root
`user\mods\` have never loaded.

The earlier `Blackjack-0.1.0.zip` had the bare layout and was replaced rather than
kept, so there is no wrong one left to pick by mistake. It remains in git history.

Note this differs from 4.0.x, where the folder is `SPT\` rather than `SPT_Runtime\`.

## What ships

`Blackjack.Server.dll`, `Blackjack.Game.dll`, `config.json`, and both `.pdb`s.

SPT's own assemblies are not bundled -- the server provides them.

Symbols ship deliberately. Nothing has run against a real server, and a stack trace
without line numbers is the difference between a report that can be fixed and a
shrug.

## Rebuilding

```
dotnet build src/Blackjack.Server/Blackjack.Server.csproj -c Release
```

Then stage the five files under `SPT_Runtime/user/mods/Blackjack/` and zip them.

**Keep forward slashes in the entry names.** PowerShell's `Compress-Archive` writes
backslash entries, which extract as one literal filename on Linux. Pack with
`System.IO.Compression` or Python's `zipfile` instead.

The version lives in **seven** places and they must all agree: both csprojs'
`<Version>`, `BlackjackClientPlugin.PluginVersion`, `ModMetadata.Version`, the
installer's `<AssemblyName>` and `<Version>`, its `Program.Version` banner, and
`VERSION` in `tools/build-zip.py`. `SptVersion` in `ModMetadata` is a hard load
gate -- outside its range the server loads nothing and logs nothing.

**Bump the version before building, not after.** `build-zip.py` copies a payload
staged by `build-installer.py`; bumping in between packages the previous build's
binaries under the new name, and the zip then disagrees with itself. Read the
version back out of the packaged DLLs rather than trusting the filename.
