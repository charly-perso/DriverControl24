' ══════════════════════════════════════════════════════════════════════════
'  REAPER CONFIG READER — lecteur générique de fichier INI
' ══════════════════════════════════════════════════════════════════════════

''' <summary>
''' Lit et parse un fichier INI au format Reaper/DriverControl24.
''' <br/>
''' Fournit des accesseurs typés (<c>String</c>, <c>Boolean</c>,
''' <c>Integer</c>, <c>Double</c>, liste) par section et par clé.
''' <br/>
''' Format supporté :
''' <code>
''' ; commentaire
''' [Section]
''' Key=Value
''' Key2=True
''' List0=premier
''' List1=second
''' </code>
''' Usage :
''' <code>
''' Dim reader As New ReaperConfigReader("C:\...\ReaperDriver.ini")
''' reader.Load()
''' Dim iface = reader.GetString("Driver", "NetworkInterface", String.Empty)
''' Dim autostart = reader.GetBoolean("Driver", "Autostart", False)
''' Dim plugins = reader.GetList("Plugins", "Plugin")
''' </code>
''' </summary>
Public Class ReaperConfigReader

    ' ── Stockage interne : SECTION → clé → valeur (tout en majuscules pour la section et la clé) ──
    Private ReadOnly _data As New Dictionary(Of String, Dictionary(Of String, String))

    Private ReadOnly _iniPath As String

    ' ── Événements ────────────────────────────────────────────────────────

    ''' <summary>Déclenché si le fichier INI est introuvable lors de <see cref="Load"/>.</summary>
    Public Event On_FileNotFound(iniPath As String)

    ''' <summary>Déclenché après un chargement réussi.</summary>
    Public Event On_Loaded(sectionCount As Integer)

    ' ── Constructeur ──────────────────────────────────────────────────────

    ''' <summary>
    ''' Initialise le reader pour le fichier INI spécifié.
    ''' <see cref="Load"/> doit être appelé pour remplir les données.
    ''' </summary>
    ''' <param name="iniPath">Chemin complet du fichier INI à lire.</param>
    Public Sub New(iniPath As String)
        _iniPath = iniPath
    End Sub

    ' ── Propriétés ────────────────────────────────────────────────────────

    ''' <summary>Chemin complet du fichier INI.</summary>
    Public ReadOnly Property IniPath As String
        Get
            Return _iniPath
        End Get
    End Property

    ''' <summary>True si au moins un chargement réussi a eu lieu.</summary>
    Public ReadOnly Property IsLoaded As Boolean
        Get
            Return _data.Count > 0
        End Get
    End Property

    ' ── Chargement ────────────────────────────────────────────────────────

    ''' <summary>
    ''' Lit et parse le fichier INI.
    ''' Déclenche <see cref="On_FileNotFound"/> si le fichier est absent.
    ''' Déclenche <see cref="On_Loaded"/> après un chargement réussi.
    ''' </summary>
    Public Sub Load()
        _data.Clear()

        If Not IO.File.Exists(_iniPath) Then
            RaiseEvent On_FileNotFound(_iniPath)
            Return
        End If

        Dim lines = IO.File.ReadAllLines(_iniPath)
        Dim section = String.Empty

        For Each line In lines
            Dim trimmed = line.Trim()
            If String.IsNullOrEmpty(trimmed) Then Continue For
            If trimmed.StartsWith(";") Then Continue For

            ' ── Changement de section ─────────────────────────────────────
            If trimmed.StartsWith("[") AndAlso trimmed.EndsWith("]") Then
                section = trimmed.Substring(1, trimmed.Length - 2).ToUpperInvariant()
                If Not _data.ContainsKey(section) Then
                    _data(section) = New Dictionary(Of String, String)
                End If
                Continue For
            End If

            If String.IsNullOrEmpty(section) Then Continue For

            ' ── Paire clé=valeur ──────────────────────────────────────────
            Dim eqIdx = trimmed.IndexOf("="c)
            If eqIdx < 0 Then Continue For

            Dim key = trimmed.Substring(0, eqIdx).Trim().ToUpperInvariant()
            Dim value = trimmed.Substring(eqIdx + 1).Trim()

            _data(section)(key) = value
        Next

        RaiseEvent On_Loaded(_data.Count)
    End Sub

    ' ── Présence ──────────────────────────────────────────────────────────

    ''' <summary>True si la section existe dans le fichier INI.</summary>
    Public Function HasSection(section As String) As Boolean
        Return _data.ContainsKey(section.ToUpperInvariant())
    End Function

    ''' <summary>True si la clé existe dans la section donnée.</summary>
    Public Function HasKey(section As String, key As String) As Boolean
        Dim sectionUp = section.ToUpperInvariant()
        If Not _data.ContainsKey(sectionUp) Then Return False
        Return _data(sectionUp).ContainsKey(key.ToUpperInvariant())
    End Function

    ' ── Accesseurs typés ──────────────────────────────────────────────────

    ''' <summary>
    ''' Retourne la valeur brute d'une clé, ou <paramref name="defaultValue"/>
    ''' si la section ou la clé est absente.
    ''' </summary>
    Public Function GetString(section As String, key As String, defaultValue As String) As String
        Dim sectionUp = section.ToUpperInvariant()
        Dim keyUp = key.ToUpperInvariant()
        If Not _data.ContainsKey(sectionUp) Then Return defaultValue
        Dim sectionData = _data(sectionUp)
        If Not sectionData.ContainsKey(keyUp) Then Return defaultValue
        Return sectionData(keyUp)
    End Function

    ''' <summary>
    ''' Retourne la valeur d'une clé interprétée comme <c>Boolean</c>.
    ''' Valeurs truthy reconnues : <c>TRUE</c>, <c>1</c>.
    ''' Toute autre valeur est considérée False.
    ''' </summary>
    Public Function GetBoolean(section As String, key As String, defaultValue As Boolean) As Boolean
        Dim raw = GetString(section, key, Nothing)
        If raw Is Nothing Then Return defaultValue
        Dim upper = raw.ToUpperInvariant()
        Return upper = "TRUE" OrElse upper = "1"
    End Function

    ''' <summary>
    ''' Retourne la valeur d'une clé interprétée comme <c>Integer</c>.
    ''' Retourne <paramref name="defaultValue"/> si la conversion échoue.
    ''' </summary>
    Public Function GetInteger(section As String, key As String, defaultValue As Integer) As Integer
        Dim raw = GetString(section, key, Nothing)
        If raw Is Nothing Then Return defaultValue
        Dim result As Integer
        If Integer.TryParse(raw, result) Then
            Return result
        End If
        Return defaultValue
    End Function

    ''' <summary>
    ''' Retourne la valeur d'une clé interprétée comme <c>Double</c> (InvariantCulture).
    ''' Retourne <paramref name="defaultValue"/> si la conversion échoue.
    ''' </summary>
    Public Function GetDouble(section As String, key As String, defaultValue As Double) As Double
        Dim raw = GetString(section, key, Nothing)
        If raw Is Nothing Then Return defaultValue
        Dim result As Double
        If Double.TryParse(raw, Globalization.NumberStyles.Any,
                           Globalization.CultureInfo.InvariantCulture, result) Then
            Return result
        End If
        Return defaultValue
    End Function

    ''' <summary>
    ''' Retourne toutes les entrées d'une section dont la clé commence par
    ''' <paramref name="keyPrefix"/> (insensible à la casse), triées par clé.
    ''' <br/>
    ''' Exemple : <c>GetList("Plugins", "Plugin")</c> retourne les valeurs de
    ''' <c>Plugin0=</c>, <c>Plugin1=</c>, <c>Plugin2=</c>…
    ''' </summary>
    Public Function GetList(section As String, keyPrefix As String) As IReadOnlyList(Of String)
        Dim sectionUp = section.ToUpperInvariant()
        Dim prefixUp = keyPrefix.ToUpperInvariant()
        Dim result As New List(Of String)

        If Not _data.ContainsKey(sectionUp) Then
            Return result
        End If

        ' ── Filtrer + trier par clé pour respecter l'ordre Plugin0..N ────
        Dim sectionData = _data(sectionUp)
        Dim matching As New List(Of String)
        For Each k In sectionData.Keys
            If k.StartsWith(prefixUp) Then
                matching.Add(k)
            End If
        Next
        matching.Sort(StringComparer.Ordinal)

        For Each k In matching
            result.Add(sectionData(k))
        Next
        Return result
    End Function

    ''' <summary>
    ''' Retourne toutes les clés d'une section, ou une liste vide si la section
    ''' est absente.
    ''' </summary>
    Public Function GetKeys(section As String) As IReadOnlyList(Of String)
        Dim sectionUp = section.ToUpperInvariant()
        If Not _data.ContainsKey(sectionUp) Then
            Return Array.Empty(Of String)()
        End If
        Return New List(Of String)(_data(sectionUp).Keys)
    End Function

    ''' <summary>
    ''' Retourne toutes les sections présentes dans le fichier INI.
    ''' </summary>
    Public Function GetSections() As IReadOnlyList(Of String)
        Return New List(Of String)(_data.Keys)
    End Function

End Class