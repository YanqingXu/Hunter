using BigWorld.HotUpdate;
using System.Text.Json;

var checks = new List<string>();
const string baseId = "test-base";
const string platform = "StandaloneWindows64";
HotUpdateManifest Create() => new() {
    baseId=baseId, platform=platform, version="v1", entryScene="Assets/Game.unity", sceneBundle="hotupdate/scenes.assetbundle",
    files=new[] { Make("VersionFile.bytes"), Make("AssetInfo.bytes"), Make("hotupdate/scenes.assetbundle"),
        Make("hotupdate/Skill.dll.bytes", "assembly", "SkillEditorKit.Runtime"), Make("hotupdate/Game.dll.bytes", "assembly", "Assembly-CSharp") }
};
HotUpdateFile Make(string path, string kind="content", string assembly=null) => new() { path=path,kind=kind,assembly=assembly,size=3,sha256=HotUpdateManifest.Hash(new byte[]{1,2,3}) };
void Check(bool valid, string name) { if(!valid) throw new Exception(name); checks.Add(name); }
void Reject(Action action, string name) { try { action(); } catch(InvalidDataException) { checks.Add(name); return; } throw new Exception("Accepted invalid input: "+name); }
Create().Validate(baseId,platform); checks.Add("A complete release validates");
foreach(string path in new[]{"../outside", "/root", "C:/outside", "x\\outside", "x/../outside", "x//y", "x/%2e%2e/y", "x?query", "x#fragment", "./x"})
    Reject(()=>HotUpdateManifest.ValidatePath(path), "Reject unsafe path: "+path);
Reject(()=>Create().Validate("different-base",platform),"Reject a patch for a different AOT base");
Reject(()=>Create().Validate(baseId,"Android"),"Reject a patch for another platform");
var duplicate=Create(); duplicate.files=duplicate.files.Append(Make("versionfile.bytes")).ToArray();
Reject(()=>duplicate.Validate(baseId,platform),"Reject case-insensitive duplicate file names");
var invalidHash=Create(); invalidHash.files[0].sha256=new string('z',64);
Reject(()=>invalidHash.Validate(baseId,platform),"Reject invalid SHA256 encoding");
var missing=Create(); missing.files=missing.files.Where(file=>file.assembly!="Assembly-CSharp").ToArray();
Reject(()=>missing.Validate(baseId,platform),"Reject missing game assembly");
var wrongKind=Create(); wrongKind.files.Single(file=>file.assembly=="Assembly-CSharp").kind="aot";
Reject(()=>wrongKind.Validate(baseId,platform),"Reject game assembly incorrectly marked as AOT metadata");
var extraAssembly=Create(); extraAssembly.files=extraAssembly.files.Append(Make("Extra.dll.bytes","assembly","Extra")).ToArray();
Reject(()=>extraAssembly.Validate(baseId,platform),"Reject assemblies not present in the main Player contract");
var oversize=Create(); oversize.files[0].size=536870913;
Reject(()=>oversize.Validate(baseId,platform),"Reject oversized file declarations");
string directory=Path.Combine(Path.GetTempPath(),"bigworld-hybrid-manifest-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try {
    var record=Make("data.bytes");
    Check(!HotUpdateManifest.VerifyFile(directory,record),"Reject a missing payload");
    File.WriteAllBytes(Path.Combine(directory,record.path),new byte[]{1,2,3});
    Check(HotUpdateManifest.VerifyFile(directory,record),"Accept payload with matching size and hash");
    File.WriteAllBytes(Path.Combine(directory,record.path),new byte[]{3,2,1});
    Check(!HotUpdateManifest.VerifyFile(directory,record),"Reject equal-length corrupted payload");
    File.WriteAllBytes(Path.Combine(directory,record.path),new byte[]{1});
    Check(!HotUpdateManifest.VerifyFile(directory,record),"Reject a truncated download");
} finally { Directory.Delete(directory,true); }
string output=args.Length>0?args[0]:"manifest-checks.json";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output,JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Manifest checks passed: {checks.Count}");
