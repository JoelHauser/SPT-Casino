"""Builds the release zip from the installer's payload.

The same files, laid out the same way, so the zip and the installer cannot drift
apart: extracting this into an SPT folder does exactly what the installer does.
Run tools/build-installer.py first to stage the payload.
"""
import os, shutil, zipfile

REPO = r'H:\SPTMods\Blackjack'
PAYLOAD = os.path.join(REPO, r'tools\Blackjack.Installer\payload.zip')
VERSION = '1.0'
OUT = os.path.join(REPO, 'releases', 'Blackjack_V%s.zip' % VERSION)

README = """Blackjack %s -- for SPT 4.1.x
================================

INSTALLING
    Stop the server, then extract this archive into your SPT folder: the one
    that holds SPT_Runtime. The files belong at:

        SPT_Runtime\\user\\mods\\Blackjack\\      the server mod
        BepInEx\\plugins\\Blackjack\\             the in-game half

    Both are needed. The server deals and holds the money; the client draws the
    table and sends what you asked for.

    Start the server. "Blackjack" should appear in the mod list, and a BLACKJACK
    entry on the game's main menu.

    There is an installer as well, if you would rather not place files by hand.

PLAYING
    BLACKJACK on the main menu opens the table. Pick a currency, type a bet,
    DEAL. Escape leaves.

    Six decks, dealer stands on soft 17, blackjack pays 3:2 in currency and even
    money in valuables -- one bitcoin at 3:2 would settle on half a coin.

    You can stake roubles, dollars, euros, GP coins, bitcoin or Lega medals.
    They are your own; a hand you lose is gone.

THE TABLE MAXIMUM
    500,000 roubles a hand, 5,000 dollars or euros, 50 GP, 10 bitcoin, 5 Lega.

    It is the house's only real protection. The edge on these rules is about
    half a percent, which is nothing across a session; what stops a player
    compounding is being unable to cover a losing streak by doubling up.

    Turn it off in the BepInEx menu (F12) under Table if you would rather play
    without one. The minimum always applies.

ART
    The card faces are Chris Aguilar's Vectorized Playing Cards 1.3, from
    opengameart.org. They live in BepInEx\\plugins\\Blackjack\\cards\\ as one PNG
    per card. Delete them and the mod draws its own instead.

    The table is a photograph, table.png beside the plugin. Delete it and a
    drawn table takes its place.

https://github.com/JoelHauser/Blackjack
""" % VERSION

if not os.path.exists(PAYLOAD):
    raise SystemExit('no payload; run tools/build-installer.py first')

os.makedirs(os.path.dirname(OUT), exist_ok=True)
if os.path.exists(OUT):
    os.remove(OUT)

shutil.copyfile(PAYLOAD, OUT)

with zipfile.ZipFile(OUT, 'a', zipfile.ZIP_DEFLATED) as z:
    z.writestr('README.txt', README.replace('\n', '\r\n'))

with zipfile.ZipFile(OUT) as z:
    names = z.namelist()
    backslashes = [n for n in names if '\\' in n]
    print('%d entries, %d with a backslash' % (len(names), len(backslashes)))
    for n in names[:7]:
        print('   ' + n)
    print('   ... and %d more' % (len(names) - 7))

print()
print('%s  (%.1f MB)' % (OUT, os.path.getsize(OUT) / 1024 / 1024))
