#!/usr/bin/env python3
"""Create a drag-to-Applications DMG from the validated app without modifying it.
Prerequisite: python3 -m pip install --target .verification/dmg-python ds-store==1.3.3 mac-alias==2.2.3 dmgbuild==1.6.7
"""
import hashlib, os, plistlib, subprocess, sys, tempfile
from pathlib import Path
root = Path(__file__).resolve().parent.parent
sys.path.insert(0,str(root/'.verification/dmg-python'))
from ds_store import DSStore
from dmgbuild import build_dmg
stage = root/'.verification/macos-build'
app = stage/'RELYR Mac Trial.app'
version = plistlib.loads((app/'Contents/Info.plist').read_bytes())['CFBundleShortVersionString']
def run(*args): return subprocess.run(args,check=True,stdout=subprocess.PIPE).stdout
run('codesign','--verify','--deep','--strict',str(app))
signature = subprocess.run(['codesign','-d','-vvv',str(app)],check=True,capture_output=True).stderr.decode()
assert 'CDHash='+ (stage/'validated-signature.txt').read_text().strip() in signature
out = root/'.verification/free-trial'; out.mkdir(exist_ok=True)
run('python3',str(root/'macos/package-free-trial.py'))
with tempfile.TemporaryDirectory(dir=out) as tmp:
    tmp = Path(tmp); content=tmp/'content'; content.mkdir()
    run('ditto',str(app),str(content/app.name))
    (content/'Applications').symlink_to('/Applications')
    (content/'.background').mkdir()
    renderer=tmp/'background-renderer'
    run('xcrun','swiftc',str(root/'macos/Tools/InstallerBackground.swift'),'-o',str(renderer),'-module-cache-path',str(stage/'module-cache'),'-framework','AppKit')
    run(str(renderer),str(content/'.background/background.png'))
    (content/'はじめに.html').write_bytes((out/'はじめに.html').read_bytes())
    (content/'LICENSE.txt').write_bytes((root/'LICENSE').read_bytes())
    mount=tmp/'mounted'; mount.mkdir()
    target=out/f'RELYR-Mac-{version}-arm64.dmg'
    pending=tmp/'release.dmg'
    build_dmg(str(pending),'RELYR Installer',settings={
        'files':[str(content/app.name), str(content/'はじめに.html'), str(content/'LICENSE.txt')],
        'symlinks':{'Applications':'/Applications'},
        'background':str(content/'.background/background.png'),
        'window_rect':((160,120),(640,400)),
        'icon_size':88,'text_size':13,'default_view':'icon-view',
        'show_status_bar':False,'show_toolbar':False,'show_sidebar':False,
        'show_pathbar':False,'arrange_by':None,
        'icon_locations':{app.name:(170,192),'Applications':(470,192),
                          'はじめに.html':(170,303),'LICENSE.txt':(470,303)},
    })
    run('hdiutil','verify',str(pending))
    run('hdiutil','attach','-readonly','-nobrowse','-mountpoint',str(mount),str(pending))
    try:
        run('codesign','--verify','--deep','--strict',str(mount/app.name))
        assert (mount/app.name/'Contents/MacOS/RELYR').read_bytes() == (app/'Contents/MacOS/RELYR').read_bytes()
        with DSStore.open(str(mount/'.DS_Store'),'r') as ds:
            assert ds['Applications']['Iloc'] == (470,192)
        assert os.readlink(mount/'Applications') == '/Applications'
        for name in ['はじめに.html', 'LICENSE.txt']:
            assert (mount/name).read_bytes() == (content/name).read_bytes()
    finally: run('hdiutil','detach',str(mount))
    os.replace(pending,target)
    Path(str(target)+'.sha256').write_text(hashlib.sha256(target.read_bytes()).hexdigest()+'  '+target.name+'\n')
print(target)
print('PASS: read-only DMG, checksum, signed application unchanged, Applications link, Finder layout. Not published; no installed app or settings changed.')
