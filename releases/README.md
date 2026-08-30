# Releases

Packaged builds, laid out so the zip extracts straight into an SPT install.

| Version | Built for | Form | Notes |
| --- | --- | --- | --- |
| 1.0 | SPT 4.1.3 | `Blackjack_V1.0.exe` | Installer. Both halves, the table and the card art. |
| 0.2.0 | SPT 4.1.3 | zip | Server only, no client plugin. Superseded. |
| 0.1.0 | SPT 4.1.3 | zip | Superseded. Wrong layout, and three money-path bugs -- do not install. |

## The installer

`Blackjack_V1.0.exe` carries the mod inside it and writes it into an SPT folder.
Run it, and either point it at the folder or drop it in there first and let it
find itself.

It looks for `SPT_Runtime\SPT.Server.exe` before writing anything, and asks
before proceeding if it cannot find one. Extracting into the wrong folder is the
failure that looks exactly like the mod not working, so it is worth one question.

It is around 40 MB, which is almost entirely a .NET runtime: SPT ships its own
rather than installing one system-wide, so assuming a shared runtime is the
assumption that fails on somebody else's machine. The mod itself is 5 MB of that,
mostly the table photograph and 52 card faces.

Rebuild it with:

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

The version lives in **two** places and they must agree:
`Blackjack.Server.csproj` `<Version>` and `ModMetadata.Version`. `SptVersion` in
that same record is a hard load gate -- outside its range the server loads nothing
and logs nothing.
