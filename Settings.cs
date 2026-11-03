using Mutagen.Bethesda.Synthesis.Settings;

namespace ORFArmorScanner;

public class Settings
{
    [SynthesisTooltip(
        "Plugins skipped entirely. Add mods that already ship hand-authored ORF KID files " +
        "(base ORF, FIBBI's per-mod patches, your own manual entries) to avoid re-tagging " +
        "their records with a possibly-conflicting guess.")]
    public HashSet<string> ExcludedPlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "Outfit Recognition Framework.esp",
    };

    [SynthesisTooltip(
        "Also text-match each armor's ArmorAddon model filenames (male/female World Model), " +
        "not just its display Name and EditorID. Helps when the in-game name is generic " +
        "(\"Outfit 1\") but the mesh/texture filename encodes color or style.")]
    public bool ScanModelPaths = true;

    [SynthesisTooltip(
        "After the run, print armor that got at least one ORF_ skimpiness-style keyword " +
        "(from the SLA_ mapping or name text) but no ORF_ color keyword at all. These are " +
        "candidates for a hand-written KID entry — color is the weakest signal this patcher has.")]
    public bool ReportColorlessCandidates = true;
}
