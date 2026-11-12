"""Synthetic end-to-end tests for the actual C# installation engine."""
import copy, hashlib, json, os, subprocess, time, uuid
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/'src'
OUT=ROOT/'data'
CSC=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
RUN=SRC/'sandbox'/uuid.uuid4().hex
RUN.mkdir(parents=True)
def digest(data):return hashlib.sha256(data).hexdigest()
def writejson(path,data):path.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
def compile_exe(exe,manifest,test=True,resources=True,source=None):
    args=[str(CSC),'/nologo','/optimize+','/platform:anycpu','/target:winexe',
          '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',
          '/win32manifest:'+str(SRC/('asInvoker.manifest' if test else 'administrator.manifest')),'/out:'+str(exe)]
    if test:args+=['/define:SANDBOX_TEST']
    if resources:
        args+=['/resource:'+str(manifest)+',manifest.json']
        for kind in ('ui','dialogue'):args+=['/resource:'+str(OUT/f'{kind}.resource')+f',{kind}.resource']
    if source is None:
        # Isolate the process-name dependency; the production source stays unchanged.
        production=(SRC/'Installer.cs').read_text(encoding='utf8')
        needle='Process.GetProcessesByName("ACBlackFlag")'
        assert production.count(needle)==1
        isolated=RUN/'IsolatedInstaller.cs'
        isolated.write_text(production.replace(needle,'Process.GetProcessesByName("ACBFTestGame")'),encoding='utf8')
        source=isolated
    args+=[str(source)]
    result=subprocess.run(args,capture_output=True,text=True)
    assert result.returncode==0,(result.stdout,result.stderr)

manifest=json.loads((OUT/'manifest.json').read_text())
originals={};candidates={};legacy={}
for i,p in enumerate(manifest['archives']):
    b=bytes(range(256))*(17+i*2);originals[p['file']]=b
    p['original_size']=len(b);p['original_sha256']=digest(b);p['row_offset']=120
    p['original_row']=b[120:144].hex();p['append_offset']=65536
    p['new_row']=bytes([i+31])*24;p['new_row']=p['new_row'].hex()
    raw=(OUT/p['payload_resource']).read_bytes()
    p['size']=(65536+len(raw)+65535)&~65535
    new=bytearray(b);new[120:144]=bytes.fromhex(p['new_row'])
    new.extend(b'\0'*(65536-len(new)));new.extend(raw);new.extend(b'\0'*(p['size']-len(new)))
    candidates[p['file']]=bytes(new);p['sha256']=digest(new)
    previous=bytearray(b);previous[120:144]=b'LEGACY-ROW-REFERENCE!!!!!'[:24]
    previous+=b'OLD-TRANSLATION-RESOURCE'
    legacy[p['file']]=bytes(previous);p['legacy_sizes']=[len(previous)];p['legacy_sha256']=[digest(previous)]
old_patch=b'KNOWN-FAILED-PATCH02';manifest['known_patch02']=[digest(old_patch)]
manifest_path=RUN/'synthetic-manifest.json';writejson(manifest_path,manifest)
exe=SRC/'native-engine-test.exe';compile_exe(exe,manifest_path)
tests=[]
def fixture(name):
    game=RUN/name;game.mkdir()
    (game/'ACBlackFlag.exe').write_bytes(b'SYNTHETIC NOT EXECUTABLE')
    for n,b in originals.items():(game/n).write_bytes(b)
    return game
def invoke(game,restore=False,fail=None,available=None):
    env=os.environ.copy()
    if fail:env['ACBF_TEST_FAIL_AFTER_COMMIT']=fail
    if available is not None:env['ACBF_TEST_AVAILABLE_BYTES']=str(available)
    result=subprocess.run([str(exe),'--restore' if restore else '--install',str(game)],capture_output=True,env=env,timeout=50)
    return result.returncode,result.stdout.decode('utf-8',errors='replace'),result.stderr.decode('utf-8',errors='replace')
