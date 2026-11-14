#!/usr/bin/env python3
"""Persistent, repository-local signing identity. Never silently rotate or use ad hoc signing."""
import hashlib
import os
from pathlib import Path
import secrets
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parent.parent
STATE = ROOT / '.local-signing'
CHAIN = STATE / 'relyr.keychain-db'
PASSWORD = STATE / 'keychain-password'
CERT = STATE / 'certificate.der'

def run(*args, **kwargs):
    try:
        return subprocess.run([str(a) for a in args], check=True, **kwargs)
    except subprocess.CalledProcessError as error:
        # Security commands may contain the local keychain password: never echo argv.
        raise SystemExit(f'{Path(str(args[0])).name} failed (exit {error.returncode}); signing stopped.') from None

def initialize():
    if STATE.exists():
        raise SystemExit('Signing state already exists. Refusing to replace the identity.')
    STATE.mkdir(mode=0o700)
    password = secrets.token_urlsafe(48)
    PASSWORD.write_text(password)
    PASSWORD.chmod(0o600)
    # Restrict key use to codesign, without installing a system-wide trusted root.
    run('/usr/bin/security', 'create-keychain', '-p', password, CHAIN, stdout=subprocess.DEVNULL)
    with tempfile.TemporaryDirectory(dir=STATE) as temporary:
        folder = Path(temporary)
        config = folder / 'certificate.cnf'
        config.write_text('''[req]
prompt = no
distinguished_name = subject
x509_extensions = signing
[subject]
CN = RELYR Local Development
O = RELYR Local Development
[signing]
basicConstraints = critical,CA:false
keyUsage = critical,digitalSignature
extendedKeyUsage = critical,codeSigning
subjectKeyIdentifier = hash
''')
        key, pem, p12 = [folder / n for n in ('key.pem', 'certificate.pem', 'identity.p12')]
        run('/usr/bin/openssl', 'req', '-new', '-x509', '-newkey', 'rsa:3072', '-nodes', '-sha256', '-days', '7300', '-config', config, '-keyout', key, '-out', pem, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        run('/usr/bin/openssl', 'x509', '-in', pem, '-outform', 'DER', '-out', CERT)
        run('/usr/bin/openssl', 'pkcs12', '-export', '-inkey', key, '-in', pem, '-out', p12, '-passout', 'stdin', input=(password+'\n').encode())
        run('/usr/bin/security', 'import', p12, '-k', CHAIN, '-P', password, '-x', '-T', '/usr/bin/codesign', stdout=subprocess.DEVNULL)
    print('Created persistent RELYR local identity; no system trust settings changed.')

def sign(app):
    if not all(p.is_file() for p in (CHAIN, PASSWORD, CERT)):
        raise SystemExit('Persistent signing identity missing. Run python3 macos/sign-local.py --initialize once. Do not delete .local-signing or recreate it on each update.')
    password = PASSWORD.read_text()
    run('/usr/bin/security', 'unlock-keychain', '-p', password, CHAIN)
    fingerprint = hashlib.sha1(CERT.read_bytes()).hexdigest().upper()
    run('/usr/bin/codesign', '--force', '--sign', fingerprint, '--keychain', CHAIN, '--timestamp=none', app)
    run('/usr/bin/codesign', '--verify', '--strict', '--verbose=2', app)

if __name__ == '__main__':
    os.umask(0o077)
    if sys.argv[1:] == ['--initialize']:
        initialize()
    elif len(sys.argv) == 2:
        sign(Path(sys.argv[1]).resolve())
    else:
        raise SystemExit('Usage: sign-local.py --initialize | app-path')
