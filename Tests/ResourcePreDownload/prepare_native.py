from pathlib import Path
import argparse
import json
import shutil

work = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('--project', type=Path, required=True)
parser.add_argument('--packages', type=Path, default=work.parents[1] / 'Packages')
parser.add_argument('--output', type=Path)
args = parser.parse_args()
project, packages = args.project.resolve(), args.packages.resolve()
native = args.output.resolve() if args.output else project / '.scratch/july-predownload/UnityValidation'
(native / 'Assets/Checks').mkdir(parents=True, exist_ok=True)
(native / 'Packages').mkdir(exist_ok=True)
(native / 'ProjectSettings').mkdir(exist_ok=True)
shutil.copy2(project / 'ProjectSettings/ProjectVersion.txt', native / 'ProjectSettings/ProjectVersion.txt')
dependencies = {}
dependencies['com.july.resource'] = 'file:' + (packages / 'com.july.resource').as_posix()
# Reserve YooAsset's friend-assembly name for the controlled test filesystem.
# The optional SDK adapters are excluded from this validation snapshot only.
adapter = native / 'Packages/com.july.resource.yooasset'
shutil.copytree(packages / 'com.july.resource.yooasset', adapter, dirs_exist_ok=True,
                ignore=shutil.ignore_patterns('Platforms', 'Platforms.meta'))
dependencies['com.july.resource.yooasset'] = 'file:' + adapter.as_posix()
for name in ('com.july.arch', 'com.july.logging', 'com.july.events', 'com.unity.scriptablebuildpipeline',
             'com.unity.test-framework', 'com.unity.ext.nunit'):
    folder = next((project / 'Library/PackageCache').glob(name + '@*'))
    dependencies[name] = 'file:' + folder.as_posix()
for name in ('com.cysharp.unitask', 'com.tuyoogame.yooasset@2.3.12'):
    folder = project / 'Packages' / name
    package_name = json.loads((folder / 'package.json').read_text(encoding='utf-8-sig'))['name']
    dependencies[package_name] = 'file:' + folder.as_posix()
for name in ('assetbundle', 'unitywebrequest', 'unitywebrequestassetbundle', 'ui', 'imgui', 'physics', 'physics2d', 'jsonserialize'):
    dependencies['com.unity.modules.' + name] = '1.0.0'
(native / 'Packages/manifest.json').write_text(json.dumps({'dependencies': dependencies}, indent=2), encoding='utf-8')
shutil.copy2(work / 'NativeUnityChecks.cs', native / 'Assets/Checks/NativeUnityChecks.cs')
(native / 'Assets/Checks/Checks.asmdef').write_text(json.dumps({
    'name': 'YooAsset.MiniGame',
    'references': ['YooAsset', 'UniTask', 'July.Arch', 'July.Resource', 'July.Resource.YooAsset'],
    'includePlatforms': ['Editor']
}), encoding='utf-8')
print(native)
