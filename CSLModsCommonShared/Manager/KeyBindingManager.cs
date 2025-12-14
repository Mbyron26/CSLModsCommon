using CSLModsCommon.Collections;
using CSLModsCommon.KeyBindings;
using CSLModsCommon.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KeyBinding = CSLModsCommon.KeyBindings.KeyBinding;
using Object = UnityEngine.Object;

namespace CSLModsCommon.Manager; 
public class KeyBindingManager : ManagerBase {
    public event Action<List<KeyBindingEntry>> BindingsChanged;

    private readonly List<KeyBindingEntry> _entries = new();
    private KeyBindingDispatcher _dispatcher;

    protected override void OnGameLoaded(LoadContext context) {
        base.OnGameLoaded(context);
        OnBindingsChanged();
    }

    public bool Register(string name, KeyBinding binding, Action action, KeyBindingContext context = KeyBindingContext.Global, bool overwrite = false) {
        if (!overwrite && _entries.Any(e => e.Name == name))
            return false;

        _entries.RemoveAll(e => e.Name == name);
        _entries.Add(new KeyBindingEntry(name, binding, action, context));
        RecalculateConflicts();
        EnsureDispatcherExists();
        OnBindingsChanged();
        Logger.Debug($"KeyBinding '{name}' registered with binding '{binding}' in context '{context}'");
        return true;
    }

    public void Unregister(string name) {
        _entries.RemoveAll(e => e.Name == name);
        RecalculateConflicts();
        if (_entries.Count == 0) DestroyDispatcher();
        OnBindingsChanged();
    }

    public List<KeyBindingEntry> GetActiveBindings() {
        var result = new List<KeyBindingEntry>();
        switch (CurrentMode) {
            case GameMode.MainMenu:
                result.AddRange(_entries.Where(e => e.Context is KeyBindingContext.MainMenu or KeyBindingContext.Global));
                break;
            case GameMode.Game:
                result.AddRange(_entries.Where(e => e.Context is KeyBindingContext.InGame or KeyBindingContext.Global));
                break;
        }

        return result;
    }

    public bool HasConflict(KeyBinding binding, KeyBindingContext context) => _entries.Any(e => e.Binding.ConflictWith(binding) && e.Context == context);

    private void RecalculateConflicts() {
        using var map = ReusableDictionary<string, List<KeyBindingEntry>>.Rent();
        foreach (var entry in _entries) {
            var key = entry.Binding + "|" + entry.Context;
            if (!map.ContainsKey(key))
                map[key] = new List<KeyBindingEntry>();
            map[key].Add(entry);
        }

        using var conflicted = ReusableList<KeyBindingEntry>.Rent();
        foreach (var group in map.Values) {
            var hasConflict = group.Count > 1;
            foreach (var entry in group) {
                entry.HasConflict = hasConflict;
                if (hasConflict) conflicted.Add(entry);
            }
        }

        if (conflicted.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine($"KeyBinding encountered {conflicted.Count} conflicted bindings:");
        foreach (var entry in conflicted) sb.AppendLine($"KeyBinding conflict：Name='{entry.Name}', Binding='{entry.Binding}', Context='{entry.Context}'");

        Logger.Warn(sb.ToString());
    }

    private void EnsureDispatcherExists() {
        if (_dispatcher != null) return;
        var go = new GameObject($"{AssemblyHelper.CurrentAssemblyName}KeyBindingDispatcher");
        _dispatcher = go.AddComponent<KeyBindingDispatcher>();
        Object.DontDestroyOnLoad(go);
    }

    private void DestroyDispatcher() {
        if (_dispatcher == null) return;
        Object.Destroy(_dispatcher.gameObject);
        _dispatcher = null;
    }

    private void OnBindingsChanged() => BindingsChanged?.Invoke(GetActiveBindings());
}