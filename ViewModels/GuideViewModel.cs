using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Desktop.Controls.Icons;
using PlumbobForge.Desktop.Services.Guide;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

public partial class GuideViewModel : ObservableObject
{
    private enum Page { Home, Section, Article }

    private readonly GuideService _service = new();
    private readonly Stack<Action> _back = new();
    private Page _page = Page.Home;
    private string? _contextCategory;
    private string? _contextSection;

    public event Action<GuideShowMe>? ShowMeRequested;
    public event Action<string>? AskRequested;
    public event Action? CloseRequested;

    public GuideViewModel()
    {
        Reload();
        LocalizationManager.Instance.LanguageChanged += _ => Reload();
    }

    public ObservableCollection<GuideCategoryItem> Categories { get; } = new();
    public ObservableCollection<GuideArticleItem> Suggestions { get; } = new();
    public ObservableCollection<GuideArticleItem> SectionArticles { get; } = new();
    public ObservableCollection<GuideArticleItem> Results { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _sectionTitle = string.Empty;
    [ObservableProperty] private string _sectionBreadcrumb = string.Empty;
    [ObservableProperty] private GuideArticleView? _article;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHome), nameof(IsSection), nameof(IsResults), nameof(IsArticle), nameof(HasNoResults), nameof(CanGoBack))]
    private int _version; // bumped whenever the visible page changes

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);
    public bool IsResults => IsSearching;
    public bool IsHome => !IsSearching && _page == Page.Home;
    public bool IsSection => !IsSearching && _page == Page.Section;
    public bool IsArticle => !IsSearching && _page == Page.Article;
    public bool HasNoResults => IsSearching && Results.Count == 0;
    public bool HasSuggestions => Suggestions.Count > 0;
    public bool CanGoBack => IsSearching || _page != Page.Home;

    partial void OnSearchTextChanged(string value)
    {
        Results.Clear();
        if (!string.IsNullOrWhiteSpace(value))
        {
            foreach (var r in _service.Search(value))
                Results.Add(Item(r.Article, r.MatchedWords));
        }
        Version++;
    }

    public void SetContext(string? category, string? section)
    {
        _contextCategory = category;
        _contextSection = section;
        RefreshSuggestions();
    }

    private void Reload()
    {
        _service.Load(LocalizationManager.Instance.EffectiveLanguage);

        Categories.Clear();
        foreach (var c in _service.Data.Categories)
        {
            var cat = c;
            Categories.Add(new GuideCategoryItem(
                cat.Title,
                cat.Description,
                Enum.TryParse(cat.Icon, out PackIconBoxIconsKind kind) ? kind : PackIconBoxIconsKind.RegularBook,
                cat.Sections.Where(s => _service.Data.Articles.Any(a => a.Category == cat.Id && a.Section == s.Id))
                    .Select(s => new GuideSectionItem(s.Title, new RelayCommand(() => OpenSection(cat.Id, s.Id, push: true))))
                    .ToList()));
        }

        _back.Clear();
        _page = Page.Home;
        Article = null;
        RefreshSuggestions();
        OnSearchTextChanged(SearchText);
    }

    private void RefreshSuggestions()
    {
        Suggestions.Clear();
        if (_contextCategory != null)
        {
            var articles = _service.Data.Articles.Where(a => a.Category == _contextCategory).ToList();
            var inSection = _contextSection == null ? new List<GuideArticle>() : articles.Where(a => a.Section == _contextSection).ToList();
            // The tab is already the category, so only the section is worth showing.
            foreach (var a in inSection.Concat(articles.Except(inSection)).Take(4))
                Suggestions.Add(Item(a, crumb: SectionNameOf(a)));
        }
        OnPropertyChanged(nameof(HasSuggestions));
    }

    private GuideArticleItem Item(GuideArticle a, IReadOnlyList<string>? matched = null, string? crumb = null) =>
        new(a.Title, a.Summary, crumb ?? Breadcrumb(a),
            matched is { Count: > 0 } ? string.Join(" · ", matched) : null,
            new RelayCommand(() => OpenArticle(a.Id, push: true)));

    private string SectionNameOf(GuideArticle a) =>
        _service.FindCategory(a.Category)?.Sections.FirstOrDefault(s => s.Id == a.Section)?.Title ?? string.Empty;

    private string Breadcrumb(GuideArticle a)
    {
        var cat = _service.FindCategory(a.Category);
        var sec = cat?.Sections.FirstOrDefault(s => s.Id == a.Section);
        return sec != null ? $"{cat!.Title} › {sec.Title}" : cat?.Title ?? string.Empty;
    }

    private void Remember()
    {
        // Back returns to exactly what was on screen, search included.
        var page = _page;
        var search = SearchText;
        var article = Article;
        var sectionTitle = SectionTitle;
        var sectionCrumb = SectionBreadcrumb;
        var sectionArticles = SectionArticles.ToList();
        _back.Push(() =>
        {
            _page = page;
            Article = article;
            SectionTitle = sectionTitle;
            SectionBreadcrumb = sectionCrumb;
            SectionArticles.Clear();
            foreach (var a in sectionArticles) SectionArticles.Add(a);
            SearchText = search;
        });
    }

    private void OpenSection(string categoryId, string sectionId, bool push)
    {
        if (push) Remember();
        var cat = _service.FindCategory(categoryId);
        var sec = cat?.Sections.FirstOrDefault(s => s.Id == sectionId);
        SectionTitle = sec?.Title ?? string.Empty;
        SectionBreadcrumb = cat?.Title ?? string.Empty;
        SectionArticles.Clear();
        // The section's name is already the page title, so no breadcrumb on each row.
        foreach (var a in _service.Data.Articles.Where(a => a.Category == categoryId && a.Section == sectionId))
            SectionArticles.Add(Item(a, crumb: string.Empty));
        _page = Page.Section;
        SearchText = string.Empty;
        Version++;
    }

    private void OpenArticle(string id, bool push)
    {
        var a = _service.Find(id);
        if (a == null) return;
        if (push) Remember();

        Article = new GuideArticleView(
            a.Title,
            a.Summary,
            Breadcrumb(a),
            a.Steps.Select((s, i) => new GuideStepItem(i + 1, s)).ToList(),
            a.Tips,
            a.Related.Select(_service.Find).Where(r => r != null).Select(r => Item(r!)).ToList(),
            a.ShowMe);
        _page = Page.Article;
        SearchText = string.Empty;
        Version++;
    }

    [RelayCommand]
    private void Back()
    {
        if (_back.Count > 0)
        {
            _back.Pop()();
        }
        else
        {
            _page = Page.Home;
            SearchText = string.Empty;
        }
        Version++;
    }

    [RelayCommand]
    private void Home()
    {
        _back.Clear();
        _page = Page.Home;
        Article = null;
        SearchText = string.Empty;
        Version++;
    }

    [RelayCommand]
    private void ShowMe()
    {
        if (Article?.ShowMe is { } target) ShowMeRequested?.Invoke(target);
    }

    [RelayCommand]
    private void Ask() => AskRequested?.Invoke(SearchText.Trim());

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}

public sealed record GuideCategoryItem(string Title, string Description, PackIconBoxIconsKind Icon, IReadOnlyList<GuideSectionItem> Sections);

public sealed record GuideSectionItem(string Title, IRelayCommand OpenCommand);

public sealed record GuideArticleItem(string Title, string Summary, string Breadcrumb, string? Matched, IRelayCommand OpenCommand)
{
    public bool HasMatched => !string.IsNullOrEmpty(Matched);
    public bool HasBreadcrumb => !string.IsNullOrEmpty(Breadcrumb);
    public bool HasFooter => HasMatched || HasBreadcrumb;
}

public sealed record GuideStepItem(int Number, string Text);

public sealed record GuideArticleView(
    string Title,
    string Summary,
    string Breadcrumb,
    IReadOnlyList<GuideStepItem> Steps,
    IReadOnlyList<string> Tips,
    IReadOnlyList<GuideArticleItem> Related,
    GuideShowMe? ShowMe)
{
    public bool HasTips => Tips.Count > 0;
    public bool HasRelated => Related.Count > 0;
    public bool HasShowMe => ShowMe != null;
}