def assert_files(game,files):
    for name,data in files.items():assert (game/name).read_bytes()==data,name
def pass_test(name):tests.append({'name':name,'passed':True})

game=fixture('clean')
result=invoke(game);assert result[0]==0,result;assert_files(game,candidates)
for n,b in originals.items():assert (game/'.acbf-zhhk-native'/(n+'.original')).read_bytes()==b
pass_test('clean_install_preserves_complete_originals')
assert invoke(game)[0]==0;assert_files(game,candidates);pass_test('repeat_install_is_safe')
result=invoke(game,True);assert result[0]==0,result;assert_files(game,originals);pass_test('both_archives_restore_exactly')
assert invoke(game,True)[0]==0;assert_files(game,originals);pass_test('repeat_restore_is_safe')

game=fixture('mixed-legacy')
previous=game/'.acbf-native-originals-20261107';previous.mkdir()
for n,b in originals.items():(previous/(n+'.original')).write_bytes(b)
base='DataPC_boot.forge';(game/base).write_bytes(legacy[base]);(game/(base+'.native-restore-stage')).write_bytes(originals[base])
assert invoke(game)[0]==0;assert_files(game,candidates)
assert not (game/(base+'.native-restore-stage')).exists();pass_test('mixed_previous_version_upgrades_both_archives_and_reuses_verified_stage')
assert invoke(game,True)[0]==0;assert_files(game,originals);pass_test('mixed_version_upgrade_restores_to_pristine_not_to_old_candidate')

game=fixture('installed-previous-release');back=game/'.acbf-zhhk-native';back.mkdir()
for n,b in originals.items():
    (back/(n+'.original')).write_bytes(b);(game/n).write_bytes(legacy[n])
writejson(back/'state.json',{'format':1,'target':str(game.resolve()),'version':'2026.11.09','phase':'installed'})
assert invoke(game)[0]==0;assert_files(game,candidates)
assert json.loads((back/'state.json').read_text())['version']==manifest['version']
assert invoke(game,True)[0]==0;assert_files(game,originals)
pass_test('installed_20261109_upgrades_to_new_version_and_restores_pristine_originals')

game=fixture('installed-same-day-r1');back=game/'.acbf-zhhk-native';back.mkdir()
for n,b in originals.items():
    (back/(n+'.original')).write_bytes(b);(game/n).write_bytes(legacy[n])
writejson(back/'state.json',{'format':1,'target':str(game.resolve()),'version':'2026.11.10','phase':'installed'})
assert invoke(game)[0]==0;assert_files(game,candidates)
assert json.loads((back/'state.json').read_text())['version']==manifest['version']
assert invoke(game,True)[0]==0;assert_files(game,originals)
pass_test('installed_same_day_20261110_r1_upgrades_to_current_and_restores_pristine_originals')

game=fixture('installed-r2');back=game/'.acbf-zhhk-native';back.mkdir()
for n,b in originals.items():
    (back/(n+'.original')).write_bytes(b);(game/n).write_bytes(legacy[n])
writejson(back/'state.json',{'format':1,'target':str(game.resolve()),'version':'2026.11.10-r2','phase':'installed'})
assert invoke(game)[0]==0;assert_files(game,candidates)
assert invoke(game,True)[0]==0;assert_files(game,originals)
pass_test('installed_r2_upgrades_to_current_and_restores_pristine_originals')

# Read aliases are safe: stages are unique files and only fixture paths are atomically replaced.
game=fixture('read-aliases');back=game/'.acbf-zhhk-native';back.mkdir();aliases={}
for n,b in originals.items():
    target=game/n;target.write_bytes(legacy[n]);alias=RUN/(n+'.read-alias');os.link(target,alias)
    original_alias=RUN/(n+'.original-alias');original_alias.write_bytes(b)
    os.link(original_alias,back/(n+'.original'));aliases[alias]=legacy[n];aliases[original_alias]=b
