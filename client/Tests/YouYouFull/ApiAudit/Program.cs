using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

if (args.Length < 3 || args.Length > 4) throw new ArgumentException("Expected original framework root, migrated framework root, report path, optional --pure-csharp.");
bool pureCSharp = args.Length == 4 && args[3] == "--pure-csharp";
var rows = new List<object>();
var missing = new List<string>();
var authorizedRemovals = new List<string>();
int apiCount = 0, fileCount = 0;
foreach (var source in Directory.EnumerateFiles(args[0], "*.cs", SearchOption.AllDirectories))
{
    var relative = Path.GetRelativePath(args[0], source);
    var target = Path.Combine(args[1], relative);
    var original = Surface(File.ReadAllText(source));
    var migrated = File.Exists(target) ? Surface(File.ReadAllText(target)) : new HashSet<string>();
    var removed = original.Except(migrated).Order().ToArray();
    if (!File.Exists(target)) (pureCSharp && IsRemovedModule(relative) ? authorizedRemovals : missing).Add(relative);
    foreach (var member in removed)
        (pureCSharp && IsAuthorized(relative, member, migrated) ? authorizedRemovals : missing).Add(relative + " :: " + member);
    rows.Add(new { path = relative.Replace('\\', '/'), exists = File.Exists(target), originalApi = original.Count, removed, added = migrated.Except(original).Order().ToArray() });
    apiCount += original.Count; fileCount++;
}
var report = new { utc = DateTime.UtcNow, mode = pureCSharp ? "Pure C# migration; explicit script-runtime exclusions" : "Full original API", original = Path.GetFullPath(args[0]), migrated = Path.GetFullPath(args[1]), sourceFiles = fileCount, originalApiMembers = apiCount, authorizedRemovals, missing, files = rows };
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
File.WriteAllText(args[2], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Original C# files: {fileCount}; API declarations: {apiCount}; missing declarations/files: {missing.Count}");
Console.WriteLine($"Authorized script-runtime removals/files: {authorizedRemovals.Count}");
foreach (var item in missing.Take(50)) Console.WriteLine(item);
return missing.Count == 0 ? 0 : 1;

static bool IsRemovedModule(string path) => path.Replace('\\', '/').StartsWith("Managers/Lua/", StringComparison.Ordinal) ||
    path.Replace('\\', '/').StartsWith("Editor/Dll2LuaLib_v1.4/", StringComparison.Ordinal);

static bool IsAuthorized(string path, string member, HashSet<string> current)
{
    path = path.Replace('\\', '/');
    if (IsRemovedModule(path)) return true;
    // Only the removed final resource category may differ; all earlier enum names/values must match.
    if (path == "Managers/CommonEnum.cs" && member.EndsWith(",xLuaLogic }"))
        return current.Contains(member.Replace(",xLuaLogic }", " }"));
    var expected = new Dictionary<string, string[]> {
        ["GameEntry.cs"] = new[] { "YouYou.GameEntry :: public static LuaManager Lua { get; private set; }" },
        ["Managers/ConstDefine.cs"] = new[] { "YouYou.ConstDefine :: public const string Lua_DataTableLife", "YouYou.ConstDefine :: public const string XLuaAssetBundlePath" },
        ["Managers/Event/SysEventId.cs"] = new[] { "YouYou.SysEventId :: public const ushort LoadLuaDataTableComplete", "YouYou.SysEventId :: public const ushort LuaFullGc" },
        ["YouYouAssetsScript/AboutUs.cs"] = new[] { "AboutUs :: public string LuaManager" },
        ["Editor/YouYouEditor/Menu.cs"] = new[] { "Menu :: public static void CreateLuaView()" },
        ["Editor/YouYouEditor/ShareDataSettings.cs"] = new[] { "ShareDataSettings.ShareData :: public string LuaScriptPath" },
        ["Managers/Socket/SocketManager.cs"] = new[] { "YouYou.SocketManager :: public void SendMainMsgForLua(ushort protoId, byte category, byte[] buffer)" }
    };
    return expected.TryGetValue(path, out var entries) && entries.Contains(member);
}

static HashSet<string> Surface(string source)
{
    var result = new HashSet<string>();
    // Inspect both resource modes and the editor/player conditional surfaces.
    foreach (var symbols in new[] {
        new[] { "UNITY_EDITOR", "UNITY_STANDALONE_WIN", "DEBUG_MODEL", "DEBUG_LOG_ERROR", "DISABLE_ASSETBUNDLE" },
        new[] { "UNITY_EDITOR", "UNITY_STANDALONE_WIN", "DEBUG_MODEL" },
        new[] { "UNITY_STANDALONE_WIN" }, new[] { "UNITY_ANDROID", "DEBUG_MODEL" } })
    {
        var root = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(preprocessorSymbols: symbols)).GetRoot();
        foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            SyntaxTokenList modifiers = member switch {
                BaseTypeDeclarationSyntax t => t.Modifiers, DelegateDeclarationSyntax d => d.Modifiers,
                BaseMethodDeclarationSyntax m => m.Modifiers, BasePropertyDeclarationSyntax p => p.Modifiers,
                BaseFieldDeclarationSyntax f => f.Modifiers, _ => default };
            if (!modifiers.Any(SyntaxKind.PublicKeyword) && !modifiers.Any(SyntaxKind.ProtectedKeyword)) continue;
            string owner = string.Join(".", member.Ancestors().Reverse().Select(n => n switch {
                BaseNamespaceDeclarationSyntax ns => ns.Name.ToString(), TypeDeclarationSyntax t => t.Identifier.Text + (t.TypeParameterList?.ToString() ?? ""),
                EnumDeclarationSyntax e => e.Identifier.Text, _ => null }).Where(s => s != null));
            string? declaration = member switch {
                MethodDeclarationSyntax m => m.WithBody(null).WithExpressionBody(null).WithAttributeLists(default).WithSemicolonToken(default).WithoutTrivia().NormalizeWhitespace().ToFullString(),
                ConstructorDeclarationSyntax c => c.WithBody(null).WithExpressionBody(null).WithInitializer(null).WithAttributeLists(default).WithSemicolonToken(default).WithoutTrivia().NormalizeWhitespace().ToFullString(),
                OperatorDeclarationSyntax o => o.WithBody(null).WithExpressionBody(null).WithAttributeLists(default).WithSemicolonToken(default).WithoutTrivia().NormalizeWhitespace().ToFullString(),
                ConversionOperatorDeclarationSyntax c => c.WithBody(null).WithExpressionBody(null).WithAttributeLists(default).WithSemicolonToken(default).WithoutTrivia().NormalizeWhitespace().ToFullString(),
                PropertyDeclarationSyntax p => $"{p.Modifiers} {p.Type} {p.Identifier} {{ {Accessors(p.AccessorList)} }}",
                EventDeclarationSyntax e => $"{e.Modifiers} event {e.Type} {e.Identifier}",
                BaseFieldDeclarationSyntax f => $"{f.Modifiers} {(f is EventFieldDeclarationSyntax ? "event " : "")}{f.Declaration.Type} {string.Join(",", f.Declaration.Variables.Select(v => v.Identifier.Text))}",
                DelegateDeclarationSyntax d => d.WithAttributeLists(default).WithoutTrivia().NormalizeWhitespace().ToFullString(),
                TypeDeclarationSyntax t => $"{t.Modifiers} {t.Keyword} {t.Identifier}{t.TypeParameterList} {t.BaseList} {t.ConstraintClauses}",
                EnumDeclarationSyntax e => $"{e.Modifiers} enum {e.Identifier} {e.BaseList} {{ {string.Join(",",e.Members.Select(m => m.ToString()))} }}", _ => null };
            if (declaration != null) result.Add(owner + " :: " + string.Join(" ", declaration.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        }
    }
    return result;
}
static string Accessors(AccessorListSyntax? accessors) => accessors == null ? "get;" : string.Join(" ", accessors.Accessors.Select(a => $"{a.Modifiers} {a.Keyword};"));
