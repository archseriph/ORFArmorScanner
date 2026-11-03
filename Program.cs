using System.Text.RegularExpressions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Synthesis;
using Noggog;

// ORF Armor Scanner — infers Outfit Recognition Framework keywords for armor
// that neither the base ORF plugin nor FIBBI's per-mod KID patches cover.
//
// Two independent passes, same split as the base ORF KID files use (keyword
// translation where a reliable distributed source exists, text matching where
// it doesn't):
//
//   Pass 1 (skimpiness style) — translates existing SLA_ (SexLab Aroused)
//   keywords already on the armor into the matching ORF_ keyword, the same
//   approach ANDArmorScanner uses for AND_. SLA_ coverage is already broad
//   across SLAL-tagged armor mods, so this is far more reliable than guessing
//   "tight"/"revealing"/etc. from a name.
//
//   Pass 2 (color + garment-type style) — regex word-match against the
//   armor's display Name, EditorID, and (optionally) its ArmorAddon model
//   filenames. There's no distributed keyword source for color or for
//   garment-type words like "robes"/"hooded"/"sturdy", so this is the only
//   option for them — same thing the base ORF file does with its *Daedric
//   Armor / *Hooded wildcard patterns, just generalized across the whole
//   load order instead of enumerated per plugin.
//
// Color is treated as singular: an armor that already carries any ORF_ color
// keyword (from the base ORF plugin or a FIBBI patch) is left alone rather
// than risk a conflicting second color. Style keywords are additive and
// stack, matching how the base ORF file already layers multiple style
// keywords on the same vanilla armor set.

namespace ORFArmorScanner;

public class Program
{
    private static readonly Dictionary<string, string[]> SlaToOrf =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Bra / chest-only ─────────────────────────────────────────────
        ["SLA_BraBikini"]            = ["ORF_Bra"],
        ["SLA_ArmorHarness"]         = ["ORF_Bra"],
        ["SLA_PastiesNipples"]       = ["ORF_Revealing"],
        ["SLA_ArmorIllegal"]        = ["ORF_Revealing"],
        ["SLA_ImpossibleClothes"]   = ["ORF_NearlyNaked"],
        ["SLA_ArmorBondage"]        = ["ORF_Revealing"],

        // ── Underwear / pelvis-only ───────────────────────────────────────
        ["SLA_ThongT"]              = ["ORF_Underwear", "ORF_Transparent"],
        ["SLA_ThongLowleg"]         = ["ORF_Underwear"],
        ["SLA_ThongString"]        = ["ORF_Underwear"],
        ["SLA_ThongGstring"]       = ["ORF_Underwear"],
        ["SLA_PantiesThong"]       = ["ORF_Underwear"],
        ["SLA_PastiesCrotch"]      = ["ORF_Revealing"],
        ["SLA_PantiesNormal"]      = ["ORF_Underwear"],
        ["SLA_ArmorHalfNakedBikini"] = ["ORF_Bra", "ORF_Underwear"],

        // ── Bottom coverage ───────────────────────────────────────────────
        ["SLA_MicroHotPants"]      = ["ORF_Revealing"],
        ["SLA_MiniSkirt"]          = ["ORF_ShortSkirt"],
        ["SLA_MicroSkirt"]         = ["ORF_ShortSkirt"],
        ["SLA_ArmorPartBottom"]    = ["ORF_Revealing"],
        ["SLA_PelvicCurtain"]      = ["ORF_Revealing"],
        ["SLA_ShowgirlSkirt"]      = ["ORF_ShortSkirt"],

