' ══════════════════════════════════════════════════════════════════════════
'  FX NAMING — compression des noms de plugins et de paramètres FX
'  Logique pure (sans dépendance à Reaper), partagée entre le driver
'  et l'éditeur web de configuration.
' ══════════════════════════════════════════════════════════════════════════

Public Module FxNaming

    ''' <summary>
    ''' Compresse un nom de FX en exactement 8 caractères selon la cascade :
    ''' 1. Déjà ≤ 8 → padded tel quel.
    ''' 2. Suppression du préfixe de format (VST3:, VST:, CLAP:, JS:) et du suffixe éditeur entre parenthèses.
    ''' 3. Suppression des voyelles internes (hors 1re lettre de chaque mot).
    ''' 4. Troncature forcée Left(4) + Right(4).
    ''' </summary>
    ''' <param name="fxName">Nom complet du FX.</param>
    ''' <returns>Nom compressé de exactement 8 caractères.</returns>
    Public Function CompressFxName(fxName As String) As String
        If String.IsNullOrWhiteSpace(fxName) Then
            Return "        "
        End If

        Dim name = fxName.Trim()

        ' ── Étape 1 ──────────────────────────────────────────────────────
        If name.Length <= 8 Then
            Return name.PadRight(8).Substring(0, 8)
        End If

        ' ── Étape 2 : strip préfixe format + suffixe éditeur ─────────────
        Dim prefixes As String() = {"VST3:", "VST2:", "VST:", "CLAP:", "JS:", "AU:"}
        For Each prefix In prefixes
            If name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then
                name = name.Substring(prefix.Length).Trim()
                Exit For
            End If
        Next

        Dim parenOpen = name.LastIndexOf("("c)
        If parenOpen > 0 Then
            name = name.Substring(0, parenOpen).Trim()
        End If

        If name.Length <= 8 Then
            Return name.PadRight(8).Substring(0, 8)
        End If

        ' ── Étape 3 : suppression des voyelles internes ───────────────────
        Dim vowels As New HashSet(Of Char)({"a"c, "e"c, "i"c, "o"c, "u"c,
                                            "A"c, "E"c, "I"c, "O"c, "U"c})
        Dim words = name.Split({" "c, "-"c, "_"c, "."c}, StringSplitOptions.RemoveEmptyEntries)
        Dim parts As New List(Of String)

        For Each word In words
            If word.Length <= 1 Then
                parts.Add(word)
                Continue For
            End If
            Dim kept As New System.Text.StringBuilder()
            kept.Append(word(0))
            For j = 1 To word.Length - 1
                If Not vowels.Contains(word(j)) Then
                    kept.Append(word(j))
                End If
            Next
            parts.Add(kept.ToString())
        Next

        name = String.Join("", parts)

        If name.Length <= 8 Then
            Return name.PadRight(8).Substring(0, 8)
        End If

        ' ── Étape 4 : troncature forcée Left(4) + Right(4) ───────────────
        name = name.Substring(0, 4) & name.Substring(name.Length - 4, 4)

        Return name.PadRight(8).Substring(0, 8)
    End Function

    ''' <summary>
    ''' Compresse un nom de paramètre FX en 4 caractères (initiales de chaque mot).
    ''' Garantit l'unicité dans la liste fournie en ajoutant un suffixe numérique si besoin.
    ''' </summary>
    ''' <param name="paramName">Nom complet du paramètre.</param>
    ''' <param name="usedNames">Ensemble des noms déjà attribués (mis à jour en sortie).</param>
    ''' <returns>Nom compressé de exactement 4 caractères, unique dans <paramref name="usedNames"/>.</returns>
    Public Function CompressParamNameShort(paramName As String, usedNames As HashSet(Of String)) As String
        Dim compressed = BuildShortName(paramName)
        Dim unique = EnsureUnique(compressed, 4, usedNames)
        usedNames.Add(unique)
        Return unique
    End Function

    ''' <summary>
    ''' Compresse un nom de paramètre FX en 8 caractères (troncature intelligente par mots).
    ''' Garantit l'unicité dans la liste fournie en ajoutant un suffixe numérique si besoin.
    ''' </summary>
    ''' <param name="paramName">Nom complet du paramètre.</param>
    ''' <param name="usedNames">Ensemble des noms déjà attribués (mis à jour en sortie).</param>
    ''' <returns>Nom compressé de exactement 8 caractères, unique dans <paramref name="usedNames"/>.</returns>
    Public Function CompressParamNameLong(paramName As String, usedNames As HashSet(Of String)) As String
        Dim compressed = BuildLongName(paramName)
        Dim unique = EnsureUnique(compressed, 8, usedNames)
        usedNames.Add(unique)
        Return unique
    End Function

    ''' <summary>
    ''' Génère les noms court (4 car.) et long (8 car.) pour une liste de paramètres,
    ''' en garantissant l'unicité globale au sein de chaque liste.
    ''' </summary>
    ''' <param name="paramNames">Noms complets des paramètres.</param>
    ''' <returns>Liste de tuples (NomCourt4, NomLong8).</returns>
    Public Function BuildParamNamePairs(paramNames As IReadOnlyList(Of String)) As List(Of (Short4 As String, Long8 As String))
        Dim result As New List(Of (Short4 As String, Long8 As String))
        Dim usedShort As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim usedLong As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        For Each name In paramNames
            Dim s = CompressParamNameShort(name, usedShort)
            Dim l = CompressParamNameLong(name, usedLong)
            result.Add((s, l))
        Next

        Return result
    End Function

    ' ── Helpers privés ────────────────────────────────────────────────────

    ''' <summary>
    ''' Construit un nom court de 4 caractères : initiales de chaque mot.
    ''' Si moins de 4 mots, complète avec les premières lettres du premier mot.
    ''' </summary>
    Private Function BuildShortName(paramName As String) As String
        If String.IsNullOrWhiteSpace(paramName) Then
            Return "    "
        End If

        Dim words = paramName.Trim().Split(
            {" "c, "-"c, "_"c, "."c, "/"c, "("c, ")"c},
            StringSplitOptions.RemoveEmptyEntries)

        Dim initials As New System.Text.StringBuilder()

        For Each word In words
            If initials.Length >= 4 Then
                Exit For
            End If
            initials.Append(word(0))
        Next

        ' ── Compléter si moins de 4 initiales ────────────────────────────
        If initials.Length < 4 AndAlso words.Length > 0 Then
            Dim firstWord = words(0)
            Dim pos As Integer = 1
            While initials.Length < 4 AndAlso pos < firstWord.Length
                initials.Append(firstWord(pos))
                pos += 1
            End While
        End If

        Return initials.ToString().PadRight(4).Substring(0, 4)
    End Function

    ''' <summary>
    ''' Construit un nom long de 8 caractères : premier(s) mot(s) tronqué(s).
    ''' Concatène les mots jusqu'à 8 caractères, puis tronque si nécessaire.
    ''' </summary>
    Private Function BuildLongName(paramName As String) As String
        If String.IsNullOrWhiteSpace(paramName) Then
            Return "        "
        End If

        Dim name = paramName.Trim()

        If name.Length <= 8 Then
            Return name.PadRight(8).Substring(0, 8)
        End If

        ' ── Concatène les mots sans séparateur jusqu'à 8 car. ────────────
        Dim words = name.Split(
            {" "c, "-"c, "_"c, "."c, "/"c, "("c, ")"c},
            StringSplitOptions.RemoveEmptyEntries)

        Dim built As New System.Text.StringBuilder()
        For Each word In words
            If built.Length >= 8 Then
                Exit For
            End If
            Dim remaining = 8 - built.Length
            built.Append(word.Substring(0, Math.Min(word.Length, remaining)))
        Next

        Return built.ToString().PadRight(8).Substring(0, 8)
    End Function

    ''' <summary>
    ''' Garantit l'unicité d'un nom en ajoutant un suffixe numérique croissant si nécessaire.
    ''' Le nom résultat fait exactement <paramref name="totalLen"/> caractères.
    ''' </summary>
    ''' <param name="name">Nom candidat (exactement <paramref name="totalLen"/> car.).</param>
    ''' <param name="totalLen">Longueur cible (4 ou 8).</param>
    ''' <param name="usedNames">Noms déjà utilisés.</param>
    Private Function EnsureUnique(name As String, totalLen As Integer, usedNames As HashSet(Of String)) As String
        If Not usedNames.Contains(name) Then
            Return name
        End If

        Dim suffix As Integer = 2
        Dim unique As String = name

        While usedNames.Contains(unique)
            Dim suffixStr = suffix.ToString()
            Dim baseLen = Math.Max(1, totalLen - suffixStr.Length)
            unique = name.Substring(0, Math.Min(baseLen, name.TrimEnd().Length)).TrimEnd()
            unique = unique.PadRight(baseLen).Substring(0, baseLen) & suffixStr
            unique = unique.PadRight(totalLen).Substring(0, totalLen)
            suffix += 1
        End While

        Return unique
    End Function

End Module
