using ColossalFramework.UI;
using CSLModsCommon.Logging;
using CSLModsCommon.Manager;
using CSLModsCommon.Setting;
using CSLModsCommon.UI;
using CSLModsCommon.UI.Atlas;
using CSLModsCommon.UI.Buttons;
using UnityEngine;

namespace CSLModsCommon.ToolButton;

public abstract class ToolButtonBase : ToggleButton {
    protected ILog _logger;
    protected SettingManager _settingManager;
    protected ModSettingBase _defaultSetting;

    protected abstract UITextureAtlas ButtonAtlas { get; }
    protected abstract string ButtonSpriteName { get; }
    protected Vector2 ScreenFixedSize => new(UIView.GetAView().fixedWidth, UIView.GetAView().fixedHeight);

    public override void Start() {
        base.Start();
        _logger = LogManager.GetLogger();
        size = new Vector2(40, 40);
        _bgAtlas = Atlases.Shared;
        _offVisuals.BgSprites.SetValues(SharedAtlasKeys.Circle);
        _offVisuals.BgColors.SetValues(new Color32(0, 0, 0, 0), new Color32(50, 50, 50, 100), new Color32(70, 70, 70, 100), new Color32(70, 70, 70, 100), new Color32(0, 0, 0, 0));
        OnVisuals.BgSprites.SetValues(SharedAtlasKeys.Circle);
        OnVisuals.BgColors.SetValues(UIColors.GreenFocused);
        OnVisuals.BgColors.HoveredColor = UIColors.GreenHovered;
        _settingManager = Domain.DefaultDomain.GetOrCreateManager<SettingManager>();
        _defaultSetting = _settingManager.GetDefaultSetting();
        _fgAtlas = ButtonAtlas;
        _offVisuals.FgSprites.SetValues(ButtonSpriteName);
        _onVisuals.FgSprites.SetValues(ButtonSpriteName);
        _renderFg = true;
        SetRelativePosition();
    }

    public virtual void SetDefaultPosition() {
        relativePosition = GetDefaultPosition();
        SavePosition(relativePosition);
    }

    private void SetRelativePosition() {
        var settingPosition = GetSettingPosition();
        var defaultPosition = GetDefaultPosition();
        _logger.Verbose($"{GetType().Name}.SetRelativePosition, screen fixed size: {ScreenFixedSize}, default position: {defaultPosition}, settings position: {settingPosition}");
        if (settingPosition.Equals(Vector2.zero)) {
            relativePosition = defaultPosition;
            _logger.Debug($"Setting position is zero, set default position: {defaultPosition}");
        }
        else {
            if (settingPosition.x > ScreenFixedSize.x || settingPosition.y > ScreenFixedSize.y) {
                relativePosition = defaultPosition;
                _logger.Info($"Setting position is too large, set default position: {defaultPosition}");
            }
            else {
                relativePosition = settingPosition;
                _logger.Debug($"Set setting position: {settingPosition}");
            }
        }

        SavePosition(relativePosition);
    }

    protected override void OnMouseMove(UIMouseEventParameter p) {
        base.OnMouseMove(p);
        if (!p.buttons.IsFlagSet(UIMouseButton.Right)) return;
        var ratio = UIView.GetAView().ratio;
        position = new Vector3(position.x + p.moveDelta.x * ratio, position.y + p.moveDelta.y * ratio, position.z);
    }

    protected override void OnMouseUp(UIMouseEventParameter p) {
        base.OnMouseUp(p);
        if (p.buttons.IsFlagSet(UIMouseButton.Right))
            SavePosition(relativePosition.x, relativePosition.y);
    }

    protected override void OnClick(UIMouseEventParameter p) {
        if (!p.used)
            IsOn = !IsOn;

        base.OnClick(p);
    }

    protected virtual Vector2 GetDefaultPosition() => Vector2.zero;

    private Vector2 GetSettingPosition() => new Vector2(_defaultSetting.ToolButtonPositionX, _defaultSetting.ToolButtonPositionY);

    private void SavePosition(float x, float y) {
        _defaultSetting.ToolButtonPositionX = x;
        _defaultSetting.ToolButtonPositionY = y;
        _settingManager.SaveDefaultSetting();
    }

    private void SavePosition(Vector2 vector2) {
        _defaultSetting.ToolButtonPositionX = vector2.x;
        _defaultSetting.ToolButtonPositionY = vector2.y;
        _settingManager.SaveDefaultSetting();
    }
}