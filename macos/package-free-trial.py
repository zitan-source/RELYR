#!/usr/bin/env python3
"""Package the already-validated local trial unchanged, with public user documentation."""
import hashlib, html, plistlib, re, subprocess, tempfile, zipfile
from pathlib import Path
root = Path(__file__).resolve().parent.parent
stage = root / '.verification/macos-build'
version = plistlib.loads((root/'macos/Info.plist').read_bytes())['CFBundleShortVersionString']
archive = stage / f'RELYR-Mac-Trial-{version}-arm64.zip'
assert hashlib.sha256(archive.read_bytes()).hexdigest() == Path(str(archive)+'.sha256').read_text().split()[0], 'ZIP checksum mismatch'
with zipfile.ZipFile(archive) as z:
    assert all(not n.startswith('/') and '..' not in Path(n).parts for n in z.namelist())
out = root/'.verification/free-trial'; out.mkdir(exist_ok=True)
guide = (root/'macos/distribution/はじめに.ja.md').read_text()
def inline(s):
    s = html.escape(s)
    s = re.sub(r'\*\*(.+?)\*\*',r'<strong>\1</strong>',s)
    return re.sub(r'`([^`]+)`',r'<code>\1</code>',s)
blocks=[]
for line in guide.splitlines():
    if line.startswith('# '): blocks.append('<h1>'+inline(line[2:])+'</h1>')
    elif line.startswith('## '): blocks.append('<h2>'+inline(line[3:])+'</h2>')
    elif line.startswith('図解：'): blocks.append('<div class="flow">'+inline(line[3:])+'</div>')
    elif line: blocks.append('<p>'+inline(line)+'</p>')
page='''<!doctype html><html lang="ja"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>RELYR Mac はじめに</title><style>
:root{color-scheme:light dark}body{font:17px/1.9 -apple-system,BlinkMacSystemFont,sans-serif;max-width:780px;margin:48px auto;padding:0 24px;color:light-dark(#202528,#eceff0);background:light-dark(#fafafa,#151719)}h1{font-size:30px;line-height:1.4}h2{font-size:22px;margin-top:52px;border-top:1px solid #8885;padding-top:28px}strong{font-weight:650}.flow{padding:20px;border:1px solid #168773;border-radius:14px;margin:24px 0;overflow-wrap:anywhere}code{font:inherit}p{margin:14px 0}@media print{body{font-size:11pt;margin:0}h2{break-after:avoid}.flow{break-inside:avoid}}</style><main>'''+''.join(blocks)+'</main></html>'
with tempfile.TemporaryDirectory(dir=out) as tmp:
    folder=Path(tmp)/f'RELYR-Mac-{version}'
    folder.mkdir()
    subprocess.run(['ditto','-x','-k',str(archive),str(folder)],check=True)
    app=folder/'RELYR Mac Trial.app'
    subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True)
    for name in ['はじめに.ja.md','RELEASE-NOTES.md']:
        (folder/name).write_bytes((root/'macos/distribution'/name).read_bytes())
    (folder/'はじめに.html').write_text(page)
    (out/'はじめに.html').write_text(page)
    (folder/'LICENSE.txt').write_bytes((root/'LICENSE').read_bytes())
    target=out/f'RELYR-Mac-Trial-{version}-arm64-with-guide.zip'
    subprocess.run(['ditto','-c','-k','--sequesterRsrc','--keepParent',str(folder),str(target)],check=True)
    digest=hashlib.sha256(target.read_bytes()).hexdigest()
    Path(str(target)+'.sha256').write_text(digest+'  '+target.name+'\n')
    with zipfile.ZipFile(target) as z:
        assert not any(token in name for name in z.namelist() for token in ['.local-signing','.keychain-db','keychain-password','.git/'])
        assert z.read(f'RELYR-Mac-{version}/RELYR Mac Trial.app/Contents/MacOS/RELYR') == (stage/'RELYR Mac Trial.app/Contents/MacOS/RELYR').read_bytes()
print(target)
print('PASS: checksum, signed app unchanged, documentation included, no signing secrets. No publication performed.')
