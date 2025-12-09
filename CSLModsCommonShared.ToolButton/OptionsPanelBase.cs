using CSLModsCommon.Extension;
using CSLModsCommon.Localization;
using CSLModsCommon.ToolButton;
using CSLModsCommon.UI.Buttons;
using CSLModsCommon.UI.Dialogs;
using CSLModsCommon.UI.DropDown;
using CSLModsCommon.Utilities;
using System;
using UnityEngine;

namespace CSLModsCommon.UI.OptionsPanel;

public partial class OptionsPanelBase {
    private static readonly DropDownItem<ToolButtonPresent>[] ToolButtonDropDownItem = {
        new(ToolButtonPresent.None, nameof(SharedTranslations.None), true),
        new(ToolButtonPresent.InGame, nameof(SharedTranslations.OnlyInGame), true),
        new(ToolButtonPresent.UnifiedUI, nameof(SharedTranslations.OnlyInUUI), true),
        new(ToolButtonPresent.Both, nameof(SharedTranslations.Both), true)
    };

    private InGameToolManagerBase _inGameToolManager;

    public InGameToolManagerBase InGameToolManager => _inGameToolManager ??= GetInGameToolManager();

    protected abstract InGameToolManagerBase GetInGameToolManager();

    protected virtual void AddInGameToolButtonSection(Action<ToolButtonPresent> dropDownSelectedCallback) {
        var toolButtonSection = AddSection(_generalPage, SharedTranslations.ToolButton);

        var isUnifiedUIModEnabled = InGameToolManager.IsUnifiedUIModEnabled;
        if (isUnifiedUIModEnabled) {
            ToolButtonDropDownItem[2].IsEnabled = true;
            ToolButtonDropDownItem[3].IsEnabled = true;
        }
        else {
            ToolButtonDropDownItem[2].IsEnabled = false;
            ToolButtonDropDownItem[3].IsEnabled = false;
            ModSettingBase.ToolButtonPresent = (ToolButtonPresent)Mathf.Clamp((int)ModSettingBase.ToolButtonPresent, 0, 1);
        }

        ToolButtonDropDownItem.ForEach(v => v.RefreshLocalization());
        toolButtonSection.AddDropDown(SharedTranslations.ToolButtonDisplay, null, ToolButtonDropDownItem, i => i.Value == ModSettingBase.ToolButtonPresent, v => dropDownSelectedCallback?.Invoke(v.Value), 300);

        toolButtonSection.AddButton(SharedTranslations.ResetToolButtonPosition, null, SharedTranslations.Reset, null, 30, ResetToolButtonPosition);
    }

    private void ResetToolButtonPosition(NormalButton _) => _dialogManager.Show<OkDialog>().AddContent(AssemblyHelper.CurrentAssemblyName, InGameToolManager.SetDefaultPosition() ? SharedTranslations.ToolButtonPositionResetSuccessMessage : SharedTranslations.ToolButtonPositionResetFailureMessage);
}