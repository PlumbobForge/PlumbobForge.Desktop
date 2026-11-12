using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PlumbobForge.Desktop.Controls.Icons;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Database;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.ViewModels;

public enum CommandNoun
{
    Set,
    Configuration,
    Item,
    Collection
}


public partial class QuickSwitcherViewModel
{
    private sealed record CommandVerb(
        string Name,
        string[] Aliases,
        string Description,
        PackIconBoxIconsKind Icon,
        CommandNoun[] Nouns,
        string? DestinationWord = null);

    private sealed record CommandTarget(
        CommandNoun Noun,
        long Id,
        long? SecondaryId,
        string Title,
        string? Subtitle,
        PackIconBoxIconsKind Icon,
        IBrush Brush);

    private static readonly CommandNoun[] AllNouns = { CommandNoun.Set, CommandNoun.Configuration, CommandNoun.Item, CommandNoun.Collection };

    private static readonly (CommandNoun Noun, string Name, string[] Aliases)[] NounNames =
    {
        (CommandNoun.Set, "set", new[] { "sets", "folder", "s" }),
        (CommandNoun.Configuration, "config", new[] { "configuration", "configs", "cfg", "profile", "c" }),
        (CommandNoun.Item, "item", new[] { "items", "mod", "mods", "cc", "package", "file", "i" }),
        (CommandNoun.Collection, "collection", new[] { "collections", "coll", "col" }),
    };

    private static readonly CommandVerb[] Verbs =
    {
        new("open", new[] { "go", "goto", "jump", "show", "find" }, "Jump to a set, configuration, item or collection", PackIconBoxIconsKind.RegularArrowRight, AllNouns),
        new("edit", new[] { "customize", "modify", "change", "e" }, "Customize a set or configuration, retag an item, or edit a collection", PackIconBoxIconsKind.RegularEdit, AllNouns),
        new("rename", new[] { "rn" }, "Rename a set, configuration, item or collection", PackIconBoxIconsKind.RegularRename, AllNouns),
        new("new", new[] { "create", "add", "make", "mk" }, "Create a set, configuration or collection", PackIconBoxIconsKind.RegularPlusCircle, new[] { CommandNoun.Set, CommandNoun.Configuration, CommandNoun.Collection }, "in"),
        new("delete", new[] { "del", "rm", "remove", "trash" }, "Delete a set, configuration, item or collection", PackIconBoxIconsKind.RegularTrash, AllNouns),
        new("move", new[] { "mv" }, "Move an item or a set into another set", PackIconBoxIconsKind.RegularFolderUpArrow, new[] { CommandNoun.Item, CommandNoun.Set }, "to"),
        new("activate", new[] { "use", "switch", "apply", "load" }, "Make a configuration the active one", PackIconBoxIconsKind.RegularCheckCircle, new[] { CommandNoun.Configuration }),
        new("duplicate", new[] { "dup", "copy", "clone" }, "Copy a configuration", PackIconBoxIconsKind.RegularCopy, new[] { CommandNoun.Configuration }),
        new("enable", new[] { "on" }, "Turn an item on", PackIconBoxIconsKind.RegularToggleRight, new[] { CommandNoun.Item }),
        new("disable", new[] { "off" }, "Turn an item off", PackIconBoxIconsKind.RegularToggleLeft, new[] { CommandNoun.Item }),
        new("favorite", new[] { "fav", "star" }, "Add an item to favorites", PackIconBoxIconsKind.RegularStar, new[] { CommandNoun.Item }),
        new("unfavorite", new[] { "unfav", "unstar" }, "Remove an item from favorites", PackIconBoxIconsKind.RegularStar, new[] { CommandNoun.Item }),
        new("tag", new[] { "tags", "retag" }, "Change an item's type and tags", PackIconBoxIconsKind.RegularTag, new[] { CommandNoun.Item }),
        new("inspect", new[] { "look", "peek", "lookinside" }, "Look inside an item's package", PackIconBoxIconsKind.RegularEye, new[] { CommandNoun.Item }),
        new("export", Array.Empty<string>(), "Export an item as a package, or a collection as hider XML", PackIconBoxIconsKind.RegularArrowOutUpRightSquare, new[] { CommandNoun.Item, CommandNoun.Collection }),
        new("rebuild", Array.Empty<string>(), "Rebuild the cache for changed sets", PackIconBoxIconsKind.RegularRefreshCw, Array.Empty<CommandNoun>()),
    };

