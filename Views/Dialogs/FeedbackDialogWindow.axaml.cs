using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class FeedbackDialogWindow : Window
{
    private readonly IReadOnlyList<string> _gameLogs = Array.Empty<string>();
    private FeedbackCategory _category = FeedbackCategory.BugReport;
    private bool _sending;
    private bool _sent;

    public FeedbackDialogWindow()
    {
        InitializeComponent();
        MessageTextBox.MaxLength = FeedbackService.MaxMessageLength;
        UpdateCategoryButtons();
    }

    public FeedbackDialogWindow(IReadOnlyList<string> gameLogs) : this()
    {
        _gameLogs = gameLogs;
        if (_gameLogs.Count > 0)
        {
            IncludeGameLogsCheckBox.IsVisible = true;
            bool hasCfg = _gameLogs.Any(FeedbackService.IsResourceCfg);
            bool hasLogs = _gameLogs.Any(f => !FeedbackService.IsResourceCfg(f));
            IncludeGameLogsCheckBox.Content = L(hasCfg && hasLogs ? "dialogs.feedback.include_game_logs"
                : hasCfg ? "dialogs.feedback.include_resource_cfg"
                : "dialogs.feedback.include_game_logs_only");
        }
        Opened += (_, _) => MessageTextBox.Focus();
    }

    private static string L(string key) => LocalizationManager.Instance.GetString(key);

    private void OnHeaderPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } && Enum.TryParse(tag, out FeedbackCategory category))
            _category = category;
        // Single choice: re-sync every pill, including re-checking one that was clicked off.
        UpdateCategoryButtons();
    }

    private void UpdateCategoryButtons()
    {
        foreach (var button in CategoryPanel.Children.OfType<ToggleButton>())
            button.IsChecked = button.Tag as string == _category.ToString();
    }

    private void OnMessageChanged(object? sender, TextChangedEventArgs e) => UpdateSendEnabled();

    private void OnAttachmentOptionChanged(object? sender, RoutedEventArgs e) => UpdateViewFilesVisibility();

    private void UpdateSendEnabled()
    {
        SendButton.IsEnabled = !_sending && (_sent || !string.IsNullOrWhiteSpace(MessageTextBox.Text));
    }

    private void UpdateViewFilesVisibility()
    {
        ViewFilesButton.IsVisible = !_sent && BuildReport().HasAttachments();
    }

    private FeedbackReport BuildReport() => new(
        _category,
        MessageTextBox.Text ?? string.Empty,
        ContactTextBox.Text,
        IncludeAppLogsCheckBox.IsChecked == true,
        IncludeGameLogsCheckBox.IsVisible && IncludeGameLogsCheckBox.IsChecked == true ? _gameLogs : Array.Empty<string>());

    private void OnViewFilesClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "PlumbobForge-feedback-preview");
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            if (FeedbackService.WriteAttachments(BuildReport(), folder) == 0)
            {
                ShowStatus(L("dialogs.feedback.no_files"), isError: false);
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Feedback preview failed: {ex.Message}", ex, "Feedback");
            ShowStatus(ex.Message, isError: true);
        }
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e) => Close(true);

    /// <summary>Swaps the form for the thank-you view, keeping the window the same size.</summary>
    public void ShowSent(FeedbackReport report)
    {
        _sent = true;

        SentPanel.MinHeight = FormPanel.Bounds.Height + FormPanel.Margin.Top + FormPanel.Margin.Bottom;
        FormPanel.IsVisible = false;
        SentPanel.IsVisible = true;

        SentTitleTextBlock.Text = L(report.Category switch
        {
            FeedbackCategory.BugReport => "dialogs.feedback.sent_title_bug",
            FeedbackCategory.FeatureRequest => "dialogs.feedback.sent_title_feature",
            _ => "dialogs.feedback.sent_title"
        });
        SentMessageTextBlock.Text = L(string.IsNullOrWhiteSpace(report.Contact)
            ? "dialogs.feedback.sent_message"
            : "dialogs.feedback.sent_message_contact");

        ViewFilesButton.IsVisible = false;
        CancelButton.IsVisible = false;
        SendButton.IsVisible = false;
        DoneButton.IsVisible = true;
        DoneButton.Focus();
    }

    private async void OnSendClick(object? sender, RoutedEventArgs e)
    {
        if (_sent || string.IsNullOrWhiteSpace(MessageTextBox.Text)) return;

        _sending = true;
        UpdateSendEnabled();
        FormPanel.IsEnabled = false;
        SendButtonText.Text = L("dialogs.feedback.sending");
        StatusTextBlock.IsVisible = false;

        try
        {
            var report = BuildReport();
            await Task.Run(() => FeedbackService.SendAsync(report));
            AppLogger.LogInfo($"Feedback sent ({report.Category}).", "Feedback");

            FormPanel.IsEnabled = true;
            ShowSent(report);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Feedback send failed: {ex.Message}", ex, "Feedback");
            ShowStatus(LocalizationManager.Instance.GetString("dialogs.feedback.failed", ex.Message), isError: true);
            SendButtonText.Text = L("dialogs.feedback.send");
            FormPanel.IsEnabled = true;
        }
        finally
        {
            _sending = false;
            UpdateSendEnabled();
        }
    }

    private void ShowStatus(string text, bool isError)
    {
        StatusTextBlock.Text = text;
        StatusTextBlock.Foreground = (IBrush?)this.FindResource(isError ? "StatusRed" : "AccentBrush") ?? Brushes.Gray;
        StatusTextBlock.IsVisible = true;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (!_sending) Close(_sent);
    }
}

internal static class FeedbackReportExtensions
{
    public static bool HasAttachments(this FeedbackReport report) => report.IncludeAppLogs || report.GameLogFiles.Count > 0;
}
