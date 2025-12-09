using CSLModsCommon.Manager;
using System;
using System.IO;

namespace CSLModsCommon.Setting; 
public abstract partial class ModSettingBase : IModSetting {
    public static string DefaultFilePath { get; } = Path.Combine(FileLocationAttribute.DefaultDirectory, "GlobalSetting.json");
    public Version ModVersion { get; set; } = new();
    public string LocaleId { get; set; } = LocalizationManager.UseGameLanguage;

    public virtual void SetDefaults() {
        ModVersion = new Version();
        LocaleId = LocalizationManager.UseGameLanguage;
    }
}