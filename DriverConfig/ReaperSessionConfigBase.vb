' ══════════════════════════════════════════════════════════════════════════
'  SESSION CONFIG (base) — configuration de session dans un fichier INI
'  Partagée entre le driver (qui l'étend avec les appels à Reaper) et
'  l'éditeur web de configuration (qui l'utilise directement).
' ══════════════════════════════════════════════════════════════════════════

' ── Structure de configuration d'un paramètre FX ─────────────────────────
Public Structure FxParamConfig
    ''' <summary>Index 0-based du paramètre sur le FX (tel que retourné par Reaper). -1 pour une ligne de texte.</summary>
    Public ParamIndex As Integer
    ''' <summary>Nom complet du paramètre tel que retourné par Reaper.</summary>
    Public FullName As String
    ''' <summary>Nom court affiché sur le strip (4 caractères).</summary>
    Public ShortName As String
    ''' <summary>Nom long affiché sur l'écran étendu (8 caractères).</summary>
    Public LongName As String
    ''' <summary>True si le paramètre est visible sur le Control24.</summary>
    Public Visible As Boolean
    ''' <summary>
    ''' Texte libre affiché à la place de la valeur du paramètre.
    ''' Nothing si aucun texte n'est défini (affichage normal de la valeur).
    ''' </summary>
    Public TextOnly As String
End Structure

''' <summary>
''' Lit et persiste la configuration de session dans un fichier INI.
''' <br/>
''' Utilise <see cref="ReaperConfigReader"/> pour la lecture et
''' <see cref="ReaperConfigWriter"/> pour l'écriture.
''' <br/>
''' Cette classe ne dépend pas de Reaper : la liste des plugins installés lui est
''' fournie via <see cref="ApplyInstalledPlugins"/>.
''' </summary>
Public Class ReaperSessionConfigBase

    Private ReadOnly _iniPath As String
    Private ReadOnly _plugins As New List(Of String)
    Private ReadOnly _eq As New List(Of String)
    Private ReadOnly _dyn As New List(Of String)
    ' ── Suppresions explicites (via RemoveEqPlugin / RemoveDynPlugin) ─────
    ' Empêche la ré-injection depuis le fichier lors du prochain Save().
    Private ReadOnly _removedEq As New HashSet(Of String)
    Private ReadOnly _removedDyn As New HashSet(Of String)
    ' ── Visibilité par plugin : True = affiché dans Control24 ─────────────
    Private ReadOnly _pluginVisible As New Dictionary(Of String, Boolean)

    Private _autostart As Boolean = True
    Private _selectedInterface As String = "TestWithoutC24"
    Private _vsDebug As Boolean = False
    Private ReadOnly _allNetworkInterfaces As New List(Of String)
    Private ReadOnly _availableInterfaces As New List(Of String)

    Public Event On_PluginsRefreshed(count As Integer)
    Public Event On_InterfacesRefreshed(count As Integer)
    ''' <summary>Déclenché si l'écriture du fichier INI échoue.</summary>
    Public Event On_SaveError(message As String)
    Private ReadOnly _pluginShortNames As New Dictionary(Of String, String)

    ' ── Configs des paramètres FX : clé = nom complet du plugin ──────────
    Private ReadOnly _fxParamConfigs As New Dictionary(Of String, List(Of FxParamConfig))

    ' ── Clé de section INI ────────────────────────────────────────────────
    Private Shared ReadOnly FxParamSectionPrefix As String = "FxParams:"
    ' ── Lignes brutes des sections FxParams déjà présentes dans le fichier INI ──
    ' Clé = fullName du plugin  Valeur = lignes originales (header + contenu)
    ' Une section non modifiée est réécrite à l'identique (commentaires compris).
    Private ReadOnly _existingFxParamSectionLines As New Dictionary(Of String, List(Of String))
    ' Sections dont le contenu a été modifié en mémoire : elles sont régénérées
    ' depuis _fxParamConfigs au lieu d'être recopiées depuis les lignes brutes.
    Private ReadOnly _dirtyFxSections As New HashSet(Of String)
    ' Sections FxParams dont le plugin est inconnu : conservées telles quelles.
    Private ReadOnly _orphanFxSections As New List(Of List(Of String))

    ' ── Mode batch : plusieurs modifications, une seule écriture ──────────
    Private _batchDepth As Integer = 0
    Private _pendingSave As Boolean = False

    Public Sub New()
        Me.New(IO.Path.Combine(AppContext.BaseDirectory, "ReaperDriverControl24.ini"))
    End Sub

    Public Sub New(iniPath As String)
        _iniPath = iniPath
        RefreshNetworkInterfaces()
        Dim fileExistedBeforeLoad = IO.File.Exists(_iniPath)
        Load()
        If Not fileExistedBeforeLoad Then
            Save()
        End If
    End Sub

    Public ReadOnly Property fxParamConfigs As Dictionary(Of String, List(Of FxParamConfig))
        Get
            Return _fxParamConfigs
        End Get
    End Property

    Public ReadOnly Property IniPath As String
        Get
            Return _iniPath
        End Get
    End Property

    Public ReadOnly Property Plugins As IReadOnlyList(Of String)
        Get
            Return _plugins
        End Get
    End Property

    Public ReadOnly Property EQPlugins As IReadOnlyList(Of String)
        Get
            Return _eq
        End Get
    End Property

    Public ReadOnly Property DynPlugins As IReadOnlyList(Of String)
        Get
            Return _dyn
        End Get
    End Property

    Public Property Autostart As Boolean
        Get
            Return _autostart
        End Get
        Set
            _autostart = Value
            AutoSave()
        End Set
    End Property

    Public Property SelectedNetworkInterface As String
        Get
            Return _selectedInterface
        End Get
        Set
            _selectedInterface = Value
            AutoSave()
        End Set
    End Property

    Public Property VSDebug As Boolean
        Get
            Return _vsDebug
        End Get
        Set
            _vsDebug = Value
            AutoSave()
        End Set
    End Property

    Public ReadOnly Property AllNetworkInterfaces As IReadOnlyList(Of String)
        Get
            Return _allNetworkInterfaces
        End Get
    End Property

    Public ReadOnly Property AvailableInterfaces As IReadOnlyList(Of String)
        Get
            Return _availableInterfaces
        End Get
    End Property

    Public ReadOnly Property IsInterfaceValid As Boolean
        Get
            If String.IsNullOrEmpty(_selectedInterface) Then Return False
            Return _allNetworkInterfaces.Contains(_selectedInterface)
        End Get
    End Property

    Public Function IsEQPlugin(fxFullName As String) As Boolean
        Return _eq.Contains(fxFullName)
    End Function

    Public Function IsDynPlugin(fxFullName As String) As Boolean
        Return _dyn.Contains(fxFullName)
    End Function

    ' ── Mode batch ────────────────────────────────────────────────────────

    ''' <summary>
    ''' Suspend l'écriture automatique du fichier jusqu'à la libération du scope retourné.
    ''' Les modifications faites entre-temps sont écrites en une seule fois.
    ''' <code>Using cfg.BeginBatch() : ... : End Using</code>
    ''' </summary>
    Public Function BeginBatch() As IDisposable
        _batchDepth += 1
        Return New BatchScope(Me)
    End Function

    Private Sub EndBatch()
        _batchDepth = Math.Max(0, _batchDepth - 1)
        If _batchDepth = 0 AndAlso _pendingSave Then
            Save()
        End If
    End Sub

    Private NotInheritable Class BatchScope
        Implements IDisposable
        Private _owner As ReaperSessionConfigBase
        Friend Sub New(owner As ReaperSessionConfigBase)
            _owner = owner
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            If _owner Is Nothing Then Return
            Dim o = _owner
            _owner = Nothing
            o.EndBatch()
        End Sub
    End Class

    ''' <summary>Écrit le fichier, ou reporte l'écriture à la fin du batch en cours.</summary>
    Private Sub AutoSave()
        If _batchDepth > 0 Then
            _pendingSave = True
        Else
            Save()
        End If
    End Sub

    ' ── Visibilité plugin ─────────────────────────────────────────────────

    ''' <summary>
    ''' Retourne True si le plugin doit apparaître dans la liste Control24.
    ''' Retourne True par défaut si le plugin est inconnu.
    ''' </summary>
    ''' <param name="fullName">Nom complet du plugin tel que retourné par Reaper.</param>
    Public Function IsPluginVisible(fullName As String) As Boolean
        Dim visible As Boolean = True
        _pluginVisible.TryGetValue(fullName, visible)
        Return visible
    End Function

    ''' <summary>
    ''' Définit la visibilité d'un plugin dans la liste Control24 et sauvegarde le fichier INI.
    ''' </summary>
    ''' <param name="fullName">Nom complet du plugin tel que retourné par Reaper.</param>
    ''' <param name="visible">True = affiché dans Control24  False = masqué.</param>
    Public Sub SetPluginVisible(fullName As String, visible As Boolean)
        If Not _plugins.Contains(fullName) Then Return
        _pluginVisible(fullName) = visible
        AutoSave()
    End Sub

    ''' <summary>
    ''' Retourne la liste des plugins marqués comme visibles dans Control24.
    ''' </summary>
    Public Function GetVisiblePlugins() As IReadOnlyList(Of String)
        Dim result As New List(Of String)
        For Each name In _plugins
            If IsPluginVisible(name) Then
                result.Add(name)
            End If
        Next
        Return result
    End Function

    ' ── Interfaces réseau ─────────────────────────────────────────────────

    Public Sub RefreshNetworkInterfaces()
        _allNetworkInterfaces.Clear()
        _availableInterfaces.Clear()
        Dim nics = Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
        For Each nic In nics
            _allNetworkInterfaces.Add(nic.Name)
            Dim t = nic.NetworkInterfaceType
            If t <> Net.NetworkInformation.NetworkInterfaceType.Ethernet AndAlso
               t <> Net.NetworkInformation.NetworkInterfaceType.Wireless80211 Then
                Continue For
            End If
            Dim s = nic.OperationalStatus
            If s = Net.NetworkInformation.OperationalStatus.Up OrElse
               s = Net.NetworkInformation.OperationalStatus.Unknown Then
                _availableInterfaces.Add(nic.Name)
            End If
        Next
        RaiseEvent On_InterfacesRefreshed(_allNetworkInterfaces.Count)
    End Sub

    ' ── Chargement ────────────────────────────────────────────────────────

    ''' <summary>
    ''' Abandonne l'état en mémoire et relit le fichier INI depuis le disque.
    ''' </summary>
    Public Sub Reload()
        _removedEq.Clear()
        _removedDyn.Clear()
        _dirtyFxSections.Clear()
        _pendingSave = False
        Load()
    End Sub

    Private Sub Load()
        Dim reader As New ReaperConfigReader(_iniPath)
        reader.Load()
        If Not reader.IsLoaded Then Return

        _autostart = reader.GetBoolean("Driver", "Autostart", False)
        _selectedInterface = reader.GetString("Driver", "NetworkInterface", String.Empty)
        _vsDebug = reader.GetBoolean("Driver", "VSDebug", False)

        _plugins.Clear()
        _pluginVisible.Clear()
        For Each entry In reader.GetList("Plugins", "Plugin")
            Dim sepIdx = entry.IndexOf("|"c)
            If sepIdx > 0 Then
                Dim pluginName = entry.Substring(0, sepIdx)
                Dim visible = entry.Substring(sepIdx + 1) <> "0"
                _plugins.Add(pluginName)
                _pluginVisible(pluginName) = visible
            Else
                _plugins.Add(entry)
                _pluginVisible(entry) = True
            End If
        Next

        _eq.Clear()
        _eq.AddRange(reader.GetList("EQ", "EQ"))

        _dyn.Clear()
        _dyn.AddRange(reader.GetList("DYN", "DYN"))

        _pluginShortNames.Clear()
        For Each entry In reader.GetList("PluginShortNames", "PluginShortName")
            Dim sepIdx = entry.IndexOf("|"c)
            If sepIdx > 0 AndAlso sepIdx < entry.Length - 1 Then
                Dim fullName = entry.Substring(0, sepIdx)
                Dim shortName = entry.Substring(sepIdx + 1)
                _pluginShortNames(fullName) = shortName
            End If
        Next

        ' ── FxParams : sections [FxParams:shortName] ──────────────────
        _fxParamConfigs.Clear()
        _existingFxParamSectionLines.Clear()
        _orphanFxSections.Clear()
        _dirtyFxSections.Clear()
        If IO.File.Exists(_iniPath) Then
            LoadFxParams(IO.File.ReadAllLines(_iniPath))
        End If
    End Sub

    ' ── Mise à jour des plugins ───────────────────────────────────────────

    ''' <summary>
    ''' Remplace la liste des plugins par celle fournie (typiquement la liste des FX installés
    ''' dans Reaper) en conservant visibilité et noms courts déjà connus, puis sauvegarde.
    ''' </summary>
    ''' <param name="installed">Plugins installés : nom complet + type (VST, VST3, JS…).</param>
    Public Sub ApplyInstalledPlugins(installed As IEnumerable(Of (Name As String, FxType As String)))
        ' ── Snapshot des états existants avant reset ─────────────────────
        Dim existingVisible As New Dictionary(Of String, Boolean)(_pluginVisible)
        Dim existingShortNames As New Dictionary(Of String, String)(_pluginShortNames)

        _plugins.Clear()
        _pluginVisible.Clear()
        For Each fx In installed
            _plugins.Add(fx.Name)
            ' ── Conserver la visibilité existante ─────────────────────────
            ' Pour un nouveau plugin : visible par défaut si VST/VST3/VSTi/VST3i
            ' On vérifie fx.FxType ET fx.Name car FxType peut être vide selon la source
            Dim vis As Boolean = False
            If Not existingVisible.TryGetValue(fx.Name, vis) Then
                vis = fx.FxType.StartsWith("VST", StringComparison.OrdinalIgnoreCase) OrElse
                      fx.Name.StartsWith("VST", StringComparison.OrdinalIgnoreCase)
            End If
            _pluginVisible(fx.Name) = vis
        Next

        Dim usedShortNames As New HashSet(Of String)

        For Each fullName In _plugins
            Dim savedShort As String = Nothing
            If existingShortNames.TryGetValue(fullName, savedShort) Then
                _pluginShortNames(fullName) = savedShort
                usedShortNames.Add(savedShort)
            End If
        Next

        For Each fullName In _plugins
            If _pluginShortNames.ContainsKey(fullName) Then
                Continue For
            End If
            Dim compressed = FxNaming.CompressFxName(fullName)
            Dim unique = compressed
            Dim suffix As Integer = 2
            While usedShortNames.Contains(unique)
                Dim suffixStr = suffix.ToString()
                Dim baseLen As Integer = Math.Max(1, 8 - suffixStr.Length)
                unique = compressed.Substring(0, Math.Min(baseLen, compressed.Length)).TrimEnd() & suffixStr
                unique = unique.PadRight(8).Substring(0, 8)
                suffix += 1
            End While
            _pluginShortNames(fullName) = unique
            usedShortNames.Add(unique)
        Next

        Save()
        RaiseEvent On_PluginsRefreshed(_plugins.Count)
    End Sub

    Public Function GetPluginShortName(fullName As String) As String
        Dim shortName As String = Nothing
        If _pluginShortNames.TryGetValue(fullName, shortName) Then
            Return shortName
        End If
        Return FxNaming.CompressFxName(fullName)
    End Function

    ''' <summary>
    ''' Retourne le nom complet d'un plugin à partir de son nom court (8 car.).
    ''' Retourne <see cref="String.Empty"/> si le nom court est introuvable.
    ''' </summary>
    ''' <param name="shortName">Nom court tel qu'affiché sur le Control24.</param>
    Public Function GetPluginFullName(shortName As String) As String
        If shortName = "NoInsert" Then
            Return "NoInsert"
        End If
        For Each kvp In _pluginShortNames
            If kvp.Value.Equals(shortName, StringComparison.OrdinalIgnoreCase) Then
                Return kvp.Key
            End If
        Next
        Return String.Empty
    End Function

    ''' <summary>
    ''' Définit le nom court (1 à 8 car.) d'un plugin et sauvegarde.
    ''' Les paramètres FX du plugin suivent automatiquement (la section INI est renommée).
    ''' </summary>
    ''' <exception cref="ArgumentException">Plugin inconnu, nom vide, trop long, caractères interdits ou déjà utilisé.</exception>
    Public Sub SetPluginShortName(fullName As String, shortName As String)
        If Not _plugins.Contains(fullName) Then
            Throw New ArgumentException($"Plugin inconnu : {fullName}")
        End If
        Dim err = ValidatePluginShortName(fullName, shortName)
        If err IsNot Nothing Then Throw New ArgumentException(err)
        _pluginShortNames(fullName) = shortName.Trim()
        AutoSave()
    End Sub

    ''' <summary>
    ''' Applique plusieurs noms courts d'un coup (clé = nom complet du plugin) et sauvegarde.
    ''' L'unicité est vérifiée sur l'état final : permuter deux noms est donc possible.
    ''' Rien n'est modifié si une valeur est invalide.
    ''' </summary>
    ''' <exception cref="ArgumentException">Plugin inconnu, nom invalide ou en doublon.</exception>
    Public Sub SetPluginShortNames(names As IDictionary(Of String, String))
        Dim final As New Dictionary(Of String, String)(_pluginShortNames)
        For Each kvp In names
            If Not _plugins.Contains(kvp.Key) Then
                Throw New ArgumentException($"Plugin inconnu : {kvp.Key}")
            End If
            Dim err = ValidateShortNameFormat(kvp.Value)
            If err IsNot Nothing Then Throw New ArgumentException($"{kvp.Key} : {err}")
            final(kvp.Key) = kvp.Value.Trim()
        Next

        For Each changed In names.Keys
            Dim wanted = final(changed)
            For Each kvp In final
                If kvp.Key <> changed AndAlso kvp.Value.Trim().Equals(wanted, StringComparison.OrdinalIgnoreCase) Then
                    Throw New ArgumentException($"Nom court « {wanted} » utilisé à la fois par {changed} et {kvp.Key}")
                End If
            Next
        Next

        Dim modified As Boolean = False
        For Each kvp In names
            Dim current As String = Nothing
            If Not _pluginShortNames.TryGetValue(kvp.Key, current) OrElse current <> final(kvp.Key) Then
                _pluginShortNames(kvp.Key) = final(kvp.Key)
                modified = True
            End If
        Next
        If modified Then AutoSave()
    End Sub

    Private Shared Function ValidateShortNameFormat(shortName As String) As String
        If String.IsNullOrWhiteSpace(shortName) Then Return "Le nom court ne peut pas être vide."
        Dim trimmed = shortName.Trim()
        If trimmed.Length > 8 Then Return "Le nom court est limité à 8 caractères."
        If trimmed.IndexOfAny({"|"c, "="c, "["c, "]"c, ChrW(10), ChrW(13)}) >= 0 Then
            Return "Caractères interdits : | = [ ]"
        End If
        Return Nothing
    End Function

    ''' <summary>
    ''' Vérifie un nom court de plugin. Retourne Nothing si valide, sinon le message d'erreur.
    ''' </summary>
    Public Function ValidatePluginShortName(fullName As String, shortName As String) As String
        Dim formatError = ValidateShortNameFormat(shortName)
        If formatError IsNot Nothing Then Return formatError
        Dim trimmed = shortName.Trim()
        For Each kvp In _pluginShortNames
            If kvp.Key <> fullName AndAlso kvp.Value.Trim().Equals(trimmed, StringComparison.OrdinalIgnoreCase) Then
                Return $"Nom court déjà utilisé par : {kvp.Key}"
            End If
        Next
        Return Nothing
    End Function

    ' ────────────────────────────────────────────────────────────────────
    ' API FxParam
    ' ────────────────────────────────────────────────────────────────────

    ''' <summary>True si une configuration de paramètres existe pour ce plugin.</summary>
    Public Function HasFxParams(fxFullName As String) As Boolean
        Return _fxParamConfigs.ContainsKey(fxFullName)
    End Function

    ''' <summary>Copie de la liste ordonnée (paramètres + lignes de texte) d'un plugin.</summary>
    Public Function GetFxParams(fxFullName As String) As List(Of FxParamConfig)
        Dim configs As List(Of FxParamConfig) = Nothing
        If _fxParamConfigs.TryGetValue(fxFullName, configs) Then
            Return New List(Of FxParamConfig)(configs)
        End If
        Return New List(Of FxParamConfig)
    End Function

    ''' <summary>
    ''' Remplace entièrement la liste de paramètres d'un plugin (ordre, noms, visibilité,
    ''' lignes de texte) et sauvegarde. Les noms sont normalisés à 4 / 8 caractères.
    ''' </summary>
    Public Sub SetFxParams(fxFullName As String, configs As IEnumerable(Of FxParamConfig))
        Dim list As New List(Of FxParamConfig)
        For Each cfg In configs
            list.Add(NormalizeFxParam(cfg))
        Next
        _fxParamConfigs(fxFullName) = list
        _dirtyFxSections.Add(fxFullName)
        AutoSave()
    End Sub

    ''' <summary>Supprime la configuration de paramètres d'un plugin et sauvegarde.</summary>
    Public Sub RemoveFxParams(fxFullName As String)
        If Not _fxParamConfigs.Remove(fxFullName) Then Return
        _existingFxParamSectionLines.Remove(fxFullName)
        _dirtyFxSections.Remove(fxFullName)
        AutoSave()
    End Sub

    Private Shared Function NormalizeFxParam(cfg As FxParamConfig) As FxParamConfig
        If cfg.ParamIndex < 0 Then
            cfg.TextOnly = CleanLine(If(cfg.TextOnly, String.Empty))
            cfg.FullName = String.Empty
            cfg.ShortName = String.Empty
            cfg.LongName = String.Empty
            cfg.Visible = False
        Else
            cfg.FullName = CleanLine(If(cfg.FullName, String.Empty))
            cfg.ShortName = CleanLine(If(cfg.ShortName, String.Empty)).PadRight(4).Substring(0, 4)
            cfg.LongName = CleanLine(If(cfg.LongName, String.Empty)).PadRight(8).Substring(0, 8)
        End If
        Return cfg
    End Function

    Private Shared Function CleanLine(value As String) As String
        Return value.Replace(vbCr, " ").Replace(vbLf, " ")
    End Function

    ''' <summary>
    ''' Retourne le nom court (4 car.) d'un paramètre FX.
    ''' </summary>
    Public Function GetFxParamShortName(fxFullName As String, paramIndex As Integer) As String
        Dim cfg = GetFxParamConfig(fxFullName, paramIndex)
        If cfg.HasValue Then
            Return cfg.Value.ShortName
        End If
        Return "Prm" & paramIndex.ToString().PadLeft(1)
    End Function

    ''' <summary>
    ''' Retourne le nom long (8 car.) d'un paramètre FX.
    ''' </summary>
    Public Function GetFxParamLongName(fxFullName As String, paramIndex As Integer) As String
        Dim cfg = GetFxParamConfig(fxFullName, paramIndex)
        If cfg.HasValue Then
            Return cfg.Value.LongName
        End If
        Return ("Param" & paramIndex.ToString()).PadRight(8).Substring(0, 8)
    End Function

    ''' <summary>
    ''' Retourne True si le paramètre est visible sur le Control24.
    ''' </summary>
    Public Function IsFxParamVisible(fxFullName As String, paramIndex As Integer) As Boolean
        Dim cfg = GetFxParamConfig(fxFullName, paramIndex)
        If cfg.HasValue Then
            Return cfg.Value.Visible
        End If
        Return True
    End Function

    ''' <summary>
    ''' Définit le nom court (4 car.) d'un paramètre et sauvegarde.
    ''' </summary>
    Public Sub SetFxParamShortName(fxFullName As String, paramIndex As Integer, shortName As String)
        Dim pos = EnsureParamExists(fxFullName, paramIndex)
        Dim cfg = _fxParamConfigs(fxFullName)(pos)
        cfg.ShortName = CleanLine(shortName).PadRight(4).Substring(0, 4)
        _fxParamConfigs(fxFullName)(pos) = cfg
        _dirtyFxSections.Add(fxFullName)
        AutoSave()
    End Sub

    ''' <summary>
    ''' Définit le nom long (8 car.) d'un paramètre et sauvegarde.
    ''' </summary>
    Public Sub SetFxParamLongName(fxFullName As String, paramIndex As Integer, longName As String)
        Dim pos = EnsureParamExists(fxFullName, paramIndex)
        Dim cfg = _fxParamConfigs(fxFullName)(pos)
        cfg.LongName = CleanLine(longName).PadRight(8).Substring(0, 8)
        _fxParamConfigs(fxFullName)(pos) = cfg
        _dirtyFxSections.Add(fxFullName)
        AutoSave()
    End Sub

    ''' <summary>
    ''' Définit la visibilité d'un paramètre et sauvegarde.
    ''' </summary>
    Public Sub SetFxParamVisible(fxFullName As String, paramIndex As Integer, visible As Boolean)
        Dim pos = EnsureParamExists(fxFullName, paramIndex)
        Dim cfg = _fxParamConfigs(fxFullName)(pos)
        cfg.Visible = visible
        _fxParamConfigs(fxFullName)(pos) = cfg
        _dirtyFxSections.Add(fxFullName)
        AutoSave()
    End Sub

    ''' <summary>
    ''' Retourne les indices des paramètres visibles pour un plugin donné.
    ''' </summary>
    Public Function GetVisibleFxParamIndices(fxFullName As String) As List(Of Integer)
        Dim result As New List(Of Integer)
        If Not _fxParamConfigs.ContainsKey(fxFullName) Then
            Return result
        End If
        Dim configs = _fxParamConfigs(fxFullName)
        For i As Integer = 0 To configs.Count - 1
            If configs(i).Visible Then
                result.Add(i)
            End If
        Next
        Return result
    End Function

    ''' <summary>
    ''' Génère une configuration par défaut à partir des noms de paramètres fournis
    ''' (index, nom complet, nom court 4 car., nom long 8 car., visible).
    ''' </summary>
    Public Shared Function BuildDefaultFxParams(paramNames As IReadOnlyList(Of String)) As List(Of FxParamConfig)
        Dim result As New List(Of FxParamConfig)
        Dim pairs = FxNaming.BuildParamNamePairs(paramNames)
        For i As Integer = 0 To paramNames.Count - 1
            result.Add(New FxParamConfig With {
                .ParamIndex = i,
                .FullName = paramNames(i),
                .ShortName = pairs(i).Short4,
                .LongName = pairs(i).Long8,
                .Visible = True
            })
        Next
        Return result
    End Function

    ' ── Méthodes de gestion EQ ────────────────────────────────────────────

    ''' <summary>Ajoute un plugin dans la liste EQ et sauvegarde.</summary>
    Public Sub AddEqPlugin(fullName As String)
        If String.IsNullOrEmpty(fullName) Then Return
        If _eq.Contains(fullName) Then Return
        _eq.Add(fullName)
        _removedEq.Remove(fullName)
        AutoSave()
    End Sub

    ''' <summary>Retire un plugin de la liste EQ et sauvegarde.</summary>
    Public Sub RemoveEqPlugin(fullName As String)
        If _eq.Remove(fullName) Then
            _removedEq.Add(fullName)
            AutoSave()
        End If
    End Sub

    ''' <summary>Bascule l'appartenance d'un plugin à la liste EQ et sauvegarde.</summary>
    Public Sub ToggleEqPlugin(fullName As String)
        If _eq.Contains(fullName) Then
            RemoveEqPlugin(fullName)
        Else
            AddEqPlugin(fullName)
        End If
    End Sub

    ''' <summary>Ajoute un plugin dans la liste DYN et sauvegarde.</summary>
    Public Sub AddDynPlugin(fullName As String)
        If String.IsNullOrEmpty(fullName) Then Return
        If _dyn.Contains(fullName) Then Return
        _dyn.Add(fullName)
        _removedDyn.Remove(fullName)
        AutoSave()
    End Sub

    ''' <summary>Retire un plugin de la liste DYN et sauvegarde.</summary>
    Public Sub RemoveDynPlugin(fullName As String)
        If _dyn.Remove(fullName) Then
            _removedDyn.Add(fullName)
            AutoSave()
        End If
    End Sub

    ''' <summary>Bascule l'appartenance d'un plugin à la liste DYN et sauvegarde.</summary>
    Public Sub ToggleDynPlugin(fullName As String)
        If _dyn.Contains(fullName) Then
            RemoveDynPlugin(fullName)
        Else
            AddDynPlugin(fullName)
        End If
    End Sub

    ' ── Helpers privés ────────────────────────────────────────────────────

    Private Function GetFxParamConfig(fxFullName As String, paramIndex As Integer) As FxParamConfig?
        Dim pos = FindParamPosition(fxFullName, paramIndex)
        If pos < 0 Then Return Nothing
        Return _fxParamConfigs(fxFullName)(pos)
    End Function

    ''' <summary>Position dans la liste du paramètre portant cet index Reaper, ou -1.</summary>
    Private Function FindParamPosition(fxFullName As String, paramIndex As Integer) As Integer
        Dim configs As List(Of FxParamConfig) = Nothing
        If Not _fxParamConfigs.TryGetValue(fxFullName, configs) Then Return -1
        For i As Integer = 0 To configs.Count - 1
            If configs(i).ParamIndex = paramIndex Then Return i
        Next
        Return -1
    End Function

    ''' <summary>Crée la config du paramètre si absente. Retourne sa position dans la liste.</summary>
    Private Function EnsureParamExists(fxFullName As String, paramIndex As Integer) As Integer
        If Not _fxParamConfigs.ContainsKey(fxFullName) Then
            _fxParamConfigs(fxFullName) = New List(Of FxParamConfig)
        End If
        Dim pos = FindParamPosition(fxFullName, paramIndex)
        If pos >= 0 Then Return pos
        Dim configs = _fxParamConfigs(fxFullName)
        configs.Add(New FxParamConfig With {
            .ParamIndex = paramIndex,
            .FullName = String.Empty,
            .ShortName = "Prm" & paramIndex.ToString().PadLeft(1),
            .LongName = ("Param" & paramIndex.ToString()).PadRight(8).Substring(0, 8),
            .Visible = True
        })
        Return configs.Count - 1
    End Function

    ''' <summary>
    ''' Ajoute uniquement les configs de paramètres absentes et sauvegarde si besoin.
    ''' Les configs existantes (modifiées par l'utilisateur) sont conservées.
    ''' </summary>
    Public Sub RefreshFxParams(fxFullName As String, paramNames As IReadOnlyList(Of String))
        If AddMissingFxParams(fxFullName, paramNames) Then AutoSave()
    End Sub

    Private Function AddMissingFxParams(fxFullName As String,
                                         paramNames As IReadOnlyList(Of String)) As Boolean
        If Not _fxParamConfigs.ContainsKey(fxFullName) Then
            _fxParamConfigs(fxFullName) = New List(Of FxParamConfig)
        End If

        Dim configs = _fxParamConfigs(fxFullName)
        Dim pairs = FxNaming.BuildParamNamePairs(paramNames)
        Dim added As Boolean = False

        For i As Integer = 0 To paramNames.Count - 1
            If FindParamPosition(fxFullName, i) >= 0 Then
                Continue For
            End If
            configs.Add(New FxParamConfig With {
                .ParamIndex = i,
                .FullName = paramNames(i),
                .ShortName = pairs(i).Short4,
                .LongName = pairs(i).Long8,
                .Visible = True
            })
            added = True
        Next

        If added Then _dirtyFxSections.Add(fxFullName)
        Return added
    End Function

    ' ── Load : lire les sections [FxParams:shortName] ─────────────────────

    Private Class RawFxSection
        Public Header As String
        Public ReadOnly Lines As New List(Of String)
    End Class

    Private Sub LoadFxParams(lines As IReadOnlyList(Of String))
        ' ── 1. Découpage en sections [FxParams:...] ──────────────────────
        Dim sections As New List(Of RawFxSection)
        Dim current As RawFxSection = Nothing

        For Each line In lines
            Dim trimmed = line.Trim()

            If trimmed.StartsWith("[") AndAlso trimmed.EndsWith("]") Then
                If trimmed.StartsWith("[" & FxParamSectionPrefix, StringComparison.OrdinalIgnoreCase) Then
                    current = New RawFxSection With {.Header = trimmed}
                    sections.Add(current)
                Else
                    current = Nothing
                End If
                Continue For
            End If

            ' Les lignes vides sont ignorées : BuildFxParamSections réinsère
            ' lui-même une ligne vide avant chaque section (sinon elles s'accumulent).
            If current IsNot Nothing AndAlso trimmed.Length > 0 Then
                current.Lines.Add(trimmed)
            End If
        Next

        ' ── 2. Interprétation de chaque section ──────────────────────────
        For Each section In sections
            Dim shortName = section.Header.Substring(FxParamSectionPrefix.Length + 1,
                                                     section.Header.Length - FxParamSectionPrefix.Length - 2).Trim()
            Dim fullName = GetPluginFullName(shortName)

            Dim raw As New List(Of String)
            raw.Add(section.Header)
            raw.AddRange(section.Lines)

            If String.IsNullOrEmpty(fullName) Then
                ' Plugin inconnu : on conserve la section telle quelle.
                _orphanFxSections.Add(raw)
                Continue For
            End If

            _fxParamConfigs(fullName) = ParseFxSection(section.Lines)
            _existingFxParamSectionLines(fullName) = raw
        Next
    End Sub

    ''' <summary>
    ''' Interprète le contenu d'une section : lignes p{n}_i/n/s/l/v et text="...".
    ''' L'ordre d'apparition dans le fichier est conservé.
    ''' </summary>
    Private Shared Function ParseFxSection(sectionLines As IEnumerable(Of String)) As List(Of FxParamConfig)
        ' IsText=True → séparateur text=  |  IsText=False → paramètre p{n}
        Dim orderedSlots As New List(Of (IsText As Boolean, TextValue As String, ParamN As Integer))
        Dim paramDict As New Dictionary(Of Integer, FxParamConfig)

        For Each trimmed In sectionLines
            Dim eqPos = trimmed.IndexOf("="c)
            If eqPos < 0 Then Continue For

            Dim key = trimmed.Substring(0, eqPos).Trim().ToLowerInvariant()
            Dim value = trimmed.Substring(eqPos + 1)

            ' ── Ligne text="..." : extraire le contenu entre guillemets ──
            If key = "text" Then
                Dim firstQuote = value.IndexOf(""""c)
                Dim lastQuote = value.LastIndexOf(""""c)
                Dim textContent As String
                If firstQuote >= 0 AndAlso lastQuote > firstQuote Then
                    textContent = value.Substring(firstQuote + 1, lastQuote - firstQuote - 1)
                Else
                    textContent = value
                End If
                orderedSlots.Add((True, textContent, -1))
                Continue For
            End If

            ' ── Lignes p{n}_* uniquement ──────────────────────────────────
            If Not key.StartsWith("p", StringComparison.OrdinalIgnoreCase) Then
                Continue For
            End If

            Dim underPos = key.IndexOf("_"c)
            If underPos < 2 Then Continue For

            Dim indexStr = key.Substring(1, underPos - 1)
            Dim suffix = key.Substring(underPos + 1)
            Dim idx As Integer = -1
            If Not Integer.TryParse(indexStr, idx) OrElse idx < 0 Then Continue For

            ' Premier attribut de ce p{n} → enregistrer sa position dans l'ordre fichier
            If Not paramDict.ContainsKey(idx) Then
                paramDict(idx) = New FxParamConfig With {
                    .ShortName = "    ",
                    .LongName = "        ",
                    .Visible = True
                }
                orderedSlots.Add((False, String.Empty, idx))
            End If

            Dim c = paramDict(idx)
            Select Case suffix
                Case "i"
                    Dim pi As Integer = -1
                    If Integer.TryParse(value, pi) Then
                        c.ParamIndex = pi
                    End If
                Case "n"
                    c.FullName = value
                Case "s"
                    c.ShortName = value.PadRight(4).Substring(0, 4)
                Case "l"
                    c.LongName = value.PadRight(8).Substring(0, 8)
                Case "v"
                    c.Visible = value = "1"
            End Select
            paramDict(idx) = c
        Next

        Dim result As New List(Of FxParamConfig)
        For Each slot In orderedSlots
            If slot.IsText Then
                result.Add(New FxParamConfig With {
                    .ParamIndex = -1,
                    .TextOnly = slot.TextValue
                })
            Else
                Dim paramCfg As FxParamConfig = Nothing
                If paramDict.TryGetValue(slot.ParamN, paramCfg) Then
                    result.Add(paramCfg)
                End If
            End If
        Next
        Return result
    End Function

    ' ── Save : écrire les sections [FxParams:shortName] ──────────────────

    Private Function BuildFxParamSections() As List(Of String)
        Dim lines As New List(Of String)
        lines.Add("; L'ordre des lignes p{n}_* détermine l'ordre d'apparition des paramètres")
        lines.Add("; sur les channel strips du Control24 (de gauche à droite, de haut en bas).")
        lines.Add("; Réorganiser les blocs p{n}_* pour modifier cet ordre.")
        lines.Add(";")
        lines.Add("; Pour insérer un séparateur ou afficher un texte libre sur l'écran d'un")
        lines.Add("; channel strip (sans paramètre Reaper associé), ajouter une ligne :")
        lines.Add(";   text=MonTexte")

        For Each kvp In _fxParamConfigs
            Dim fullName = kvp.Key
            Dim header = "[" & FxParamSectionPrefix & GetPluginShortName(fullName).Trim() & "]"
            lines.Add(String.Empty)

            ' ── Section présente dans le fichier et non modifiée → reproduction fidèle ──
            ' L'en-tête est recalculé : il suit un éventuel renommage du nom court.
            Dim rawLines As List(Of String) = Nothing
            If _existingFxParamSectionLines.TryGetValue(fullName, rawLines) AndAlso
               Not _dirtyFxSections.Contains(fullName) Then
                lines.Add(header)
                For i As Integer = 1 To rawLines.Count - 1
                    lines.Add(rawLines(i))
                Next
                Continue For
            End If

            ' ── Section nouvelle ou modifiée : génération depuis la mémoire ──
            lines.Add(header)

            Dim configs = kvp.Value
            For i As Integer = 0 To configs.Count - 1
                Dim cfg = configs(i)
                If cfg.ParamIndex < 0 Then
                    If cfg.TextOnly IsNot Nothing Then
                        lines.Add("text=""" & cfg.TextOnly & """")
                    End If
                Else
                    lines.Add("p" & i.ToString() & "_i=" & cfg.ParamIndex.ToString())
                    lines.Add("p" & i.ToString() & "_n=" & cfg.FullName)
                    lines.Add("p" & i.ToString() & "_s=" & cfg.ShortName)
                    lines.Add("p" & i.ToString() & "_l=" & cfg.LongName)
                    lines.Add("p" & i.ToString() & "_v=" & If(cfg.Visible, "1", "0"))
                End If
            Next
        Next

        ' ── Sections de plugins inconnus : conservées telles quelles ─────
        For Each orphan In _orphanFxSections
            lines.Add(String.Empty)
            lines.AddRange(orphan)
        Next

        Return lines
    End Function

    ' ── Sauvegarde ────────────────────────────────────────────────────────

    ''' <summary>
    ''' Écrit le fichier INI (via un fichier temporaire puis remplacement, pour ne jamais
    ''' laisser un fichier tronqué). Retourne False et déclenche <see cref="On_SaveError"/> en cas d'échec.
    ''' </summary>
    Public Function Save() As Boolean
        _pendingSave = False

        ' ── Fusionner EQ/DYN depuis le fichier avant d'écrire ────────────────
        ' Préserve les modifications manuelles faites directement dans le fichier
        ' INI pendant que l'application tourne (ex : l'utilisateur ajoute un
        ' plugin dans [Eq] avec un éditeur de texte).
        ' Les suppressions explicites (RemoveEqPlugin / RemoveDynPlugin) ont
        ' priorité sur les entrées du fichier grâce aux sets _removedEq/_removedDyn.
        If IO.File.Exists(_iniPath) Then
            Dim tmpReader As New ReaperConfigReader(_iniPath)
            tmpReader.Load()
            For Each entry In tmpReader.GetList("EQ", "EQ")
                If Not _eq.Contains(entry) AndAlso Not _removedEq.Contains(entry) Then
                    _eq.Add(entry)
                End If
            Next
            For Each entry In tmpReader.GetList("DYN", "DYN")
                If Not _dyn.Contains(entry) AndAlso Not _removedDyn.Contains(entry) Then
                    _dyn.Add(entry)
                End If
            Next
        End If

        Dim tmpPath = _iniPath & ".tmp"
        Dim writer As New ReaperConfigWriter(tmpPath)
        Dim writerError As String = Nothing
        AddHandler writer.On_SaveError, Sub(path As String, message As String) writerError = message

        writer.SetFileHeader("; ReaperDriverControl24 — Configuration de session" &
                             Environment.NewLine &
                             "; Ce fichier est mis à jour automatiquement.")

        writer.AddSection("Driver")
        writer.SetBoolean("Driver", "Autostart", _autostart,
                          "; Démarrage automatique du driver à l'ouverture de l'application")
        writer.SetString("Driver", "NetworkInterface", _selectedInterface,
                         "; Nom de l'interface réseau utilisée pour le driver Control24" &
                         Environment.NewLine &
                         "; Copier un nom depuis la section [Interfaces] ci-dessous" &
                         Environment.NewLine &
                         "; Simulation d'une console Control24 : NETWORKINTERFACE=TestWithoutC24")
        writer.SetBoolean("Driver", "VSDebug", _vsDebug,
                          "; Active le débogage Visual Studio au démarrage du driver (défaut : False)")

        writer.AddSection("Interfaces",
                          "; Liste de toutes les interfaces réseau détectées sur cet ordinateur." &
                          Environment.NewLine &
                          "; Section générée automatiquement — ne pas modifier." &
                          Environment.NewLine &
                          "; Copier un nom dans [Driver] NetworkInterface= pour l'utiliser.")
        writer.SetList("Interfaces", "Interface", _allNetworkInterfaces)

        writer.AddSection("Plugins",
                          "; Mis à jour automatiquement au démarrage." &
                          Environment.NewLine &
                          "; Format : PluginN=NomDuPlugin|Visible  (1=affiché dans Control24  0=masqué)" &
                          Environment.NewLine &
                          "; Modifier le chiffre après | pour afficher ou masquer un plugin.")
        Dim pluginEntries As New List(Of String)
        For Each name In _plugins
            Dim visible As Boolean = True
            _pluginVisible.TryGetValue(name, visible)
            pluginEntries.Add(name & "|" & If(visible, "1", "0"))
        Next
        writer.SetList("Plugins", "Plugin", pluginEntries)

        writer.AddSection("PluginShortNames",
                          "; Noms compressés à 8 caractères affichés sur le Control24." &
                          Environment.NewLine &
                          "; Format : PluginShortNameN=NomCompletDuPlugin|NomCourt8Car" &
                          Environment.NewLine &
                          "; Vous pouvez modifier la partie après le | (max 8 caractères)." &
                          Environment.NewLine &
                          "; Les modifications sont conservées lors du prochain démarrage.")
        Dim shortNameList As New List(Of String)
        For Each kvp In _pluginShortNames
            shortNameList.Add(kvp.Key & "|" & kvp.Value)
        Next
        writer.SetList("PluginShortNames", "PluginShortName", shortNameList)

        writer.AddSection("EQ", "; Noms exacts des plugins EQ (copier depuis [Plugins]) ex:EQ0=JS: 4-Band EQ")
        writer.SetList("EQ", "EQ", _eq)

        writer.AddSection("DYN", "; Noms exacts des plugins de dynamique (copier depuis [Plugins]) ex:DYN0=VST: ReaComp (Cockos)")
        writer.SetList("DYN", "DYN", _dyn)

        Try
            writer.Save()
            If writerError IsNot Nothing Then
                Throw New IO.IOException(writerError)
            End If

            ' ── FxParams : sections [FxParams:shortName] ──────────────────
            Dim fxParamLines = BuildFxParamSections()
            If fxParamLines.Count > 0 Then
                IO.File.AppendAllLines(tmpPath, fxParamLines)
            End If

            IO.File.Move(tmpPath, _iniPath, True)
        Catch ex As Exception
            Try
                If IO.File.Exists(tmpPath) Then IO.File.Delete(tmpPath)
            Catch
            End Try
            RaiseEvent On_SaveError(ex.Message)
            Return False
        End Try

        ' Les sections écrites deviennent la nouvelle référence « fichier ».
        ' Les lignes brutes ne sont plus à jour pour les sections modifiées : on les
        ' régénère au prochain Load ; en attendant elles restent marquées modifiées.
        Return True
    End Function

End Class
