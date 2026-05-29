Imports System.IO

Namespace Utility
    Module Utility

        ''' <summary>
        ''' Retourne un nouveau dictionnaire avec les clés et valeurs inversées quand c'est possible (si doublons, uniquement le premier utilisé)
        ''' </summary>
        Public Function ReverseDictionary(Of TKey, TValue)(source As Dictionary(Of TKey, TValue)) As Dictionary(Of TValue, TKey)
            Dim OutputDic As New Dictionary(Of TValue, TKey)
            For Each Key In source.Keys
                Dim Value = source(Key)
                If OutputDic.ContainsKey(Value) = False Then
                    OutputDic.Add(Value, Key)
                End If
            Next
            Return OutputDic
        End Function

        ''' <summary>
        ''' Mappe une valeur dans [Min, Max] vers un index LED dans [0, Led_Number - 1].
        ''' </summary>
        Public Function GetLedIndex(Value As Double, Min As Double, Max As Double, Led_Number As Integer) As Integer
            If Led_Number < 2 OrElse Max <= Min Then Return 0          ' ✅ garde-fous

            Value = Math.Max(Min, Math.Min(Max, Value))                ' ✅ clamp → élimine <Min et >Max

            If Value <= Min Then Return 0                              ' ✅ borne basse
            If Value >= Max Then Return Led_Number - 1                 ' ✅ borne haute

            ' ✅ O(1) : index direct sans boucle
            Dim ratio As Double = (Value - Min) / (Max - Min)          ' 0.0 < ratio < 1.0
            Return CInt(Math.Floor(ratio * (Led_Number - 1)))          ' ✅ [1, Led_Number-2]
        End Function
    End Module
    Public Module ReaperFxList
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
        ''' Lit la liste de tous les plugins installés dans Reaper
        ''' directement depuis les fichiers de cache Reaper (%APPDATA%\REAPER).
        ''' </summary>
        ''' <returns>Liste de noms de plugins (format "VST3: ReaComp (Cockos)")</returns>
        Public Function GetInstalledPlugins() As List(Of String)
            Dim plugins As New List(Of String)

            Dim reaperDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "REAPER")

            ' ✅ Reaper scinde les plugins par type dans plusieurs fichiers
            Dim cacheFiles = {
                "reaper-vstplugins64.ini",   ' VST2 / VST3
                "reaper-fxplugins.ini"       ' JSFX + autres
            }

            For Each cacheFile In cacheFiles
                Dim fullPath = Path.Combine(reaperDataPath, cacheFile)
                If Not IO.File.Exists(fullPath) Then Continue For

                For Each line In IO.File.ReadAllLines(fullPath)
                    ' ✅ Les sections [VST3: ReaComp (Cockos)] = noms utilisables directement
                    If line.StartsWith("[") AndAlso line.EndsWith("]") Then
                        Dim name = line.Substring(1, line.Length - 2).Trim()
                        ' Exclure les sections techniques (contiennent "=" ou sont vides)
                        If name.Length > 0 AndAlso Not name.Contains("=") Then
                            plugins.Add(name)
                        End If
                    End If
                Next
            Next

            Return plugins.Distinct().OrderBy(Function(p) p).ToList()
        End Function

        ''' <summary>Filtre les plugins par type (VST, VST3, JS, AU...).</summary>
        Public Function GetInstalledPluginsByType(FilterType As String) As List(Of String)
            Return GetInstalledPlugins() _
                .Where(Function(p) p.StartsWith(FilterType, StringComparison.OrdinalIgnoreCase)) _
                .ToList()
        End Function

    End Module

    Public Module ReaperFxParamList

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

    Namespace Bit
        Module BitManager

            Function GetBytes(Bits_indexes As Integer(), Input_Bytes As Byte(), Optional Bit_Value As Boolean = True) As Byte()
                Dim _bitarray As New BitArray(Input_Bytes)
                For Each _Bit In Bits_indexes
                    _bitarray.Set((_bitarray.Count - 1) - _Bit, Bit_Value)
                Next
                _bitarray.CopyTo(Input_Bytes, 0)
                Dim Output_Bytes_List = Input_Bytes.ToList
                Output_Bytes_List.Reverse()
                Return Output_Bytes_List.ToArray
            End Function

            Function GetBits_Indexes(Input_Bytes As Byte(), Optional Bit_Value As Boolean = True) As Integer()
                Dim Bits_indexes As New List(Of Integer)
                Dim Input_Bytes_List = Input_Bytes.ToList
                Input_Bytes_List.Reverse()

                Dim _bitarray As New BitArray(Input_Bytes)
                For i = 0 To _bitarray.Count - 1
                    If _bitarray.Item(i) = Bit_Value Then
                        Bits_indexes.Add(i)
                    End If
                Next

                Return Bits_indexes.ToArray
            End Function

            ''' <summary>
            ''' Retourne un nouveau BitArray avec les bits dans l'ordre inverse.
            ''' </summary>
            Public Function ReverseBitArray(source As BitArray) As BitArray
                Dim n = source.Count
                Dim result As New BitArray(n)
                For i = 0 To n - 1
                    result(i) = source(n - 1 - i)   ' ✅ swap direct, 0 allocation intermédiaire
                Next
                Return result
            End Function
        End Module

        Module ByteManager

            Public Function ConcatBytes(ParamArray arrays As Byte()()) As Byte()
                Dim result(arrays.Sum(Function(a) a.Length) - 1) As Byte
                Dim offset As Integer = 0
                For Each arr In arrays
                    Buffer.BlockCopy(arr, 0, result, offset, arr.Length)
                    offset += arr.Length
                Next
                Return result
            End Function

            ''' <summary>
            ''' Extrait une sous-séquence de bytes par copie directe en mémoire.
            ''' Plus rapide que Span, LINQ ou Array.Copy pour les petits tableaux (< 1KB).
            ''' </summary>
            Public Function SliceBytes(source As Byte(), offset As Integer, length As Integer) As Byte()
                Dim result(length - 1) As Byte
                Buffer.BlockCopy(source, offset, result, 0, length)
                Return result
            End Function

            Public Function ReverseBytes(source As Byte()) As Byte()
                Dim result(source.Length - 1) As Byte
                Buffer.BlockCopy(source, 0, result, 0, source.Length)
                Array.Reverse(result)
                Return result
            End Function
            ''' <summary>
            ''' Calcule le checksum 16 bits Big-Endian d'un tableau de bytes.
            ''' Algorithme : somme arithmétique simple de tous les bytes.
            ''' Confirmé sur le protocole C24 (champ Unknown1/DataChecksum).
            ''' Exemple : {&HF0, &H13, &H01} → somme = 260 = {&H01, &H04}
            ''' </summary>
            Public Function CalcChecksum(source As Byte()) As Byte()
                If source Is Nothing OrElse source.Length = 0 Then
                    Return {&H0, &H0}
                End If
                Dim sum As UInt32 = 0
                For Each b In source
                    sum += b
                Next
                Return ReverseBytes({CByte((sum >> 8) And &HFF), CByte(sum And &HFF)})
            End Function

            ''' <summary>
            ''' Convertit un UInt16 en tableau de 2 bytes Little-Endian Inverse
            ''' </summary>
            Public Function UInt16ToBytesLErev(value As UInt16) As Byte()
                Return ReverseBytes({CByte(value And &HFF), CByte((value >> 8) And &HFF)})
            End Function

            ''' <summary>
            ''' Convertit un UInt32 en tableau de 4 bytes Little-Endian Inverse
            ''' </summary>
            Public Function UInt32ToBytesLErev(value As UInt32) As Byte()
                Return ReverseBytes({
                    CByte(value And &HFF),
                    CByte((value >> 8) And &HFF),
                    CByte((value >> 16) And &HFF),
                    CByte((value >> 24) And &HFF)
                })
            End Function

            ''' <summary>
            ''' Convertit un tableau de 2 bytes Little-Endian Inverse en UInt16
            ''' </summary>
            Public Function BytesLEToUInt16(source As Byte()) As UInt16
                source = ReverseBytes(source)
                Return CUShort(source(0) Or (CUShort(source(1)) << 8))
            End Function

            ''' <summary>
            ''' Convertit un tableau de 4 bytes Little-Endian Inverse en UInt32
            ''' </summary>
            Public Function BytesLEToUInt32(source As Byte()) As UInt32
                source = ReverseBytes(source)
                Return CUInt(source(0)) Or
                       (CUInt(source(1)) << 8) Or
                       (CUInt(source(2)) << 16) Or
                       (CUInt(source(3)) << 24)
            End Function

            ''' <summary>
            ''' Applique un masque AND sur chaque byte d'un tableau
            ''' </summary>
            Public Function AndBytes(source As Byte(), mask As Byte()) As Byte()
                Dim result(source.Length - 1) As Byte
                For i = 0 To source.Length - 1
                    If i >= mask.Count Then
                        result(i) = CByte(source(i))
                    Else
                        result(i) = CByte(source(i) And mask(i))
                    End If

                Next
                Return result
            End Function

            ''' <summary>
            ''' Décalage binaire à gauche d'un tableau de bytes
            ''' Ex: {0b00000001, 0b10000000} << 1 → {0b00000011, 0b00000000}
            ''' </summary>
            Public Function ShiftBytesLeft(source As Byte(), positions As Integer) As Byte()
                Dim result(source.Length - 1) As Byte
                Buffer.BlockCopy(source, 0, result, 0, source.Length)
                For p = 1 To positions
                    Dim carry As Byte = 0
                    For i = result.Length - 1 To 0 Step -1
                        Dim newCarry As Byte = CByte((result(i) And &H80) >> 7)  ' MSB sortant
                        result(i) = CByte((result(i) << 1) Or carry)
                        carry = newCarry
                    Next
                Next
                Return result
            End Function

            ''' <summary>
            ''' Décalage binaire à droite d'un tableau de bytes
            ''' Ex: {0b10000000, 0b00000001} >> 1 → {0b01000000, 0b10000000}
            ''' </summary>
            Public Function ShiftBytesRight(source As Byte(), positions As Integer) As Byte()
                Dim result(source.Length - 1) As Byte
                Buffer.BlockCopy(source, 0, result, 0, source.Length)
                For p = 1 To positions
                    Dim carry As Byte = 0
                    For i = 0 To result.Length - 1
                        Dim newCarry As Byte = CByte(result(i) And &H1)           ' LSB sortant
                        result(i) = CByte((result(i) >> 1) Or (carry << 7))
                        carry = newCarry
                    Next
                Next
                Return result
            End Function


            ''' <summary>
            ''' Applique un OR bit à bit sur chaque byte de deux tableaux
            ''' </summary>
            Public Function OrBytes(source As Byte(), mask As Byte()) As Byte()
                Dim result(source.Length - 1) As Byte
                For i = 0 To source.Length - 1
                    result(i) = CByte(source(i) Or mask(i))
                Next
                Return result
            End Function
        End Module
    End Namespace

End Namespace





















