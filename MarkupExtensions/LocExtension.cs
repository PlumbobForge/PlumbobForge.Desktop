using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.MarkupExtensions;

public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocExtension() { }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay
        };
    }
}
