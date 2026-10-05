using System.Text;
using System.Text.Json;

namespace Karolina.Core;

/// <summary>工程路径、持久身份和本机存储的唯一入口；不依赖当前工作目录或浏览器端口。</summary>
public sealed class ProjectContext
{
    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public string Root { get; }
    public string Key { get; }
    public string LocalRoot { get; }
    public string StateDirectory { get; }
    public string StatePath(string relative)
    {
        if(Path.IsPathRooted(relative))throw new ArgumentException("状态路径必须相对工程状态目录");
        string target=Path.GetFullPath(Path.Combine(StateDirectory,relative));
        if(!target.StartsWith(StateDirectory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("状态路径越界");
        for(string? node=target;node!=null&&!node.Equals(Root,StringComparison.OrdinalIgnoreCase);node=Path.GetDirectoryName(node))
            if((File.Exists(node)||Directory.Exists(node))&&(File.GetAttributes(node)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("工程状态不能使用链接："+node);
        return target;
    }
    public ProjectContext(string project, bool requireUnity = false)
    {
        Root = Normalize(project);
        if (!Directory.Exists(Root)) throw new ArgumentException("请选择现有工程目录");
        if (requireUnity && (!Directory.Exists(Path.Combine(Root,"Assets")) || !Directory.Exists(Path.Combine(Root,"ProjectSettings")))) throw new ArgumentException("请选择包含 Assets 和 ProjectSettings 的 Unity 工程根目录");
        Key = RequirementStore.Hash(Encoding.UTF8.GetBytes(Root.ToUpperInvariant()));
        LocalRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Karolina");
        StateDirectory = Path.Combine(Root,".karolina","state");
        foreach(string part in new[]{Path.Combine(Root,".karolina"),StateDirectory}) if(Directory.Exists(part)&&(File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("工程状态目录不能是链接："+part);
        Directory.CreateDirectory(StateDirectory);
    }
    public void RecoverLegacyIdentity()
    {
        string identity=StatePath("project.json");
        if(File.Exists(identity))
        {
            using var manifest=JsonDocument.Parse(File.ReadAllText(identity));
            if(!Normalize(manifest.RootElement.GetProperty("root").GetString()!).Equals(Root,StringComparison.OrdinalIgnoreCase)||manifest.RootElement.GetProperty("key").GetString()!=Key)throw new InvalidDataException("工程状态来源不匹配，原记录保留："+identity);
            return;
        }
        string conflictPath=StatePath("recovery-conflicts.json");
        if(File.Exists(conflictPath))throw new InvalidDataException("旧工程状态存在未处理的同名冲突，原记录保留："+conflictPath);
        var existing=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stateFolders=new Stack<string>();stateFolders.Push(StateDirectory);
        while(stateFolders.TryPop(out string? folder))
        {
            foreach(string child in Directory.EnumerateDirectories(folder)){StatePath(Path.GetRelativePath(StateDirectory,child));stateFolders.Push(child);}
            foreach(string file in Directory.EnumerateFiles(folder)){StatePath(Path.GetRelativePath(StateDirectory,file));existing.Add(Path.GetRelativePath(StateDirectory,file));}
        }
        var copied=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        // 只迁移由同一个规范根路径推导出的旧键，保留旧目录；同名冲突不覆盖人工记录。
        string[] legacyKeys = [Key,RequirementStore.Hash(Encoding.UTF8.GetBytes((Root+Path.DirectorySeparatorChar).ToUpperInvariant())), RequirementStore.Hash(Encoding.UTF8.GetBytes((Root+Path.AltDirectorySeparatorChar).ToUpperInvariant()))];
        foreach (string legacyKey in legacyKeys.Distinct())
        {
            string source=Path.Combine(LocalRoot,"projects",legacyKey); if(!Directory.Exists(source))continue;
            var folders=new Stack<string>();folders.Push(source);
            while(folders.TryPop(out string? folder))
            {
                if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("旧工程状态含链接："+folder);
                foreach(string child in Directory.EnumerateDirectories(folder))folders.Push(child);
                foreach(string file in Directory.EnumerateFiles(folder))
                {
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("旧工程状态含链接："+file);
                string relative=Path.GetRelativePath(source,file);
                if(relative.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase)||relative=="project.json")continue;
                string destination=StatePath(relative);
                if(File.Exists(destination))
                {
                    if(!existing.Contains(relative)&&!File.ReadAllBytes(file).AsSpan().SequenceEqual(File.ReadAllBytes(destination)))
                    {File.WriteAllText(conflictPath,JsonSerializer.Serialize(new{first=copied.GetValueOrDefault(relative),second=file,destination},DocumentLibrary.Json));throw new InvalidDataException("旧状态同名内容冲突，原记录均保留："+conflictPath);}
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                // 本机旧文件可能带命令环境的 EFS 加密属性；读内容再原子写入，避免复制密钥/加密属性。
                string temp=StatePath(relative+".recover.tmp");File.WriteAllBytes(temp,File.ReadAllBytes(file));File.Move(temp,destination,false);copied[relative]=file;
                }
            }
        }
        string identityTemp=StatePath("project.json.tmp");
        File.WriteAllText(identityTemp,JsonSerializer.Serialize(new { root=Root,key=Key },DocumentLibrary.Json));File.Move(identityTemp,identity,false);
    }
}
public sealed record WorkspacePreferences(string Mode="discuss-requirement");
public sealed class WorkspacePreferencesStore(ProjectContext project)
{
    private readonly object gate=new();
    private string PathForState => project.StatePath("preferences.json");
    private string LegacyViewPath => project.StatePath("view.json");
    public WorkspacePreferences Read()
    {
        lock(gate)
        {
            var preferences=File.Exists(PathForState)?JsonSerializer.Deserialize<WorkspacePreferences>(File.ReadAllText(PathForState),DocumentLibrary.Json)??new():File.Exists(LegacyViewPath)?JsonSerializer.Deserialize<WorkspacePreferences>(File.ReadAllText(LegacyViewPath),DocumentLibrary.Json)??new():new();
            if(!File.Exists(PathForState) && File.Exists(LegacyViewPath))Save(preferences);
            if(File.Exists(LegacyViewPath))File.Delete(LegacyViewPath);
            return preferences;
        }
    }
    public void Save(WorkspacePreferences preferences)
    {
        if(preferences.Mode is not ("discuss-requirement" or "formulate-plan" or "execute-task"))throw new ArgumentException("无效工作模式");
        lock(gate){string temp=project.StatePath("preferences.json.tmp");File.WriteAllText(temp,JsonSerializer.Serialize(preferences,DocumentLibrary.Json));File.Move(temp,PathForState,true);}
    }
}
