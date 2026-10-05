using System.Text.Json;

namespace Karolina.Core;

public sealed record ModelProfile(string Model = "", string Effort = "");

public sealed class WorkbenchSettings
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, ModelProfile> ModelProfiles { get; set; } = new(StringComparer.Ordinal);
    public bool AutoVerification { get; set; } = true;
    public int MaxVerificationRepairRounds { get; set; } = 1;

    public static readonly string[] Modes = ["discuss-requirement", "formulate-plan", "execute-task"];

    public WorkbenchSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        ModelProfiles = new(ModelProfiles, StringComparer.Ordinal),
        AutoVerification = AutoVerification,
        MaxVerificationRepairRounds = MaxVerificationRepairRounds
    };
}

public sealed record SettingsRecoveryResult(WorkbenchSettings Settings, string? BackupFile);

/// <summary>Owns project-scoped, non-secret workbench preferences.</summary>
public sealed class WorkbenchSettingsStore(ProjectContext project)
{
    private readonly object gate = new();
    private string PathForSettings => project.StatePath("settings.json");

    public WorkbenchSettings Read()
    {
        lock (gate)
        {
            if (!File.Exists(PathForSettings)) return new();
            var settings = JsonSerializer.Deserialize<WorkbenchSettings>(File.ReadAllText(PathForSettings), DocumentLibrary.Json)
                ?? throw new InvalidDataException("Karolina 设置文件为空或损坏，原文件已保留");
            Validate(settings);
            return settings;
        }
    }

    public WorkbenchSettings Save(WorkbenchSettings settings)
    {
        Validate(settings);
        lock (gate)
        {
            EnsureExistingSettingsReadable();
            WriteAtomic(settings);
            return settings.Clone();
        }
    }

    public SettingsRecoveryResult ResetCorruptWithBackup()
    {
        lock (gate)
        {
            string? backupFile = null;
            if (File.Exists(PathForSettings))
            {
                try
                {
                    var existing = JsonSerializer.Deserialize<WorkbenchSettings>(File.ReadAllText(PathForSettings), DocumentLibrary.Json)
                        ?? throw new InvalidDataException("Karolina 设置文件为空或损坏");
                    Validate(existing);
                    throw new InvalidOperationException("当前设置文件有效，无需恢复");
                }
                catch (Exception failure) when (failure is JsonException or InvalidDataException or ArgumentException)
                {
                    backupFile = Path.GetFileName(PathForSettings) + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N") + ".bak";
                    File.Copy(PathForSettings, Path.Combine(project.StateDirectory, backupFile), false);
                }
            }

            var defaults = new WorkbenchSettings();
            WriteAtomic(defaults);
            return new(defaults.Clone(), backupFile);
        }
    }

    private void EnsureExistingSettingsReadable()
    {
        if (!File.Exists(PathForSettings)) return;
        try
        {
            var existing = JsonSerializer.Deserialize<WorkbenchSettings>(File.ReadAllText(PathForSettings), DocumentLibrary.Json)
                ?? throw new InvalidDataException("Karolina 设置文件为空或损坏");
            Validate(existing);
        }
        catch (Exception failure) when (failure is JsonException or InvalidDataException or ArgumentException)
        {
            throw new InvalidDataException("现有设置无法读取；请先通过恢复操作备份损坏文件，再重置设置", failure);
        }
    }

    private void WriteAtomic(WorkbenchSettings settings)
    {
        string temporary = project.StatePath("settings.json.tmp");
        string text = JsonSerializer.Serialize(settings, DocumentLibrary.Json);
        File.WriteAllText(temporary, text, new System.Text.UTF8Encoding(false));
        File.Move(temporary, PathForSettings, true);
    }

    public static void Validate(WorkbenchSettings settings)
    {
        if (settings.SchemaVersion != 1) throw new InvalidDataException("不支持的 Karolina 设置版本");
        if (settings.MaxVerificationRepairRounds is < 0 or > 3) throw new ArgumentException("Agent 自动修复轮数必须为 0–3");
        if (settings.ModelProfiles is null || settings.ModelProfiles.Count > WorkbenchSettings.Modes.Length)
            throw new ArgumentException("模型预设数量无效");
        foreach (var (mode, profile) in settings.ModelProfiles)
        {
            if (!WorkbenchSettings.Modes.Contains(mode, StringComparer.Ordinal) || profile is null ||
                profile.Model is null || profile.Effort is null || profile.Model.Length > 200 || profile.Effort.Length > 40 ||
                profile.Model.Any(char.IsControl) || profile.Effort.Any(char.IsControl))
                throw new ArgumentException("模型预设内容无效");
        }
    }
}
