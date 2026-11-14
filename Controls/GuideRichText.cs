using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace PlumbobForge.Desktop.Controls;

public class GuideRichText : TextBlock
{
    public static readonly StyledProperty<string?> MarkupProperty =
        AvaloniaProperty.Register<GuideRichText, string?>(nameof(Markup));

    public string? Markup
    {
        get => GetValue(MarkupProperty);
        set => SetValue(MarkupProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkupProperty) Rebuild();
    }

    private void Rebuild()
    {
        var inlines = new InlineCollection();
        var parts = (Markup ?? string.Empty).Split("**");
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            var run = new Run(parts[i]);
            if (i % 2 == 1)
            {
                run.FontWeight = FontWeight.SemiBold;
                run.Foreground = (IBrush?)this.FindResource("TextMain") ?? Foreground;
            }
            inlines.Add(run);
        }
        Inlines = inlines;
    }
}
