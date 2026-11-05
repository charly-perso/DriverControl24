using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace DriverConfig.Web.Services;

/// <summary>
/// Point d'accès unique au fichier ReaperDriverControl24.ini pour toute l'application web.
/// Toutes les opérations passent par <see cref="ReaperSessionConfigBase"/> (même logique que le driver)
/// sous un verrou. Gère les sauvegardes, le rechargement et la détection de modification externe.
/// </summary>
public sealed class ConfigService : IDisposable
{
    private const string IniFileName = "ReaperDriverControl24.ini";
    private const int MaxBackups = 30;

    private readonly object _gate = new();
    private readonly ILogger<ConfigService> _log;
    private readonly string _backupDir;
    private readonly ReaperSessionConfigBase? _cfg;
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _debounce;
    private readonly Timer _nicDebounce;
    private DateTime _knownWriteUtc;

    public string IniPath { get; }
    /// <summary>Faux si le fichier INI est introuvable (on n'en crée jamais un depuis l'éditeur).</summary>
    public bool Available => _cfg != null;
    /// <summary>Vrai si le fichier a été modifié par un autre processus (ex : le driver) depuis le dernier chargement.</summary>
    public bool ExternalChange { get; private set; }
    private string? _lastError;
    private bool _iniNotFound;

    /// <summary>Dernière erreur (texte dans la langue de la requête courante pour « fichier introuvable »).</summary>
    public string? LastError
    {
        get => _iniNotFound ? Loc.T("err.notfound", IniPath) : _lastError;
        private set => _lastError = value;
    }

    /// <summary>Un enregistrement, un rechargement ou une modification externe vient d'avoir lieu.</summary>
    public event Action? Changed;
    /// <summary>L'état en mémoire a été remplacé par le contenu du disque : les brouillons des pages sont périmés.</summary>
    public event Action? Reloaded;
    /// <summary>La liste des interfaces réseau vient d'être relue (bouton ou changement détecté par le système).</summary>
    public event Action? InterfacesChanged;

