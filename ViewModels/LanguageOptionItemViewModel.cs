using CommunityToolkit.Mvvm.ComponentModel;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

public partial class LanguageOptionItemViewModel : ObservableObject
{
    public LanguageOption Language { get; }

    public string Code => Language.Code;
    public string DisplayName => Language.DisplayName;
    public string NativeName => Language.NativeName;

    public LanguageOptionItemViewModel(LanguageOption language)
    {
        Language = language;
    }
}
