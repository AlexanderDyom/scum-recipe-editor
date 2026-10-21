// The edited recipe -> cooking ops of modkit.json (cooking.recipes[recipe]), the same JSON arrays Food_Mod/modkit.json
// holds. Ops are generated against a working copy of the game's recipe and applied to it as they are written, so every
// reference (slot title when unique, else index) is valid at the point modkit applies it. Order: numbers, texts, then
// per group: changes of kept slots, removals (last first), added slots.
using System.Text.Json;
using System.Text.Json.Nodes;
using RecipeEditor.Core.Game;

namespace RecipeEditor.Core.Recipe;

public static class RecipeOps
{
    /// <summary>
    /// Ops that turn the game's recipe into <paramref name="target"/>; empty when it is unchanged.
    /// NotSupportedException for edits modkit cannot express (e.g. removing a time the game sets).
    /// </summary>
    public static List<JsonNode> Generate(RecipeInfo game, RecipeState target)
    {
        var ops = new List<JsonNode>();
        var w = RecipeState.FromGame(game);
        void Emit(JsonArray op)
        {
            ops.Add(op);
            using var doc = JsonDocument.Parse(op.ToJsonString());
            w.Apply(doc.RootElement);
        }

        if (target.Name != null) Emit(["text", "RecipeName", target.Name]);
        if (target.Description != null) Emit(["text", "Description", target.Description]);
        if (target.Result != null) Emit(["result", target.Result]);
        if (!Nullable.Equals(target.CookingTime, w.CookingTime)) Emit(["set", "CookingTime", Number(target.CookingTime, "CookingTime")]);
        if (!Nullable.Equals(target.CookingTemperature, w.CookingTemperature))
            Emit(["set", "CookingTemperature", Number(target.CookingTemperature, "CookingTemperature")]);
        foreach (var (k, v) in target.OtherNumbers)
            if (!w.OtherNumbers.TryGetValue(k, out var have) || have != v) Emit(["set", k, v]);
        if (!target.Utilities.SequenceEqual(w.Utilities))
            Emit(["utility", new JsonArray(target.Utilities.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray())]);

        foreach (var g in Groups.All)
        {
            // kept game slots: variants, amounts, litres, nutrition, title
            foreach (var t in target.Slots[g].Where(s => !s.IsNew && !s.Removed))
            {
                var c = w.Slots[g].First(s => s.Origin == t.Origin);
                if (!SameVariants(c, t)) foreach (var op in SlotOps(w, g, w.Slots[g].IndexOf(c), t, target.Id)) Emit(op);
                if (t.TitleSource != c.TitleSource) Emit(["title", g, Ref(w, g, c), t.TitleSource]);
            }
            // added slots first (modkit copies the group's first slot, so a recipe "from scratch" still has one to copy)
            foreach (var t in target.Slots[g].Where(s => s.IsNew && !s.Removed))
            {
                if (t.Options.Count == 0) throw new NotSupportedException($"{target.Id}: the new slot {t.Title} has no variants.");
                Emit(["add_ingredient", g, t.TitleSource, VariantList(t.Options)]);
                var c = w.Slots[g].Last();
                foreach (var op in Fix(w, g, c, t)) Emit(op);
            }
            // removed game slots, from the last one, so indexes of the others stay valid (added slots come after them)
            foreach (var t in target.Slots[g].Where(s => !s.IsNew && s.Removed).OrderByDescending(s => s.Origin))
            {
                var c = w.Slots[g].First(s => s.Origin == t.Origin);
                if (!c.Removed) Emit(["remove", g, Ref(w, g, c)]);
            }
        }

        if (!target.Live(Groups.Main).Any()) throw new NotSupportedException($"{target.Id}: a recipe needs at least one main ingredient.");

        if (!w.SameAs(target)) throw new InvalidOperationException($"{target.Id}: the generated ops do not give the edited recipe.");
        return ops;
    }

    private static double Number(double? v, string what) =>
        v ?? throw new NotSupportedException($"{what}: the game sets a value, a mod cannot remove it (set a number instead).");

    /// <summary>Title when exactly one live slot of the group has it (as modkit matches), else the index among live slots.</summary>
    private static JsonNode Ref(RecipeState w, string group, Slot slot)
    {
        var live = w.Live(group).ToList();
        return live.Count(s => s.TitleSource == slot.TitleSource) == 1 ? JsonValue.Create(slot.TitleSource)! : JsonValue.Create(live.IndexOf(slot))!;
    }