        // ── Body / top coverage ───────────────────────────────────────────
        ["SLA_ArmorHalfNaked"]     = ["ORF_Revealing"],
        ["SLA_ArmorSpendex"]       = ["ORF_Tight"],
        ["SLA_ArmorLewdLeotard"]   = ["ORF_Revealing"],
        ["SLA_ArmorTransparent"]  = ["ORF_Transparent"],
    };

    private static readonly string[] SkimpinessStyleEdids =
    {
        "ORF_Bra", "ORF_Underwear", "ORF_Revealing", "ORF_NearlyNaked",
        "ORF_ShortSkirt", "ORF_Tight", "ORF_Transparent", "ORF_Flimsy"
    };

    private static readonly string[] ColorEdids =
    {
        "ORF_Red", "ORF_Blue", "ORF_Green", "ORF_Yellow", "ORF_Black", "ORF_Grey",
        "ORF_Brown", "ORF_White", "ORF_Pink", "ORF_Purple", "ORF_Silver", "ORF_Gold",
        "ORF_Colourful"
    };

    // Order matters: first match wins for color (treated as singular).
    private static readonly (Regex Pattern, string Edid)[] ColorPatterns =
    {
        (new Regex(@"\b(colou?rful|rainbow|multicolou?red?)\b", RegexOptions.IgnoreCase), "ORF_Colourful"),
        (new Regex(@"\bred\b", RegexOptions.IgnoreCase), "ORF_Red"),
        (new Regex(@"\bblue\b", RegexOptions.IgnoreCase), "ORF_Blue"),
        (new Regex(@"\bgreen\b", RegexOptions.IgnoreCase), "ORF_Green"),
        (new Regex(@"\byellow\b", RegexOptions.IgnoreCase), "ORF_Yellow"),
        (new Regex(@"\bblack\b", RegexOptions.IgnoreCase), "ORF_Black"),
        (new Regex(@"\bgr[ae]y\b", RegexOptions.IgnoreCase), "ORF_Grey"),
        (new Regex(@"\bbrown\b", RegexOptions.IgnoreCase), "ORF_Brown"),
        (new Regex(@"\bwhite\b", RegexOptions.IgnoreCase), "ORF_White"),
        (new Regex(@"\bpink\b", RegexOptions.IgnoreCase), "ORF_Pink"),
        (new Regex(@"\b(purple|violet)\b", RegexOptions.IgnoreCase), "ORF_Purple"),
        (new Regex(@"\bsilver\b", RegexOptions.IgnoreCase), "ORF_Silver"),
        (new Regex(@"\bgold(en)?\b", RegexOptions.IgnoreCase), "ORF_Gold"),
    };

    // Additive — every pattern that matches gets added.
    private static readonly (Regex Pattern, string Edid)[] StylePatterns =
    {
        (new Regex(@"\brobes?\b", RegexOptions.IgnoreCase), "ORF_Robes"),
        (new Regex(@"\bhood(ed)?\b", RegexOptions.IgnoreCase), "ORF_Hooded"),
        (new Regex(@"\bdress\b", RegexOptions.IgnoreCase), "ORF_Dress"),
        (new Regex(@"\b(classy|noble|elegant|fancy)\b", RegexOptions.IgnoreCase), "ORF_Classy"),
        (new Regex(@"\b(scruffy|ragged|tattered|beggar|prisoner|rags)\b", RegexOptions.IgnoreCase), "ORF_Scruffy"),
        (new Regex(@"\bcleavage\b", RegexOptions.IgnoreCase), "ORF_Cleavage"),
        (new Regex(@"\b(flimsy|fragile|frail)\b", RegexOptions.IgnoreCase), "ORF_Flimsy"),
        (new Regex(@"\b(transparent|sheer|see-?through|fishnet)\b", RegexOptions.IgnoreCase), "ORF_Transparent"),
        (new Regex(@"\bbra\b", RegexOptions.IgnoreCase), "ORF_Bra"),
        (new Regex(@"\b(underwear|panties|lingerie)\b", RegexOptions.IgnoreCase), "ORF_Underwear"),
        (new Regex(@"\b(nearly\s*naked|almost\s*naked)\b", RegexOptions.IgnoreCase), "ORF_NearlyNaked"),
        (new Regex(@"\b(short\s*skirt|mini\s*skirt|micro\s*skirt)\b", RegexOptions.IgnoreCase), "ORF_ShortSkirt"),
        // Material/tier words also used by the base ORF file's *Daedric Armor-style
        // wildcards, generalized here to match anywhere in the name/model text.
        (new Regex(@"\b(sturdy|reinforced|plated?|daedric|dragonplate|dragonscale|ebony|orcish|nordic|stalhrim|chitin|steel\s*plate|blades)\b", RegexOptions.IgnoreCase), "ORF_Sturdy"),
        (new Regex(@"\b(intimidating|menacing|dread(ful)?|demonic|nightingale)\b", RegexOptions.IgnoreCase), "ORF_Intimidating"),
    };

    private static Lazy<Settings> _settings = null!;

    public static async Task<int> Main(string[] args)
    {
        return await SynthesisPipeline.Instance
            .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
            .SetAutogeneratedSettings("Settings", "settings.json", out _settings)
            .SetTypicalOpen(GameRelease.SkyrimSE, "ORFArmorScanner.esp")
            .Run(args);
    }

    public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
    {
        var settings = _settings.Value;

        var orfKeywords = new Dictionary<string, FormKey>(StringComparer.OrdinalIgnoreCase);
        var slaKeywords = new Dictionary<FormKey, string>();

        foreach (var kw in state.LoadOrder.PriorityOrder.Keyword().WinningOverrides())
        {
            if (kw.EditorID == null) continue;
            if (kw.EditorID.StartsWith("ORF_", StringComparison.OrdinalIgnoreCase))
                orfKeywords[kw.EditorID] = kw.FormKey;
            if (kw.EditorID.StartsWith("SLA_", StringComparison.OrdinalIgnoreCase))
                slaKeywords[kw.FormKey] = kw.EditorID;
        }

        if (orfKeywords.Count == 0)
        {
            Console.WriteLine("[ERROR] No ORF_ keywords found. Ensure 'Outfit Recognition Framework.esp' is active.");
            return;
        }
        if (slaKeywords.Count == 0)
            Console.WriteLine("[WARN] No SLA_ keywords found in the load order — skimpiness-style pass will rely on name text only.");

        var colorFormKeys = ColorEdids
            .Where(orfKeywords.ContainsKey)
            .Select(e => orfKeywords[e])
            .ToHashSet();

        Console.WriteLine($"Found {orfKeywords.Count} ORF_ keywords, {slaKeywords.Count} SLA_ keywords.");

        int tagged = 0, skippedExcluded = 0, skippedNothing = 0;
        var colorlessCandidates = new List<(ModKey ModKey, string Label)>();

        foreach (var armorCtx in state.LoadOrder.PriorityOrder.Armor().WinningContextOverrides())
        {
            var armor = armorCtx.Record;

            if (armor.MajorFlags.HasFlag(Armor.MajorFlag.NonPlayable)) continue;

            if (settings.ExcludedPlugins.Contains(armorCtx.ModKey.FileName))
            {
                skippedExcluded++;
                continue;
            }

            var existingEdids = (armor.Keywords ?? Enumerable.Empty<IFormLinkGetter<IKeywordGetter>>())
                .Select(k => k.FormKey)
                .ToHashSet();
            bool hasColor = existingEdids.Overlaps(colorFormKeys);

            var textParts = new List<string>();
            if (armor.Name?.TryLookup(Language.English, out var name) == true && !string.IsNullOrWhiteSpace(name))
                textParts.Add(name);
            if (!string.IsNullOrWhiteSpace(armor.EditorID))
                textParts.Add(armor.EditorID!);

            if (settings.ScanModelPaths && armor.Armature != null)
            {
                foreach (var armatureLink in armor.Armature)
                {
                    if (!state.LinkCache.TryResolve<IArmorAddonGetter>(armatureLink.FormKey, out var addon)) continue;
                    var male = addon.WorldModel?.Male?.File;
                    var female = addon.WorldModel?.Female?.File;
                    if (!string.IsNullOrWhiteSpace(male))
                        textParts.Add(Path.GetFileNameWithoutExtension(male));
                    if (!string.IsNullOrWhiteSpace(female))
                        textParts.Add(Path.GetFileNameWithoutExtension(female));
                }
            }

            var combinedText = string.Join(" | ", textParts);

            var toAdd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool slaMatchedAny = false;

            // Pass 1: SLA_ -> ORF_ skimpiness-style translation.
            if (armor.Keywords != null)
            {
                foreach (var kwLink in armor.Keywords)
                {
                    if (!slaKeywords.TryGetValue(kwLink.FormKey, out var slaEdid)) continue;
                    if (!SlaToOrf.TryGetValue(slaEdid, out var orfEdids)) continue;
                    slaMatchedAny = true;
                    foreach (var orfEdid in orfEdids)
                        if (orfKeywords.TryGetValue(orfEdid, out var orfFk) && !existingEdids.Contains(orfFk))
                            toAdd.Add(orfEdid);
                }
            }

            // Pass 2a: garment-type/aesthetic style text matching (additive).
            if (combinedText.Length > 0)
            {
                foreach (var (pattern, edid) in StylePatterns)
                {
                    if (!orfKeywords.ContainsKey(edid)) continue;
                    if (existingEdids.Contains(orfKeywords[edid])) continue;
                    if (pattern.IsMatch(combinedText))
                        toAdd.Add(edid);
                }

                // Pass 2b: color text matching — only if no color keyword yet.
                if (!hasColor)
                {
                    foreach (var (pattern, edid) in ColorPatterns)
                    {
                        if (!orfKeywords.ContainsKey(edid)) continue;
                        if (!pattern.IsMatch(combinedText)) continue;
                        toAdd.Add(edid);
                        hasColor = true;
                        break;
                    }
                }
            }

            bool hasSkimpySignal = slaMatchedAny || existingEdids.Any(fk => SkimpinessStyleEdids
                .Where(orfKeywords.ContainsKey)
                .Select(e => orfKeywords[e])
                .Contains(fk));

            if (settings.ReportColorlessCandidates && hasSkimpySignal && !hasColor)
            {
                colorlessCandidates.Add((armorCtx.ModKey, armor.EditorID ?? armor.FormKey.ToString()));
            }

            if (toAdd.Count == 0)
            {
                skippedNothing++;
                continue;
            }

            var resolved = new List<FormLink<IKeyword>>();
            foreach (var edid in toAdd)
            {
                if (orfKeywords.TryGetValue(edid, out var fk))
                    resolved.Add(new FormLink<IKeyword>(fk));
            }
            if (resolved.Count == 0)
            {
                skippedNothing++;
                continue;
            }

            var patch = state.PatchMod.Armors.GetOrAddAsOverride(armor);
            patch.Keywords ??= new ExtendedList<IFormLinkGetter<IKeywordGetter>>();
            foreach (var link in resolved)
                patch.Keywords.Add(link);

            var label = armor.EditorID ?? armor.FormKey.ToString();
            Console.WriteLine($"  [TAG] {armorCtx.ModKey.FileName} :: {label}  ->  {string.Join(", ", toAdd)}");
            tagged++;
        }

        Console.WriteLine();
        Console.WriteLine("=== ORF Armor Scanner complete ===");
        Console.WriteLine($"  Tagged:              {tagged}");
        Console.WriteLine($"  Nothing to add:      {skippedNothing}");
        Console.WriteLine($"  Excluded plugin:     {skippedExcluded}");

        if (settings.ReportColorlessCandidates && colorlessCandidates.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("=== Armor with a skimpiness style but no color keyword (manual KID candidates) ===");
            foreach (var candidate in colorlessCandidates
                         .OrderBy(c => c.ModKey.FileName.String, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  {candidate.ModKey.FileName} :: {candidate.Label}");
            }
            Console.WriteLine($"  Total: {colorlessCandidates.Count} — add these to a hand-written ORF KID file.");
        }
    }
}
