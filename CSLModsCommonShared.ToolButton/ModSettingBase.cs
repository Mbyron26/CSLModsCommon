using CSLModsCommon.ToolButton;

namespace CSLModsCommon.Setting;

public abstract partial class ModSettingBase {
    public ToolButtonPresent ToolButtonPresent { get; set; } = ToolButtonPresent.UnifiedUI;
    public float ToolButtonPositionX { get; set; }
    public float ToolButtonPositionY { get; set; }
}