assert invoke(game)[0]==0;assert_files(game,candidates)
assert all(p.read_bytes()==b for p,b in aliases.items())
assert invoke(game,True)[0]==0;assert_files(game,originals)
assert all(p.read_bytes()==b for p,b in aliases.items())
pass_test('hardlinked_read_sources_and_backups_remain_unchanged_after_atomic_install_and_restore')

harness=RUN/'hash-method-test.exe'
compile_exe(harness,None,resources=False,source=ROOT/'tests/hash_method_test.cs')
checked=subprocess.run([str(harness),str(exe)],capture_output=True,timeout=60)
assert checked.returncode==0,(checked.stdout,checked.stderr)
pass_test('actual_hash_method_matches_sha256_across_15_boundaries_and_short_reads')

game=fixture('installed-rc1');back=game/'.acbf-zhhk-native';back.mkdir()
for n,b in originals.items():
    (back/(n+'.original')).write_bytes(b);(game/n).write_bytes(legacy[n])
writejson(back/'state.json',{'format':1,'target':str(game.resolve()),'version':'2026.11.10-rc1','phase':'installed'})
assert invoke(game)[0]==0;assert_files(game,candidates)
assert json.loads((back/'state.json').read_text())['version']==manifest['version']
assert invoke(game,True)[0]==0;assert_files(game,originals)
pass_test('installed_rc1_upgrades_to_rc2_and_restores_pristine_originals')

game=fixture('unknown-state-version');back=game/'.acbf-zhhk-native';back.mkdir()
writejson(back/'state.json',{'format':1,'target':str(game.resolve()),'version':'2099.01.01','phase':'installed'})
before_state=(back/'state.json').read_bytes();assert invoke(game)[0]!=0
assert_files(game,originals);assert (back/'state.json').read_bytes()==before_state
pass_test('unknown_future_install_state_is_preserved_and_rejected')

game=fixture('known-patch02');(game/'DataPC_boot_patch_02.forge').write_bytes(old_patch)
assert invoke(game)[0]==0;assert not (game/'DataPC_boot_patch_02.forge').exists()
assert next((game/'.acbf-zhhk-native').glob('old-patch02-*.disabled')).read_bytes()==old_patch
pass_test('known_failing_patch02_is_quarantined_and_preserved')
assert invoke(game,True)[0]==0;assert not (game/'DataPC_boot_patch_02.forge').exists();pass_test('restore_keeps_failed_patch02_disabled')

game=fixture('unknown-patch02');(game/'DataPC_boot_patch_02.forge').write_bytes(b'USER MOD')
assert invoke(game)[0]!=0;assert_files(game,originals);assert (game/'DataPC_boot_patch_02.forge').read_bytes()==b'USER MOD'
pass_test('unknown_patch02_is_preserved_without_archive_changes')

game=fixture('unknown-archive');name=manifest['archives'][1]['file'];(game/name).write_bytes(b'USER MOD')
expected={n:(game/n).read_bytes() for n in originals};assert invoke(game)[0]!=0;assert_files(game,expected)
pass_test('unknown_archive_stops_before_both_replacements')

game=fixture('rollback');result=invoke(game,fail='ui');assert result[0]!=0,result;assert_files(game,originals)
state=json.loads((game/'.acbf-zhhk-native/state.json').read_text());assert state['phase']=='failed_restored',state
pass_test('failure_after_first_replacement_restores_both_originals')

game=fixture('rollback-after-both');result=invoke(game,fail='dialogue');assert result[0]!=0,result;assert_files(game,originals)
assert json.loads((game/'.acbf-zhhk-native/state.json').read_text())['phase']=='failed_restored'
pass_test('failure_after_both_replacements_restores_both_originals')

required_stages=sum(p['size'] for p in manifest['archives'])
required_restore=sum(p['original_size'] for p in manifest['archives'])
game=fixture('insufficient-dual-rollback-space')
assert invoke(game,available=128*1024*1024+required_stages+max(p['original_size'] for p in manifest['archives']))[0]!=0
assert_files(game,originals);pass_test('space_preflight_reserves_both_original_archives_not_only_the_larger_one')
game=fixture('enough-dual-rollback-space')
assert invoke(game,available=128*1024*1024+required_stages+required_restore)[0]==0
assert_files(game,candidates);pass_test('exact_dual_archive_space_boundary_allows_install')

