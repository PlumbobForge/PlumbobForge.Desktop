using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using PlumbobForge.Desktop.Controls.Icons;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

public partial class NavTabViewModel : ObservableObject
{
    private readonly string _titleKey;
    private readonly Func<bool> _activeWhen;

    public NavTabViewModel(string key, string titleKey, PackIconBoxIconsKind icon, ICommand command, Func<bool> isActive, bool isPinned = false)
    {
        Key = key;
        _titleKey = titleKey;
        Icon = icon;
        Command = command;
        _activeWhen = isActive;
        IsPinned = isPinned;
        _title = LocalizationManager.Instance.GetString(titleKey);
        LocalizationManager.Instance.LanguageChanged += _ => Title = LocalizationManager.Instance.GetString(_titleKey);
    }

    // Stable identifier, used to save and restore pins.
    public string Key { get; }

    public PackIconBoxIconsKind Icon { get; }

    public ICommand Command { get; }

    // Built-in tabs can't be removed; pinned ones can.
    public bool IsPinned { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isActive;

    public void RefreshIsActive() => IsActive = _activeWhen();
}
