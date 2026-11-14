#!/usr/bin/env python3
"""Verify update identity continuity without opening apps or touching privacy grants."""
from pathlib import Path
import shutil
import subprocess
import sys
app, output = map(lambda p: Path(p).resolve(), sys.argv[1:])
root = Path(__file__).resolve().parents[2]
assert output.is_relative_to(root / '.verification')
output.mkdir(parents=True, exist_ok=True)
def run(*args, **kw):
    return subprocess.run([str(a) for a in args], check=True, capture_output=True, text=True, **kw)
def requirement(path):
    result = run('/usr/bin/codesign', '-d', '-r-', path)
    value = result.stdout + result.stderr
    return value.split('designated => ', 1)[1].splitlines()[0].strip()
def cdhash(path):
    return next(line for line in run('/usr/bin/codesign', '-d', '-vvv', path).stderr.splitlines() if line.startswith('CDHash='))
old = requirement(app)
assert 'cdhash' not in old and ('anchor' in old or 'certificate' in old), old
updated = output / 'updated.app'
if updated.exists(): shutil.rmtree(updated)
shutil.copytree(app, updated)
(updated / 'Contents/Resources/update-identity-test.txt').write_text('Different signed resources for update continuity test.\n')
run(sys.executable, root / 'macos/sign-local.py', updated)
assert cdhash(updated) != cdhash(app), 'Test update must have different signed content'
assert old == requirement(updated), 'Update changed the designated requirement'
run('/usr/bin/codesign', '--verify', '--strict', '-R', '='+old, updated)
run('/usr/bin/codesign', '--verify', '--strict', '-R', '='+requirement(updated), app)
run('/usr/bin/codesign', '--force', '--sign', '-', updated)
rejected = subprocess.run(['/usr/bin/codesign','--verify','-R','='+old,str(updated)], capture_output=True)
assert rejected.returncode != 0, 'Identity must reject an unrelated signer even with the same bundle ID'
shutil.rmtree(updated)
(output / 'designated-requirement.txt').write_text(old+'\n')
print('PASS: changed app content keeps mutual signing identity; same-ID ad hoc impostor rejected; no privacy settings changed')