game=fixture('partial-current-install-space');assert invoke(game)[0]==0
(game/base).write_bytes(originals[base]);expected={n:(game/n).read_bytes() for n in originals}
base_plan=next(p for p in manifest['archives'] if p['file']==base)
assert invoke(game,available=128*1024*1024+base_plan['size']+base_plan['original_size'])[0]!=0
assert_files(game,expected);pass_test('partial_upgrade_also_reserves_rollback_of_already_installed_archive')

game=fixture('bad-backup');back=game/'.acbf-native-originals-20261107';back.mkdir()
(back/(base+'.original')).write_bytes(b'BAD BACKUP')
assert invoke(game)[0]!=0;assert_files(game,originals);pass_test('bad_existing_backup_is_preserved_and_blocks_install')

game=fixture('bad-stage');(game/(base+'.zhHK-'+manifest['version']+'-stage')).write_bytes(b'USER STAGE')
assert invoke(game)[0]!=0;assert_files(game,originals);pass_test('unknown_stage_is_preserved_without_archive_replacement')

game=fixture('external-after-install');assert invoke(game)[0]==0
(game/base).write_bytes(b'EXTERNAL CHANGE');expected={n:(game/n).read_bytes() for n in originals}
assert invoke(game,True)[0]!=0;assert_files(game,expected);pass_test('restore_preserves_external_changes')

game=fixture('backup-after-install');assert invoke(game)[0]==0
(game/'.acbf-zhhk-native'/(base+'.original')).write_bytes(b'BAD BACKUP')
assert invoke(game,True)[0]!=0;assert_files(game,candidates);pass_test('restore_rejects_corrupted_backup_before_mutation')

game=fixture('locked-operation');back=game/'.acbf-zhhk-native';back.mkdir()
with (back/'operation.lock').open('ab') as lock:
    assert invoke(game)[0]!=0
assert_files(game,originals);pass_test('concurrent_operation_is_blocked')

game=fixture('hardlink-stage');p=manifest['archives'][0];other=RUN/'other-file';other.write_bytes(originals[p['file']])
os.link(other,game/(p['file']+'.zhHK-'+manifest['version']+'-stage'))
assert invoke(game)[0]!=0;assert_files(game,originals);assert other.read_bytes()==originals[p['file']]
pass_test('hardlinked_stage_cannot_modify_another_file')

outside=SRC/'outside-fixture';outside.mkdir(exist_ok=True)
assert invoke(outside)[0]!=0;pass_test('test_binary_refuses_paths_outside_named_sandbox')

fake_source=RUN/'FakeGame.cs';fake_source.write_text('class FakeGame { static void Main() { System.Threading.Thread.Sleep(120000); } }')
fake=RUN/'ACBFTestGame.exe';compile_exe(fake,None,resources=False,source=fake_source)
dummy=subprocess.Popen([str(fake)],creationflags=subprocess.CREATE_NO_WINDOW)
try:
    time.sleep(.2);game=fixture('running-game');assert invoke(game)[0]!=0;assert_files(game,originals)
    assert not (game/'.acbf-zhhk-native').exists();pass_test('running_game_blocks_all_installation_changes')
finally:dummy.terminate();dummy.wait(timeout=10)

report={'passed':True,'test_count':len(tests),'source_sha256':digest((SRC/'Installer.cs').read_bytes()),
        'synthetic_root':str(RUN),'tests':tests,'formal_game_modified':False,'real_game_started':False,'process_guard_test_dependency':'ACBFTestGame; only the synthetic source changes this literal; production ACBlackFlag guard unchanged'}
writejson(SRC/'test-results.json',report)
print(json.dumps(report,indent=2))
