' ══════════════════════════════════════════════════════════════════════════
'  REAPER CONFIG WRITER — écriture générique de fichier INI
' ══════════════════════════════════════════════════════════════════════════

''' <summary>
''' Construit et écrit un fichier INI au format Reaper/DriverControl24.
''' <br/>
''' Conserve l'ordre d'insertion des sections et des clés.
''' Complémentaire de <see cref="ReaperConfigReader"/>.
''' <br/>
''' Usage :
''' <code>
''' Dim writer As New ReaperConfigWriter("C:\...\ReaperDriver.ini")
''' writer.SetFileHeader("; ReaperDriverControl24 — Configuration")
''' writer.AddSection("Driver", "; Paramètres du driver")
''' writer.SetBoolean("Driver", "Autostart", False, "; Démarrage automatique")
''' writer.SetString("Driver", "NetworkInterface", "Ethernet 2")
''' writer.AddSection("Plugins", "; Plugins installés — ne pas modifier")
''' writer.SetList("Plugins", "Plugin", myPlugins)
''' writer.Save()
''' </code>
''' </summary>
Public Class ReaperConfigWriter

    ' ── Types internes ────────────────────────────────────────────────────

    Private Enum EntryKind
        Comment
        KeyValue
        Blank
    End Enum

    Private Structure WriterEntry
        Public ReadOnly Kind As EntryKind
        Public ReadOnly Key As String
        Public ReadOnly Value As String
        Public ReadOnly Comment As String

        Public Shared Function ForComment(text As String) As WriterEntry
            Dim e As New WriterEntry()
            Dim eKind As EntryKind = EntryKind.Comment
            Dim eKey As String = String.Empty
            Dim eValue As String = String.Empty
            Dim eComment As String = text
            Return New WriterEntry(eKind, eKey, eValue, eComment)
        End Function

        Public Shared Function ForKeyValue(key As String, value As String, comment As String) As WriterEntry
            Return New WriterEntry(EntryKind.KeyValue, key, value, comment)
        End Function

        Public Shared Function ForBlank() As WriterEntry
            Return New WriterEntry(EntryKind.Blank, String.Empty, String.Empty, String.Empty)
        End Function

        Private Sub New(kind As EntryKind, key As String, value As String, comment As String)
            Me.Kind = kind
            Me.Key = key
            Me.Value = value
            Me.Comment = comment
        End Sub
    End Structure

    ' ── État interne ──────────────────────────────────────────────────────

    Private ReadOnly _iniPath As String
    Private ReadOnly _sectionOrder As New List(Of String)
    Private ReadOnly _sections As New Dictionary(Of String, List(Of WriterEntry))
    Private ReadOnly _sectionComments As New Dictionary(Of String, String)
    Private _fileHeader As String = String.Empty

    ' ── Événements ────────────────────────────────────────────────────────

    ''' <summary>Déclenché après une sauvegarde réussie.</summary>
    Public Event On_Saved(iniPath As String)

    ''' <summary>Déclenché si une erreur survient lors de la sauvegarde.</summary>
    Public Event On_SaveError(iniPath As String, message As String)

    ' ── Constructeur ──────────────────────────────────────────────────────

    ''' <summary>
    ''' Initialise le writer pour le chemin INI spécifié.
    ''' </summary>
    ''' <param name="iniPath">Chemin complet du fichier INI à écrire.</param>
    Public Sub New(iniPath As String)
        _iniPath = iniPath
    End Sub

    ' ── Propriétés ────────────────────────────────────────────────────────

    ''' <summary>Chemin complet du fichier INI cible.</summary>
    Public ReadOnly Property IniPath As String
        Get
            Return _iniPath
        End Get
    End Property

    ' ── En-tête de fichier ────────────────────────────────────────────────

    ''' <summary>
    ''' Définit les lignes d'en-tête écrites au tout début du fichier,
    ''' avant toute section.  Chaque ligne doit commencer par <c>;</c>.
    ''' </summary>
    Public Sub SetFileHeader(header As String)
        _fileHeader = header
    End Sub

    ' ── Gestion des sections ──────────────────────────────────────────────

    ''' <summary>
    ''' Crée la section si elle n'existe pas encore.
    ''' Si <paramref name="comment"/> est non vide, il est écrit juste avant
    ''' l'en-tête <c>[Section]</c>.
    ''' </summary>
    Public Sub AddSection(section As String, Optional comment As String = "")
        Dim key = section.ToUpperInvariant()
        If _sections.ContainsKey(key) Then Return
        _sectionOrder.Add(key)
        _sections(key) = New List(Of WriterEntry)
        _sectionComments(key) = comment
    End Sub

    ''' <summary>True si la section a déjà été ajoutée.</summary>
    Public Function HasSection(section As String) As Boolean
        Return _sections.ContainsKey(section.ToUpperInvariant())
    End Function

    ''' <summary>Supprime tous les contenus de la section (garde la section vide).</summary>
    Public Sub ClearSection(section As String)
        Dim key = section.ToUpperInvariant()
        If Not _sections.ContainsKey(key) Then Return
        _sections(key).Clear()
    End Sub

    ' ── Ajout de commentaires dans une section ────────────────────────────

    ''' <summary>
    ''' Ajoute une ligne de commentaire dans la section.
    ''' Crée la section si nécessaire.
    ''' </summary>
    Public Sub AddComment(section As String, comment As String)
        EnsureSection(section)
        _sections(section.ToUpperInvariant()).Add(WriterEntry.ForComment(comment))
    End Sub

    ''' <summary>Ajoute une ligne vide dans la section.</summary>
    Public Sub AddBlankLine(section As String)
        EnsureSection(section)
        _sections(section.ToUpperInvariant()).Add(WriterEntry.ForBlank())
    End Sub

    ' ── Setters typés ─────────────────────────────────────────────────────

    ''' <summary>
    ''' Écrit ou remplace une valeur <c>String</c>.
    ''' Crée la section si nécessaire.
    ''' </summary>
    ''' <param name="comment">Commentaire optionnel écrit sur la ligne précédente.</param>
    Public Sub SetString(section As String, key As String, value As String,
                         Optional comment As String = "")
        SetEntry(section, key, value, comment)
    End Sub

    ''' <summary>
    ''' Écrit ou remplace une valeur <c>Boolean</c> (<c>True</c>/<c>False</c>).
    ''' </summary>
    Public Sub SetBoolean(section As String, key As String, value As Boolean,
                          Optional comment As String = "")
        SetEntry(section, key, If(value, "True", "False"), comment)
    End Sub

    ''' <summary>
    ''' Écrit ou remplace une valeur <c>Integer</c>.
    ''' </summary>
    Public Sub SetInteger(section As String, key As String, value As Integer,
                          Optional comment As String = "")
        SetEntry(section, key, value.ToString(Globalization.CultureInfo.InvariantCulture), comment)
    End Sub

    ''' <summary>
    ''' Écrit ou remplace une valeur <c>Double</c> en <c>InvariantCulture</c>.
    ''' </summary>
    Public Sub SetDouble(section As String, key As String, value As Double,
                         Optional comment As String = "")
        SetEntry(section, key, value.ToString("G", Globalization.CultureInfo.InvariantCulture), comment)
    End Sub

    ''' <summary>
    ''' Écrit une liste de valeurs sous la forme <c>Prefix0=</c>, <c>Prefix1=</c>…
    ''' Remplace intégralement les entrées existantes portant ce préfixe.
    ''' </summary>
    ''' <param name="keyPrefix">Préfixe des clés, ex : <c>"Plugin"</c>.</param>
    ''' <param name="values">Valeurs à écrire.</param>
    ''' <param name="comment">Commentaire optionnel écrit avant la première entrée.</param>
    Public Sub SetList(section As String, keyPrefix As String,
                       values As IEnumerable(Of String),
                       Optional comment As String = "")
        Dim sectionKey = section.ToUpperInvariant()
        Dim prefixUp = keyPrefix.ToUpperInvariant()
        EnsureSection(section)

        ' ── Supprimer les entrées existantes avec ce préfixe ──────────────
        Dim entries = _sections(sectionKey)
        Dim toRemove As New List(Of Integer)
        For i = 0 To entries.Count - 1
            If entries(i).Kind = EntryKind.KeyValue AndAlso
               entries(i).Key.StartsWith(prefixUp) Then
                toRemove.Add(i)
            End If
        Next
        For i = toRemove.Count - 1 To 0 Step -1
            entries.RemoveAt(toRemove(i))
        Next

        ' ── Ajouter le commentaire puis les nouvelles entrées ─────────────
        Dim idx = 0
        Dim isFirst = True
        For Each v In values
            Dim entryComment = String.Empty
            If isFirst AndAlso Not String.IsNullOrEmpty(comment) Then
                entryComment = comment
                isFirst = False
            End If
            entries.Add(WriterEntry.ForKeyValue(
                $"{keyPrefix}{idx}",
                v,
                entryComment))
            idx += 1
        Next
    End Sub

    ' ── Sauvegarde ────────────────────────────────────────────────────────

    ''' <summary>
    ''' Génère et écrit le fichier INI sur le disque.
    ''' Déclenche <see cref="On_Saved"/> ou <see cref="On_SaveError"/>.
    ''' </summary>
    Public Sub Save()
        Try
            Dim lines As New List(Of String)

            ' ── En-tête de fichier ────────────────────────────────────────
            If Not String.IsNullOrEmpty(_fileHeader) Then
                lines.Add(_fileHeader)
                lines.Add(String.Empty)
            End If

            ' ── Sections dans l'ordre d'insertion ─────────────────────────
            For Each sectionKey In _sectionOrder
                ' Commentaire de section
                Dim sectionComment As String = String.Empty
                If _sectionComments.TryGetValue(sectionKey, sectionComment) Then
                    If Not String.IsNullOrEmpty(sectionComment) Then
                        lines.Add(sectionComment)
                    End If
                End If

                ' En-tête [Section]
                ' Le nom affiché = première lettre en majuscule, reste en minuscule
                Dim displayName = sectionKey.Substring(0, 1) &
                                  sectionKey.Substring(1).ToLowerInvariant()
                lines.Add($"[{displayName}]")

                ' Entrées
                For Each entry In _sections(sectionKey)
                    Select Case entry.Kind
                        Case EntryKind.Comment
                            lines.Add(entry.Comment)
                        Case EntryKind.Blank
                            lines.Add(String.Empty)
                        Case EntryKind.KeyValue
                            If Not String.IsNullOrEmpty(entry.Comment) Then
                                lines.Add(entry.Comment)
                            End If
                            lines.Add($"{entry.Key}={entry.Value}")
                    End Select
                Next
                lines.Add(String.Empty)
            Next

            IO.File.WriteAllLines(_iniPath, lines)
            RaiseEvent On_Saved(_iniPath)

        Catch ex As Exception
            RaiseEvent On_SaveError(_iniPath, ex.Message)
        End Try
    End Sub

    ' ── Helpers privés ────────────────────────────────────────────────────

    Private Sub EnsureSection(section As String)
        Dim key = section.ToUpperInvariant()
        If Not _sections.ContainsKey(key) Then
            AddSection(section)
        End If
    End Sub

    Private Sub SetEntry(section As String, key As String, value As String, comment As String)
        Dim sectionKey = section.ToUpperInvariant()
        Dim keyUp = key.ToUpperInvariant()
        EnsureSection(section)

        Dim entries = _sections(sectionKey)

        ' ── Remplacer si la clé existe déjà ──────────────────────────────
        For i = 0 To entries.Count - 1
            If entries(i).Kind = EntryKind.KeyValue AndAlso entries(i).Key = keyUp Then
                entries(i) = WriterEntry.ForKeyValue(keyUp, value, comment)
                Return
            End If
        Next

        ' ── Ajouter en fin de section ─────────────────────────────────────
        entries.Add(WriterEntry.ForKeyValue(keyUp, value, comment))
    End Sub

End Class