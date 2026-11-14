import pathlib, sys, zipfile, stat
root = pathlib.Path(sys.argv[1]); root.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(root / 'good.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
    archive.writestr('folder/hello.txt', 'RELYR archive test')
with zipfile.ZipFile(root / 'escape.zip', 'w') as archive:
    archive.writestr('../escaped.txt', 'must not escape')
with zipfile.ZipFile(root / 'link.zip', 'w') as archive:
    entry = zipfile.ZipInfo('link'); entry.create_system = 3
    entry.external_attr = (stat.S_IFLNK | 0o777) << 16
    archive.writestr(entry, '../../outside')
(root / 'broken.zip').write_text('not a zip file')

(root / 'broken.gz').write_text('not gzip')
import gzip
with gzip.open(root / 'single.gz', 'wb') as archive:
    archive.write(b'RELYR gzip test')