    public ConfigService(IConfiguration configuration, ILogger<ConfigService> log)
    {
        _log = log;
        _debounce = new Timer(_ => CheckExternalChange(), null, Timeout.Infinite, Timeout.Infinite);
        _nicDebounce = new Timer(_ => RefreshInterfaces(), null, Timeout.Infinite, Timeout.Infinite);
        // Rafraîchissement en direct : le système signale branchement / débranchement / changement d'adresse.
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        IniPath = ResolveIniPath(configuration["IniPath"]);
        _backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DriverControl24", "backups");

        if (!File.Exists(IniPath))
        {
            _iniNotFound = true;
            return;
        }

        _cfg = new ReaperSessionConfigBase(IniPath);
        _cfg.On_SaveError += msg => { LastError = msg; _log.LogError("Écriture INI : {Message}", msg); };
        _knownWriteUtc = File.GetLastWriteTimeUtc(IniPath);

        _watcher = new FileSystemWatcher(Path.GetDirectoryName(IniPath)!, Path.GetFileName(IniPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        FileSystemEventHandler onEvent = (_, _) => _debounce.Change(400, Timeout.Infinite);
        _watcher.Changed += onEvent;
        _watcher.Created += onEvent;
        _watcher.Renamed += (_, _) => _debounce.Change(400, Timeout.Infinite);
    }

    /// <summary>
    /// Ordre : réglage « IniPath », puis dossier UserPlugins de REAPER (où le driver écrit son INI),
    /// puis dossier de l'application.
    /// </summary>
    private static string ResolveIniPath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);

        var candidates = new List<string>();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                        "Library", "Application Support", "REAPER", "UserPlugins", IniFileName));
        else
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                        "REAPER", "UserPlugins", IniFileName));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, IniFileName));
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    // ── Lecture ───────────────────────────────────────────────────────────

    public DriverSettings GetDriver()
    {
        lock (_gate)
        {
            var c = Cfg();
            return new DriverSettings(c.Autostart, c.VSDebug, c.SelectedNetworkInterface ?? "",
                                      c.AllNetworkInterfaces.ToList(), c.IsInterfaceValid);
        }
    }

    /// <summary>Relit la liste des interfaces réseau du système (sans écrire le fichier INI).</summary>
    public void RefreshInterfaces()
    {
        if (_cfg == null) return;
        lock (_gate) _cfg.RefreshNetworkInterfaces();
        InterfacesChanged?.Invoke();
    }

    // Les événements système arrivent souvent en rafale : on regroupe.
    private void OnNetworkChanged(object? sender, EventArgs e) => _nicDebounce.Change(500, Timeout.Infinite);

    public List<PluginEdit> GetPlugins()
    {
        lock (_gate)
        {
            var c = Cfg();
            return c.Plugins.Select(name =>
            {
                string sn = c.GetPluginShortName(name).Trim();
                bool vis = c.IsPluginVisible(name), eq = c.IsEQPlugin(name), dyn = c.IsDynPlugin(name);
                return new PluginEdit
                {
                    Name = name, OriginalShortName = sn, OriginalVisible = vis, OriginalEq = eq, OriginalDyn = dyn,
                    HasParams = c.HasFxParams(name),
                    ShortName = sn, Visible = vis, Eq = eq, Dyn = dyn
                };
            }).ToList();
        }
    }

    public List<string> GetPluginsWithParams()
    {
        lock (_gate)
        {
            var c = Cfg();
            return c.Plugins.Where(c.HasFxParams).ToList();
        }
    }

    public List<ParamEdit> GetParams(string plugin)
    {
        lock (_gate)
        {
            return Cfg().GetFxParams(plugin).Select(p => p.ParamIndex < 0
                ? new ParamEdit { ParamIndex = -1, Text = p.TextOnly ?? "", Visible = false }
                : new ParamEdit
                {
                    ParamIndex = p.ParamIndex, FullName = p.FullName ?? "",
                    ShortName = (p.ShortName ?? "").TrimEnd(), LongName = (p.LongName ?? "").TrimEnd(),
                    Visible = p.Visible
                }).ToList();
        }
    }

    public string ReadRaw()
    {
        lock (_gate) return File.Exists(IniPath) ? File.ReadAllText(IniPath) : "";
    }

    // ── Écriture ──────────────────────────────────────────────────────────

    public void SaveDriver(bool autostart, bool vsDebug, string networkInterface)
        => Commit(c =>
        {
            c.Autostart = autostart;
            c.VSDebug = vsDebug;
            c.SelectedNetworkInterface = networkInterface;
        });

    public void SavePlugins(IEnumerable<PluginEdit> edited)
    {
        var rows = edited.Where(r => r.IsDirty).ToList();
        if (rows.Count == 0) return;
        Commit(c =>
        {
            var renames = rows.Where(r => r.ShortName.Trim() != r.OriginalShortName.Trim())
                              .ToDictionary(r => r.Name, r => r.ShortName.Trim());
            if (renames.Count > 0) c.SetPluginShortNames(renames);

            foreach (var r in rows)
            {
                if (r.Visible != r.OriginalVisible) c.SetPluginVisible(r.Name, r.Visible);
                if (r.Eq != r.OriginalEq) { if (r.Eq) c.AddEqPlugin(r.Name); else c.RemoveEqPlugin(r.Name); }
                if (r.Dyn != r.OriginalDyn) { if (r.Dyn) c.AddDynPlugin(r.Name); else c.RemoveDynPlugin(r.Name); }
            }
        });
    }

    public void SaveParams(string plugin, IEnumerable<ParamEdit> rows)
    {
        var list = rows.Select(r => r.IsText
            ? new FxParamConfig { ParamIndex = -1, TextOnly = r.Text }
            : new FxParamConfig
            {
                ParamIndex = r.ParamIndex, FullName = r.FullName, ShortName = r.ShortName,
                LongName = r.LongName, Visible = r.Visible
            }).ToList();
        Commit(c => c.SetFxParams(plugin, list));
    }

    // ── Rechargement ──────────────────────────────────────────────────────

    public void Reload()
    {
        lock (_gate)
        {
            Cfg().Reload();
            _knownWriteUtc = File.GetLastWriteTimeUtc(IniPath);
            ExternalChange = false;
            LastError = null;
        }
        Reloaded?.Invoke();
        Changed?.Invoke();
    }

    // ── Sauvegardes ───────────────────────────────────────────────────────

    public List<BackupInfo> GetBackups()
    {
        if (!Directory.Exists(_backupDir)) return [];
        return new DirectoryInfo(_backupDir).GetFiles("*.ini")
            .OrderByDescending(f => f.Name)
            .Select(f => new BackupInfo(f.Name, f.LastWriteTimeUtc, f.Length))
            .ToList();
    }

    public void RestoreBackup(string fileName)
    {
        // Pas de séparateur de chemin : on ne restaure que depuis le dossier de sauvegardes.
        if (fileName != Path.GetFileName(fileName)) throw new ArgumentException(Loc.T("err.badbackup"));
        string source = Path.Combine(_backupDir, fileName);
        if (!File.Exists(source)) throw new FileNotFoundException(Loc.T("err.nobackup"), fileName);

        lock (_gate)
        {
            Cfg();
            Backup();
            File.Copy(source, IniPath, overwrite: true);
        }
        Reload();
    }

    // ── Interne ───────────────────────────────────────────────────────────

    private ReaperSessionConfigBase Cfg() => _cfg ?? throw new InvalidOperationException(LastError ?? Loc.T("err.unavailable"));

    /// <summary>Sauvegarde le fichier actuel puis applique les modifications en une seule écriture.</summary>
    private void Commit(Action<ReaperSessionConfigBase> edit)
    {
        lock (_gate)
        {
            var c = Cfg();
            if (ExternalChange)
                throw new InvalidOperationException(Loc.T("err.external"));

            LastError = null;
            Backup();
            using (c.BeginBatch()) edit(c);
            if (LastError != null) throw new IOException(LastError);
            _knownWriteUtc = File.GetLastWriteTimeUtc(IniPath);
        }
        Changed?.Invoke();
    }

    private void Backup()
    {
        try
        {
            if (!File.Exists(IniPath)) return;
            Directory.CreateDirectory(_backupDir);
            string name = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.ini";
            File.Copy(IniPath, Path.Combine(_backupDir, name), overwrite: true);
            foreach (var old in new DirectoryInfo(_backupDir).GetFiles("*.ini")
                         .OrderByDescending(f => f.Name).Skip(MaxBackups))
                old.Delete();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Sauvegarde préalable impossible");
        }
    }

    private void CheckExternalChange()
    {
        bool changed;
        lock (_gate)
        {
            DateTime now = File.Exists(IniPath) ? File.GetLastWriteTimeUtc(IniPath) : DateTime.MinValue;
            changed = now != _knownWriteUtc && !ExternalChange;
            if (changed) ExternalChange = true;
        }
        if (changed) Changed?.Invoke();
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        _nicDebounce.Dispose();
        _watcher?.Dispose();
        _debounce.Dispose();
    }
}
