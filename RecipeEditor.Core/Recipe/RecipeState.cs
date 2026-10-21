// A recipe as the editor shows and edits it: the game's recipe with the project's cooking ops of modkit.json applied,
// with the same meaning as modkit's CookingEdits (tools/modkit/ScumModKit.Build/Ops/CookingEdits.cs). Slots remember
// where they came from, so the editor can mark added / removed slots and turn the state back into ops (RecipeOps).
using System.Text.Json;
using RecipeEditor.Core.Game;

namespace RecipeEditor.Core.Recipe;

public static class Groups
{
    public const string Main = "main";
    public const string Optional = "optional";
    public static readonly string[] All = [Main, Optional];
}

/// <summary>A variant of a slot: an item (object path of its class) or a liquid resource.</summary>
public sealed class SlotOption
{
    public string? Item { get; set; }
    public string? Resource { get; set; }
    public bool Whole { get; set; }
    public int Usage { get; set; } = 1;
    public double Liters { get; set; }
    public double Nutrient { get; set; } = 1;
    public bool IsResource => Resource != null;

    public SlotOption Clone() => (SlotOption)MemberwiseClone();

    public static SlotOption From(IngredientOption o) => new()
    {
        Item = o.Item, Resource = o.Resource, Whole = o.Whole, Usage = o.Usage, Liters = o.Liters, Nutrient = o.Nutrient,
    };

    /// <summary>Same in the game; Liters only matters for liquids (items keep whatever the copied variant had).</summary>
    public bool SameAs(SlotOption o) => Item == o.Item && Resource == o.Resource && Whole == o.Whole && Usage == o.Usage &&
                                        (!IsResource || Math.Abs(Liters - o.Liters) < 1e-6) && Math.Abs(Nutrient - o.Nutrient) < 1e-6;

    public override string ToString() => IsResource ? $"{Resource} {Liters} l" : $"{IngredientOption.ShortClass(Item)} {(Whole ? "x" : "u")}{Usage}";
}

public sealed class Slot
{
    /// <summary>Shown title (localized for game slots, verbatim for ones the mod titled).</summary>
    public string Title { get; set; } = "";
    /// <summary>What modkit matches a title ref against: the game's source string, or the verbatim text the mod set.</summary>
    public string TitleSource { get; set; } = "";
    /// <summary>The title was set by the mod (shown and stored verbatim).</summary>
    public bool TitleEdited { get; set; }
    public bool IsResource { get; set; }
    public List<SlotOption> Options { get; set; } = [];
    /// <summary>Index in the game's group, or -1 for a slot the mod added.</summary>
    public int Origin { get; init; } = -1;
    /// <summary>A game slot the mod removed (kept to show it struck out).</summary>
    public bool Removed { get; set; }

    public bool IsNew => Origin < 0;

    public Slot Clone() => new()
    {
        Title = Title, TitleSource = TitleSource, TitleEdited = TitleEdited, IsResource = IsResource,
        Options = Options.Select(o => o.Clone()).ToList(), Origin = Origin, Removed = Removed,
    };

    public bool SameAs(Slot s) => TitleSource == s.TitleSource && IsResource == s.IsResource && Removed == s.Removed &&
                                  Options.Count == s.Options.Count && Options.Zip(s.Options).All(p => p.First.SameAs(p.Second));

    public override string ToString() => $"{(Removed ? "-" : "")}{TitleSource}[{string.Join(", ", Options)}]";
}

