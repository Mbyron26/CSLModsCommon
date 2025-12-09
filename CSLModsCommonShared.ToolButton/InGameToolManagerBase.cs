using ColossalFramework.UI;
using CSLModsCommon.KeyBindings;
using CSLModsCommon.Manager;
using CSLModsCommon.Setting;
using CSLModsCommon.UI.Buttons;
using CSLModsCommon.UI.Utilities;
using CSLModsCommon.Utilities;
using ICities;
using System;
using System.Collections.Generic;
using System.Linq;
using UnifiedUI.Helpers;
using UnityEngine;

namespace CSLModsCommon.ToolButton;

public abstract class InGameToolManagerBase : ManagerBase {
    private const string InGameButton = nameof(InGameButton);
    private const string PanelClose = nameof(PanelClose);
    private const string EnsurePanelOpen = nameof(EnsurePanelOpen);
    private const string ReloadPanelIfOpen = nameof(ReloadPanelIfOpen);
    private const string ForcePanelOpen = nameof(ForcePanelOpen);

    protected SettingManager _settingManager;
    protected ModSettingBase _defaultSetting;
    protected bool _panelTargetState;
    private readonly Dictionary<string, Action<bool>> _buttonSetters = new();
    private ModSearcher _unifiedUIModSearcher;
    private Texture2D _unifiedUIIcon;
    private string _currentTrigger;
    private PanelManagerBase _panelManager;

    protected abstract KeyBinding ToggleKeyBinding { get; }

    protected virtual string Tooltip => ToggleKeyBinding is null ? string.Empty : $"{Domain.GetManager<ModManagerBase>().ModName} ({ToggleKeyBinding})";
    public ModSearcher UnifiedUIModSearcher => _unifiedUIModSearcher ??= new ModSearcher("UnifiedUIMod", "UnifiedUI");
    public bool IsUnifiedUIModEnabled => UnifiedUIModSearcher.Search().IsEnabled;
    public ToolButtonBase InGameToolButton { get; protected set; }
    public bool IsUnifiedUIRegistered { get; protected set; }

    public bool IsUnifiedUIButtonOn {
        get => UnifiedUIButton?.IsPressed ?? false;
        set {
            if (UnifiedUIButton is not null) UnifiedUIButton.IsPressed = value;
        }
    }

    public bool IsInGameButtonOn {
        get => InGameToolButton?.IsOn ?? false;
        set {
            if (InGameToolButton is not null) InGameToolButton.IsOn = value;
        }
    }

    protected Texture2D UnifiedUIIcon => _unifiedUIIcon ??= GetIconTexture();
    protected UUICustomButton UnifiedUIButton { get; set; }

    protected abstract Type GetToolButtonType();
    protected abstract PanelManagerBase CreatePanelManager();

    protected override void OnCreate() {
        base.OnCreate();
        _settingManager = Domain.GetOrCreateManager<SettingManager>();
        _defaultSetting = _settingManager.GetDefaultSetting();
        _panelManager = CreatePanelManager();
    }

    protected override void OnGameLoaded(LoadContext loadContext) {
        base.OnGameLoaded(loadContext);
        if (loadContext.LoadMode is not (LoadMode.NewGame or LoadMode.LoadGame or LoadMode.NewMap or LoadMode.LoadMap or LoadMode.NewAsset or LoadMode.LoadAsset)) return;
        Enable();
    }

    protected override void OnGameUnloaded() {
        base.OnGameUnloaded();
        Disable();
    }

    protected virtual Texture2D GetIconTexture() => TextureLoader.FromAssemblySafe($"{AssemblyHelper.CurrentAssemblyName}.UI.Resources.InGameButton.png");

    protected virtual void Enable() {
        EnsurePresent();
        if (IsUnifiedUIModEnabled) {
            switch (_defaultSetting.ToolButtonPresent) {
                case ToolButtonPresent.InGame:
                    AddInGameToolButton();
                    break;
                case ToolButtonPresent.UnifiedUI:
                    AddUnifiedUIButton();
                    break;
                case ToolButtonPresent.Both:
                    AddInGameToolButton();
                    AddUnifiedUIButton();
                    break;
            }
        }
        else {
            if (_defaultSetting.ToolButtonPresent == ToolButtonPresent.InGame)
                AddInGameToolButton();
        }

        RegisterTriggerSource(nameof(KeyBinding), _ => { });
        RegisterTriggerSource(PanelClose, _ => { });
        RegisterTriggerSource(EnsurePanelOpen);
        RegisterTriggerSource(ReloadPanelIfOpen);
        RegisterTriggerSource(ForcePanelOpen);
        _panelTargetState = false;
        UpdateButtonsState();
    }

    protected virtual void Disable() {
        RemoveUnifiedUIButton();
        RemoveInGameToolButton();

        UnregisterTriggerSource(nameof(KeyBinding));
        UnregisterTriggerSource(PanelClose);
        UnregisterTriggerSource(EnsurePanelOpen);
        UnregisterTriggerSource(ReloadPanelIfOpen);
        UnregisterTriggerSource(ForcePanelOpen);
    }

