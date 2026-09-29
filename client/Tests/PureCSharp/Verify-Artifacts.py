"""Verify exported source, main Player, frozen baseline and production patch agree."""
import argparse
import hashlib
import json
import re
import zipfile
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--source', required=True)
parser.add_argument('--baseline', required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
checks = []

def check(ok, text):
    if not ok: raise AssertionError(text)
    checks.append(text)

def load(path): return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def forbidden(path):
    path = path.replace('\\', '/').lower()
    return any(part in path for part in ('/xlua/', '/xlualogic/', '/managers/lua/', '/dll2lualib_')) or bool(
        re.search(r'/(?:lib)?xlua[^/]*$|/[^/]+\.lua(?:\.txt|\.bytes)?$', path))

def verify_zip(path, label):
    with zipfile.ZipFile(path) as archive:
        check(archive.testzip() is None, label + ': archive CRC')
        check(not any(forbidden('/'+name) for name in archive.namelist()), label + ': no script runtime, tools or native plugins')
        if label == 'source':
            check(not any('/.codex/' in name or '/TestResults/' in name or '/Library/' in name for name in archive.namelist()),
                  'source: no backups or build caches')
            check(any(name.endswith('PureCSharpChecks.cs') for name in archive.namelist()), 'source: migrated regression checks included')

build = load(root/'Builds/latest-windows.json')
patch = load(root/'Builds/HotUpdate/latest-patch.json')
config = load(root/'ProjectSettings/HotUpdateProject.json')
check(build['result'] == 'Succeeded' and build['errors'] == 0 and build['scriptingBackend'] == 'IL2CPP', 'successful IL2CPP build')
check(patch['baseId'] == config['baseId'], 'production patch matches current base')
player = Path(build['output'])
check(not any(forbidden('/'+p.relative_to(player).as_posix()) for p in player.rglob('*') if p.is_file()), 'Player contains no script runtime files')
check(not (Path(patch['output'])/'VALIDATION-ONLY.txt').exists(), 'latest patch is production content')
for name, folder, manifest_path in [
    ('Player', player/'BigWorld_Data/StreamingAssets', player/'BigWorld_Data/StreamingAssets/hotupdate/manifest.json'),
    ('baseline', root/'Builds/HotUpdate/Bases'/config['baseId']/'content', root/'Builds/HotUpdate/Bases'/config['baseId']/'manifest.json'),
    ('patch', Path(patch['output']), Path(patch['output'])/'manifest.json')]:
    manifest = load(manifest_path)
    check(manifest['baseId'] == config['baseId'], name + ': same base ID')
    check(not any(forbidden('/'+item['path']) for item in manifest['files']), name + ': clean manifest')
    bundles = [item for item in manifest['files'] if item['kind'] == 'content' and item['path'].endswith('.assetbundle')
               and item['path'] != manifest['sceneBundle']]
    check(len(bundles) == 5, name + ': exactly five original-format content bundles')
    for item in manifest['files']:
        file = folder/item['path']
        check(file.is_file() and file.stat().st_size == item['size'] and hashlib.sha256(file.read_bytes()).hexdigest() == item['sha256'],
              name + ': verified '+item['path'])
    hot = (folder/'hotupdate/assemblies/Assembly-CSharp.dll.bytes').read_bytes()
    check(b'LuaEnv\0' not in hot and b'LuaManager\0' not in hot and b'LuaArrAccess\0' not in hot,
          name + ': no script runtime types in hot assembly')
    check(b'PureCSharpChecks\0' not in hot and b'GameplayPlayChecks\0' not in hot, name + ': no validation probes')
for path, name in [(build['archive'],'Player'), (args.source,'source'), (args.baseline,'baseline')]: verify_zip(path,name)

# Removed scripts must not leave missing serialized references in the active project.
removed = root/'.codex/backups/PureCSharp-20260929/Removed'
guids = set()
for meta in removed.rglob('*.meta'):
    found = re.search(rb'^guid: ([a-f0-9]{32})', meta.read_bytes(), re.M)
    if found: guids.add(found.group(1))
dangling = []
for p in (root/'Assets').rglob('*'):
    if p.suffix not in ('.prefab','.unity','.asset'): continue
    if any(guid in guids for guid in re.findall(rb'guid: ([a-f0-9]{32})', p.read_bytes())):
        dangling.append(str(p.relative_to(root)))
check(not dangling, 'no serialized references to removed assets: '+str(dangling))
result = dict(passed=True, count=len(checks), baseId=config['baseId'], player=build['archive'],
              source=args.source, baseline=args.baseline, patch=patch['output'], checks=checks)
output = root/'TestResults/PureCSharp/artifacts.json'
output.parent.mkdir(exist_ok=True)
output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(f'Artifact checks passed: {len(checks)}; {output}')
