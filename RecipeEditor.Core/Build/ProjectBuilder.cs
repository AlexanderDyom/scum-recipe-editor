// Building a project into its mod pak (modkit's ModBuilder with the key kept by the editor), checking the built packages,
// installing the pak into Paks\~mods and rebuilding the shared registry of all installed mods with the installed
// ScumRegistryMerger (its console part, ScumRegistryMerger.Cli.exe), then checking that every mod is in it.
using System.Diagnostics;
using System.Text;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using RecipeEditor.Core.Project;
using ScumModKit.Build;

namespace RecipeEditor.Core.Build;

public enum StepState { Running, Ok, Warning, Failed }

/// <summary>A line of the build window: what is done, how it went, a short result on the right.</summary>
public sealed record BuildStep(string Key, StepState State, string Text, string? Result = null, string? Detail = null);

public sealed class BuildRequest
{
    /// <summary>The game's Paks folder.</summary>
    public required string Paks { get; init; }
    public required string AesKey { get; init; }
    /// <summary>Copy the pak (+ .sig) into Paks\~mods.</summary>
    public bool Install { get; init; } = true;
    /// <summary>When not installing: a folder for the pak and .sig (for a server or Nexus).</summary>
    public string? OutputFolder { get; init; }
    /// <summary>After installing, rebuild the shared registry of all mods.</summary>
    public bool UpdateRegistry { get; init; } = true;
    /// <summary>ScumRegistryMerger.Cli.exe of the installed merger; null = not installed.</summary>
    public string? MergerCli { get; init; }
    /// <summary>Also install into this game's Paks (and rebuild its registry): the SCUM client when building for SCUM Server.</summary>
    public string? AlsoPaks { get; init; }
}

public sealed class BuildReport
{
    public bool Ok { get; set; }
    public string? Pak { get; set; }
    public long PakSize { get; set; }
    public string? InstalledPak { get; set; }
    /// <summary>Mods of ~mods and how the shared registry has them (name, included).</summary>
    public List<(string Mod, bool Included)> Mods { get; } = [];
    public List<string> Log { get; } = [];
}

public static class ProjectBuilder
{
    /// <summary>SCUM / SCUM Server running: it keeps the paks open, a build cannot replace them.</summary>
    public static bool GameRunning() => ScumModKit.Game.GameLocator.IsGameRunning();