    protected virtual void EnsurePresent() {
        if (IsUnifiedUIModEnabled || (_defaultSetting.ToolButtonPresent != ToolButtonPresent.UnifiedUI && _defaultSetting.ToolButtonPresent != ToolButtonPresent.Both)) return;
        _defaultSetting.ToolButtonPresent = ToolButtonPresent.InGame;
        Logger.Verbose($"{ManagerType.Name}.EnsurePresent: ensure tool button present only in game");
        _settingManager.SaveDefaultSetting();
    }

    protected virtual void AddInGameToolButton() {
        InGameToolButton = (ToolButtonBase)UIView.GetAView().AddUIComponent(GetToolButtonType());
        if (InGameToolButton == null) return;
        InGameToolButton.tooltipBox = UIView.GetAView()?.defaultTooltipBox;
        InGameToolButton.tooltip = Tooltip;
        InGameToolButton.ToggleChanged += OnInGameButtonToggle;
        RegisterTriggerSource(InGameButton, val => IsInGameButtonOn = val);
        _panelTargetState = false;
        Logger.Debug("Added In game tool button");
    }

    protected virtual void RemoveInGameToolButton() {
        if (InGameToolButton is null) return;
        InGameToolButton.ToggleChanged -= OnInGameButtonToggle;
        UnregisterTriggerSource(InGameButton);
        InGameToolButton.Destroy();
        InGameToolButton = null;
        Logger.Debug("Removed In game tool button");
    }

    public bool SetDefaultPosition() {
        if (InGameToolButton is null) return false;
        InGameToolButton.SetDefaultPosition();
        return true;
    }

    public void OnButtonStatuesChanged(ToolButtonPresent value) {
        _defaultSetting.ToolButtonPresent = value;
        if (GameLoading is null) return;
        Disable();
        Enable();
    }

    public void OnForcePanelOpen() => TogglePanelFromSource(ForcePanelOpen, true, () => _panelManager?.ForcePanelOpen());

    public void OnReloadPanelIfOpen() => TogglePanelFromSource(ReloadPanelIfOpen, _panelManager.IsVisible, () => _panelManager?.ReloadPanelIfOpen());

    public void OnEnsurePanelOpen() => TogglePanelFromSource(EnsurePanelOpen, true, () => _panelManager?.EnsurePanelOpen());

    public void OnPanelClosed() {
        TogglePanelFromSource(PanelClose, false, () => _panelManager?.DestroyPanel());
        Logger.Debug("Panel closed, all registered buttons turned off");
    }

    public void OnKeyBindingToggle() => TogglePanelFromSource(nameof(KeyBinding), !_panelTargetState);

    protected void RegisterTriggerSource(string sourceName, Action<bool> setButtonState = null) => _buttonSetters[sourceName] = setButtonState;

    protected void UnregisterTriggerSource(string sourceName) => _buttonSetters.Remove(sourceName);

    protected void TogglePanelFromSource(string sourceName, bool newState, Action action = null) {
        if (_currentTrigger != null) return;

        Logger.Debug($"TogglePanelFromSource: {sourceName}, newState: {newState}");
        _currentTrigger = sourceName;
        _panelTargetState = newState;

        if (action is not null)
            action();
        else
            _panelManager?.TogglePanel();

        UpdateButtonsState();

        _currentTrigger = null;
    }

    protected void UpdateButtonsState() {
        foreach (var kv in _buttonSetters.ToList())
            if (kv.Key != _currentTrigger)
                kv.Value?.Invoke(_panelTargetState);
    }

    private void OnInGameButtonToggle(ToggleButton element, bool isOn) => TogglePanelFromSource(InGameButton, isOn);

    private void OnUUIButtonToggle(bool isOn) => TogglePanelFromSource(nameof(UnifiedUIButton), isOn);

    private void AddUnifiedUIButton() {
        if (!IsUnifiedUIModEnabled || IsUnifiedUIRegistered) return;
        UnifiedUIButton = UUIHelpers.RegisterCustomButton(AssemblyHelper.CurrentAssemblyName, null, Tooltip, UnifiedUIIcon, OnUUIButtonToggle);
        RegisterTriggerSource(nameof(UnifiedUIButton), val => IsUnifiedUIButtonOn = val);
        UnifiedUIButton.Button.eventTooltipEnter += (c, _) => c.tooltip = Tooltip;
        UnifiedUIButton.IsPressed = false;
        _panelTargetState = false;
        UpdateButtonsState();
        IsUnifiedUIRegistered = true;
        Logger.Debug("Added UnifiedUI button");
    }

    private void RemoveUnifiedUIButton() {
        if (!IsUnifiedUIRegistered || !IsUnifiedUIModEnabled || UnifiedUIButton is null) return;
        Logger.Info("UnifiedUI detected, unregistering UUI");
        UnregisterTriggerSource(nameof(UnifiedUIButton));
        UnifiedUIButton.Button.Destroy();
        UnifiedUIButton = null;
        IsUnifiedUIRegistered = false;
        Logger.Debug("Removed UnifiedUI button");
    }
}