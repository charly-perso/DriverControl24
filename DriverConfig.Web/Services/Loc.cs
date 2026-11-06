using System.Globalization;

namespace DriverConfig.Web.Services;

/// <summary>
/// Textes de l'interface en français et en anglais. La langue suit la culture de la requête
/// (en-tête Accept-Language, voir UseRequestLocalization dans Program.cs) : français si le
/// navigateur est en français, anglais pour toutes les autres langues.
/// </summary>
public sealed class Loc
{
    private static readonly Dictionary<string, (string Fr, string En)> Texts = new()
    {
        // ── Layout ────────────────────────────────────────────────────────
        ["nav.params"] = ("Paramètres FX", "FX parameters"),
        ["nav.files"] = ("Fichier & sauvegardes", "File & backups"),
        ["banner.external"] = ("Le fichier INI a été modifié en dehors de l'éditeur (le driver tourne ?). Rechargez pour repartir de la version sur disque : vos modifications non enregistrées seront perdues.",
                               "The INI file was modified outside the editor (is the driver running?). Reload to start again from the version on disk: your unsaved changes will be lost."),
        ["btn.reload"] = ("Recharger", "Reload"),
        ["ui.error"] = ("Une erreur est survenue.", "An error has occurred."),

        // ── Communs ───────────────────────────────────────────────────────
        ["btn.save"] = ("Enregistrer", "Save"),
        ["btn.cancel"] = ("Annuler", "Cancel"),
        ["msg.saved"] = ("Enregistré.", "Saved."),
        ["col.visible"] = ("Visible", "Visible"),

        // ── Page Driver ───────────────────────────────────────────────────
        ["title.driver"] = ("Driver — Control24 Config", "Driver — Control24 Config"),
        ["driver.autostart"] = ("Démarrage automatique", "Automatic start"),
        ["driver.autostart.help"] = ("Lance le driver à l'ouverture de l'application.", "Starts the driver when the application opens."),
        ["driver.vsdebug"] = ("Débogage Visual Studio", "Visual Studio debugging"),
        ["driver.vsdebug.help"] = ("Attache le débogueur au démarrage du driver (défaut : désactivé).", "Attaches the debugger when the driver starts (default: disabled)."),
        ["driver.nic"] = ("Interface réseau", "Network interface"),
        ["driver.nic.refresh"] = ("Actualiser", "Refresh"),
        ["driver.nic.refresh.help"] = ("Relit la liste des interfaces réseau (mise à jour aussi automatique au branchement / débranchement)", "Re-reads the list of network interfaces (also updated automatically when one is plugged in / unplugged)"),
        ["driver.nic.none"] = ("(aucune)", "(none)"),
        ["driver.nic.missing"] = ("introuvable", "not found"),
        ["driver.nic.test"] = ("TestWithoutC24 (simulation sans console)", "TestWithoutC24 (simulation without a console)"),
        ["driver.nic.unknown"] = ("Cette interface n'existe pas sur cet ordinateur : le driver ne pourra pas démarrer avec.",
                                  "This interface does not exist on this computer: the driver will not be able to start with it."),
        ["driver.nic.simulation"] = ("Mode simulation : aucune console Control24 n'est requise.", "Simulation mode: no Control24 console is required."),

        // ── Page Plugins ──────────────────────────────────────────────────
        ["title.plugins"] = ("Plugins — Control24 Config", "Plugins — Control24 Config"),
        ["plugins.filter"] = ("Filtrer par nom…", "Filter by name…"),
        ["plugins.allkinds"] = ("Tous les types", "All types"),
        ["plugins.show.all"] = ("Tous", "All"),
        ["plugins.show.visible"] = ("Visibles", "Visible"),
        ["plugins.show.hidden"] = ("Masqués", "Hidden"),
        ["plugins.show.dirty"] = ("Modifiés", "Modified"),
        ["plugins.showfilter"] = ("Afficher le filtre", "Show filtered"),
        ["plugins.hidefilter"] = ("Masquer le filtre", "Hide filtered"),
        ["plugins.filter.help"] = ("Applique aux plugins actuellement filtrés", "Applies to the plugins currently filtered"),
        ["plugins.col.plugin"] = ("Plugin", "Plugin"),
        ["plugins.col.short"] = ("Nom court", "Short name"),
        ["plugins.col.chars8"] = ("(8 car.)", "(8 chars)"),
        ["plugins.col.params"] = ("Paramètres", "Parameters"),
        ["plugins.edit"] = ("Éditer", "Edit"),
        ["plugins.pending"] = ("{0} modification(s) en attente", "{0} pending change(s)"),
        ["plugins.saved"] = ("Enregistré ({0} plugin(s)).", "Saved ({0} plugin(s))."),
        ["plugins.err.empty"] = ("Le nom court ne peut pas être vide.", "The short name cannot be empty."),
        ["plugins.err.max8"] = ("8 caractères maximum.", "8 characters maximum."),
        ["plugins.err.chars"] = ("Caractères interdits : | = [ ]", "Forbidden characters: | = [ ]"),
        ["plugins.err.dup"] = ("Nom court déjà utilisé par un autre plugin.", "Short name already used by another plugin."),

        // ── Page Paramètres FX ────────────────────────────────────────────
        ["title.params"] = ("Paramètres FX — Control24 Config", "FX parameters — Control24 Config"),
        ["params.choose"] = ("— choisir —", "— choose —"),
        ["params.lines"] = ("{0} ligne(s) · {1} page(s) de 24", "{0} line(s) · {1} page(s) of 24"),
        ["params.intro"] = ("Choisissez un plugin pour éditer l'ordre, les noms et la visibilité de ses paramètres sur le Control24. Pour ajouter des paramètres Fx : ajoutez des Fx dans Reaper puis appuyez sur le bouton EXT TRANS de la surface Control24. (Les paramètres déjà présents dans le fichier seront conservés. Seuls les nouveaux paramètres seront ajoutés.)",
                            "Choose a plugin to edit the order, names and visibility of its parameters on the Control24. To add Fx parameters: add Fx in Reaper, then press the EXT TRANS button on the Control24 surface. (Parameters already in the file are kept; only new parameters are added.)"),
        ["params.preview"] = ("Aperçu Control24", "Control24 preview"),
        ["params.preview.aria"] = ("Aperçu des strips", "Strip preview"),
        ["params.preview.help"] = ("(indicatif — l'ordre est celui de la liste, 24 strips par page)", "(indicative — the order is that of the list, 24 strips per page)"),
        ["params.page"] = ("Page {0} / {1}", "Page {0} / {1}"),
        ["params.col.param"] = ("Paramètre Reaper", "Reaper parameter"),
        ["params.col.short"] = ("Court", "Short"),
        ["params.col.long"] = ("Long", "Long"),
        ["params.col.display"] = ("Affichage", "Display"),
        ["params.col.display.help"] = ("Long : le nom long occupe 2 strips (séparateur texte + paramètre)", "Long: the long name takes 2 strips (text separator + parameter)"),
        ["params.col.order"] = ("Ordre", "Order"),
        ["params.drag.linked"] = ("Bloc lié (séparateur + paramètre) : se déplace ensemble", "Linked block (separator + parameter): moves together"),
        ["params.drag"] = ("Glisser pour réordonner", "Drag to reorder"),
        ["params.text.placeholder"] = ("Texte affiché sur le strip", "Text shown on the strip"),
        ["params.text.remove"] = ("Supprimer ce séparateur", "Remove this separator"),
        ["params.mode.short"] = ("Court", "Short"),
        ["params.mode.long"] = ("Long", "Long"),
        ["params.mode.long.help"] = ("Le nom long doit dépasser 4 caractères", "The long name must be longer than 4 characters"),
        ["params.up"] = ("Monter", "Move up"),
        ["params.down"] = ("Descendre", "Move down"),
        ["params.addtext"] = ("+ Séparateur texte", "+ Text separator"),
        ["params.showall"] = ("Tout afficher", "Show all"),
        ["params.nonascii"] = ("Caractères non ASCII : ils risquent de mal s'afficher sur l'écran de la console.", "Non-ASCII characters: they may not display correctly on the console screen."),

        // ── Page Fichier ──────────────────────────────────────────────────
        ["title.files"] = ("Fichier — Control24 Config", "File — Control24 Config"),
        ["files.h1"] = ("Fichier & sauvegardes", "File & backups"),
        ["files.backups"] = ("Sauvegardes", "Backups"),
        ["files.backups.help"] = ("Une copie du fichier est faite automatiquement avant chaque enregistrement (30 conservées).", "A copy of the file is made automatically before each save (30 are kept)."),
        ["files.nobackup"] = ("Aucune sauvegarde pour l'instant.", "No backup yet."),
        ["files.col.date"] = ("Date", "Date"),
        ["files.col.size"] = ("Taille", "Size"),
        ["files.size.unit"] = ("Ko", "KB"),
        ["files.restore"] = ("Restaurer", "Restore"),
        ["files.restored"] = ("Sauvegarde restaurée. Les autres pages affichent désormais cette version.", "Backup restored. The other pages now show this version."),
        ["files.content"] = ("Contenu actuel du fichier", "Current file content"),
        ["files.readonly"] = ("(lecture seule)", "(read-only)"),
        ["files.refresh"] = ("Actualiser", "Refresh"),
        ["files.reload"] = ("Recharger depuis le disque", "Reload from disk"),

        // ── Pages système ─────────────────────────────────────────────────
        ["notfound.title"] = ("Page introuvable", "Page not found"),
        ["notfound.text"] = ("Cette page n'existe pas.", "This page does not exist."),

        // ── Erreurs du service ────────────────────────────────────────────
        ["err.notfound"] = ("Fichier introuvable : {0}. Lancez une fois le driver, ou renseignez « IniPath » dans appsettings.json.",
                            "File not found: {0}. Run the driver once, or set \"IniPath\" in appsettings.json."),
        ["err.unavailable"] = ("Configuration indisponible.", "Configuration unavailable."),
        ["err.external"] = ("Le fichier a été modifié en dehors de l'éditeur (le driver ?). Rechargez avant d'enregistrer.",
                            "The file was modified outside the editor (the driver?). Reload before saving."),
        ["err.badbackup"] = ("Nom de sauvegarde invalide.", "Invalid backup name."),
        ["err.nobackup"] = ("Sauvegarde introuvable.", "Backup not found."),
    };

    /// <summary>True si la culture courante est le français.</summary>
    public static bool IsFrench => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";

    /// <summary>
    /// Texte dans la langue de la culture courante. Utilisable aussi hors composant (ConfigService),
    /// tant que l'appel a lieu dans le contexte de la requête / du circuit Blazor.
    /// </summary>
    public static string T(string key, params object[] args)
    {
        if (!Texts.TryGetValue(key, out var t)) return key;
        string text = IsFrench ? t.Fr : t.En;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    public string this[string key] => T(key);
}