    /// <summary>
    /// Builds <paramref name="project"/> (saved modkit.json) into project\build\&lt;pak&gt;.pak, checks it, then installs it and
    /// rebuilds the shared registry, or copies it to the output folder. Steps are reported as they go.
    /// </summary>
    public static BuildReport Run(RecipeProject project, BuildRequest req, IProgress<BuildStep> steps, CancellationToken cancel = default)
    {
        var report = new BuildReport();
        void Step(BuildStep s) => steps.Report(s);

        if (req.Install && GameRunning())
        {
            Step(new BuildStep("game", StepState.Failed, "GameRunning"));
            return report;
        }

        // 1. modkit build: clones, cooking edits, edits -> stage -> pak + .sig (+ registry when there are clones)
        var buildDir = Path.Combine(project.Folder, "build");
        var stage = Path.Combine(buildDir, "stage");
        var pak = Path.Combine(buildDir, project.Pak + ".pak");
        Step(new BuildStep("clones", StepState.Running, "StepClones"));
        int cloneFiles;
        try
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, true); // our own build folder: modkit needs it empty
            foreach (var f in new[] { pak, Path.ChangeExtension(pak, ".sig") }) if (File.Exists(f)) File.Delete(f);
            ModBuilder.Build(new ScumModKit.Build.BuildOptions
            {
                Project = project.ModkitPath, Stage = stage, OutputPak = pak, Paks = req.Paks, Key = req.AesKey,
            }, line => report.Log.Add(line));
            report.Pak = pak;
            report.PakSize = new FileInfo(pak).Length;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Step(new BuildStep("clones", StepState.Failed, "StepBuildFailed", null, ex.Message + (report.Log.Count > 0 ? "\n\n" + string.Join("\n", report.Log) : "")));
            return report;
        }
        // packages of the pak: the clones (new dishes, recipes, books) and the game's packages the mod changes
        var all = Directory.EnumerateFiles(stage, "*.uasset", SearchOption.AllDirectories).ToList();
        var clonePackages = project.Modkit.CloneGroups().SelectMany(g => g.Packages).Select(p => p.New.Split('/').Last()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        cloneFiles = all.Count(f => clonePackages.Contains(Path.GetFileNameWithoutExtension(f)));
        Step(new BuildStep("clones", StepState.Ok, "StepClones", cloneFiles.ToString()));
        Step(new BuildStep("edits", StepState.Ok, "StepEdits", (all.Count - cloneFiles).ToString()));
        cancel.ThrowIfCancellationRequested();

        // 2. every built package opens in CUE4Parse and the recipes read back (what the game will load)
        Step(new BuildStep("check", StepState.Running, "StepCheck"));
        var problems = CheckPackages(stage);
        Step(new BuildStep("check", problems.Count == 0 ? StepState.Ok : StepState.Failed, "StepCheck", problems.Count.ToString(), string.Join("\n", problems)));
        if (problems.Count > 0) return report;

        if (!req.Install)
        {
            // 3'. a folder for a server / Nexus: pak + .sig, ~mods and the shared registry are not touched
            var folder = req.OutputFolder ?? throw new ArgumentException("No output folder.");
            Directory.CreateDirectory(folder);
            foreach (var f in new[] { pak, Path.ChangeExtension(pak, ".sig") })
                if (File.Exists(f)) File.Copy(f, Path.Combine(folder, Path.GetFileName(f)), true);
            Step(new BuildStep("copy", StepState.Ok, "StepCopied", folder));
            report.Ok = true;
            return report;
        }

        InstallInto(req.Paks, "", pak, req, report, Step);
        // SCUM Server: the same mod into the SCUM client too (ingredients and new recipes need it on the players' side)
        if (req.AlsoPaks != null && report.InstalledPak != null) InstallInto(req.AlsoPaks, "-client", pak, req, report, Step);
        return report;
    }

    /// <summary>
    /// Installs the pak into <paramref name="paks"/>~mods and rebuilds that game's shared registry; steps are keyed with
    /// <paramref name="suffix"/> ("" for the game of the project, "-client" for SCUM next to SCUM Server). It
    /// stops early when the install fails or the registry is skipped.
    /// </summary>
    private static void InstallInto(string paks, string suffix, string pak, BuildRequest req, BuildReport report, Action<BuildStep> Step)
    {
        // 3. install into ~mods (a running game would have locked the old pak: compare what landed there)
        var mods = Path.Combine(paks, "~mods");
        var target = Path.Combine(mods, Path.GetFileName(pak));
        Step(new BuildStep("install" + suffix, StepState.Running, "StepInstall"));
        try
        {
            Directory.CreateDirectory(mods);
            File.Copy(pak, target, true);
            var sig = Path.ChangeExtension(pak, ".sig");
            if (File.Exists(sig)) File.Copy(sig, Path.ChangeExtension(target, ".sig"), true);
            if (!File.ReadAllBytes(pak).AsSpan().SequenceEqual(File.ReadAllBytes(target))) throw new IOException("The pak in ~mods was not replaced.");
            if (suffix.Length == 0) report.InstalledPak = target;
            Step(new BuildStep("install" + suffix, StepState.Ok, "StepInstalled", report.PakSize.ToString()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Step(new BuildStep("install" + suffix, StepState.Failed, "StepInstallFailed", null, ex.Message));
            return;
        }

        // 4. shared registry of every mod in ~mods: the installed ScumRegistryMerger builds it, then (dry run) says how
        // every mod stands; the editor has no merger of its own (players download the program)
        if (!req.UpdateRegistry)
        {
            Step(new BuildStep("registry" + suffix, StepState.Warning, "StepRegistrySkipped"));
            return;
        }
        if (req.MergerCli == null)
        {
            Step(new BuildStep("registry" + suffix, StepState.Warning, "StepRegistryNoMerger"));
            return;
        }
        Step(new BuildStep("registry" + suffix, StepState.Running, "StepRegistry"));
        try
        {
            var output = RunMergerCli(req.MergerCli, paks, req.AesKey, dryRun: false);
            report.Log.Add(output);
            var check = RunMergerCli(req.MergerCli, paks, req.AesKey, dryRun: true);
            var states = ParseMods(check);
            foreach (var (file, state) in states.Where(m => m.State != "NotNeeded"))
                if (suffix.Length == 0) report.Mods.Add((DisplayName(file), state == "Included"));
            var baseFromGame = output.Contains("base: game registry", StringComparison.Ordinal);
            var issues = states.Where(m => m.State is "Missing" or "Unreadable").Select(m => $"{DisplayName(m.File)}: {m.State}")
                .Concat(OtherVersionMods(check).Select(m => $"{DisplayName(m)}: built for another game version"))
                .Concat(baseFromGame ? [] : ["base: a mod's copy of the game registry, not the game's own"]).ToList();
            var ok = issues.Count == 0;
            Step(new BuildStep("registry" + suffix, ok ? StepState.Ok : StepState.Warning, ok ? "StepRegistryOk" : "StepRegistryWarn",
                string.Join(", ", states.Where(m => m.State != "NotNeeded").Select(m => DisplayName(m.File))), string.Join("\n", issues)));
            report.Ok = ok && (suffix.Length == 0 || report.Ok);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // no mod in ~mods ships a registry (only changes of game recipes): nothing to merge, nothing is wrong
            if (ex.Message.Contains("No mod pak with a readable AssetRegistry.bin", StringComparison.Ordinal))
                Step(new BuildStep("registry" + suffix, StepState.Warning, "StepRegistryNotNeeded"));
            else Step(new BuildStep("registry" + suffix, StepState.Failed, "StepRegistryFailed", null, ex.Message));
        }
    }

    /// <summary>
    /// The mods lines of ScumRegistryMerger.Cli's output: "  &lt;file&gt;.pak: 12 new records, Included, new" -> (file, state).
    /// </summary>
    public static List<(string File, string State)> ParseMods(string output)
    {
        var list = new List<(string, string)>();
        foreach (var line in output.Split('\n'))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, @"^\s+(\S.*?\.pak): (.*)$");
            if (!m.Success) continue;
            var state = m.Groups[2].Value.Split(',').Select(s => s.Trim()).FirstOrDefault(s => s is "NotNeeded" or "Included" or "Missing" or "Unreadable");
            if (state == null && m.Groups[2].Value.Contains("can't be read")) state = "Unreadable";
            if (state != null) list.Add((m.Groups[1].Value, state));
        }
        return list;
    }

    /// <summary>
    /// Mods ScumRegistryMerger (from its version with game references) finds built for another game version: their
    /// registry lacks records of this game or holds records it no longer has ("..., BUILT FOR ANOTHER GAME VERSION (...)").
    /// </summary>
    public static List<string> OtherVersionMods(string output) => output.Split('\n')
        .Select(l => System.Text.RegularExpressions.Regex.Match(l, @"^\s+(\S.*?\.pak): .*BUILT FOR ANOTHER GAME VERSION"))
        .Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();

    /// <summary>pakchunk901-FoodMod_P.pak -> FoodMod (as ScumRegistryMerger names mods).</summary>
    public static string DisplayName(string pak)
    {
        var n = Path.GetFileNameWithoutExtension(pak);
        n = System.Text.RegularExpressions.Regex.Replace(n, @"^pakchunk\d+(_s\d+)?-", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        n = System.Text.RegularExpressions.Regex.Replace(n, @"(_\d+)?_P$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return n.Length > 0 ? n : pak;
    }

    /// <summary>Runs ScumRegistryMerger.Cli merge; the key goes in the environment (SCUM_AES_KEY), never on the command line.</summary>
    private static string RunMergerCli(string cli, string paks, string key, bool dryRun)
    {
        var psi = new ProcessStartInfo(cli)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("merge");
        psi.ArgumentList.Add("--paks");
        psi.ArgumentList.Add(paks);
        if (dryRun) psi.ArgumentList.Add("--dry-run");
        psi.Environment["SCUM_AES_KEY"] = key;
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ScumRegistryMerger did not start.");
        p.StandardInput.Close();
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(TimeSpan.FromMinutes(10))) { p.Kill(); throw new TimeoutException("ScumRegistryMerger did not finish in 10 minutes."); }
        if (p.ExitCode != 0) throw new InvalidOperationException((err.Result + "\n" + output).Trim());
        return output;
    }

    /// <summary>Opens every package of the stage with CUE4Parse; recipes must read back. Returns the problems found.</summary>
    public static List<string> CheckPackages(string stage)
    {
        var problems = new List<string>();
        using var stageProvider = new DefaultFileProvider(stage, SearchOption.AllDirectories, new VersionContainer(EGame.GAME_UE4_27), StringComparer.OrdinalIgnoreCase);
        stageProvider.Initialize();
        foreach (var file in stageProvider.Files.Keys.Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)))
        {
            var package = file[..^".uasset".Length];
            try
            {
                if (!stageProvider.TryLoadPackage(package, out var pkg)) { problems.Add($"{package}: does not open"); continue; }
                _ = pkg.GetExports().ToList(); // reads every export
                if (package.Contains("/Cooking/Recipes/", StringComparison.OrdinalIgnoreCase) && Game.GameData.ReadRecipe(stageProvider, package) == null)
                    problems.Add($"{package}: the recipe does not read back");
            }
            catch (Exception ex)
            {
                problems.Add($"{package}: {ex.Message}");
            }
        }
        return problems;
    }

    /// <summary>
    /// #SpawnItem commands to test the mod: cookbooks of the changed / new recipes (own books too) and the item of the
    /// first variant of every main slot.
    /// </summary>
    /// <summary>An item that comes filled with <paramref name="resource"/> (ItemInfo.Holds), or null (also before the catalog is read).</summary>
    private static string? BottleOf(ProjectDocument doc, string? resource) => resource == null ? null
        : doc.Game.Items.Values.Where(i => i.Holds?.Equals(resource, StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(i => i.Category == Game.ItemCategory.Inedible ? 1 : 0).ThenBy(i => i.Class, StringComparer.Ordinal).FirstOrDefault()?.Class;

    /// <summary>
    /// A mod for a server alone is enough: it only sets cooking time or temperature of game recipes (the server cooks with
    /// them; checked in game: players still see the old time). Ingredients, cookware, new recipes, dishes and books are
    /// checked by the player's game, so the players need the mod too (checked: an added slot works only with it).
    /// </summary>
    public static bool ServerAlone(RecipeProject p)
    {
        var c = p.Modkit.Cooking;
        if (p.Modkit.Clones.Count > 0 || p.Modkit.Edits.Count > 0) return false;
        if (c != null && (c.NewRecipes.Count > 0 || c.Books.Count > 0 || c.OwnBooks.Count > 0 || c.KnownByDefault.Count > 0 || c.ItemTexts.Count > 0)) return false;
        return c == null || c.Recipes.Values.All(ops => ops.All(op => op.ValueKind == System.Text.Json.JsonValueKind.Array && op.GetArrayLength() >= 2
            && op[0].GetString() == "set" && op[1].GetString() is "CookingTime" or "CookingTemperature"));
    }

    public static List<string> SpawnCommands(ProjectDocument doc)
    {
        var p = doc.Project;
        var cooking = p.Modkit.Cooking;
        var recipes = p.ChangedRecipes.Concat(p.NewRecipes).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var lines = new List<string>();
        var books = new List<string>();
        foreach (var r in recipes)
        {
            books.AddRange(doc.Game.Books.Where(b => b.Recipes.Contains(r, StringComparer.OrdinalIgnoreCase)).Select(b => b.Id));
            if (cooking != null)
            {
                books.AddRange(cooking.Books.Where(kv => kv.Value.Contains(r, StringComparer.OrdinalIgnoreCase)).Select(kv => kv.Key));
                books.AddRange(cooking.OwnBooks.Where(kv => kv.Value.Contains(r, StringComparer.OrdinalIgnoreCase)).Select(kv => kv.Key));
            }
        }
        lines.AddRange(books.Distinct(StringComparer.OrdinalIgnoreCase).Select(b => $"#SpawnItem {b} 1"));
        // one item of every main slot (all are needed), and every item the mod puts into a slot, main or optional
        var items = new List<string>();
        foreach (var r in recipes)
        {
            if (doc.State(r) is not { } s) continue;
            var game = doc.GameState(r);
            foreach (var g in Recipe.Groups.All)
                foreach (var slot in s.Live(g))
                {
                    var stock = slot.IsNew ? null : game?.Slots[g].FirstOrDefault(x => x.Origin == slot.Origin);
                    var stockItems = new HashSet<string?>(stock?.Options.Select(o => o.Item) ?? [], StringComparer.OrdinalIgnoreCase);
                    // a liquid can't be spawned: the bottle that holds it can (absinthe, isopropyl alcohol)
                    var added = slot.Options.Select(o => o.Item ?? BottleOf(doc, o.Resource)).Where(i => i != null && !stockItems.Contains(i)).Select(i => i!);
                    var first = g == Recipe.Groups.Main ? slot.Options.FirstOrDefault(o => o.Item != null)?.Item : null;
                    items.AddRange(added.Prepend(first).Where(i => i != null).Select(i => Game.IngredientOption.ShortClass(i)));
                }
        }
        lines.AddRange(items.Distinct(StringComparer.OrdinalIgnoreCase).Select(i => $"#SpawnItem {i} 1"));
        return lines;
    }
}