    [ObservableProperty]
    private bool _isCommandMode;

    public event Action? CompletionApplied;

    private MainViewModel Main => _serviceProvider.GetRequiredService<MainViewModel>();

    public void ApplyCompletion(string text)
    {
        SearchQuery = text;
        CompletionApplied?.Invoke();
    }

    public bool CompleteSelection()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count) return false;
        var text = Results[SelectedIndex].CompletionText;
        if (text == null || text == SearchQuery) return false;
        ApplyCompletion(text);
        return true;
    }

    private IEnumerable<CommandVerb> AvailableVerbs =>
        Main.IsVaultActive ? Verbs.Where(v => v.Name != "rebuild") : Verbs;

    private CommandVerb? FindVerb(string token) =>
        AvailableVerbs.FirstOrDefault(v => v.Name.Equals(token, StringComparison.OrdinalIgnoreCase)
                                           || v.Aliases.Any(a => a.Equals(token, StringComparison.OrdinalIgnoreCase)));

    private static CommandNoun? FindNoun(string token)
    {
        foreach (var (noun, name, aliases) in NounNames)
        {
            if (name.Equals(token, StringComparison.OrdinalIgnoreCase) || aliases.Any(a => a.Equals(token, StringComparison.OrdinalIgnoreCase)))
            {
                return noun;
            }
        }
        return null;
    }

    private static string NounName(CommandNoun noun) => NounNames.First(n => n.Noun == noun).Name;

    private static QuickSwitcherItemType NounType(CommandNoun noun) => noun switch
    {
        CommandNoun.Set => QuickSwitcherItemType.Set,
        CommandNoun.Configuration => QuickSwitcherItemType.Configuration,
        CommandNoun.Item => QuickSwitcherItemType.Item,
        _ => QuickSwitcherItemType.Collection
    };

    private async Task<List<QuickSwitcherItemViewModel>?> BuildCommandResultsAsync(string query, AppDbContext db, CancellationToken ct)
    {
        string text = query.TrimStart();
        bool slash = text.StartsWith('/');
        string prefix = slash ? "/" : string.Empty;
        string body = slash ? text[1..] : text;

        int space = body.IndexOf(' ');
        if (space < 0)
        {
            // Still typing the verb: only a slash asks for suggestions.
            if (!slash) return null;
            return AvailableVerbs
                .Where(v => v.Name.StartsWith(body, StringComparison.OrdinalIgnoreCase)
                            || v.Aliases.Any(a => a.StartsWith(body, StringComparison.OrdinalIgnoreCase)))
                .Select(v => CreateVerbRow(v, prefix))
                .ToList();
        }

        // Without a slash only full verb names count, so a search like "copy of" or "star" stays a search.
        string verbToken = body[..space];
        var verb = slash ? FindVerb(verbToken) : AvailableVerbs.FirstOrDefault(v => v.Name.Equals(verbToken, StringComparison.OrdinalIgnoreCase));
        if (verb == null) return slash ? new List<QuickSwitcherItemViewModel>() : null;

        string rest = body[(space + 1)..].TrimStart();
        string head = prefix + verb.Name + " ";

        if (verb.Nouns.Length == 0)
        {
            return new List<QuickSwitcherItemViewModel> { CreateVerbRow(verb, prefix) };
        }

        // The noun is resolved once it is followed by a space; a verb with one noun needs none.
        CommandNoun? noun = null;
        string args = rest;
        int nounSpace = rest.IndexOf(' ');
        string nounToken = nounSpace < 0 ? rest : rest[..nounSpace];
        if (nounSpace >= 0 && FindNoun(nounToken) is { } typed && verb.Nouns.Contains(typed))
        {
            noun = typed;
            args = rest[(nounSpace + 1)..].TrimStart();
        }
        else if (verb.Nouns.Length == 1)
        {
            noun = verb.Nouns[0];
        }

        var rows = new List<QuickSwitcherItemViewModel>();

        if (noun == null)
        {
            if (nounSpace < 0)
            {
                foreach (var (n, name, aliases) in NounNames.Where(x => verb.Nouns.Contains(x.Noun)))
                {
                    if (rest.Length == 0
                        || name.StartsWith(nounToken, StringComparison.OrdinalIgnoreCase)
                        || aliases.Any(a => a.StartsWith(nounToken, StringComparison.OrdinalIgnoreCase)))
                    {
                        rows.Add(CreateNounRow(verb, n, head));
                    }
                }
            }

            if (rest.Length > 0)
            {
                if (verb.Name == "new")
                {
                    // "/new Kitchen" offers to create each kind of thing with that name.
                    if (rows.Count == 0 && slash)
                    {
                        foreach (var n in verb.Nouns)
                        {
                            rows.AddRange(await BuildCreateRowsAsync(n, rest, head + NounName(n) + " ", allowParent: false, db, ct));
                        }
                    }
                }
                else
                {
                    foreach (var target in await FindTargetsAsync(verb.Nouns, rest, 6, db, ct))
                    {
                        rows.Add(CreateTargetRow(verb, target, head + NounName(target.Noun) + " "));
                    }
                }
            }

            return !slash && rows.Count == 0 ? null : rows;
        }

        string nounHead = head + NounName(noun.Value) + " ";

        if (verb.Name == "new")
        {
            rows = await BuildCreateRowsAsync(noun.Value, args, nounHead, allowParent: true, db, ct);
        }
        else if (verb.DestinationWord != null)
        {
            rows = await BuildMoveRowsAsync(verb, noun.Value, args, nounHead, db, ct);
        }
        else
        {
            foreach (var target in await FindTargetsAsync(new[] { noun.Value }, args, 25, db, ct))
            {
                rows.Add(CreateTargetRow(verb, target, nounHead));
            }
        }

        return !slash && rows.Count == 0 ? null : rows;
    }

    private async Task<List<QuickSwitcherItemViewModel>> BuildCreateRowsAsync(CommandNoun noun, string name, string nounHead, bool allowParent, AppDbContext db, CancellationToken ct)
    {
        var rows = new List<QuickSwitcherItemViewModel>();
        var accent = ThemeService.GetCurrentAccentBrush();
        string label = noun == CommandNoun.Configuration ? "configuration" : NounName(noun);

        // "new set Kitchen in Furniture" puts the set inside a parent; the whole text stays available as a plain name.
        int split = allowParent && noun == CommandNoun.Set ? LastIndexOfWord(name, "in") : -1;
        if (split >= 0)
        {
            string childName = name[..split].Trim();
            string parentQuery = name[(split + 4)..].TrimStart();
            if (childName.Length > 0)
            {
                foreach (var parent in await FindTargetsAsync(new[] { CommandNoun.Set }, parentQuery, 15, db, ct))
                {
                    rows.Add(new QuickSwitcherItemViewModel
                    {
                        Type = QuickSwitcherItemType.Set,
                        Title = $"Create set \"{childName}\"",
                        Subtitle = $"Inside {parent.Subtitle}",
                        DetailBadge = "NEW",
                        IconKind = PackIconBoxIconsKind.RegularPlusCircle,
                        IconBrush = parent.Brush,
                        CompletionText = $"{nounHead}{childName} in {parent.Title}",
                        CommandAction = () => CreateSetAsync(childName, parent.Id)
                    });
                }
            }
        }

        string trimmed = name.Trim();
        bool hasName = trimmed.Length > 0;
        rows.Add(new QuickSwitcherItemViewModel
        {
            Type = NounType(noun),
            Title = hasName ? $"Create {label} \"{trimmed}\"" : $"Create a new {label}…",
            Subtitle = noun switch
            {
                CommandNoun.Set => hasName ? "Top-level set  •  add \"in <set>\" to put it inside another set" : "Type a name, or press Enter to be asked for one",
                CommandNoun.Collection => "Opens the Collection Creator to pick sets and an icon",
                _ => hasName ? "Starts with every set enabled" : "Type a name, or press Enter to be asked for one"
            },
            DetailBadge = "NEW",
            IconKind = PackIconBoxIconsKind.RegularPlusCircle,
            IconBrush = accent,
            CompletionText = nounHead + trimmed,
            CommandAction = noun switch
            {
                CommandNoun.Set => () => CreateSetAsync(hasName ? trimmed : null, null),
                CommandNoun.Configuration => () => CreateConfigurationAsync(hasName ? trimmed : null),
                _ => () => CreateCollectionAsync(hasName ? trimmed : null)
            }
        });
        return rows;
    }

    private async Task<List<QuickSwitcherItemViewModel>> BuildMoveRowsAsync(CommandVerb verb, CommandNoun noun, string args, string nounHead, AppDbContext db, CancellationToken ct)
    {
        var rows = new List<QuickSwitcherItemViewModel>();
        int split = LastIndexOfWord(args, verb.DestinationWord!);

        // First pick what to move; choosing it fills in "<name> to ".
        if (split < 0)
        {
            foreach (var t in await FindTargetsAsync(new[] { noun }, args, 25, db, ct))
            {
                rows.Add(new QuickSwitcherItemViewModel
                {
                    Type = NounType(noun),
                    TargetId = t.Id,
                    Title = t.Title,
                    Subtitle = t.Subtitle,
                    DetailBadge = "MOVE",
                    IconKind = t.Icon,
                    IconBrush = t.Brush,
                    CompletionText = $"{nounHead}{t.Title} {verb.DestinationWord} "
                });
            }
            return rows;
        }

        string sourceQuery = args[..split].Trim();
        string destQuery = args[(split + verb.DestinationWord!.Length + 2)..].TrimStart();
        var source = (await FindTargetsAsync(new[] { noun }, sourceQuery, 1, db, ct)).FirstOrDefault();
        if (source == null) return rows;

        var sets = await db.SetsEntities.AsNoTracking().ToListAsync(ct);
        var setMap = sets.ToDictionary(s => s.Id);
        long? currentParent = noun == CommandNoun.Set ? setMap.GetValueOrDefault(source.Id)?.ParentSetsEntityId : source.SecondaryId;

        bool IsInsideSource(long setId)
        {
            for (long? id = setId; id.HasValue && setMap.TryGetValue(id.Value, out var s); id = s.ParentSetsEntityId)
            {
                if (id.Value == source.Id) return true;
            }
            return false;
        }

        if (noun == CommandNoun.Set && currentParent.HasValue
            && ("top level".StartsWith(destQuery, StringComparison.OrdinalIgnoreCase) || "root".StartsWith(destQuery, StringComparison.OrdinalIgnoreCase)))
        {
            rows.Add(new QuickSwitcherItemViewModel
            {
                Type = QuickSwitcherItemType.Set,
                Title = "Top level",
                Subtitle = $"Move {source.Title} out of its parent set",
                DetailBadge = "MOVE",
                IconKind = PackIconBoxIconsKind.RegularArrowUp,
                IconBrush = ThemeService.GetCurrentAccentBrush(),
                CompletionText = $"{nounHead}{source.Title} {verb.DestinationWord} top level",
                CommandAction = () => MoveSetAsync(source, null)
            });
        }

        foreach (var dest in await FindTargetsAsync(new[] { CommandNoun.Set }, destQuery, 25, db, ct))
        {
            if (dest.Id == currentParent) continue;
            if (noun == CommandNoun.Set && IsInsideSource(dest.Id)) continue;

            rows.Add(new QuickSwitcherItemViewModel
            {
                Type = QuickSwitcherItemType.Set,
                TargetId = dest.Id,
                Title = dest.Title,
                Subtitle = $"Move {source.Title}  →  {dest.Subtitle}",
                DetailBadge = "MOVE",
                IconKind = dest.Icon,
                IconBrush = dest.Brush,
                CompletionText = $"{nounHead}{source.Title} {verb.DestinationWord} {dest.Title}",
                CommandAction = noun == CommandNoun.Set ? () => MoveSetAsync(source, dest.Id) : () => MoveItemAsync(source, dest.Id)
            });
        }
        return rows;
    }

    private static int LastIndexOfWord(string text, string word) =>
        text.LastIndexOf($" {word} ", StringComparison.OrdinalIgnoreCase);

    private async Task<List<CommandTarget>> FindTargetsAsync(IReadOnlyCollection<CommandNoun> nouns, string query, int perNoun, AppDbContext db, CancellationToken ct)
    {
        string q = query.Trim();
        string[] tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var scored = new List<(int Score, int NounOrder, CommandTarget Target)>();

        void AddRanked(IEnumerable<(string Name, CommandTarget Target)> candidates, CommandNoun noun)
        {
            scored.AddRange(candidates
                .Select(c => (Score: MatchScore(c.Name, q, tokens), c.Target))
                .Where(c => c.Score >= 0)
                .OrderBy(c => c.Score).ThenBy(c => c.Target.Title.Length).ThenBy(c => c.Target.Title, StringComparer.OrdinalIgnoreCase)
                .Take(perNoun)
                .Select(c => (c.Score, Array.IndexOf(AllNouns, noun), c.Target)));
        }

        if (nouns.Contains(CommandNoun.Set))
        {
            var sets = await db.SetsEntities.AsNoTracking().ToListAsync(ct);
            var setMap = sets.ToDictionary(s => s.Id);
            AddRanked(sets.Select(s => (s.Name, new CommandTarget(
                CommandNoun.Set, s.Id, s.ParentSetsEntityId, s.Name, BuildSetPath(s, setMap),
                Enum.TryParse<PackIconBoxIconsKind>(s.Icon, out var k) ? k : PackIconBoxIconsKind.RegularFolder,
                !string.IsNullOrEmpty(s.Color) ? Brush.Parse(s.Color) : ThemeService.GetCurrentAccentBrush()))), CommandNoun.Set);
        }

        if (nouns.Contains(CommandNoun.Configuration))
        {
            var configs = await db.ConfigEntities.AsNoTracking().ToListAsync(ct);
            AddRanked(configs.Select(c => (c.Name, new CommandTarget(
                CommandNoun.Configuration, c.Id, null, c.Name,
                (c.Active ? "Active  •  " : c.Default ? "Default  •  " : string.Empty) + (!string.IsNullOrWhiteSpace(c.Description) ? c.Description : "Configuration profile"),
                Enum.TryParse<PackIconBoxIconsKind>(c.Icon, out var k) ? k : PackIconBoxIconsKind.RegularSlider,
                !string.IsNullOrEmpty(c.Color) ? Brush.Parse(c.Color) : (c.Active ? ThemeService.GetCurrentAccentBrush() : Brush.Parse("#60a5fa"))))), CommandNoun.Configuration);
        }

        if (nouns.Contains(CommandNoun.Item))
        {
            var itemQuery = db.MetaEntities.AsNoTracking().Include(i => i.SetsEntity).AsQueryable();
            foreach (var token in tokens)
            {
                string pattern = $"%{token}%";
                itemQuery = itemQuery.Where(i => EF.Functions.Like(i.FileName, pattern));
            }
            var items = await itemQuery.OrderBy(i => i.FileName).Take(200).ToListAsync(ct);
            AddRanked(items.Select(i => (i.FileName, new CommandTarget(
                CommandNoun.Item, i.Id, i.SetsEntityId, i.FileName,
                $"In set: {i.SetsEntity?.Name ?? "Unassigned"}" + (i.Enabled ? string.Empty : "  •  Disabled") + (i.IsFavorite ? "  •  Favorite" : string.Empty),
                PackIconBoxIconsKind.RegularPackage, Brush.Parse("#c084fc")))), CommandNoun.Item);
        }

        if (nouns.Contains(CommandNoun.Collection))
        {
            var health = Main.HealthVm;
            if (!health.HasLoadedCollections)
            {
                await health.LoadCollectionsAsync();
            }
            var collectionBrush = Brush.Parse("#fbbf24");
            AddRanked(health.AllCollections.Select(c => (c.Name, new CommandTarget(
                CommandNoun.Collection, unchecked((long)c.Id), null, c.Name,
                $"{c.ItemCount} items" + (c.LinkedSetNames.Count > 0 ? "  •  " + string.Join(", ", c.LinkedSetNames) : string.Empty),
                PackIconBoxIconsKind.RegularLayers, collectionBrush))), CommandNoun.Collection);
        }

        return scored.OrderBy(s => s.Score).ThenBy(s => s.NounOrder).Select(s => s.Target).ToList();
    }

    private static int MatchScore(string name, string query, string[] tokens)
    {
        if (query.Length == 0) return 4;
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 2;
        return tokens.All(t => name.Contains(t, StringComparison.OrdinalIgnoreCase)) ? 3 : -1;
    }

    private static string BuildSetPath(SetsEntity set, Dictionary<long, SetsEntity> setMap)
    {
        var parts = new List<string> { set.Name };
        var curr = set;
        while (curr.ParentSetsEntityId.HasValue && setMap.TryGetValue(curr.ParentSetsEntityId.Value, out var parent) && parts.Count < 32)
        {
            parts.Insert(0, parent.Name);
            curr = parent;
        }
        return parts.Count > 1 ? string.Join("  /  ", parts) : "Top-level set folder";
    }

    private QuickSwitcherItemViewModel CreateVerbRow(CommandVerb verb, string prefix) => new()
    {
        Type = QuickSwitcherItemType.Command,
        Title = prefix + verb.Name,
        Subtitle = verb.Aliases.Length > 0 ? $"{verb.Description}  •  also: {string.Join(", ", verb.Aliases)}" : verb.Description,
        IconKind = verb.Icon,
        IconBrush = ThemeService.GetCurrentAccentBrush(),
        CompletionText = prefix + verb.Name + " ",
        CommandAction = verb.Nouns.Length == 0 ? () => RunGlobalVerbAsync(verb) : null
    };

    private static QuickSwitcherItemViewModel CreateNounRow(CommandVerb verb, CommandNoun noun, string head)
    {
        var (_, name, aliases) = NounNames.First(n => n.Noun == noun);
        return new QuickSwitcherItemViewModel
        {
            Type = NounType(noun),
            Title = $"{verb.Name} {name}",
            Subtitle = verb.Name == "new" ? $"Create a {name}  •  also: {string.Join(", ", aliases)}" : $"Pick a {name}  •  also: {string.Join(", ", aliases)}",
            IconKind = verb.Icon,
            IconBrush = ThemeService.GetCurrentAccentBrush(),
            CompletionText = head + name + " "
        };
    }

    private QuickSwitcherItemViewModel CreateTargetRow(CommandVerb verb, CommandTarget target, string nounHead) => new()
    {
        Type = NounType(target.Noun),
        TargetId = target.Id,
        SecondaryId = target.SecondaryId,
        Title = target.Title,
        Subtitle = target.Subtitle,
        DetailBadge = verb.Name.ToUpperInvariant(),
        IconKind = target.Icon,
        IconBrush = target.Brush,
        CompletionText = nounHead + target.Title,
        CommandAction = () => RunTargetVerbAsync(verb.Name, target)
    };

    private QuickSwitcherItemViewModel CreateCommandHintRow() => new()
    {
        Type = QuickSwitcherItemType.Command,
        Title = "Run a command",
        Subtitle = "Type / for actions, like /edit collection cottage kitchen or /move item chair to living room",
        IconKind = PackIconBoxIconsKind.RegularCommand,
        IconBrush = ThemeService.GetCurrentAccentBrush(),
        CompletionText = "/"
    };

    private QuickSwitcherItemViewModel? CreateExactVerbRow(string query)
    {
        var verb = AvailableVerbs.FirstOrDefault(v => v.Name.Equals(query, StringComparison.OrdinalIgnoreCase));
        return verb == null ? null : CreateVerbRow(verb, string.Empty);
    }

    private async Task RunCommandAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await DialogHelper.ShowErrorAsync("Command Failed", DialogHelper.BuildDetailedExceptionMessage("The command could not be completed:", ex));
        }
    }

    private static Task ShowCommandErrorAsync(string message) => DialogHelper.ShowErrorAsync("Command", message);

    private async Task RunGlobalVerbAsync(CommandVerb verb)
    {
        if (verb.Name == "rebuild")
        {
            await Main.RebuildCacheAsync();
        }
    }

    private Task RunTargetVerbAsync(string verb, CommandTarget target) => target.Noun switch
    {
        CommandNoun.Set => RunSetVerbAsync(verb, target),
        CommandNoun.Configuration => RunConfigurationVerbAsync(verb, target),
        CommandNoun.Item => RunItemVerbAsync(verb, target),
        _ => RunCollectionVerbAsync(verb, target)
    };

    private async Task RunSetVerbAsync(string verb, CommandTarget target)
    {
        var main = Main;
        main.NavigateToSet(target.Id);
        if (verb == "open") return;

        var cm = main.ContentManagerVm;
        var node = cm.FindNodeById(cm.RootSets, target.Id);
        if (node == null)
        {
            await ShowCommandErrorAsync($"The set '{target.Title}' is not in the library tree yet.");
            return;
        }
        if (node.IsDefault)
        {
            await ShowCommandErrorAsync("The Default set can't be renamed, customized or deleted.");
            return;
        }

        switch (verb)
        {
            case "edit":
            case "rename":
                await cm.CustomizeSetAsync(node);
                break;
            case "delete":
                await cm.DeleteSetAsync(node);
                break;
        }
    }

    private async Task RunConfigurationVerbAsync(string verb, CommandTarget target)
    {
        var main = Main;
        var configs = main.ConfigurationsVm;
        main.NavigateToConfigurations();

        // Navigating starts a reload that replaces every row; wait for it before picking one.
        for (int i = 0; i < 100 && configs.IsLoading; i++)
        {
            await Task.Delay(50);
        }

        var config = configs.Configurations.FirstOrDefault(c => c.Id == target.Id);
        if (config == null)
        {
            await ShowCommandErrorAsync($"The configuration '{target.Title}' could not be found.");
            return;
        }
        configs.SelectedConfiguration = config;

        switch (verb)
        {
            case "edit":
            case "rename":
                await configs.CustomizeConfigurationAsync(config);
                break;
            case "delete":
                if (config.Active || config.Default)
                {
                    await ShowCommandErrorAsync(config.Active
                        ? $"'{config.Name}' is the active configuration. Activate another one before deleting it."
                        : $"'{config.Name}' is the default configuration and can't be deleted.");
                    return;
                }
                await configs.DeleteConfigurationAsync(config);
                break;
            case "activate":
                await configs.ActivateConfigurationAsync(config);
                break;
            case "duplicate":
                await configs.DuplicateConfigurationAsync(config);
                break;
        }
    }

    private async Task RunItemVerbAsync(string verb, CommandTarget target)
    {
        if (verb == "inspect")
        {
            if (await DialogHelper.ShowLookInsideAsync(target.Id, target.Title))
            {
                await Main.ContentManagerVm.RefreshEditedItemsAsync(new[] { target.Id });
            }
            return;
        }

        var main = Main;
        main.NavigateToItem(target.Id, target.SecondaryId);
        if (verb == "open") return;

        var cm = main.ContentManagerVm;
        var item = cm.Items.FirstOrDefault(i => i.Entity.Id == target.Id);
        if (item == null)
        {
            await ShowCommandErrorAsync($"'{target.Title}' is not loaded in the library yet. Try again in a moment.");
            return;
        }

        // The selection-based actions must act on exactly this item, which navigating selected.
        bool onlyThisSelected = item.IsSelected && cm.Items.Count(i => i.IsSelected) == 1;

        switch (verb)
        {
            case "edit":
            case "tag":
                await cm.EditItemTagsAsync(item);
                break;
            case "rename":
                await cm.RenameItemAsync(item);
                break;
            case "delete":
                await cm.DeleteItemAsync(item);
                break;
            case "enable":
            case "disable":
                if (item.Entity.Enabled != (verb == "enable"))
                {
                    await cm.ToggleEnableItemAsync(item);
                }
                break;
            case "favorite":
            case "unfavorite":
            case "export":
                if (!onlyThisSelected)
                {
                    await ShowCommandErrorAsync($"'{target.Title}' could not be selected in the library, so nothing was changed.");
                    return;
                }
                if (verb == "export") await cm.ExportSelectedAsync();
                else await cm.FavoriteSelectedItemsAsync(verb == "favorite");
                break;
        }
    }

    private async Task MoveItemAsync(CommandTarget item, long destSetId)
    {
        var main = Main;
        main.NavigateToItem(item.Id, item.SecondaryId);

        var cm = main.ContentManagerVm;
        var selected = cm.Items.Where(i => i.IsSelected).ToList();
        var dest = cm.FlatSets.FirstOrDefault(s => s.Id == destSetId);
        if (selected.Count != 1 || selected[0].Entity.Id != item.Id || dest == null)
        {
            await ShowCommandErrorAsync($"'{item.Title}' could not be selected in the library, so it was not moved.");
            return;
        }
        await cm.MoveSelectedToSetAsync(dest);
        main.NavigateToItem(item.Id, destSetId);
    }

    private async Task MoveSetAsync(CommandTarget set, long? destSetId)
    {
        var main = Main;
        main.NavigateToContentManager();
        await main.ContentManagerVm.MoveSetsAsync(new List<long> { set.Id }, destSetId);
        main.NavigateToSet(set.Id);
    }

    private async Task CreateSetAsync(string? name, long? parentId)
    {
        var main = Main;
        main.NavigateToContentManager();
        var cm = main.ContentManagerVm;

        var parent = parentId.HasValue ? cm.FindNodeById(cm.RootSets, parentId.Value) : null;
        if (parentId.HasValue && parent == null)
        {
            await ShowCommandErrorAsync("The parent set could not be found.");
            return;
        }

        if (name == null) await cm.CreateTopLevelSetAsync();
        else await cm.CreateSetNamedAsync(name, parent);
    }

    private async Task CreateConfigurationAsync(string? name)
    {
        var main = Main;
        main.NavigateToConfigurations();
        if (name == null) await main.ConfigurationsVm.CreateConfigurationAsync();
        else await main.ConfigurationsVm.CreateConfigurationNamedAsync(name);
    }

    private async Task CreateCollectionAsync(string? name)
    {
        var main = Main;
        var health = main.HealthVm;
        // The editor opens synchronously; opening it before navigating stops the tool from reloading the list over it.
        var editing = health.CreateNewCollectionAsync();
        if (name != null) health.EditingCollectionName = name;
        main.NavigateToToolSection("Collections");
        await editing;
    }

    private async Task RunCollectionVerbAsync(string verb, CommandTarget target)
    {
        var main = Main;
        var health = main.HealthVm;
        var collection = health.AllCollections.FirstOrDefault(c => unchecked((long)c.Id) == target.Id);
        if (collection == null)
        {
            main.NavigateToToolSection("Collections");
            await ShowCommandErrorAsync($"The collection '{target.Title}' could not be found.");
            return;
        }

        switch (verb)
        {
            case "edit":
            case "rename":
                var editing = health.EditCollectionAsync(collection);
                main.NavigateToToolSection("Collections");
                await editing;
                break;
            case "delete":
                main.NavigateToToolSection("Collections");
                var confirm = await DialogHelper.ShowDeleteConfirmAsync($"Are you sure you want to delete the collection '{collection.Name}'?");
                if (confirm.Confirmed) await health.DeleteCollectionAsync(collection);
                break;
            case "export":
                main.NavigateToToolSection("Collections");
                await health.ExportHiderXmlAsync(collection);
                break;
            default:
                main.NavigateToToolSection("Collections");
                break;
        }
    }
}
