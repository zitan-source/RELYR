#!/usr/bin/env python3
"""Prepare a Mac-only GitHub release request; does not publish or touch Windows."""
import hashlib
import json
import pathlib
import plistlib

root = pathlib.Path(__file__).resolve().parent.parent
with (root / 'macos/Info.plist').open('rb') as f:
    version = plistlib.load(f)['CFBundleShortVersionString']
asset = root / f'.verification/free-trial/RELYR-Mac-{version}-arm64.dmg'
checksum = pathlib.Path(str(asset) + '.sha256')
assert checksum.read_text().split()[0].lower() == hashlib.sha256(asset.read_bytes()).hexdigest(), 'DMG checksum mismatch'
notes = (root / 'macos/distribution/RELEASE-NOTES.md').read_text()
for language in ('en-US', 'ja-JP'):
    assert f'<!-- RELYR-RELEASE-NOTES:{language} -->' in notes
payload = dict(tag_name=f'macos-v{version}', name=f'RELYR for Mac v{version}', body=notes,
               draft=True, prerelease=False, make_latest='false')
out = root / '.verification/mac-release-request.json'
out.write_text(json.dumps(payload,ensure_ascii=False,indent=2)+'\n')
print(out)
print('Publish only with make_latest=false; upload DMG and .sha256 before clearing draft.')
