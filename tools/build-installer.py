"""Stages both halves of the mod, zips them, and reports what went in.

The zip is laid out relative to the SPT folder, so the installer can extract it
straight over the target and both halves land where they belong.
"""
import os, shutil, subprocess, zipfile

REPO = r'H:\SPTMods\Blackjack'
DOTNET = r'C:\Users\Hoel\.dotnet\dotnet.exe'
SPT = r'H:\SPT4.1.X'

SERVER_BIN = os.path.join(REPO, r'src\Blackjack.Server\bin\Release\Blackjack.Server')
CLIENT_BIN = os.path.join(REPO, r'src\Blackjack.Client\bin\Release')
ASSETS = os.path.join(REPO, r'src\Blackjack.Client\assets')

PAYLOAD = os.path.join(REPO, r'tools\Blackjack.Installer\payload.zip')

SERVER_DIR = 'SPT_Runtime/user/mods/Blackjack'
PLUGIN_DIR = 'BepInEx/plugins/Blackjack'


def run(cmd):
    r = subprocess.run(cmd, cwd=REPO, capture_output=True, text=True)
    ok = 'Build succeeded' in r.stdout or r.returncode == 0
    if not ok:
        print(r.stdout[-3000:])
        raise SystemExit('build failed: ' + ' '.join(cmd[:4]))


print('building both halves...')
run([DOTNET, 'build', r'src\Blackjack.Server\Blackjack.Server.csproj', '-c', 'Release'])
run([DOTNET, 'build', r'src\Blackjack.Client\Blackjack.Client.csproj', '-c', 'Release', '-p:SPTPath=' + SPT])

entries = []


def add(z, disk, inside):
    if not os.path.exists(disk):
        raise SystemExit('missing: ' + disk)
    z.write(disk, inside)
    entries.append((inside, os.path.getsize(disk)))


if os.path.exists(PAYLOAD):
    os.remove(PAYLOAD)

with zipfile.ZipFile(PAYLOAD, 'w', zipfile.ZIP_DEFLATED) as z:
    for name in ('Blackjack.Server.dll', 'Blackjack.Server.pdb',
                 'Blackjack.Game.dll', 'Blackjack.Game.pdb', 'config.json'):
        add(z, os.path.join(SERVER_BIN, name), SERVER_DIR + '/' + name)

    add(z, os.path.join(CLIENT_BIN, 'Blackjack.Client.dll'), PLUGIN_DIR + '/Blackjack.Client.dll')
    add(z, os.path.join(ASSETS, 'table.png'), PLUGIN_DIR + '/table.png')

    cards = os.path.join(ASSETS, 'cards')
    for card in sorted(os.listdir(cards)):
        if card.endswith('.png'):
            add(z, os.path.join(cards, card), PLUGIN_DIR + '/cards/' + card)

print()
for inside, size in entries[:8]:
    print('   %-52s %8d' % (inside, size))
print('   ... and %d card images' % (len(entries) - 8))
print()
print('payload: %d files, %.1f MB' % (len(entries), os.path.getsize(PAYLOAD) / 1024 / 1024))
