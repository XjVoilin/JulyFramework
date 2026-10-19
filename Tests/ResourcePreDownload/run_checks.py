"""Compile July resource packages and run checks against actual YooAsset sources.

Usage: python run_checks.py --project <Unity project> [--packages <July Packages>]
Requires .NET 8 SDK, the project's Unity installation and resolved dependencies.
No test code is imported into the Unity project's Assets or runtime assemblies.
"""
from pathlib import Path
import argparse
import json
import shutil
import subprocess
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--project', type=Path, required=True)
parser.add_argument('--packages', type=Path, default=Path(__file__).resolve().parents[2] / 'Packages')
parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parent / 'validation')
args = parser.parse_args()
project, packages, output = args.project.resolve(), args.packages.resolve(), args.output.resolve()
output.mkdir(parents=True, exist_ok=True)
dotnet = Path('C:/Program Files/dotnet/dotnet.exe')
sdk = max(dotnet.parent.glob('sdk/8.*'), key=lambda p: p.name)
ref_pack = max(dotnet.parent.glob('packs/Microsoft.NETCore.App.Ref/8.*'), key=lambda p: p.name)
unity = Path('E:/Install/UnityHub/2022.3.62f2/Editor/Data/Managed/UnityEngine')
assemblies = project / 'Library/ScriptAssemblies'
managed = [assemblies / (name + '.dll') for name in ('UniTask', 'July.Arch', 'July.Events', 'July.Logging')]
engine = list(unity.glob('*.dll'))
references = list((ref_pack / 'ref/net8.0').glob('*.dll')) + managed + engine
yoo = project / 'Packages/com.tuyoogame.yooasset@2.3.12/Runtime'

def compile_sources(name, sources, refs, target='library'):
    binary = output / (name + '.dll')
    response = output / (name + '.rsp')
    response.write_text('\n'.join([
        '-utf8output', '-nostdlib+', '-unsafe+', '-langversion:9.0', f'-target:{target}',
        '-define:UNITY_2019_4_OR_NEWER;UNITY_2020_2_OR_NEWER;UNITY_2021_2_OR_NEWER;UNITY_2022_3_OR_NEWER',
        f'-out:"{binary}"', *[f'-r:"{p}"' for p in refs], *[f'"{p}"' for p in sources]
    ]), encoding='utf-8')
    result = subprocess.run([str(dotnet), str(sdk / 'Roslyn/bincore/csc.dll'), '@' + str(response)],
                            cwd=project, capture_output=True, text=True, encoding='utf-8')
    (output / (name + '-compile.log')).write_text(result.stdout + result.stderr, encoding='utf-8')
    if result.returncode:
        print(result.stdout + result.stderr)
        raise SystemExit(result.returncode)
    print(name + ' COMPILE PASS', flush=True)
    return binary

domain = compile_sources('July.Resource', (packages / 'com.july.resource/Runtime').rglob('*.cs'), references)
# Check the shipping adapter against the installed YooAsset binary as well.
adapter_sources = list((packages / 'com.july.resource.yooasset/Runtime').rglob('*.cs'))
compile_sources('July.Resource.YooAsset', adapter_sources, references + [domain, assemblies / 'YooAsset.dll'])
compile_sources('July.Resource.Tests', (packages / 'com.july.resource/Tests').glob('*.cs'), references + [domain,
    project / 'Library/PackageCache/com.unity.ext.nunit@2.0.3/net40/unity-custom/nunit.framework.dll',
    assemblies / 'UnityEngine.TestRunner.dll'])
# These existing fixtures implement IResourceSystem and must compile with its
# expanded contract. Runtime code in UI/Scene is not changed by this feature.
test_refs = references + [domain,
    project / 'Library/PackageCache/com.unity.ext.nunit@2.0.3/net40/unity-custom/nunit.framework.dll',
    assemblies / 'UnityEngine.TestRunner.dll'] + [assemblies / (name + '.dll') for name in (
        'July.UI', 'July.Input', 'UnityEngine.UI', 'Unity.RenderPipelines.Universal.Runtime')]
for name, paths in (
    ('July.UI.Tests', ['com.july.ui/Tests/UISystemLifecycleTests.cs', 'com.july.ui/Tests/TipManagerTests.cs']),
    ('July.Scene.Tests', ['com.july.scene/Tests/SceneSystemTests.cs'])
):
    refs = test_refs + ([assemblies / 'July.Scene.dll'] if name == 'July.Scene.Tests' else [])
    compile_sources(name, [packages / path for path in paths], refs)
sources = list(yoo.rglob('*.cs')) + adapter_sources + [Path(__file__).resolve().with_name('PreDownloadChecks.cs')]
binary = compile_sources('PreDownloadChecks', sources, references + [domain], 'exe')
for dep in managed + engine:
    shutil.copy2(dep, output / dep.name)
(output / 'PreDownloadChecks.runtimeconfig.json').write_text(json.dumps({
    'runtimeOptions': {'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': '8.0.0'}}
}), encoding='utf-8')
result = subprocess.run([str(dotnet), str(binary)], cwd=project, capture_output=True, text=True,
                        encoding='utf-8', timeout=60)
(output / 'result.log').write_text(result.stdout + result.stderr, encoding='utf-8')
print(result.stdout + result.stderr)
raise SystemExit(result.returncode)