    private static void Apply(RecipeState w, JsonArray op)
    {
        using var doc = JsonDocument.Parse(op.ToJsonString());
        w.Apply(doc.RootElement);
    }

    /// <summary>A variant in modkit.json: [item, whole, usage] or, for a liquid, [item or "", whole, usage, liters, resource].</summary>
    private static JsonArray VariantJson(SlotOption o) => o.IsResource
        ? new JsonArray(o.Item ?? "", o.Whole, o.Usage, o.Liters, o.Resource)
        : new JsonArray(o.Item, o.Whole, o.Usage);

    private static JsonArray VariantList(IEnumerable<SlotOption> options) => new(options.Select(o => (JsonNode)VariantJson(o)).ToArray());

    private static bool SameKind(SlotOption a, SlotOption b) => a.Item == b.Item && a.Resource == b.Resource && a.Whole == b.Whole;

    private static bool SameVariants(Slot a, Slot b) =>
        a.IsResource == b.IsResource && a.Options.Count == b.Options.Count && a.Options.Zip(b.Options).All(p => p.First.SameAs(p.Second));

    /// <summary>
    /// Ops for a kept slot whose variants changed, the shortest that works: amounts only, variants added at the end, or
    /// all variants replaced; each tried on a copy and kept only when it gives exactly the edited slot.
    /// </summary>
    private static List<JsonArray> SlotOps(RecipeState w, string g, int index, Slot t, string id)
    {
        if (t.Options.Count == 0) throw new NotSupportedException($"{id}: {t.Title}: a slot needs a variant (remove the slot instead).");
        var strategies = new List<Func<RecipeState, Slot, List<JsonArray>?>>
        {
            (_, c) => c.Options.Count == t.Options.Count && c.Options.Zip(t.Options).All(p => SameKind(p.First, p.Second)) ? [] : null,
            (s, c) =>
            {
                if (t.Options.Count <= c.Options.Count || !c.Options.Zip(t.Options).All(p => SameKind(p.First, p.Second))) return null;
                var extra = t.Options.Skip(c.Options.Count).ToList();
                if (extra.Any(o => !o.IsResource) && c.Options.All(o => o.IsResource)) return null; // modkit copies an item variant
                return extra.Select(o => o.IsResource
                    ? new JsonArray("add_option", g, Ref(s, g, c), o.Item ?? "", o.Whole, o.Usage, o.Liters, o.Resource)
                    : new JsonArray("add_option", g, Ref(s, g, c), o.Item, o.Whole, o.Usage)).ToList();
            },
            (s, c) => [new JsonArray("set_options", g, Ref(s, g, c), VariantList(t.Options))],
        };
        foreach (var structure in strategies)
        {
            var trial = w.Clone();
            var c = trial.Slots[g][index];
            var ops = structure(trial, c);
            if (ops == null) continue;
            foreach (var op in ops) Apply(trial, op);
            ops.AddRange(Fix(trial, g, c, t));
            if (SameVariants(c, t)) return ops;
        }
        throw new NotSupportedException($"{id}: {t.Title}: these variants cannot be written as modkit ops.");
    }

    /// <summary>Amounts, litres and nutrition per variant (one op for all when they are all the same); applied to w as written.</summary>
    private static List<JsonArray> Fix(RecipeState w, string g, Slot c, Slot t)
    {
        var ops = new List<JsonArray>();
        if (c.Options.Count != t.Options.Count) return ops;
        void Per(string kind, Func<SlotOption, double> get, Func<int, bool> applies)
        {
            var idx = Enumerable.Range(0, t.Options.Count).Where(applies).ToList();
            var diff = idx.Where(i => Math.Abs(get(c.Options[i]) - get(t.Options[i])) > 1e-6).ToList();
            if (diff.Count == 0) return;
            List<JsonArray> now;
            if (idx.Count == t.Options.Count && idx.Select(i => get(t.Options[i])).Distinct().Count() == 1)
                now = [new JsonArray(kind, g, Ref(w, g, c), Value(kind, get(t.Options[0])))];
            else now = diff.Select(i => new JsonArray(kind, g, Ref(w, g, c), Value(kind, get(t.Options[i])), i)).ToList();
            foreach (var op in now) Apply(w, op);
            ops.AddRange(now);
        }
        Per("usage", o => o.Usage, _ => true);
        Per("liters", o => o.Liters, i => t.Options[i].IsResource);
        Per("nutrient", o => o.Nutrient, _ => true);
        return ops;
    }

    private static JsonNode Value(string kind, double v) => kind == "usage" ? JsonValue.Create((int)v)! : JsonValue.Create(v)!;
}