public sealed class RecipeState
{
    public required string Id { get; init; }
    public double? CookingTime { get; set; }
    public double? CookingTemperature { get; set; }
    public List<string> Utilities { get; set; } = [];
    /// <summary>["text", "RecipeName"/"Description", …] of the mod (null = the game's).</summary>
    public string? Name { get; set; }
    public string? Description { get; set; }
    /// <summary>["result", item] of the mod (null = the game's).</summary>
    public string? Result { get; set; }
    /// <summary>Other ["set", property, value] numbers (kept as they are).</summary>
    public SortedDictionary<string, double> OtherNumbers { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<Slot>> Slots { get; } = new() { [Groups.Main] = [], [Groups.Optional] = [] };

    public List<Slot> Main => Slots[Groups.Main];
    public List<Slot> Optional => Slots[Groups.Optional];

    /// <summary>The recipe as the game has it.</summary>
    public static RecipeState FromGame(RecipeInfo r)
    {
        var s = new RecipeState { Id = r.Id, CookingTime = r.CookingTime, CookingTemperature = r.CookingTemperature, Utilities = [.. r.Utilities] };
        foreach (var (group, list) in new[] { (Groups.Main, r.Main), (Groups.Optional, r.Optional) })
            for (var i = 0; i < list.Count; i++)
                s.Slots[group].Add(new Slot
                {
                    Title = list[i].Title, TitleSource = list[i].TitleSource, IsResource = list[i].IsResource,
                    Options = list[i].Options.Select(SlotOption.From).ToList(), Origin = i,
                });
        return s;
    }

    public RecipeState Clone()
    {
        var c = new RecipeState
        {
            Id = Id, CookingTime = CookingTime, CookingTemperature = CookingTemperature, Utilities = [.. Utilities],
            Name = Name, Description = Description, Result = Result,
        };
        foreach (var (k, v) in OtherNumbers) c.OtherNumbers[k] = v;
        foreach (var g in Groups.All) c.Slots[g].AddRange(Slots[g].Select(x => x.Clone()));
        return c;
    }

    /// <summary>Same recipe in the game (what a build of either gives); removed slots count, as they keep the mod's marks.</summary>
    public bool SameAs(RecipeState o) =>
        Nullable.Equals(CookingTime, o.CookingTime) && Nullable.Equals(CookingTemperature, o.CookingTemperature) &&
        Utilities.SequenceEqual(o.Utilities) && Name == o.Name && Description == o.Description && Result == o.Result &&
        OtherNumbers.SequenceEqual(o.OtherNumbers) &&
        Groups.All.All(g => Live(g).Count() == o.Live(g).Count() && Live(g).Zip(o.Live(g)).All(p => p.First.SameAs(p.Second) && p.First.Title == p.Second.Title));

    /// <summary>Slots of a group that the game will have (not removed), in order.</summary>
    public IEnumerable<Slot> Live(string group) => Slots[group].Where(s => !s.Removed);

    // ------------------------------------------------------------------ applying modkit ops

    /// <summary>Applies the JSON ops of cooking.recipes[Id] (modkit.json) in order.</summary>
    public void Apply(IEnumerable<JsonElement> ops)
    {
        foreach (var op in ops) Apply(op);
    }

    public void Apply(JsonElement a)
    {
        string S(int i) => a[i].GetString()!;
        var kind = S(0);
        switch (kind)
        {
            case "set":
                if (S(1) == "CookingTime") CookingTime = a[2].GetDouble();
                else if (S(1) == "CookingTemperature") CookingTemperature = a[2].GetDouble();
                else OtherNumbers[S(1)] = a[2].GetDouble();
                break;
            case "text":
                if (S(1) == "RecipeName") Name = S(2);
                else if (S(1) == "Description") Description = S(2);
                else throw new NotSupportedException($"text {S(1)}");
                break;
            case "result": Result = S(1); break;
            case "utility": Utilities = a[1].EnumerateArray().Select(t => t.GetString()!).ToList(); break;
            case "remove":
            {
                var slot = Find(S(1), a[2]);
                if (slot.IsNew) Slots[S(1)].Remove(slot);
                else slot.Removed = true;
                break;
            }
            case "add_option":
            {
                var slot = Find(S(1), a[2]);
                var v = Variant.Parse(a, 3);
                var src = v.Resource != null
                    ? slot.Options.FirstOrDefault(o => o.IsResource) ?? slot.Options[0]
                    : slot.Options.FirstOrDefault(o => !o.IsResource) ?? throw new InvalidOperationException($"{Id}: {a[2]} has only liquids, nothing to copy.");
                slot.Options.Add(v.Fill(src.Clone()));
                if (v.Resource != null) slot.IsResource = slot.Options.All(o => o.IsResource);
                break;
            }
            case "liters":
            {
                var slot = Find(S(1), a[2]);
                int? only = a.GetArrayLength() > 4 ? a[4].GetInt32() : null;
                for (var i = 0; i < slot.Options.Count; i++)
                    if (only == null || only == i) slot.Options[i].Liters = a[3].GetDouble();
                break;
            }
            case "usage":
            {
                var slot = Find(S(1), a[2]);
                int? only = a.GetArrayLength() > 4 ? a[4].GetInt32() : null;
                for (var i = 0; i < slot.Options.Count; i++)
                    if (only == null || only == i) slot.Options[i].Usage = a[3].GetInt32();
                break;
            }
            case "nutrient":
            {
                var slot = Find(S(1), a[2]);
                int? only = a.GetArrayLength() > 4 ? a[4].GetInt32() : null;
                for (var i = 0; i < slot.Options.Count; i++)
                    if (only == null || only == i) slot.Options[i].Nutrient = a[3].GetDouble();
                break;
            }
            case "set_options":
            {
                var slot = Find(S(1), a[2]);
                slot.Options = Variants(slot, a[3]);
                slot.IsResource = slot.Options.Any(o => o.IsResource) && slot.Options.All(o => o.IsResource);
                break;
            }
            case "add_ingredient":
            {
                var group = S(1);
                if (!Live(group).Any()) // modkit clones the group's first element
                    throw new InvalidOperationException($"{Id}: add_ingredient needs a slot in {group} to copy.");
                var options = Variants(null, a[3]);
                Slots[group].Add(new Slot
                {
                    Title = S(2), TitleSource = S(2), TitleEdited = true, Options = options,
                    IsResource = options.Any(o => o.IsResource) && options.All(o => o.IsResource),
                });
                break;
            }
            case "title":
            {
                var slot = Find(S(1), a[2]);
                slot.Title = slot.TitleSource = S(3);
                slot.TitleEdited = true;
                break;
            }
            default: throw new NotSupportedException($"Unknown recipe op {kind}.");
        }
    }

    /// <summary>A slot by title (as modkit matches it: the source string) or by index among the live slots.</summary>
    public Slot Find(string group, JsonElement which)
    {
        var live = Live(group).ToList();
        if (which.ValueKind == JsonValueKind.Number) return live[which.GetInt32()];
        var title = which.GetString();
        var hits = live.Where(s => s.TitleSource == title).ToList();
        if (hits.Count != 1)
            throw new InvalidOperationException($"{Id}: {group} ingredient {title}: {hits.Count} matches, have {string.Join(", ", live.Select(s => s.TitleSource))}.");
        return hits[0];
    }

    /// <summary>The item option modkit copies for new variants: the slot's first item option, else the first in the recipe.</summary>
    public SlotOption ItemTemplate(Slot? slot)
    {
        var pools = new List<Slot>();
        if (slot != null) pools.Add(slot);
        foreach (var g in Groups.All) pools.AddRange(Live(g));
        foreach (var s in pools)
            foreach (var o in s.Options)
                if (!o.IsResource) return o;
        throw new InvalidOperationException($"{Id}: the recipe has no item option to copy.");
    }

    /// <summary>The liquid option modkit copies for liquid variants: the slot's first liquid, else the recipe's, else null.</summary>
    public SlotOption? LiquidTemplate(Slot? slot)
    {
        var pools = new List<Slot>();
        if (slot != null) pools.Add(slot);
        foreach (var g in Groups.All) pools.AddRange(Live(g));
        return pools.SelectMany(s => s.Options).FirstOrDefault(o => o.IsResource);
    }

    /// <summary>Variants of set_options / add_ingredient: items copy the item template, liquids the liquid one (or the item one).</summary>
    private List<SlotOption> Variants(Slot? slot, JsonElement list)
    {
        var parsed = list.EnumerateArray().Select(e => Variant.Parse(e)).ToList();
        var tpl = ItemTemplate(slot);
        var liquid = parsed.Any(v => v.Resource != null) ? LiquidTemplate(slot) : null;
        return parsed.Select(v => v.Fill((v.Resource != null ? liquid ?? tpl : tpl).Clone())).ToList();
    }
}

/// <summary>A variant as modkit.json writes it: [item, whole, usage(, liters(, resource))] or an object; item "" = any container.</summary>
public sealed record Variant(string Item, bool Whole, int Usage, double? Liters, string? Resource)
{
    public static Variant Parse(JsonElement o)
    {
        static double? D(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;
        static string? R(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        if (o.ValueKind == JsonValueKind.Object)
            return new Variant(o.GetProperty("item").GetString() ?? "", o.GetProperty("whole").GetBoolean(), o.GetProperty("usage").GetInt32(),
                o.TryGetProperty("liters", out var l) ? D(l) : null, o.TryGetProperty("resource", out var r) ? R(r) : null);
        return new Variant(o[0].GetString() ?? "", o[1].GetBoolean(), o[2].GetInt32(),
            o.GetArrayLength() > 3 ? D(o[3]) : null, o.GetArrayLength() > 4 ? R(o[4]) : null);
    }

    /// <summary>add_option: ["add_option", group, ingr, item, whole, usage(, liters(, resource))] from index <paramref name="at"/>.</summary>
    public static Variant Parse(JsonElement a, int at)
    {
        static double? D(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;
        static string? R(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        return new Variant(a[at].GetString() ?? "", a[at + 1].GetBoolean(), a[at + 2].GetInt32(),
            a.GetArrayLength() > at + 3 ? D(a[at + 3]) : null, a.GetArrayLength() > at + 4 ? R(a[at + 4]) : null);
    }

    /// <summary>modkit's Fill: item ("" = None), whole, usage; a resource makes it a liquid; liters when given.</summary>
    public SlotOption Fill(SlotOption o)
    {
        o.Item = Item.Length > 0 ? Item : null;
        o.Whole = Whole;
        o.Usage = Usage;
        if (Resource != null) o.Resource = Resource;
        if (Liters != null) o.Liters = Liters.Value;
        return o;
    }
}
