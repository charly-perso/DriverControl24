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





















