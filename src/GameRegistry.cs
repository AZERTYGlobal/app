// Liste hardcodée de jeux et frameworks gaming pour la couche compatibilité.
//
// Sources : rapports Gemini et Perplexity du 2026-04-26 (recherches Internet
// approfondies sur les anti-cheats kernel-level avec bans documentés pour
// outils de remapping clavier — AHK, reWASD, PowerToys Keyboard Manager).
//
// Critère d'inclusion anti-cheat : driver Windows ring 0 qui scanne la mémoire
// des processes et peut détecter les hooks ou inputs synthétiques. Les anti-cheats
// user-mode permissifs (VAC standard sans extension) sont écartés.
//
// Cette liste vit dans le code source — sa mise à jour nécessite une nouvelle
// release. À l'horizon v1.0+, envisager de la charger depuis un fichier embarqué
// ou téléchargé périodiquement (cf. plan v0.9.7 § Évolutions futures).

namespace AZERTYGlobal;

internal static class GameRegistry
{
    /// <summary>
    /// Termes-clés (sous-chaîne, case-insensitive) qui identifient un process protégé
    /// par un anti-cheat kernel-level avec risque de ban pour injection de frappes.
    /// AZERTY Global se désactive complètement quand ces processes sont au foreground.
    /// </summary>
    public static readonly string[] AntiCheatTerms =
    {
        "valorant",         // Riot Vanguard (kernel ring 0, boot-start)
        "league of legends",// Riot Vanguard (depuis mai 2024)
        "fortnite",         // Easy Anti-Cheat (kernel)
        "r5apex",           // Apex Legends — EAC
        "blackops",         // Call of Duty: Black Ops 6 — RICOCHET (kernel)
        "modernwarfare",    // CoD: Modern Warfare — RICOCHET
        "tslgame",          // PUBG — BattlEye (kernel)
        "rainbowsix",       // R6 Siege — BattlEye
        "tarkov",           // Escape from Tarkov — BattlEye
        "genshin",          // Genshin Impact — mhyprot2 (kernel)
        "starrail",         // Honkai: Star Rail — mhyprot3
        "roblox",           // Roblox — Hyperion / Byfron (anti-tamper)
        "faceit",           // FACEIT AC (kernel) — bloque PowerToys Keyboard Manager
        "bf2042",           // Battlefield 2042 — EAC
        "deltaforce",       // Delta Force — Tencent ACE (kernel)
        "marvel-win64",     // Marvel Rivals — NetEase AC
        "helldivers2",      // Helldivers 2 — nProtect GameGuard
    };

    /// <summary>
    /// Match exact + chemin contenant un fragment (pour les noms ambigus).
    /// `cod.exe` = Call of Duty officiel ; `ace.exe` est ambigu donc on exige
    /// que le chemin contienne `\Tencent\` (ACE = Tencent Anti-Cheat Expert).
    /// </summary>
    public static readonly (string Exact, string? PathContains)[] AntiCheatExactWithPath =
    {
        ("cod.exe", null),
        ("ace.exe", @"\Tencent\"),
    };

    /// <summary>
    /// Termes niveau 2 — désactivation par précaution (anti-cheat kernel mais
    /// politique moins stricte vs remappers, ou ban risqué mais non documenté).
    /// </summary>
    public static readonly string[] PrecautionAntiCheatTerms =
    {
        "destiny2",          // BattlEye
        "rustclient",        // EAC
        "narakabladepoint",  // Tencent ACE
        "fc25",              // EA Sports FC 25 — EA AC
        "fc26",              // EA Sports FC 26 — EA AC
        "haloinfinite",      // EAC
        "fallguys_client",   // EAC
    };

    /// <summary>
    /// Signatures de DLL chargées qui indiquent un framework filtrant VK_PACKET
    /// et nécessitant une combo native pour les caractères AZ Global hors layout natif.
    /// Tout match (case-insensitive, sur le nom court de la DLL) déclenche le mode NativeCombo.
    /// </summary>
    public static readonly string[] GameFrameworkDlls =
    {
        // GLFW (Minecraft Java natif, beaucoup d'indés OpenGL/Vulkan)
        "glfw3.dll",
        "glfw.dll",
        // LWJGL — Minecraft Java + tous les mods (JEI inclus)
        "lwjgl.dll",
        "lwjgl_glfw.dll",
        "lwjgl_opengl.dll",
        // SDL — gros volume d'indés et certains AAA
        "sdl.dll",
        "sdl2.dll",
        "sdl3.dll",
        // DirectInput legacy
        "dinput.dll",
        "dinput8.dll",
        // Unity Engine (tous les jeux Unity)
        "unityplayer.dll",
        // Allegro
        "allegro-5.2.dll",
    };

    /// <summary>
    /// Indique si le process foreground est protégé par un anti-cheat kernel-level
    /// connu, et que l'app doit être désactivée pour éviter un ban utilisateur.
    /// </summary>
    /// <param name="processName">Nom court (ex: "valorant.exe").</param>
    /// <param name="fullPath">Chemin complet (peut être null si non disponible).</param>
    public static bool IsAntiCheatProcess(string? processName, string? fullPath)
    {
        if (string.IsNullOrEmpty(processName)) return false;

        // Niveau 1 — sous-chaîne case-insensitive
        foreach (var term in AntiCheatTerms)
            if (processName.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;

        // Niveau 1 — match exact + check chemin (ace.exe / cod.exe)
        foreach (var (exact, pathContains) in AntiCheatExactWithPath)
        {
            if (!string.Equals(processName, exact, StringComparison.OrdinalIgnoreCase))
                continue;
            if (pathContains == null)
                return true;
            if (fullPath != null && fullPath.Contains(pathContains, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Niveau 2 — sous-chaîne case-insensitive (précaution)
        foreach (var term in PrecautionAntiCheatTerms)
            if (processName.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    /// <summary>
    /// Indique si la liste de modules contient au moins une DLL signature gaming
    /// (GLFW, SDL, Unity, etc.). Un match déclenche le mode NativeCombo.
    /// </summary>
    public static bool HasGameFrameworkLoaded(IEnumerable<string>? moduleNames)
    {
        if (moduleNames == null) return false;
        foreach (var name in moduleNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            foreach (var dll in GameFrameworkDlls)
                if (string.Equals(name, dll, StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        return false;
    }
}
