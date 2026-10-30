Imports System.Globalization
Imports System.Runtime
Imports System.Runtime.InteropServices
Imports System.Runtime.Intrinsics
Imports System.Threading
Imports System.Xml
Imports DriverConfig
Imports DriverReaperControl24.Reaper.Monitors
Imports DriverReaperControl24.Reaper.Monitors.Reaper.Monitors
Imports DriverReaperControl24.Reaper.PageManagers
Imports DriverReaperControl24.Utility

Namespace Reaper

#Region "Utility"

    Public Module utility
        Private Const BridgePoolSize As Integer = 4
        Private ReadOnly _bridgeSlots As New SemaphoreSlim(BridgePoolSize, BridgePoolSize)
        Private ReadOnly _bridgePool As New System.Collections.Concurrent.ConcurrentBag(Of IO.Pipes.NamedPipeClientStream)

        ''' <summary>
        ''' Envoie une requête au bridge via une connexion persistante empruntée au pool.
        ''' Retente une fois avec une connexion neuve si la connexion réutilisée est périmée.
        ''' </summary>
        Public Function CallReaperBridge(request As String) As String
            _bridgeSlots.Wait()
            Try
                For attempt = 0 To 1
                    Dim pipe As IO.Pipes.NamedPipeClientStream = Nothing
                    Dim reused As Boolean = _bridgePool.TryTake(pipe)
                    Try
                        If Not reused Then
                            pipe = New IO.Pipes.NamedPipeClientStream(
                        ".", "c24_reaper_bridge", IO.Pipes.PipeDirection.InOut)
                            pipe.Connect(timeout:=200)
                        End If

                        WriteLineBytes(pipe, request)
                        Dim response As String = ReadLineBytes(pipe)
                        If response Is Nothing Then
                            Throw New IO.IOException("Connexion fermée par le bridge")
                        End If

                        _bridgePool.Add(pipe)
                        Return response
                    Catch ex As Exception
                        If pipe IsNot Nothing Then
                            pipe.Dispose()
                        End If
                        If reused = False Then
                            Console.WriteLine(ex.Message)
                            Return String.Empty
                        End If
                    End Try
                Next
                Return String.Empty
            Finally
                _bridgeSlots.Release()
            End Try
        End Function


        '' Dans C24_Desk — test de connexion au bridge Reaper
        'Public Function CallReaperBridge(request As String) As String
        '    Try
        '        Using pipe As New IO.Pipes.NamedPipeClientStream(
        '".", "c24_reaper_bridge",
        'IO.Pipes.PipeDirection.InOut)

        '            pipe.Connect(timeout:=200)
        '            WriteLineBytes(pipe, request)
        '            Return ReadLineBytes(pipe)
        '        End Using
        '    Catch ex As Exception
        '        Console.WriteLine(ex.Message)
        '        Return String.Empty   ' Bridge non démarré (Reaper fermé / plugin absent)
        '    End Try
        'End Function

        ''' <summary>
        ''' Écrit la requête suivie de LF dans le flux, en UTF-8, avec un buffer emprunté à ArrayPool.
        ''' </summary>
        ''' <param name="stream">Flux de destination.</param>
        ''' <param name="text">Texte à envoyer.</param>
        Friend Sub WriteLineBytes(stream As IO.Stream, text As String)
            Dim maxLength As Integer = System.Text.Encoding.UTF8.GetMaxByteCount(text.Length) + 1
            Dim buffer As Byte() = System.Buffers.ArrayPool(Of Byte).Shared.Rent(maxLength)
            Try
                Dim count As Integer = System.Text.Encoding.UTF8.GetBytes(text, 0, text.Length, buffer, 0)
                buffer(count) = 10
                stream.Write(buffer, 0, count + 1)
                stream.Flush()
            Finally
                System.Buffers.ArrayPool(Of Byte).Shared.Return(buffer)
            End Try
        End Sub

        ''' <summary>
        ''' Lit une ligne (jusqu'à LF) depuis le flux avec un buffer emprunté à ArrayPool.
        ''' Retourne Nothing si le flux est fermé avant tout octet reçu.
        ''' </summary>
        ''' <param name="stream">Flux source.</param>
        ''' <returns>La ligne lue sans CR/LF final.</returns>
        Friend Function ReadLineBytes(stream As IO.Stream) As String
            Dim buffer As Byte() = System.Buffers.ArrayPool(Of Byte).Shared.Rent(1024)
            Dim total As Integer = 0
            Try
                While True
                    If total = buffer.Length Then
                        Dim bigger As Byte() = System.Buffers.ArrayPool(Of Byte).Shared.Rent(buffer.Length * 2)
                        System.Buffer.BlockCopy(buffer, 0, bigger, 0, total)
                        System.Buffers.ArrayPool(Of Byte).Shared.Return(buffer)
                        buffer = bigger
                    End If

                    Dim read As Integer = stream.Read(buffer, total, buffer.Length - total)
                    If read = 0 Then
                        If total = 0 Then
                            Return Nothing
                        End If
                        Exit While
                    End If

                    Dim lf As Integer = Array.IndexOf(buffer, CByte(10), total, read)
                    total += read
                    If lf >= 0 Then
                        total = lf
                        Exit While
                    End If
                End While

                If total > 0 AndAlso buffer(total - 1) = 13 Then
                    total -= 1
                End If
                Return System.Text.Encoding.UTF8.GetString(buffer, 0, total)
            Finally
                System.Buffers.ArrayPool(Of Byte).Shared.Return(buffer)
            End Try
        End Function
        ''' <summary>
        ''' Retourne l'index 0-based de la dernière piste modifiée par l'utilisateur.
        ''' Retourne -1 si aucune piste n'a encore été modifiée.
        ''' </summary>
        Public Function GetLastTouchedTrackIndex() As Integer
            Dim raw = CallReaperBridge("GET_LAST_TOUCHED_TRACK")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim idx As Integer = -1
            Integer.TryParse(raw, idx)
            Return idx
        End Function

        ''' <summary>
        ''' Retourne les coordonnées du dernier paramètre FX touché.
        ''' Retourne (-1, -1, -1) si aucun FX n'a été touché.
        ''' trackIndex = -1 pour la piste maître.
        ''' </summary>
        Public Function GetLastTouchedFXInfo() As (trackIndex As Integer, fxIndex As Integer, paramIndex As Integer)
            Dim raw = CallReaperBridge("GET_LAST_TOUCHED_FX")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return (-1, -1, -1)
            Dim p = raw.Split("|"c)
            If p.Length < 3 Then Return (-1, -1, -1)
            Dim t As Integer = -1, f As Integer = -1, pm As Integer = -1
            Integer.TryParse(p(0), t)
            Integer.TryParse(p(1), f)
            Integer.TryParse(p(2), pm)
            Return (t, f, pm)
        End Function

        ''' <summary>
        ''' Retourne un <see cref="ReaperFXParam"/> chargé pour le dernier paramètre FX touché.
        ''' Retourne Nothing si aucun FX n'a été touché ou si le bridge est indisponible.
        ''' </summary>
        Public Function GetLastTouchedFXParam() As ReaperFXParam
            Dim info = GetLastTouchedFXInfo()
            If info.trackIndex = -1 AndAlso info.fxIndex = -1 Then Return Nothing
            Dim param As New ReaperFXParam(info.trackIndex, info.fxIndex, info.paramIndex)
            param.Refresh()
            Return param
        End Function

        ''' <summary>
        ''' Retourne le <see cref="ReaperFX"/> complet du dernier FX touché.
        ''' Retourne Nothing si aucun FX n'a été touché.
        ''' </summary>
        Public Function GetLastTouchedFX() As ReaperFX
            Dim info = GetLastTouchedFXInfo()
            If info.trackIndex = -1 AndAlso info.fxIndex = -1 Then Return Nothing
            Dim fx As New ReaperFX(info.trackIndex, info.fxIndex)
            fx.Refresh()
            Return fx
        End Function
        ''' <summary>
        ''' Retourne le nombre de pistes du projet actif.
        ''' Retourne -1 si le bridge est indisponible.
        ''' </summary>
        Public Function CountTracks() As Integer
            Dim raw = CallReaperBridge("COUNT_TRACKS")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim count As Integer = -1
            Integer.TryParse(raw, count)
            Return count
        End Function
        ' ── SET_TRACK_INFO ────────────────────────────────────────────────────

        ''' <summary>
        ''' Écrit un paramètre <see cref="Double"/> sur une piste via SET_TRACK_INFO.
        ''' Paramètres courants : "D_VOL", "D_PAN", "D_WIDTH", "D_DUALPANL", "D_DUALPANR",
        ''' "D_PANLAW", "D_PLAY_OFFSET".
        ''' Retourne True si Reaper a accepté la valeur.
        ''' </summary>
        Public Function SetTrackInfoD(trackIndex As Integer,
                                      param As String,
                                      value As Double) As Boolean
            Dim raw = CallReaperBridge(
                $"SET_TRACK_INFO|{trackIndex}|{param}|{value.ToString("G", CultureInfo.InvariantCulture)}")
            Return raw = "OK"
        End Function

        ''' <summary>
        ''' Écrit un paramètre <see cref="Integer"/> sur une piste via SET_TRACK_INFO.
        ''' Paramètres courants : "I_SOLO", "I_FXEN", "I_RECARM", "I_RECINPUT",
        ''' "I_RECMODE", "I_RECMON", "I_VUMODE", "I_AUTOMODE", "I_NCHAN",
        ''' "I_SELECTED", "I_FOLDERDEPTH", "I_FOLDERCOMPACT", "I_CUSTOMCOLOR",
        ''' "I_HEIGHTOVERRIDE", "I_SPACER", "I_PANMODE", "I_PANLAW_FLAGS",
        ''' "I_PLAY_OFFSET_FLAG", "I_FREEMODE", "I_NUMFIXEDLANES".
        ''' Retourne True si Reaper a accepté la valeur.
        ''' </summary>
        Public Function SetTrackInfoI(trackIndex As Integer,
                                      param As String,
                                      value As Integer) As Boolean
            Return SetTrackInfoD(trackIndex, param, CDbl(value))
        End Function

        ''' <summary>
        ''' Écrit un paramètre <see cref="Boolean"/> sur une piste via SET_TRACK_INFO.
        ''' Paramètres courants : "B_MUTE", "B_PHASE", "B_SOLO_DEFEAT",
        ''' "B_AUTO_RECARM", "B_HEIGHTLOCK", "B_SHOWINMIXER", "B_SHOWINTCP",
        ''' "B_TCPPIN", "B_MAINSEND".
        ''' Retourne True si Reaper a accepté la valeur.
        ''' </summary>
        Public Function SetTrackInfoB(trackIndex As Integer,
                                      param As String,
                                      value As Boolean) As Boolean
            Return SetTrackInfoD(trackIndex, param, If(value, 1.0, 0.0))
        End Function

        ''' <summary>
        ''' Lit la valeur brute d'un paramètre de piste via GET_TRACK_INFO.
        ''' Retourne <see cref="Double.MinValue"/> si le bridge est indisponible.
        ''' </summary>
        Public Function GetTrackInfoD(trackIndex As Integer,
                                      param As String) As Double
            Dim raw = CallReaperBridge($"GET_TRACK_INFO|{trackIndex}|{param}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return Double.MinValue
            Dim v As Double = Double.MinValue
            Double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, v)
            Return v
        End Function
        ''' <summary>
        ''' Retourne la liste de toutes les pistes du projet actif sous forme de
        ''' <see cref="ReaperTrack"/> chargées (Refresh appelé sur chacune).
        ''' </summary>
        Public Function GetAllTracks() As IReadOnlyList(Of ReaperTrack)
            Dim count = CountTracks()
            If count <= 0 Then Return Array.Empty(Of ReaperTrack)()
            Dim result(count - 1) As ReaperTrack
            For i = 0 To count - 1
                result(i) = New ReaperTrack(i)
                result(i).Refresh()
            Next
            Return result
        End Function

        ''' <summary>
        ''' Crée un envoi depuis <paramref name="srcTrackIndex"/> vers <paramref name="destTrackIndex"/>.
        ''' <paramref name="destTrackIndex"/> = -2 pour une sortie matérielle.
        ''' Retourne l'index 0-based du nouvel envoi, ou -1 en cas d'échec.
        ''' </summary>
        Public Function CreateTrackSend(srcTrackIndex As Integer,
                                        destTrackIndex As Integer) As Integer
            Dim raw = CallReaperBridge($"CREATE_TRACK_SEND|{srcTrackIndex}|{destTrackIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim idx As Integer = -1
            Integer.TryParse(raw, idx)
            Return idx
        End Function
        Public Function CreateTrackOutput(_trackIndex As Integer, _OutputIndex As Integer)
            Dim raw = utility.CallReaperBridge(
                $"SET_SEND_INFO|{_trackIndex}|{"2"}|{"-1"}|{"P_DESTTRACK"}|{_OutputIndex.ToString("G", CultureInfo.InvariantCulture)}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim idx As Integer = -1
            Integer.TryParse(raw, idx)
            Return idx
        End Function

        ''' <summary>
        ''' Crée un envoi depuis <paramref name="srcTrackIndex"/> vers <paramref name="destTrackIndex"/>
        ''' et retourne un <see cref="ReaperSend"/> chargé pour cet envoi.
        ''' Retourne Nothing en cas d'échec.
        ''' </summary>
        Public Function CreateTrackSendAndGet(srcTrackIndex As Integer,
                                              destTrackIndex As Integer) As ReaperSend
            Dim sendIdx = CreateTrackSend(srcTrackIndex, destTrackIndex)
            If sendIdx < 0 Then Return Nothing
            Dim send As New ReaperSend(srcTrackIndex, CInt(SendCategory.Sends), sendIdx)
            send.Refresh()
            Return send
        End Function

        ''' <summary>
        ''' Supprime un envoi d'une piste.
        ''' Retourne True si la suppression a réussi.
        ''' </summary>
        Public Function RemoveTrackSend(trackIndex As Integer,
                                        category As SendCategory,
                                        sendIndex As Integer) As Boolean
            Dim raw = CallReaperBridge($"REMOVE_TRACK_SEND|{trackIndex}|{CInt(category)}|{sendIndex}")
            Return raw = "OK"
        End Function

        ''' <summary>
        ''' Retourne True si au moins une piste du projet actif est en solo.
        ''' Retourne False si le bridge est indisponible.
        ''' </summary>
        Public Function AnyTrackSolo() As Boolean
            Dim raw = CallReaperBridge("ANY_TRACK_SOLO")
            Return raw = "1"
        End Function
        ' ── VU-mètre / Peaks ──────────────────────────────────────────────────

        ''' <summary>
        ''' Retourne l'amplitude linéaire actuelle d'un canal de piste.
        ''' channel : 0=L  1=R  2+=autres canaux
        ''' Convertir en dB : 20 * Math.Log10(value)
        ''' Retourne -1 si indisponible.
        ''' </summary>
        Public Function Track_GetPeakInfo(trackIndex As Integer, channel As Integer) As Double
            Dim raw = CallReaperBridge($"GET_TRACK_PEAK|{trackIndex}|{channel}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim v As Double = -1
            Double.TryParse(raw, Globalization.NumberStyles.Any,
                            Globalization.CultureInfo.InvariantCulture, v)
            Return v
        End Function
        ''' <summary>
        ''' Retourne les flags mute/solo de la piste master (valeur brute entière).
        ''' Retourne -1 si le bridge est indisponible.
        ''' </summary>
        Public Function GetMasterMuteSoloFlags() As Integer
            Dim raw As String = CallReaperBridge("GET_MASTER_MUTE_SOLO_FLAGS")
            If String.IsNullOrEmpty(raw) Then
                Return -1
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return -1
            End If

            Dim flags As Integer = -1
            Integer.TryParse(raw, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, flags)
            Return flags
        End Function
        ''' <summary>
        ''' Définit la piste la plus à gauche visible dans le mixer Reaper.
        ''' Retourne True si l'opération a réussi.
        ''' </summary>
        Public Function SetMixerScroll(trackIndex As Integer) As Boolean
            Dim raw As String = CallReaperBridge($"SET_MIXER_SCROLL|{trackIndex}")
            If raw = "OK" Then
                Return True
            End If
            Return False
        End Function
        ''' <summary>
        ''' Déplace le curseur d'édition d'un delta en secondes.
        ''' </summary>
        ''' <param name="deltaSeconds">Décalage en secondes (positif ou négatif).</param>
        ''' <param name="doSel">True pour déplacer aussi la sélection temporelle.</param>
        ''' <returns>True si l'opération a réussi.</returns>
        Public Function MoveEditCursor(deltaSeconds As Double,
                               Optional doSel As Boolean = False) As Boolean
            Dim raw As String = CallReaperBridge(
        $"MOVE_EDIT_CURSOR|{deltaSeconds.ToString("G", Globalization.CultureInfo.InvariantCulture)}|{If(doSel, "1", "0")}")

            If raw = "OK" Then
                Return True
            End If

            Return False
        End Function

        ''' <summary>
        ''' Ajoute un marqueur/région dans le projet actif.
        ''' Retourne le numéro créé, ou -1 en cas d'échec.
        ''' </summary>
        Public Function AddProjectMarker(isRegion As Boolean,
                                         pos As Double,
                                         rgnEnd As Double,
                                         name As String,
                                         Optional wantIndex As Integer = -1,
                                         Optional color As Integer = 0) As Integer
            Dim raw As String = CallReaperBridge(
                "ADD_PROJECT_MARKER|" &
                If(isRegion, "1", "0") & "|" &
                pos.ToString("G", CultureInfo.InvariantCulture) & "|" &
                rgnEnd.ToString("G", CultureInfo.InvariantCulture) & "|" &
                wantIndex.ToString(CultureInfo.InvariantCulture) & "|" &
                color.ToString(CultureInfo.InvariantCulture) & "|" &
                name)

            If String.IsNullOrEmpty(raw) Then
                Return -1
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return -1
            End If

            Dim idx As Integer = -1
            Integer.TryParse(raw, Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, idx)
            Return idx
        End Function
        ''' <summary>
        ''' Retourne le nombre total de marqueurs+régions du projet actif,
        ''' ainsi que le détail marqueurs et régions.
        ''' </summary>
        ''' <returns>
        ''' Tuple : (Total, Markers, Regions).
        ''' Retourne (-1, -1, -1) si le bridge/API est indisponible.
        ''' </returns>
        Public Function CountProjectMarkers() As (Total As Integer, Markers As Integer, Regions As Integer)
            Dim raw As String = CallReaperBridge("COUNT_PROJECT_MARKERS")

            If String.IsNullOrEmpty(raw) Then
                Return (-1, -1, -1)
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return (-1, -1, -1)
            End If

            Dim p() As String = raw.Split("|"c)
            If p.Length < 3 Then
                Return (-1, -1, -1)
            End If

            Dim total As Integer = -1
            Dim markers As Integer = -1
            Dim regions As Integer = -1

            If Integer.TryParse(p(0), Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, total) = False Then
                Return (-1, -1, -1)
            End If

            If Integer.TryParse(p(1), Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, markers) = False Then
                Return (-1, -1, -1)
            End If

            If Integer.TryParse(p(2), Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, regions) = False Then
                Return (-1, -1, -1)
            End If

            Return (total, markers, regions)
        End Function
        ''' <summary>
        ''' Énumère un marqueur/région par index d'énumération (0-based).
        ''' </summary>
        ''' <param name="markrgnidx">Index d'énumération (0-based).</param>
        ''' <returns>
        ''' Tuple :
        ''' Found, IsRegion, Position, RegionEnd, MarkerId, Name.
        ''' </returns>
        Public Function EnumProjectMarkers(markrgnidx As Integer) As (retval As Integer,
                                                                       IsRegion As Boolean,
                                                                       Position As Double,
                                                                       RegionEnd As Double,
                                                                       Name As String,
                                                                        markerId As Integer)
            Dim raw As String = CallReaperBridge(
                "ENUM_PROJECT_MARKERS|" &
                markrgnidx.ToString(CultureInfo.InvariantCulture))

            If String.IsNullOrEmpty(raw) Then
                Return (-1, False, -1, -1, "", -1)
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return (-1, False, -1, -1, "", -1)
            End If

            If raw = "0" Then
                Return (-1, False, -1, -1, "", -1)
            End If

            Dim p() As String = raw.Split("|"c)
            If p.Length < 6 Then
                Return (-1, False, -1, -1, "", -1)
            End If

            Dim retval As Integer = -1
            Integer.TryParse(p(0), Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, retval)
            Dim isRegion As Boolean = (p(1) = "1")

            Dim pos As Double = 0
            Double.TryParse(p(2), Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, pos)

            Dim rgnEnd As Double = 0
            Double.TryParse(p(3), Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, rgnEnd)

            Dim markerId As Integer = -1
            Integer.TryParse(p(4), Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, markerId)

            Dim markerName As String = p(5)

            Return (retval, isRegion, pos, rgnEnd, markerName, markerId)
        End Function
        ''' <summary>
        ''' Lit ou définit la plage Loop/Time selection via une API unifiée bridge.
        ''' </summary>
        ''' <param name="isSet">True = écriture, False = lecture.</param>
        ''' <param name="isLoop">True = loop points, False = time selection.</param>
        ''' <param name="allowAutoSeek">Comportement autoseek Reaper.</param>
        ''' <param name="startTime">Début en secondes (utilisé en Set, retourné en Get).</param>
        ''' <param name="endTime">Fin en secondes (utilisé en Set, retourné en Get).</param>
        ''' <param name="undoBeginBlock">Flag undo (utilisé en Set).</param>
        ''' <returns>True si l'opération a réussi.</returns>
        Public Function GetSet_LoopTimeRange(isSet As Boolean,
                                             isLoop As Boolean,
                                             allowAutoSeek As Boolean,
                                             ByRef startTime As Double,
                                             ByRef endTime As Double,
                                             Optional undoBeginBlock As Boolean = False) As Boolean
            Dim isSetValue As String = "0"
            If isSet Then
                isSetValue = "1"
            End If

            Dim isLoopValue As String = "0"
            If isLoop Then
                isLoopValue = "1"
            End If

            Dim allowAutoSeekValue As String = "0"
            If allowAutoSeek Then
                allowAutoSeekValue = "1"
            End If

            Dim undoValue As String = "0"
            If undoBeginBlock Then
                undoValue = "1"
            End If

            Dim raw As String = CallReaperBridge(
                "GET_SET_LOOP_TIME_RANGE|" &
                isSetValue & "|" &
                isLoopValue & "|" &
                allowAutoSeekValue & "|" &
                startTime.ToString("G", CultureInfo.InvariantCulture) & "|" &
                endTime.ToString("G", CultureInfo.InvariantCulture) & "|" &
                undoValue)

            If String.IsNullOrEmpty(raw) Then
                Return False
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return False
            End If

            If isSet Then
                If raw = "OK" Then
                    Return True
                End If
                Return False
            End If

            Dim p() As String = raw.Split("|"c)
            If p.Length < 2 Then
                Return False
            End If

            If Double.TryParse(p(0), Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, startTime) = False Then
                Return False
            End If

            If Double.TryParse(p(1), Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, endTime) = False Then
                Return False
            End If

            Return True
        End Function
        ''' <summary>
        ''' Modifie un marqueur/région existant.
        ''' </summary>
        Public Function SetProjectMarker(markerId As Integer,
                                         isRegion As Boolean,
                                         pos As Double,
                                         rgnEnd As Double,
                                         name As String,
                                         Optional color As Integer = 0) As Boolean
            Dim raw As String = CallReaperBridge(
                "SET_PROJECT_MARKER|" &
                markerId.ToString(CultureInfo.InvariantCulture) & "|" &
                If(isRegion, "1", "0") & "|" &
                pos.ToString("G", CultureInfo.InvariantCulture) & "|" &
                rgnEnd.ToString("G", CultureInfo.InvariantCulture) & "|" &
                color.ToString(CultureInfo.InvariantCulture) & "|" &
                name)

            If raw = "OK" Then
                Return True
            End If

            Return False
        End Function

        ''' <summary>
        ''' Retourne le dernier marqueur passé et la région courante à un instant donné.
        ''' </summary>
        Public Function GetLastMarkerAndCurRegion(time As Double) As (MarkerIndex As Integer, RegionIndex As Integer)
            Dim raw As String = CallReaperBridge(
                "GET_LAST_MARKER_AND_CUR_REGION|" &
                time.ToString("G", CultureInfo.InvariantCulture))

            If String.IsNullOrEmpty(raw) Then
                Return (-1, -1)
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return (-1, -1)
            End If

            Dim p() As String = raw.Split("|"c)
            If p.Length < 2 Then
                Return (-1, -1)
            End If

            Dim markerIdx As Integer = -1
            Dim regionIdx As Integer = -1
            Integer.TryParse(p(0), Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, markerIdx)
            Integer.TryParse(p(1), Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, regionIdx)

            Return (markerIdx, regionIdx)
        End Function
        ''' <summary>
        ''' Supprime un marqueur ou une région par son numéro Reaper.
        ''' </summary>
        ''' <param name="markrgnindexnumber">Numéro du marqueur/région (ID Reaper).</param>
        ''' <param name="isRegion">True pour une région, False pour un marqueur.</param>
        ''' <returns>True si la suppression a réussi, sinon False.</returns>
        Public Function DeleteProjectMarker(markrgnindexnumber As Integer,
                                            isRegion As Boolean) As Boolean
            Dim isrgnValue As String = "0"

            If isRegion Then
                isrgnValue = "1"
            Else
                isrgnValue = "0"
            End If

            Dim raw As String = CallReaperBridge(
                "DELETE_PROJECT_MARKER|" &
                markrgnindexnumber.ToString(Globalization.CultureInfo.InvariantCulture) &
                "|" &
                isrgnValue)

            If raw = "OK" Then
                Return True
            End If

            Return False
        End Function
        ''' <summary>
        ''' Supprime un marqueur/région par index d'énumération.
        ''' </summary>
        Public Function DeleteProjectMarkerByIndex(markrgnidx As Integer) As Boolean
            Dim raw As String = CallReaperBridge(
                "DELETE_PROJECT_MARKER_BY_INDEX|" &
                markrgnidx.ToString(CultureInfo.InvariantCulture))

            If raw = "OK" Then
                Return True
            End If

            Return False
        End Function

        Public Function GetSelection() As Tuple(Of Double, Double)
            Dim StartTime As Double = -1
            Dim EndTime As Double = -1

            Reaper.utility.GetSet_LoopTimeRange(False, False, False, StartTime, EndTime)
            Return New Tuple(Of Double, Double)(If(StartTime = 0, -1, StartTime), If(EndTime = 0, -1, EndTime))

        End Function
        Public Function GetLoopSelection() As Tuple(Of Double, Double)
            Dim StartTime As Double = -1
            Dim EndTime As Double = -1

            Reaper.utility.GetSet_LoopTimeRange(False, True, False, StartTime, EndTime)
            Return New Tuple(Of Double, Double)(If(StartTime = 0, -1, StartTime), If(EndTime = 0, -1, EndTime))

        End Function

        ''' <summary>
        ''' Retourne la vue d'arrangement courante (start/end en secondes).
        ''' </summary>
        Public Function GetArrangeView2(Optional screenXStart As Integer = 0,
                                        Optional screenXEnd As Integer = 0) As (StartTime As Double, EndTime As Double)
            Dim raw As String = CallReaperBridge(
                $"GET_ARRANGE_VIEW2|{screenXStart.ToString(Globalization.CultureInfo.InvariantCulture)}|{screenXEnd.ToString(Globalization.CultureInfo.InvariantCulture)}")

            If String.IsNullOrEmpty(raw) Then
                Return (-1, -1)
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return (-1, -1)
            End If

            Dim p = raw.Split("|"c)
            If p.Length < 2 Then
                Return (-1, -1)
            End If

            Dim startTime As Double = -1
            Dim endTime As Double = -1
            Double.TryParse(p(0), Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, startTime)
            Double.TryParse(p(1), Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, endTime)

            Return (startTime, endTime)
        End Function

        ''' <summary>
        ''' Définit la vue d'arrangement (start/end en secondes).
        ''' </summary>
        Public Function SetArrangeView2(startTime As Double,
                                        endTime As Double,
                                        Optional screenXStart As Integer = 0,
                                        Optional screenXEnd As Integer = 0) As Boolean
            Dim raw As String = CallReaperBridge(
                $"SET_ARRANGE_VIEW2|{startTime.ToString("G", Globalization.CultureInfo.InvariantCulture)}|{endTime.ToString("G", Globalization.CultureInfo.InvariantCulture)}|{screenXStart.ToString(Globalization.CultureInfo.InvariantCulture)}|{screenXEnd.ToString(Globalization.CultureInfo.InvariantCulture)}")

            If raw = "OK" Then
                Return True
            End If

            Return False
        End Function
        ''' <summary>
        ''' Retourne la prochaine division de grille (SWS/BR) après la position donnée.
        ''' Retourne -1 si le bridge est indisponible.
        ''' </summary>
        Public Function BR_GetNextGridDivision(position As Double) As Double
            Dim raw As String = CallReaperBridge(
                $"BR_GET_NEXT_GRID_DIVISION|{position.ToString("G", Globalization.CultureInfo.InvariantCulture)}")

            If String.IsNullOrEmpty(raw) Then
                Return -1
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return -1
            End If

            Dim value As Double = -1
            Double.TryParse(raw, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, value)
            Return value
        End Function

        ''' <summary>
        ''' Retourne la division de grille (SWS/BR) précédente avant la position donnée.
        ''' Retourne -1 si le bridge est indisponible.
        ''' </summary>
        Public Function BR_GetPrevGridDivision(position As Double) As Double
            Dim raw As String = CallReaperBridge(
                $"BR_GET_PREV_GRID_DIVISION|{position.ToString("G", Globalization.CultureInfo.InvariantCulture)}")

            If String.IsNullOrEmpty(raw) Then
                Return -1
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return -1
            End If

            Dim value As Double = -1
            Double.TryParse(raw, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, value)
            Return value
        End Function

        ''' <summary>
        ''' Retourne la division de grille (SWS/BR) la plus proche de la position donnée.
        ''' Retourne -1 si le bridge est indisponible.
        ''' </summary>
        Public Function BR_GetClosestGridDivision(position As Double) As Double
            Dim raw As String = CallReaperBridge(
                $"BR_GET_CLOSEST_GRID_DIVISION|{position.ToString("G", Globalization.CultureInfo.InvariantCulture)}")

            If String.IsNullOrEmpty(raw) Then
                Return -1
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return -1
            End If

            Dim value As Double = -1
            Double.TryParse(raw, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, value)
            Return value
        End Function
        ''' <summary>
        ''' Aligne une position temporelle sur la grille du projet actif.
        ''' Retourne -1 si le bridge est indisponible.
        ''' </summary>
        Public Function SnapToGrid(time As Double) As Double
            Dim raw As String = CallReaperBridge(
        $"SNAP_TO_GRID|{time.ToString("G", Globalization.CultureInfo.InvariantCulture)}")

            If String.IsNullOrEmpty(raw) Then
                Return -1
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return -1
            End If

            Dim snapped As Double = -1
            Double.TryParse(raw, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, snapped)
            Return snapped
        End Function
        ''' <summary>
        ''' Retourne l'état toggle d'une action Reaper.
        ''' 
        ''' </summary>
        Public Function GetToggleCommandState(commandId As Integer) As ToogleStates
            Dim raw As String = CallReaperBridge($"GET_TOGGLE_COMMAND_STATE|{commandId}")

            If String.IsNullOrEmpty(raw) Then
                Return -1
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return -1
            End If

            Dim state As Integer = -1
            Integer.TryParse(raw, state)
            Select Case state
                Case 0
                    Return ToogleStates.Off
                Case 1
                    Return ToogleStates.On
                Case 2
                    Return ToogleStates.On_Locked
                Case 16
                    Return ToogleStates.On_Bullet
                Case -1
                    Return ToogleStates.None
            End Select
            Return ToogleStates.None

        End Function
        Public Enum ToogleStates
            Off
            [On]
            On_Locked
            On_Bullet
            None
        End Enum
        ''' <summary>
        ''' Retourne l'état toggle d'une action Reaper.
        ''' 
        ''' </summary>
        Public Function GetToggleCommandStateBool(commandId As Integer) As Boolean
            Dim raw As String = CallReaperBridge($"GET_TOGGLE_COMMAND_STATE|{commandId}")

            If String.IsNullOrEmpty(raw) Then
                Return -1
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return -1
            End If

            Dim state As Integer = -1
            Integer.TryParse(raw, state)
            Select Case state
                Case 0, -1
                    Return False
                Case Else
                    Return True

            End Select

        End Function
        ''' <summary>
        ''' Appelle Plugin_Register via le bridge Reaper.
        ''' Retourne le code de retour Reaper, ou -1 si erreur bridge/API.
        ''' </summary>
        ''' <summary>
        ''' Exécute une action Reaper via son identifiant numérique.
        ''' Utilise la commande bridge MAIN_ON_COMMAND.
        ''' </summary>
        ''' <param name="commandId">Identifiant de l'action Reaper.</param>
        ''' <param name="flag">Flag optionnel (0 par défaut).</param>
        ''' <returns>True si l'action a été envoyée avec succès, sinon False.</returns>
        Public Function Main_OnCommand(commandId As Integer,
                                       Optional flag As Integer = 0) As Boolean
            Dim raw As String = CallReaperBridge(
                $"MAIN_ON_COMMAND_SAFE|{commandId.ToString(CultureInfo.InvariantCulture)}|{flag.ToString(CultureInfo.InvariantCulture)}")

            If String.IsNullOrEmpty(raw) Then
                Return False
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return False
            End If

            If raw = "OK" Then
                Return True
            End If

            Return False
        End Function
        Public Function Main_OnCommand_SafeUI(commandId As Integer,
                                       Optional flag As Integer = 0) As Boolean
            Dim raw As String = CallReaperBridge(
                $"MAIN_ON_COMMAND_SAFE|{commandId.ToString(CultureInfo.InvariantCulture)}|{flag.ToString(CultureInfo.InvariantCulture)}")

            If String.IsNullOrEmpty(raw) Then
                Return False
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return False
            End If

            If raw = "OK" Then
                Return True
            End If

            Return False
        End Function
        Public Function Plugin_Register(name As String, infoPtr As IntPtr) As Integer
            Dim raw As String = CallReaperBridge(
        $"PLUGIN_REGISTER|{name}|{infoPtr.ToInt64().ToString(Globalization.CultureInfo.InvariantCulture)}")

            If String.IsNullOrEmpty(raw) Then
                Return -1
            End If

            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return -1
            End If

            Dim rc As Integer = -1
            Integer.TryParse(raw, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, rc)
            Return rc
        End Function

        ''' <summary>
        ''' Retourne les amplitudes linéaires L et R d'une piste en un seul appel pipe.
        ''' Evite deux appels séparés lors du rafraîchissement des VU-mètres.
        ''' </summary>
        Public Function Track_GetPeakStereo(trackIndex As Integer) As (L As Double, R As Double)
            Dim raw = CallReaperBridge($"GET_TRACK_PEAK_STEREO|{trackIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return (-1, -1)
            Dim p = raw.Split("|"c)
            If p.Length < 2 Then Return (-1, -1)
            Dim ic = Globalization.CultureInfo.InvariantCulture
            Dim l As Double = -1, r As Double = -1
            Double.TryParse(p(0), Globalization.NumberStyles.Any, ic, l)
            Double.TryParse(p(1), Globalization.NumberStyles.Any, ic, r)
            Return (l, r)
        End Function

        ''' <summary>
        ''' Retourne la valeur peak-hold en dB d'un canal de piste.
        ''' channel : 0=L  1=R
        ''' Retourne Double.MinValue si indisponible.
        ''' </summary>
        Public Function Track_GetPeakHoldDB(trackIndex As Integer, channel As Integer) As Double
            Dim raw = CallReaperBridge($"GET_TRACK_PEAK_HOLD|{trackIndex}|{channel}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return Double.MinValue
            Dim v As Double = Double.MinValue
            Double.TryParse(raw, Globalization.NumberStyles.Any,
                            Globalization.CultureInfo.InvariantCulture, v)
            Return v
        End Function

        ''' <summary>
        ''' Lit et réinitialise simultanément le peak-hold d'un canal de piste.
        ''' Retourne la valeur peak-hold en dB avant réinitialisation.
        ''' </summary>
        Public Function Track_ResetPeakHoldDB(trackIndex As Integer, channel As Integer) As Double
            Dim raw = CallReaperBridge($"RESET_TRACK_PEAK_HOLD|{trackIndex}|{channel}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return Double.MinValue
            Dim v As Double = Double.MinValue
            Double.TryParse(raw, Globalization.NumberStyles.Any,
                            Globalization.CultureInfo.InvariantCulture, v)
            Return v
        End Function



        ''' <summary>
        ''' Retourne la liste de tous les FX installés dans Reaper.
        ''' Un seul appel pipe, quel que soit le nombre de plugins.
        ''' Retourne une liste vide si le bridge est indisponible.
        ''' </summary>
        Public Function GetInstalledFXList() As IReadOnlyList(Of InstalledFXInfo)
            Dim raw = CallReaperBridge("GET_INSTALLED_FX_LIST")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return Array.Empty(Of InstalledFXInfo)()
            End If
            Dim p = raw.Split("|"c)
            Dim count = p.Length \ 2
            Dim result(count - 1) As InstalledFXInfo
            For i = 0 To count - 1
                result(i) = New InstalledFXInfo(p(i * 2), p(i * 2 + 1))
            Next
            Return result
        End Function

        ''' <summary>
        ''' Retourne uniquement les noms des FX d'un type donné (VST, VST3, JS, CLAP…).
        ''' Filtre effectué côté bridge — réponse plus légère.
        ''' </summary>
        Public Function GetInstalledFXListByType(fxType As String) As IReadOnlyList(Of String)
            Dim raw = CallReaperBridge($"GET_INSTALLED_FX_LIST_BYTYPE|{fxType}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return Array.Empty(Of String)()
            End If
            Return raw.Split("|"c)
        End Function



        ' ── Groupes de piste ──────────────────────────────────────────────────

        ''' <summary>Convertit <see cref="TrackGroupType"/> en nom API Reaper (sans réflexion).</summary>
        Public Function TrackGroupTypeName(groupType As TrackGroupType) As String
            Select Case groupType
                Case TrackGroupType.VolumeLead : Return "VOLUME_LEAD"
                Case TrackGroupType.VolumeFollow : Return "VOLUME_FOLLOW"
                Case TrackGroupType.VolumeVcaLead : Return "VOLUME_VCA_LEAD"
                Case TrackGroupType.VolumeVcaFollow : Return "VOLUME_VCA_FOLLOW"
                Case TrackGroupType.PanLead : Return "PAN_LEAD"
                Case TrackGroupType.PanFollow : Return "PAN_FOLLOW"
                Case TrackGroupType.PanVcaLead : Return "PAN_VCA_LEAD"
                Case TrackGroupType.PanVcaFollow : Return "PAN_VCA_FOLLOW"
                Case TrackGroupType.WidthLead : Return "WIDTH_LEAD"
                Case TrackGroupType.WidthFollow : Return "WIDTH_FOLLOW"
                Case TrackGroupType.MuteLead : Return "MUTE_LEAD"
                Case TrackGroupType.MuteFollow : Return "MUTE_FOLLOW"
                Case TrackGroupType.SoloLead : Return "SOLO_LEAD"
                Case TrackGroupType.SoloFollow : Return "SOLO_FOLLOW"
                Case TrackGroupType.RecArmLead : Return "RECARM_LEAD"
                Case TrackGroupType.RecArmFollow : Return "RECARM_FOLLOW"
                Case TrackGroupType.PolarityLead : Return "POLARITY_LEAD"
                Case TrackGroupType.PolarityFollow : Return "POLARITY_FOLLOW"
                Case TrackGroupType.AutoModeLead : Return "AUTOMODE_LEAD"
                Case TrackGroupType.AutoModeFollow : Return "AUTOMODE_FOLLOW"
                Case TrackGroupType.ReverseLead : Return "REVERSE_LEAD"
                Case TrackGroupType.ReverseFollow : Return "REVERSE_FOLLOW"
                Case TrackGroupType.SendVolLead : Return "SEND_VOL_LEAD"
                Case TrackGroupType.SendVolFollow : Return "SEND_VOL_FOLLOW"
                Case TrackGroupType.SendPanLead : Return "SEND_PAN_LEAD"
                Case TrackGroupType.SendPanFollow : Return "SEND_PAN_FOLLOW"
                Case TrackGroupType.SendMuteLead : Return "SEND_MUTE_LEAD"
                Case Else : Return "SEND_MUTE_FOLLOW"
            End Select
        End Function

        ''' <summary>
        ''' Retourne le masque d'appartenance aux groupes pour un type donné.
        ''' bits 0-31 correspondent aux groupes 1-32.
        ''' Retourne 0 si le bridge est indisponible.
        ''' </summary>
        Public Function GetTrackGroupMask(trackIndex As Integer,
                                          groupType As TrackGroupType) As UInteger
            Dim raw = CallReaperBridge(
                $"GET_TRACK_GROUP|{trackIndex}|{TrackGroupTypeName(groupType)}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return 0UI
            Dim v As Long = 0
            Long.TryParse(raw, v)
            Return CType(v, UInteger)
        End Function

        ''' <summary>
        ''' Modifie l'appartenance à un groupe de piste et retourne le nouveau masque.
        ''' setMask : bits à modifier  setVal : valeur des bits (1=rejoindre 0=quitter)
        ''' </summary>
        Public Function SetTrackGroupMask(trackIndex As Integer,
                                          groupType As TrackGroupType,
                                          setMask As UInteger,
                                          setVal As UInteger) As UInteger
            Dim raw = CallReaperBridge(
                $"SET_TRACK_GROUP|{trackIndex}|{TrackGroupTypeName(groupType)}|{setMask}|{setVal}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return 0UI
            Dim v As Long = 0
            Long.TryParse(raw, v)
            Return CType(v, UInteger)
        End Function

        ''' <summary>
        ''' Vérifie si une piste appartient à un groupe donné (numéro 1-32).
        ''' </summary>
        Public Function IsTrackInGroup(trackIndex As Integer,
                                       groupType As TrackGroupType,
                                       groupNumber As Integer) As Boolean
            If groupNumber < 1 OrElse groupNumber > 32 Then Return False
            Dim mask = GetTrackGroupMask(trackIndex, groupType)
            Return (mask And (1UI << (groupNumber - 1))) <> 0UI
        End Function

        ''' <summary>
        ''' Ajoute ou retire une piste d'un groupe (numéro 1-32).
        ''' Retourne True si l'opération a réussi.
        ''' </summary>
        Public Function SetTrackGroupMembership(trackIndex As Integer,
                                                groupType As TrackGroupType,
                                                groupNumber As Integer,
                                                isMember As Boolean) As Boolean
            If groupNumber < 1 OrElse groupNumber > 32 Then Return False
            Dim bit = 1UI << (groupNumber - 1)
            Dim newMask = SetTrackGroupMask(trackIndex, groupType, bit,
                                            If(isMember, bit, 0UI))
            Return (newMask And bit) = If(isMember, bit, 0UI)
        End Function



        ' ── Canaux audio ──────────────────────────────────────────────────────

        ''' <summary>
        ''' Retourne le nombre d'entrées audio de l'interface active.
        ''' Retourne -1 si le bridge est indisponible.
        ''' </summary>
        Public Function GetNumAudioInputs() As Integer
            Dim raw = CallReaperBridge("GET_NUM_AUDIO_INPUTS")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim v As Integer = -1
            Integer.TryParse(raw, v)
            Return v
        End Function

        ''' <summary>
        ''' Retourne le nombre de sorties audio de l'interface active.
        ''' Retourne -1 si le bridge est indisponible.
        ''' </summary>
        Public Function GetNumAudioOutputs() As Integer
            Dim raw = CallReaperBridge("GET_NUM_AUDIO_OUTPUTS")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim v As Integer = -1
            Integer.TryParse(raw, v)
            Return v
        End Function

        ''' <summary>
        ''' Retourne le nom d'une entrée audio par index (0-based).
        ''' Retourne une chaîne vide si l'index est hors limite.
        ''' </summary>
        Public Function GetAudioInputName(channelIndex As Integer) As String
            Dim raw = CallReaperBridge($"GET_AUDIO_INPUT_NAME|{channelIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return String.Empty
            Return raw
        End Function

        ''' <summary>
        ''' Retourne le nom d'une sortie audio par index (0-based).
        ''' Retourne une chaîne vide si l'index est hors limite.
        ''' </summary>
        Public Function GetAudioOutputName(channelIndex As Integer) As String
            Dim raw = CallReaperBridge($"GET_AUDIO_OUTPUT_NAME|{channelIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return String.Empty
            Return raw
        End Function

        ''' <summary>
        ''' Retourne la liste de toutes les entrées audio en un seul appel pipe.
        ''' </summary>
        Public Function GetAllAudioInputs() As IReadOnlyList(Of AudioChannelInfo)
            Dim raw = CallReaperBridge("GET_ALL_AUDIO_INPUTS")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return Array.Empty(Of AudioChannelInfo)()
            End If
            Dim names = raw.Split("|"c)
            Dim result(names.Length - 1) As AudioChannelInfo
            For i = 0 To names.Length - 1
                result(i) = New AudioChannelInfo(i, names(i))
            Next
            Return result
        End Function

        ''' <summary>
        ''' Retourne la liste de toutes les sorties audio en un seul appel pipe.
        ''' </summary>
        Public Function GetAllAudioOutputs() As IReadOnlyList(Of AudioChannelInfo)
            Dim raw = CallReaperBridge("GET_ALL_AUDIO_OUTPUTS")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return Array.Empty(Of AudioChannelInfo)()
            End If
            Dim names = raw.Split("|"c)
            Dim result(names.Length - 1) As AudioChannelInfo
            For i = 0 To names.Length - 1
                result(i) = New AudioChannelInfo(i, names(i))
            Next
            Return result
        End Function

        ' Dans le module utility — section position / transport ───────────────────

        ' ── Curseur d'édition ─────────────────────────────────────────────────

        ''' <summary>
        ''' Retourne la position du curseur d'édition en secondes.
        ''' Retourne -1 si le bridge est indisponible.
        ''' </summary>
        Public Function GetCursorPosition() As Double
            Dim raw = CallReaperBridge("GET_CURSOR_POSITION")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim v As Double = -1
            Double.TryParse(raw, Globalization.NumberStyles.Any,
                            Globalization.CultureInfo.InvariantCulture, v)
            Return v
        End Function

        ''' <summary>
        ''' Déplace le curseur d'édition.
        ''' moveView : déplace la vue si True.
        ''' seekPlay : cherche la position en lecture si True.
        ''' </summary>
        Public Sub SetCursorPosition(time As Double,
                                     Optional moveView As Boolean = False,
                                     Optional seekPlay As Boolean = False)
            Dim ic = Globalization.CultureInfo.InvariantCulture
            CallReaperBridge(
                $"SET_CURSOR_POSITION|{time.ToString("G", ic)}|{If(moveView, "1", "0")}|{If(seekPlay, "1", "0")}")
        End Sub

        ''' <summary>
        ''' Formate une position temporelle selon le mode demandé.
        ''' </summary>
        Public Function FormatTimePosition(time As Double,
                                           mode As TimePositionMode) As String
            Dim raw = CallReaperBridge(
                $"FORMAT_TIMEPOS|{time.ToString("G", Globalization.CultureInfo.InvariantCulture)}|{CInt(mode)}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return String.Empty
            Return raw
        End Function

        ''' <summary>
        ''' Convertit des secondes en Quarter Notes.
        ''' Retourne -1 si le bridge est indisponible.
        ''' </summary>
        Public Function TimePositionToQN(time As Double) As Double
            Dim raw = CallReaperBridge(
                $"TIMEPOS_TO_QN|{time.ToString("G", Globalization.CultureInfo.InvariantCulture)}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim v As Double = -1
            Double.TryParse(raw, Globalization.NumberStyles.Any,
                            Globalization.CultureInfo.InvariantCulture, v)
            Return v
        End Function

        ''' <summary>
        ''' Convertit des Quarter Notes en secondes.
        ''' Retourne -1 si le bridge est indisponible.
        ''' </summary>
        Public Function QNToTimePosition(qn As Double) As Double
            Dim raw = CallReaperBridge(
                $"QN_TO_TIMEPOS|{qn.ToString("G", Globalization.CultureInfo.InvariantCulture)}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim v As Double = -1
            Double.TryParse(raw, Globalization.NumberStyles.Any,
                            Globalization.CultureInfo.InvariantCulture, v)
            Return v
        End Function

        ''' <summary>
        ''' Retourne signature et tempo en vigueur à une position donnée.
        ''' Retourne (0, 0, 0) si le bridge est indisponible.
        ''' </summary>
        Public Function GetTimeSigAtTime(time As Double) As (Num As Integer, Den As Integer, BPM As Double)
            Dim raw = CallReaperBridge(
                $"GET_TIMESIG_AT_TIME|{time.ToString("G", Globalization.CultureInfo.InvariantCulture)}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return (0, 0, 0)
            Dim p = raw.Split("|"c)
            If p.Length < 3 Then Return (0, 0, 0)
            Dim ic = Globalization.CultureInfo.InvariantCulture
            Dim n As Integer = 0, d As Integer = 0
            Dim bpm As Double = 0
            Integer.TryParse(p(0), n)
            Integer.TryParse(p(1), d)
            Double.TryParse(p(2), Globalization.NumberStyles.Any, ic, bpm)
            Return (n, d, bpm)
        End Function

        ''' <summary>
        ''' Retourne l'état complet du curseur d'édition en un seul appel pipe.
        ''' Préférer cette méthode pour rafraîchir l'affichage de la surface.
        ''' </summary>
        Public Function GetCursorInfo() As CursorInfo
            Dim raw = CallReaperBridge("GET_CURSOR_INFO")
            Dim ic = Globalization.CultureInfo.InvariantCulture
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return New CursorInfo(0, 0, 0, 0, 0, 4, 0, 4, 4, 120, String.Empty)
            End If
            Dim p = raw.Split("|"c)
            If p.Length < 11 Then Return New CursorInfo(0, 0, 0, 0, 0, 4, 0, 4, 4, 120, String.Empty)
            Dim t As Double = 0, qn As Double = 0
            Dim fb As Double = 0, bp As Double = 0, bpm As Double = 0
            Dim m As Integer = 0, cml As Integer = 0, cd As Integer = 0
            Dim tsn As Integer = 0, tsd As Integer = 0
            Double.TryParse(p(0), Globalization.NumberStyles.Any, ic, t)
            Double.TryParse(p(1), Globalization.NumberStyles.Any, ic, qn)
            Integer.TryParse(p(2), m)
            Integer.TryParse(p(3), cml)
            Double.TryParse(p(4), Globalization.NumberStyles.Any, ic, fb)
            Integer.TryParse(p(5), cd)
            Double.TryParse(p(6), Globalization.NumberStyles.Any, ic, bp)
            Integer.TryParse(p(7), tsn)
            Integer.TryParse(p(8), tsd)
            Double.TryParse(p(9), Globalization.NumberStyles.Any, ic, bpm)
            Return New CursorInfo(t, qn, m, cml, fb, cd, bp, tsn, tsd, bpm, p(10))
        End Function

        ''' <summary>
        ''' Ajoute un FX sur une piste par nom et retourne son index 0-based.
        ''' Utiliser <see cref="InstalledFXInfo.FullName"/> comme valeur de fxName.
        ''' Retourne -1 en cas d'échec.
        ''' </summary>
        Public Function AddFXByName(trackIndex As Integer,
                                    fxName As String,
                                    Optional recFX As Boolean = False) As Integer
            Dim raw = CallReaperBridge(
                $"ADD_FX_BY_NAME|{trackIndex}|{fxName}|{If(recFX, "1", "0")}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim idx As Integer = -1
            Integer.TryParse(raw, idx)
            Return idx
        End Function

        ' Dans le module utility — section Undo ────────────────────────────────────

        ' ── Undo / Redo ───────────────────────────────────────────────────────

        ''' <summary>
        ''' Exécute un Undo (Ctrl+Z).
        ''' Retourne True si l'opération a réussi.
        ''' </summary>
        Public Function Undo() As Boolean
            Dim raw = CallReaperBridge("UNDO")
            Return raw = "OK"
        End Function

        ''' <summary>
        ''' Exécute un Redo (Ctrl+Y).
        ''' Retourne True si l'opération a réussi.
        ''' </summary>
        Public Function Redo() As Boolean
            Dim raw = CallReaperBridge("REDO")
            Return raw = "OK"
        End Function

        ''' <summary>
        ''' Retourne la description de l'action annulable.
        ''' Retourne String.Empty si la pile d'undo est vide.
        ''' </summary>
        Public Function CanUndo() As String
            Dim raw = CallReaperBridge("CAN_UNDO")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return String.Empty
            Return raw
        End Function

        ''' <summary>
        ''' Retourne la description de l'action refaisable.
        ''' Retourne String.Empty si la pile de redo est vide.
        ''' </summary>
        Public Function CanRedo() As String
            Dim raw = CallReaperBridge("CAN_REDO")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return String.Empty
            Return raw
        End Function

        ''' <summary>
        ''' Ouvre un bloc d'annulation.
        ''' Appeler avant toute modification, fermer avec <see cref="EndUndoBlock"/>.
        ''' </summary>
        Public Sub BeginUndoBlock()
            CallReaperBridge("BEGIN_UNDO_BLOCK")
        End Sub

        ''' <summary>
        ''' Ferme le bloc d'annulation ouvert par <see cref="BeginUndoBlock"/>.
        ''' </summary>
        Public Sub EndUndoBlock(description As String,
                                Optional flags As UndoFlags = UndoFlags.All)
            CallReaperBridge($"END_UNDO_BLOCK|{description}|{CInt(flags)}")
        End Sub

        ' Dans le module utility — section gestion de projet ─────────────────────

        ' ── Projet ────────────────────────────────────────────────────────────

        ''' <summary>
        ''' Retourne toutes les informations du projet actif en un seul appel pipe.
        ''' </summary>
        Public Function GetProjectInfo() As ReaperProjectInfo
            Dim raw = CallReaperBridge("GET_PROJECT_INFO")
            Dim ic = Globalization.CultureInfo.InvariantCulture
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return New ReaperProjectInfo(String.Empty, String.Empty, False, 0, 0, 0)
            End If
            Dim p = raw.Split("|"c)
            If p.Length < 6 Then Return New ReaperProjectInfo(String.Empty, String.Empty, False, 0, 0, 0)
            Dim sc As Integer = 0, tc As Integer = 0
            Dim len As Double = 0
            Integer.TryParse(p(3), sc)
            Integer.TryParse(p(4), tc)
            Double.TryParse(p(5), Globalization.NumberStyles.Any, ic, len)
            Return New ReaperProjectInfo(p(0), p(1), p(2) = "1", sc, tc, len)
        End Function

        ''' <summary>
        ''' Retourne le compteur de changements d'état du projet.
        ''' Comparer avec la valeur précédente pour détecter toute modification.
        ''' Retourne -1 si le bridge est indisponible.
        ''' </summary>
        Public Function GetProjectStateChangeCount() As Integer
            Dim raw = CallReaperBridge("GET_PROJECT_STATE_COUNT")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim v As Integer = -1
            Integer.TryParse(raw, v)
            Return v
        End Function

        ''' <summary>
        ''' Retourne True si le projet a des modifications non sauvegardées.
        ''' </summary>
        Public Function IsProjectDirty() As Boolean
            Return CallReaperBridge("IS_PROJECT_DIRTY") = "1"
        End Function

        ''' <summary>
        ''' Sauvegarde le projet actif.
        ''' Retourne True si l'opération a réussi.
        ''' </summary>
        Public Function SaveProject() As Boolean
            Return CallReaperBridge("SAVE_PROJECT|0") = "OK"
        End Function

        ''' <summary>
        ''' Ouvre la boîte de dialogue "Enregistrer sous" de Reaper.
        ''' Retourne True si l'opération a réussi.
        ''' </summary>
        Public Function SaveProjectAs() As Boolean
            Return CallReaperBridge("SAVE_PROJECT|1") = "OK"
        End Function

        ''' <summary>
        ''' Retourne le chemin complet du fichier .rpp du projet actif.
        ''' Retourne String.Empty si le projet n'a jamais été sauvegardé.
        ''' </summary>
        Public Function GetProjectFilePath() As String
            Dim raw = CallReaperBridge("GET_PROJECT_FILE_PATH")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return String.Empty
            Return raw
        End Function

        ''' <summary>
        ''' Retourne le nom du projet sans extension ex: "MySession".
        ''' </summary>
        Public Function GetProjectName() As String
            Dim path = GetProjectFilePath()
            If String.IsNullOrEmpty(path) Then Return String.Empty
            Return IO.Path.GetFileNameWithoutExtension(path)
        End Function

        ''' <summary>
        ''' Retourne la durée totale du projet en secondes.
        ''' Retourne -1 si le bridge est indisponible.
        ''' </summary>
        Public Function GetProjectLength() As Double
            Dim raw = CallReaperBridge("GET_PROJECT_LENGTH")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim v As Double = -1
            Double.TryParse(raw, Globalization.NumberStyles.Any,
                            Globalization.CultureInfo.InvariantCulture, v)
            Return v
        End Function

        ''' <summary>
        ''' Retourne les chemins de tous les projets ouverts dans Reaper.
        ''' </summary>
        Public Function EnumAllProjects() As IReadOnlyList(Of String)
            Dim raw = CallReaperBridge("ENUM_PROJECTS")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return Array.Empty(Of String)()
            End If
            Return raw.Split("|"c)
        End Function

        ''' <summary>
        ''' Active le projet à l'index donné (0-based parmi les projets ouverts).
        ''' Retourne True si l'opération a réussi.
        ''' </summary>
        Public Function SetActiveProject(projectIndex As Integer) As Boolean
            Return CallReaperBridge($"SET_ACTIVE_PROJECT|{projectIndex}") = "OK"
        End Function

        ''' <summary>
        ''' Évalue l'enveloppe <paramref name="envIndex"/> à la position <paramref name="time"/> (secondes).
        ''' Retourne la valeur interpolée et la dérivée première (vitesse de changement).
        ''' Retourne (Double.MinValue, 0) si le bridge est indisponible.
        ''' </summary>
        Public Function EvalEnvelope(trackIndex As Integer,
                                     envIndex As Integer,
                                     time As Double) As (Value As Double, DvdS As Double)
            Dim raw = CallReaperBridge(
                $"EVAL_ENVELOPE|{trackIndex}|{envIndex}|{time.ToString("G", CultureInfo.InvariantCulture)}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return (Double.MinValue, 0)
            End If
            Dim p = raw.Split("|"c)
            If p.Length < 2 Then Return (Double.MinValue, 0)
            Dim ic = CultureInfo.InvariantCulture
            Dim v As Double = Double.MinValue, dv As Double = 0
            Double.TryParse(p(0), NumberStyles.Any, ic, v)
            Double.TryParse(p(1), NumberStyles.Any, ic, dv)
            Return (v, dv)
        End Function
        ' ── FX paramètres — écriture ──────────────────────────────────────────

        ''' <summary>
        ''' Écrit un paramètre FX en valeur brute (plage native du plugin).
        ''' Utiliser <see cref="SetFxParamNormalized"/> depuis une surface de contrôle
        ''' car les valeurs brutes varient selon chaque plugin.
        ''' Retourne True si Reaper a accepté la valeur.
        ''' </summary>
        Public Function SetFxParamValue(trackIndex As Integer,
                                        fxIndex As Integer,
                                        paramIndex As Integer,
                                        value As Double) As Boolean
            Dim raw = CallReaperBridge(
                $"SET_FX_PARAM|{trackIndex}|{fxIndex}|{paramIndex}|{value.ToString("G", CultureInfo.InvariantCulture)}")
            Return raw = "OK"
        End Function

        ''' <summary>
        ''' TRACKFX_END_PARAM_EDIT|trackIndex|fxIndex|paramIndex
        ''' </summary>
        Public Function TrackFxEndParamEdit(trackIndex As Integer,
                                              fxIndex As Integer,
                                              paramIndex As Integer) As Boolean


            Dim raw = CallReaperBridge(
                $"TRACKFX_END_PARAM_EDIT|{trackIndex}|{fxIndex}|{paramIndex}")
            Return raw = "OK"

        End Function
        Public Function SetFxParamNormalized(trackIndex As Integer,
                                              fxIndex As Integer,
                                              paramIndex As Integer,
                                              normValue As Double) As Boolean


            Dim raw = CallReaperBridge(
                $"SET_FX_PARAM_NORMALIZED|{trackIndex}|{fxIndex}|{paramIndex}|{normValue.ToString("G", CultureInfo.InvariantCulture)}")
            Return raw = "OK"

        End Function

        ''' <summary>
        ''' Lit la valeur normalisée [0..1] d'un paramètre FX.
        ''' Retourne <see cref="Double.MinValue"/> si le bridge est indisponible.
        ''' </summary>
        Public Function GetFxParamNormalized(trackIndex As Integer,
                                              fxIndex As Integer,
                                              paramIndex As Integer) As Double
            Dim raw = CallReaperBridge($"GET_FX_PARAM_NORM|{trackIndex}|{fxIndex}|{paramIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return Double.MinValue
            Dim v As Double = Double.MinValue
            Double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, v)
            Return v
        End Function


        ' ── Volume UI (post-automation) ───────────────────────────────────────

        ''' <summary>
        ''' Retourne le volume POST-automation d'une piste (valeur affichée dans Reaper).
        ''' Contrairement à <see cref="GetTrackInfoD"/> avec "D_VOL", cette fonction
        ''' reflète la valeur réelle du fader pendant la lecture d'automation.
        ''' Retourne <see cref="Double.MinValue"/> si le bridge est indisponible.
        ''' </summary>
        Public Function GetTrackUIVolume(trackIndex As Integer) As Double
            Dim raw = CallReaperBridge($"GET_TRACK_UI_VOL|{trackIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return Double.MinValue
            Dim v As Double = Double.MinValue
            Double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, v)
            Return v
        End Function

        ''' <summary>
        ''' Retourne volume et pan POST-automation d'une piste en un seul appel pipe.
        ''' Retourne (Double.MinValue, Double.MinValue) si le bridge est indisponible.
        ''' </summary>
        Public Function GetTrackUIVolPan(trackIndex As Integer) As (Vol As Double, Pan As Double)
            Dim raw = CallReaperBridge($"GET_TRACK_UI_VOL_PAN|{trackIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return (Double.MinValue, Double.MinValue)
            End If
            Dim p = raw.Split("|"c)
            If p.Length < 2 Then Return (Double.MinValue, Double.MinValue)
            Dim ic = CultureInfo.InvariantCulture
            Dim vol As Double = 1, pan As Double = 0
            Double.TryParse(p(0), NumberStyles.Any, ic, vol)
            Double.TryParse(p(1), NumberStyles.Any, ic, pan)
            Return (vol, pan)
        End Function

        ''' <summary>
        ''' Écrit le volume d'une piste via <c>SetTrackUIVolume</c> (UI-safe).
        ''' Déclenche l'enregistrement d'automation si la piste est en Touch/Write/Latch.
        ''' Retourne le volume réellement appliqué par Reaper.
        ''' </summary>
        ''' <param name="vol">Volume 0..4  (1.0 = 0 dB).</param>
        ''' <param name="done">False=touch en cours  True=fin de touch/commit (défaut).</param>
        ''' <param name="igngroupflags">&amp;1=ignore grouping  &amp;2=ignore ganging.</param>
        Public Function SetTrackUIVolume(trackIndex As Integer,
                                         vol As Double,
                                         Optional done As Boolean = True,
                                         Optional igngroupflags As Integer = 0) As Double
            Dim raw = CallReaperBridge(
                $"SET_TRACK_UI_VOL|{trackIndex}|{vol.ToString("G", CultureInfo.InvariantCulture)}|0|{If(done, "1", "0")}|{igngroupflags}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return vol
            End If
            Dim result As Double = vol
            Double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, result)
            Return result
        End Function

        ' ── Conversion dB ↔ volume linéaire ─────────────────────────────────
        '    Équivalent exact des fonctions Reaper JSFX DB2SLIDER / SLIDER2DB.
        '    "Slider" désigne la valeur linéaire de volume (D_VOL / SetTrackUIVolume)
        '    et non la position visuelle du fader (0..1 normalisé).
        '
        '    Échelle D_VOL Reaper :  0 = silence   1.0 = 0 dB   4.0 ≈ +12 dB
        '    ──────────────────────────────────────────────────────────────────
        '    DB2SLIDER(-6)  ≈ 0.501     SLIDER2DB(0.501) ≈ -6 dB
        '    DB2SLIDER( 0)  = 1.0       SLIDER2DB(1.0)   =  0 dB
        '    DB2SLIDER(+6)  ≈ 1.995     SLIDER2DB(4.0)   ≈ +12 dB
        '    DB2SLIDER(-150)= 0         SLIDER2DB(0)     = -150 dB

        ''' <summary>
        ''' Convertit une valeur en dB vers un volume linéaire compatible avec
        ''' <c>D_VOL</c>, <see cref="SetTrackUIVolume"/> et <see cref="SetTrackInfoD"/>.
        ''' Implémente la fonction Reaper <c>DB2SLIDER</c>.
        ''' Retourne 0 pour toute valeur ≤ -150 dB (silence absolu).
        ''' </summary>
        Public Function DB2SLIDER(dB As Double) As Double
            If dB <= -150.0 Then Return 0.0
            Return Math.Pow(10.0, dB / 20.0)
        End Function

        ''' <summary>
        ''' Convertit un volume linéaire (valeur <c>D_VOL</c> ou retour de
        ''' <see cref="GetTrackUIVolume"/>) vers sa valeur en dB.
        ''' Implémente la fonction Reaper <c>SLIDER2DB</c>.
        ''' Retourne -150 pour tout volume ≤ 0 (silence absolu).
        ''' </summary>
        Public Function SLIDER2DB(volume As Double) As Double
            If volume <= 0.0 Then Return -150.0
            Return 20.0 * Math.Log10(volume)
        End Function

        ' ── Mute des envois (sends / receives / HW) ───────────────────────────

        ''' <summary>
        ''' Retourne l'état mute d'un envoi.
        ''' Retourne False si le bridge est indisponible.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste source.</param>
        ''' <param name="sendIndex">Index 0-based de l'envoi.</param>
        ''' <param name="category">
        ''' <see cref="SendCategory.Sends"/>=0 (défaut) 
        ''' <see cref="SendCategory.Receives"/>=-1 
        ''' <see cref="SendCategory.Hardware"/>=1
        ''' </param>
        Public Function GetTrackSendUIMute(trackIndex As Integer,
                                           sendIndex As Integer,
                                           Optional category As SendCategory = SendCategory.Sends) As Boolean
            Dim raw = CallReaperBridge($"GET_SEND_MUTE|{trackIndex}|{CInt(category)}|{sendIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return False
            Return raw = "1"
        End Function





        ' ── Volume et Pan des envois ──────────────────────────────────────────

        ''' <summary>
        ''' Retourne le volume et le pan d'un envoi en un seul appel pipe.
        ''' Retourne (Double.MinValue, Double.MinValue) si le bridge est indisponible.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste source.</param>
        ''' <param name="sendIndex">Index 0-based de l'envoi.</param>
        ''' <param name="category"><see cref="SendCategory"/> (défaut : Sends).</param>
        Public Function GetTrackSendUIVolPan(trackIndex As Integer,
                                             sendIndex As Integer,
                                             Optional category As SendCategory = SendCategory.Sends) As (Vol As Double, Pan As Double)
            Dim raw = CallReaperBridge($"GET_SEND_VOL_PAN|{trackIndex}|{CInt(category)}|{sendIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return (Double.MinValue, Double.MinValue)
            End If
            Dim p = raw.Split("|"c)
            If p.Length < 2 Then Return (Double.MinValue, Double.MinValue)
            Dim ic = CultureInfo.InvariantCulture
            Dim vol As Double = 1, pan As Double = 0
            Double.TryParse(p(0), NumberStyles.Any, ic, vol)
            Double.TryParse(p(1), NumberStyles.Any, ic, pan)
            Return (vol, pan)
        End Function



        ''' <summary>
        ''' Écrit le pan d'un envoi.
        ''' Retourne True si Reaper a accepté la valeur.
        ''' </summary>
        ''' <param name="pan">Pan -1=Gauche  0=Centre  1=Droit.</param>
        ''' <param name="category"><see cref="SendCategory"/> (défaut : Sends).</param>
        Public Function SetTrackSendUIPan(trackIndex As Integer,
                                          sendIndex As Integer,
                                          pan As Double,
                                          Optional category As SendCategory = SendCategory.Sends) As Boolean
            Dim raw = CallReaperBridge(
                $"SET_SEND_PAN|{trackIndex}|{CInt(category)}|{sendIndex}|{pan.ToString("G", CultureInfo.InvariantCulture)}")
            Return raw = "OK"
        End Function

        ''' <summary>
        ''' Écrit le volume d'un envoi via <c>SetTrackSendUIVol</c> (UI-safe, category=0 uniquement).
        ''' Pendant la lecture d'automation, enregistre si la piste est en Touch/Write/Latch.
        ''' Retourne True si Reaper a accepté la valeur.
        ''' </summary>
        ''' <param name="vol">Volume 0..4  (1.0 = 0 dB)</param>
        ''' <param name="isend">0=drag en cours  1=fin de drag/commit (défaut)  -1=écriture directe sans undo.</param>
        Public Function SetTrackSendUIVol(trackIndex As Integer,
                                          sendIndex As Integer,
                                          vol As Double,
                                          Optional isend As Integer = 1) As Boolean
            Dim raw = CallReaperBridge(
                $"SET_SEND_VOL|{trackIndex}|{sendIndex}|{vol.ToString("G", CultureInfo.InvariantCulture)}|{isend}")
            Return raw = "OK"
        End Function
        ''' <summary>
        ''' Écrit le volume d'un envoi via SET_SEND_VOL_PAN|trackIndex|category|sendIndex|vol|pan
        ''' </summary>
        ''' <param name="vol">Volume 0..4  (1.0 = 0 dB)</param>
        ''' <param name="isend">0=drag en cours  1=fin de drag/commit (défaut)  -1=écriture directe sans undo.</param>
        Public Function SetTrackSendUIVolPan(trackIndex As Integer,
                                          sendIndex As Integer,
                                          vol As Double,
                                          pan As Double, Optional isend As Integer = 0) As Boolean
            Dim raw = CallReaperBridge(
                $"SET_SEND_VOL_PAN|{trackIndex}|{isend}|{sendIndex}|{vol.ToString("G", CultureInfo.InvariantCulture)}|{pan.ToString("G", CultureInfo.InvariantCulture)}")
            Return raw = "OK"
        End Function


        ''' <summary>
        ''' Écrit le pan d'une piste via <c>SetTrackUIPan</c> (UI-safe).
        ''' Déclenche l'enregistrement d'automation si la piste est en Touch/Write/Latch.
        ''' Retourne le pan réellement appliqué par Reaper.
        ''' </summary>
        ''' <param name="pan">Pan -1=Gauche  0=Centre  1=Droite.</param>
        ''' <param name="done">False=touch en cours  True=fin de touch/commit (défaut).</param>
        ''' <param name="igngroupflags">&amp;1=ignore grouping  &amp;2=ignore ganging.</param>
        Public Function SetTrackUIPan(trackIndex As Integer,
                                      pan As Double,
                                      Optional done As Boolean = True,
                                      Optional igngroupflags As Integer = 0) As Double
            Dim raw = CallReaperBridge(
                $"SET_TRACK_UI_PAN|{trackIndex}|{pan.ToString("G", CultureInfo.InvariantCulture)}|{If(done, "1", "0")}|{igngroupflags}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return pan
            End If
            Dim result As Double = pan
            Double.TryParse(raw, Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, result)
            Return result
        End Function

        ''' <summary>
        ''' Retourne l'état mute POST-automation d'une piste via <c>GetTrackUIMute</c>.
        ''' Retourne False si le bridge est indisponible.
        ''' </summary>
        Public Function GetTrackUIMute(trackIndex As Integer) As Boolean
            Dim raw = CallReaperBridge($"GET_TRACK_UI_MUTE|{trackIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return False
            Return raw = "1"
        End Function

        ''' <summary>
        ''' Écrit l'état mute d'une piste via <c>SetTrackUIMute</c> (UI-safe).
        ''' Déclenche l'enregistrement d'automation si la piste est en Touch/Write/Latch.
        ''' Retourne le nouvel état mute (True=muet), ou False en cas d'erreur.
        ''' </summary>
        ''' <param name="mute">True=mute  False=unmute.</param>
        ''' <param name="igngroupflags">&amp;1=ignore grouping  &amp;2=ignore ganging.</param>
        Public Function SetTrackUIMute(trackIndex As Integer,
                                       mute As Boolean,
                                       Optional igngroupflags As Integer = 0) As Boolean
            Dim muteInt As Integer = If(mute, 1, 0)
            Dim raw = CallReaperBridge(
                $"SET_TRACK_UI_MUTE|{trackIndex}|{muteInt}|{igngroupflags}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return mute
            End If
            Dim result As Integer = -1
            Integer.TryParse(raw, result)
            Return result > 0
        End Function

        ''' <summary>
        ''' Bascule l'état mute d'une piste (toggle UI-safe).
        ''' Retourne le nouvel état mute, ou False si le bridge est indisponible.
        ''' </summary>
        ''' <param name="igngroupflags">&amp;1=ignore grouping  &amp;2=ignore ganging.</param>
        Public Function ToggleTrackUIMute(trackIndex As Integer,
                                          Optional igngroupflags As Integer = 0) As Boolean
            Dim raw = CallReaperBridge(
                $"SET_TRACK_UI_MUTE|{trackIndex}|-1|{igngroupflags}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return False
            End If
            Dim result As Integer = -1
            Integer.TryParse(raw, result)
            Return result > 0
        End Function

        ''' <summary>
        ''' Bascule l'état mute d'un envoi via <c>ToggleTrackSendUIMute</c> (atomique, UI-safe).
        ''' Préférer cette fonction à Get+Set pour éviter toute race condition.
        ''' Retourne True si Reaper a accepté la valeur.
        ''' </summary>
        ''' <param name="isend">0=drag en cours  1=fin de drag/commit (défaut)  -1=écriture directe sans undo.</param>
        Public Function ToggleTrackSendUIMute(trackIndex As Integer,
                                              sendIndex As Integer,
                                              Optional isend As Integer = 1) As Boolean
            Dim raw = CallReaperBridge(
                $"TOGGLE_SEND_MUTE|{trackIndex}|{sendIndex}|{isend}")
            Return raw = "OK"
        End Function
        ''' <summary>
        ''' Impose l'état mute d'un envoi (UI-safe).
        ''' Lit l'état courant via <see cref="GetTrackSendUIMute"/>.
        ''' Si l'état est déjà correct → no-op (aucun appel pipe supplémentaire).
        ''' Sinon bascule via <see cref="ToggleTrackSendUIMute"/> (atomique).
        ''' Retourne True si l'état final correspond à <paramref name="mute"/>.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste source.</param>
        ''' <param name="sendIndex">Index 0-based de l'envoi.</param>
        ''' <param name="mute">True=muet  False=actif.</param>
        ''' <param name="isend">0=drag en cours  1=fin de drag/commit (défaut)  -1=écriture directe sans undo.</param>
        Public Function SetTrackSendUIMute(trackIndex As Integer,
                                           sendIndex As Integer,
                                           mute As Boolean,
                                           Optional isend As Integer = 1) As Boolean
            Dim current = GetTrackSendUIMute(trackIndex, sendIndex)
            If current = mute Then Return True
            Return ToggleTrackSendUIMute(trackIndex, sendIndex, isend)
        End Function

        ''' <summary>
        ''' Retourne le nombre d'enveloppes d'une piste.
        ''' Inclut vol, pan, mute, width et toutes les enveloppes FX activées.
        ''' Retourne -1 si le bridge est indisponible ou si la piste est introuvable.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        Public Function CountTrackEnvelopes(trackIndex As Integer) As Integer
            Dim raw = CallReaperBridge($"COUNT_TRACK_ENVELOPES|{trackIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim v As Integer = -1
            Integer.TryParse(raw, v)
            Return v
        End Function

        ''' <summary>
        ''' Retourne le nom affiché d'une enveloppe de piste.
        ''' Exemples : "Volume", "Pan", "Mute", ou le nom d'un paramètre FX.
        ''' Retourne <see cref="String.Empty"/> si le bridge est indisponible
        ''' ou si l'enveloppe n'existe pas à cet index.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="envIndex">Index 0-based de l'enveloppe sur la piste.</param>
        Public Function GetEnvelopeName(trackIndex As Integer,
                                         envIndex As Integer) As String
            Dim raw = CallReaperBridge($"GET_ENVELOPE_NAME|{trackIndex}|{envIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return String.Empty
            Return raw
        End Function

        ' ── Propriétés chaîne des enveloppes ──────────────────────────────────

        ''' <summary>
        ''' Retourne le GUID stable d'une enveloppe ex: "{A1B2C3D4-…}".
        ''' Ce GUID est persistant entre les sessions (sauvegardé dans le .rpp).
        ''' Retourne <see cref="String.Empty"/> si le bridge est indisponible.
        ''' </summary>
        Public Function GetEnvelopeGuid(trackIndex As Integer,
                                         envIndex As Integer) As String
            Dim raw = CallReaperBridge($"GET_ENVELOPE_GUID|{trackIndex}|{envIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return String.Empty
            Return raw
        End Function

        ''' <summary>
        ''' Lit une valeur d'extension personnalisée attachée à une enveloppe.
        ''' La clé est préfixée automatiquement par "P_EXT:".
        ''' La valeur est persistée dans le fichier .rpp du projet.
        ''' Retourne <see cref="String.Empty"/> si la clé n'existe pas.
        ''' </summary>
        ''' <param name="key">Clé sans préfixe ex: "MyPlugin_State".</param>
        Public Function GetEnvelopeExt(trackIndex As Integer,
                                        envIndex As Integer,
                                        key As String) As String
            Dim raw = CallReaperBridge($"GET_ENVELOPE_EXT|{trackIndex}|{envIndex}|{key}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return String.Empty
            Return raw
        End Function

        ''' <summary>
        ''' Écrit une valeur d'extension personnalisée attachée à une enveloppe.
        ''' La clé est préfixée automatiquement par "P_EXT:".
        ''' La valeur est persistée dans le fichier .rpp du projet.
        ''' Retourne True si Reaper a accepté la valeur.
        ''' </summary>
        ''' <param name="key">Clé sans préfixe ex: "MyPlugin_State".</param>
        ''' <param name="value">Valeur chaîne à stocker.</param>
        Public Function SetEnvelopeExt(trackIndex As Integer,
                                        envIndex As Integer,
                                        key As String,
                                        value As String) As Boolean
            Dim raw = CallReaperBridge($"SET_ENVELOPE_EXT|{trackIndex}|{envIndex}|{key}|{value}")
            Return raw = "OK"
        End Function
        ' ── Propriétés numériques des enveloppes ──────────────────────────────

        ''' <summary>
        ''' Lit une propriété numérique d'une enveloppe via <c>GetSetEnvelopeInfo_String</c>.
        ''' Paramètres : "ACTIVE" "VISIBLE" "ARMED" "SHOWLANE
        ''' Retourne <see cref="Double.MinValue"/> si le bridge est indisponible.
        ''' </summary>
        Public Function GetEnvelopePropVal(trackIndex As Integer,
                                            envIndex As Integer,
                                            paramName As String) As Double
            Dim raw = CallReaperBridge($"GET_ENVELOPE_PROP_VAL|{trackIndex}|{envIndex}|{paramName}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return Double.MinValue
            Dim v As Double = Double.MinValue
            Double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, v)
            Return v
        End Function
        ' ── Propriétés numériques des enveloppes ──────────────────────────────

        ''' <summary>
        ''' Lit une propriété numérique d'une enveloppe via <c>GetSetEnvelopeInfo_Value</c>.
        ''' Paramètres : "ACTIVE" "VISIBLE" "ARMED" "IN_LANE" "LANEHEIGHT" "DEFSHAPE"
        '''              "MINVALUE" "MAXVALUE" "CENTERVALUE" "FADERSCALING" "AUTO_ITEMS_OPTIONS".
        ''' Retourne <see cref="Double.MinValue"/> si le bridge est indisponible.
        ''' </summary>
        Public Function GetEnvelopePropVal2(trackIndex As Integer,
                                            envIndex As Integer,
                                            paramName As String) As Double
            Dim raw = CallReaperBridge($"GET_ENVELOPE_PROP_VAL2|{trackIndex}|{envIndex}|{paramName}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return Double.MinValue
            Dim v As Double = Double.MinValue
            Double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, v)
            Return v
        End Function

        ''' <summary>Propriétés d'une enveloppe retournées par <c>GET_ENVELOPE_ALL_INFO</c>.</summary>
        Public Structure EnvelopeAllInfo
            ''' <summary>Valeur brute de ACTIVE (Double.MinValue si illisible).</summary>
            Public Active As Double
            ''' <summary>Valeur brute de VISIBLE (Double.MinValue si illisible).</summary>
            Public Visible As Double
            ''' <summary>Valeur brute de ARM (Double.MinValue si illisible).</summary>
            Public Armed As Double
            ''' <summary>Valeur brute de SHOWLANE (Double.MinValue si illisible).</summary>
            Public ShowLane As Double
            ''' <summary>I_SEND_IDX 1-based, 0 si pas un envoi (Double.MinValue si illisible).</summary>
            Public SendIdx As Double
            ''' <summary>GUID de l'enveloppe (vide si illisible).</summary>
            Public Guid As String
            ''' <summary>Nom de l'enveloppe (vide si illisible).</summary>
            Public Name As String
        End Structure

        Private _envAllInfoUnsupported As Boolean = False

        Private Function ParseEnvValue(s As String) As Double
            Dim v As Double = Double.MinValue
            If Double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, v) = False Then
                Return Double.MinValue
            End If
            Return v
        End Function

        ''' <summary>
        ''' Lit toutes les propriétés d'une enveloppe en une requête via
        ''' <c>GET_ENVELOPE_ALL_INFO</c>.
        ''' Retourne False si le bridge est indisponible ou ne connaît pas la commande
        ''' (ancien plugin) : l'appelant doit alors utiliser les requêtes unitaires.
        ''' Si Reaper répond une erreur métier (enveloppe absente), retourne True avec
        ''' des valeurs Double.MinValue / vides.
        ''' </summary>
        Public Function TryGetEnvelopeAllInfo(trackIndex As Integer,
                                              envIndex As Integer,
                                              ByRef info As EnvelopeAllInfo) As Boolean
            If _envAllInfoUnsupported Then
                Return False
            End If
            Dim raw = CallReaperBridge($"GET_ENVELOPE_ALL_INFO|{trackIndex}|{envIndex}")
            If String.IsNullOrEmpty(raw) Then
                Return False
            End If
            If raw.StartsWith("ERROR:Unknown", StringComparison.Ordinal) Then
                _envAllInfoUnsupported = True
                Return False
            End If
            info.Active = Double.MinValue
            info.Visible = Double.MinValue
            info.Armed = Double.MinValue
            info.ShowLane = Double.MinValue
            info.SendIdx = Double.MinValue
            info.Guid = String.Empty
            info.Name = String.Empty
            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return True
            End If
            Dim p = raw.Split("|"c, 7)
            If p.Length < 7 Then
                Return False
            End If
            info.Active = ParseEnvValue(p(0))
            info.Visible = ParseEnvValue(p(1))
            info.Armed = ParseEnvValue(p(2))
            info.ShowLane = ParseEnvValue(p(3))
            info.SendIdx = ParseEnvValue(p(4))
            info.Guid = p(5)
            info.Name = p(6)
            Return True
        End Function

        ''' <summary>
        ''' Écrit une propriété numérique d'une enveloppe via <c>GetSetEnvelopeInfo_Value</c>.
        ''' Retourne True si Reaper a accepté la valeur.
        ''' </summary>
        Public Function SetEnvelopePropVal(trackIndex As Integer,
                                            envIndex As Integer,
                                            paramName As String,
                                            value As Double) As Boolean
            Dim raw = CallReaperBridge(
                $"SET_ENVELOPE_PROP_VAL|{trackIndex}|{envIndex}|{paramName}|{value.ToString("G", CultureInfo.InvariantCulture)}")
            Return raw = "OK"
        End Function

        ' ── Enveloppes de paramètres FX ───────────────────────────────────────

        ''' <summary>
        ''' Retourne l'index 0-based de l'enveloppe d'un paramètre FX
        ''' dans la liste des enveloppes de la piste (compatible avec
        ''' <see cref="GetEnvelopePropVal"/>, <see cref="GetEnvelopeName"/>…).
        ''' Si <paramref name="create"/> est True et que l'enveloppe n'existe pas,
        ''' elle est créée dans Reaper avant de retourner l'index.
        ''' Retourne -1 si l'enveloppe n'existe pas (<paramref name="create"/>=False)
        ''' ou si le bridge est indisponible.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
        ''' <param name="paramIndex">Index 0-based du paramètre sur le FX.</param>
        ''' <param name="create">True = créer l'enveloppe si absente (défaut : False).</param>
        Public Function GetFXEnvelopeIndex(trackIndex As Integer,
                                            fxIndex As Integer,
                                            paramIndex As Integer,
                                            Optional create As Boolean = False) As Integer
            Dim raw = CallReaperBridge(
                $"GET_FX_ENVELOPE|{trackIndex}|{fxIndex}|{paramIndex}|{If(create, "1", "0")}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim v As Integer = -1
            Integer.TryParse(raw, v)
            Return v
        End Function

        ''' <summary>
        ''' Retourne un <see cref="ReaperFxParamEnvelope"/> chargé pour l'enveloppe
        ''' d'un paramètre FX.
        ''' Si <paramref name="create"/> est True, crée l'enveloppe dans Reaper
        ''' si elle n'existe pas encore.
        ''' Retourne Nothing si l'enveloppe est absente (<paramref name="create"/>=False)
        ''' ou si le bridge est indisponible.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
        ''' <param name="paramIndex">Index 0-based du paramètre sur le FX.</param>
        ''' <param name="create">True = créer l'enveloppe si absente (défaut : False).</param>
        Public Function GetFXEnvelopeParam(trackIndex As Integer,
                                            fxIndex As Integer,
                                            paramIndex As Integer,
                                            Optional create As Boolean = False) As ReaperFxParamEnvelope
            Dim envIdx = GetFXEnvelopeIndex(trackIndex, fxIndex, paramIndex, create)
            If envIdx < 0 Then Return Nothing
            Dim env As New ReaperFxParamEnvelope(trackIndex, fxIndex, paramIndex, envIdx)
            env.Refresh()
            Return env
        End Function

        ' ── State Chunk des enveloppes ────────────────────────────────────────

        ''' <summary>
        ''' Retourne le state chunk complet d'une enveloppe (format .rpp).
        ''' Utile pour sauvegarder/restaurer précisément tous les points d'automation,
        ''' la mise en forme ou les paramètres non exposés par <see cref="GetEnvelopePropVal"/>.
        ''' Les sauts de ligne sont décodés automatiquement depuis le transport pipe.
        ''' Retourne <see cref="String.Empty"/> si le bridge est indisponible.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="envIndex">Index 0-based de l'enveloppe sur la piste.</param>
        Public Function GetEnvelopeStateChunk(trackIndex As Integer,
                                               envIndex As Integer) As String
            Dim raw = CallReaperBridge($"GET_ENVELOPE_STATE_CHUNK|{trackIndex}|{envIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return String.Empty
            ' Décoder \n littéral → vrais sauts de ligne LF
            Return raw.Replace("\n", vbLf)
        End Function

        ''' <summary>
        ''' Restaure l'état d'une enveloppe depuis un state chunk .rpp.
        ''' Le chunk peut être lu via <see cref="GetEnvelopeStateChunk"/> puis modifié.
        ''' Les sauts de ligne sont encodés automatiquement pour le transport pipe.
        ''' Retourne True si Reaper a accepté le chunk.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="envIndex">Index 0-based de l'enveloppe sur la piste.</param>
        ''' <param name="chunk">State chunk .rpp (avec sauts de ligne réels).</param>
        Public Function SetEnvelopeStateChunk(trackIndex As Integer,
                                               envIndex As Integer,
                                               chunk As String) As Boolean
            ' Encoder CR+LF / LF / CR → \n littéral pour le transport pipe
            Dim encoded = chunk.Replace(vbCrLf, "\n").Replace(vbLf, "\n").Replace(vbCr, "")
            Dim raw = CallReaperBridge(
                $"SET_ENVELOPE_STATE_CHUNK|{trackIndex}|{envIndex}|{encoded}")
            Return raw = "OK"
        End Function

        ' ── Transport ─────────────────────────────────────────────────────────

        ''' <summary>
        ''' Retourne la position de lecture courante en secondes via <c>GetPlayPosition</c>.
        ''' Retourne -1 si le transport est arrêté ou si le bridge est indisponible.
        ''' </summary>
        Public Function GetPlayPosition() As Double
            Dim raw = CallReaperBridge("GET_PLAY_POSITION")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return -1
            Dim v As Double = -1
            Double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, v)
            Return v
        End Function

        ' ── Points d'enveloppe ────────────────────────────────────────────────

        ''' <summary>
        ''' Insère un point d'automation dans une enveloppe à <paramref name="time"/> secondes.
        ''' Appeler <see cref="SortEnvelopePoints"/> après un batch d'insertions.
        ''' shape : 0=linéaire 1=carré 2=slow start/end 3=fast start 4=fast end 5=bézier.
        ''' Retourne True si Reaper a accepté le point.
        ''' </summary>
        Public Function InsertEnvelopePoint(trackIndex As Integer,
                                             envIndex As Integer,
                                             time As Double,
                                             value As Double,
                                             Optional shape As Integer = 0,
                                             Optional tension As Double = 0) As Boolean
            Dim ic = CultureInfo.InvariantCulture
            Dim raw = CallReaperBridge(
                $"INSERT_ENVELOPE_POINT|{trackIndex}|{envIndex}" &
                $"|{time.ToString("G", ic)}|{value.ToString("G", ic)}" &
                $"|{shape}|{tension.ToString("G", ic)}")
            Return raw = "OK"
        End Function

        ''' <summary>
        ''' Trie les points d'une enveloppe après un batch d'insertions.
        ''' Toujours appeler avant <c>UPDATE_ARRANGE</c>.
        ''' Retourne True si Reaper a accepté l'opération.
        ''' </summary>
        Public Function SortEnvelopePoints(trackIndex As Integer,
                                            envIndex As Integer) As Boolean
            Return CallReaperBridge($"SORT_ENVELOPE_POINTS|{trackIndex}|{envIndex}") = "OK"
        End Function

        ' ── Cache des index d'enveloppes pour les sessions d'automation live ──
        ' Clé : "trackIndex|fxIndex|paramIndex"  Valeur : envIndex résolu
        ' Evite de rappeler GetFXEnvelopeIndex (create=True) à chaque frame,
        ' ce qui peut déclencher la création d'un point d'ancrage parasite dans Reaper.
        Private ReadOnly _fxEnvCache As New Dictionary(Of String, Integer)

        ''' <summary>
        ''' Réinitialise le cache des enveloppes FX.
        ''' Appeler quand le projet change ou après un rafraîchissement complet des pistes.
        ''' </summary>
        Public Sub ClearFxEnvCache()
            _fxEnvCache.Clear()
        End Sub

        ''' <summary>
        ''' Insère un point d'automation FX à la position de lecture courante (atomique).
        ''' Si le transport est arrêté, écrit la valeur live directement.
        ''' Retourne True si l'opération a réussi.
        ''' </summary>
        ''' <remarks>
        ''' Utilise la commande <c>INSERT_FX_ENVELOPE_POINT_NOW</c> qui capture
        ''' <c>GetPlayPosition()</c> et insère le point dans le même handler bridge,
        ''' éliminant le décalage pipe (~5 ms) qui déclenchait un point d'ancrage
        ''' parasite de continuité dans Reaper.<br/>
        ''' <c>ARMED=0</c> désactive l'écriture automatique en mode TOUCH/LATCH.<br/>
        ''' <c>shape=1</c> (carré) maintient la valeur jusqu'au point suivant.
        ''' </remarks>
        Public Function SetFxParamNormalizedUI(trackIndex As Integer,
                                                fxIndex As Integer,
                                                paramIndex As Integer,
                                                normValue As Double,
                                                Optional shape As Integer = 1) As Boolean
            Dim ic = CultureInfo.InvariantCulture

            ' ── Un seul appel pipe : position + insertion atomiques ────────────
            Dim raw = CallReaperBridge(
                $"INSERT_FX_ENVELOPE_POINT_NOW|{trackIndex}|{fxIndex}|{paramIndex}" &
                $"|{normValue.ToString("G", ic)}|{shape}|0")

            ' Transport arrêté → écriture live directe sans automation
            If raw = "STOPPED" Then
                Return SetFxParamNormalized(trackIndex, fxIndex, paramIndex, normValue)
            End If

            Return raw = "OK"
        End Function
        ''' <summary>
        ''' Insère un point d'automation FX à la position de lecture courante (atomique).
        ''' N'appelle jamais <see cref="SetFxParamNormalized"/> — insertion pure dans l'enveloppe.
        ''' Retourne False si le transport est arrêté ou si le bridge est indisponible.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
        ''' <param name="paramIndex">Index 0-based du paramètre sur le FX.</param>
        ''' <param name="normValue">Valeur normalisée [0..1] à enregistrer.</param>
        ''' <param name="shape">
        ''' Forme du point : 0=linéaire  1=carré (hold, défaut)  2=slow start/end
        ''' 3=fast start  4=fast end  5=bézier.
        ''' </param>
        Public Function InsertFxEnvelopePointNow(trackIndex As Integer,
                                                  fxIndex As Integer,
                                                  paramIndex As Integer,
                                                  normValue As Double,
                                                  Optional shape As Integer = 1) As Boolean
            Dim ic = CultureInfo.InvariantCulture
            Dim raw = CallReaperBridge(
                $"INSERT_FX_ENVELOPE_POINT_NOW|{trackIndex}|{fxIndex}|{paramIndex}" &
                $"|{normValue.ToString("G", ic)}|{shape}|0")
            Return raw = "OK"
        End Function
        ''' <summary>
        ''' Termine une session d'automation FX : trie et rafraîchit l'arrangement.
        ''' À appeler quand l'utilisateur relâche le contrôle (fin de touch).
        ''' </summary>
        Public Sub CommitFxAutoSession(trackIndex As Integer,
                                        fxIndex As Integer,
                                        paramIndex As Integer)
            Dim envIdx = GetFXEnvelopeIndex(trackIndex, fxIndex, paramIndex, create:=False)
            If envIdx >= 0 Then SortEnvelopePoints(trackIndex, envIdx)
            CallReaperBridge("UPDATE_ARRANGE")
        End Sub
        ''' <summary>
        ''' Supprime l'enveloppe d'automation d'un paramètre FX :
        ''' efface tous les points, désactive et masque l'enveloppe.
        ''' <br/>
        ''' Retourne False si l'enveloppe n'existe pas ou si le bridge est indisponible.
        ''' <br/>
        ''' ⚠️ Reaper ne propose pas d'API <c>DeleteEnvelope</c> publique.
        ''' Cette implémentation vide les points et passe <c>ACTIVE=0 / VISIBLE=0</c>,
        ''' ce qui est l'équivalent fonctionnel pour les enveloppes FX.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
        ''' <param name="paramIndex">Index 0-based du paramètre sur le FX.</param>
        Public Function DeleteFXEnvelope(trackIndex As Integer,
                                          fxIndex As Integer,
                                          paramIndex As Integer) As Boolean
            Dim raw = CallReaperBridge($"DELETE_FX_ENVELOPE|{trackIndex}|{fxIndex}|{paramIndex}")
            Return raw = "OK"
        End Function

        ''' <summary>
        ''' Retourne le GUID stable d'une piste (format "{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}").
        ''' Ce GUID est persistant entre les sessions — il identifie la piste de manière unique
        ''' indépendamment de son index (qui change lors d'insertions/suppressions).
        ''' Retourne <see cref="String.Empty"/> si le bridge est indisponible ou si
        ''' la piste est introuvable.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        Public Function GetTrackGUID(trackIndex As Integer) As String
            Dim raw = CallReaperBridge($"GET_TRACK_GUID|{trackIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return "err"
            Return raw
        End Function

        ' ── Gestion des entrées audio ─────────────────────────────────────────

        ''' <summary>
        ''' Construit la liste complète des entrées audio disponibles en 1 appel pipe :
        ''' <list type="bullet">
        ''' <item>"-- Aucune --" (<c>I_RECINPUT</c> = -1)</item>
        ''' <item>Entrées <b>mono</b> 1..N (<c>I_RECINPUT</c> = 0..N-1)</item>
        ''' <item>Paires <b>stéréo</b> consécutives 1/2, 3/4… (<c>I_RECINPUT</c> = 1024 + leftChannel)</item>
        ''' </list>
        ''' Si le bridge est indisponible ou si le moteur audio Reaper n'est pas
        ''' démarré, retourne uniquement "-- Aucune --".
        ''' </summary>
        ''' <remarks>
        ''' Encodage stéréo Reaper :
        ''' <code>
        '''   Paire 1/2  → I_RECINPUT = 1024 + 0 = 1024
        '''   Paire 3/4  → I_RECINPUT = 1024 + 2 = 1026
        '''   Paire 5/6  → I_RECINPUT = 1024 + 4 = 1028
        ''' </code>
        ''' Seules les paires de canaux pairs sont proposées (comportement standard UI Reaper).
        ''' </remarks>
        Public Function BuildAudioInputList() As IReadOnlyList(Of ReaperInputChannel)
            Dim result As New List(Of ReaperInputChannel)

            ' ── Aucune entrée ─────────────────────────────────────────────────
            result.Add(New ReaperInputChannel(-1, "-- Aucune --", False, -1))

            ' ── Récupérer tous les noms d'entrées en 1 appel pipe ─────────────
            Dim inputs = GetAllAudioInputs()
            If inputs.Count = 0 Then Return result

            ' ── Entrées mono ──────────────────────────────────────────────────
            ' I_RECINPUT = index 0-based
            ' Affichage 1-based comme dans Reaper UI  ex: "1: Mic L"
            For i = 0 To inputs.Count - 1
                Dim displayName = $"{i + 1}: {inputs(i).Name}"
                result.Add(New ReaperInputChannel(i, displayName, False, i))
            Next

            ' ── Paires stéréo consécutives (0/1, 2/3, 4/5…) ─────────────────
            ' I_RECINPUT = 1024 + leftChannel (canal gauche 0-based)
            ' Uniquement les paires paires (standard Reaper) :
            '   leftChannel = 0 → paire 1/2 → valeur 1024
            '   leftChannel = 2 → paire 3/4 → valeur 1026
            '   leftChannel = 4 → paire 5/6 → valeur 1028
            Dim n = 0
            While n + 1 < inputs.Count
                Dim leftName = inputs(n).Name
                Dim rightName = inputs(n + 1).Name
                Dim displayName = $"{n + 1}/{n + 2}: {leftName}/{rightName}"
                Dim recValue = 1024 + n   ' ← encodage Reaper stéréo
                result.Add(New ReaperInputChannel(recValue, displayName, True, n))
                n += 2
            End While

            Return result
        End Function

        ''' <summary>
        ''' Recherche l'index 0-based de <paramref name="recInputValue"/> dans
        ''' <paramref name="inputs"/>.
        ''' Retourne 0 (= "-- Aucune --") si la valeur n'est pas trouvée.
        ''' </summary>
        ''' <param name="recInputValue">Valeur <c>I_RECINPUT</c> de la piste.</param>
        ''' <param name="inputs">Liste construite par <see cref="BuildAudioInputList"/>.</param>
        Public Function FindInputIndex(recInputValue As Integer,
                                        inputs As IReadOnlyList(Of ReaperInputChannel)) As Integer
            For i = 0 To inputs.Count - 1
                If inputs(i).RecInputValue = recInputValue Then Return i
            Next
            Return 0
        End Function


        ''' <summary>
        ''' Construit la liste complète des sorties audio hardware disponibles :
        ''' <list type="bullet">
        ''' <item>Sorties <b>mono</b> 1..N</item>
        ''' <item>Paires <b>stéréo</b> consécutives 1/2, 3/4…</item>
        ''' </list>
        ''' Retourne une liste vide si le bridge est indisponible.
        ''' </summary>
        ''' <remarks>
        ''' Encodage Reaper pour un hardware send :
        ''' <code>
        '''   I_DSTCHAN bit 10 (&amp;1024) = 1 → canal hardware unique (mono)
        '''   I_DSTCHAN bit 10 (&amp;1024) = 0 → paire stéréo (deux canaux consécutifs)
        '''   I_SRCCHAN bit 10 (&amp;1024) = 1 → source stéréo (2 canaux de la piste)
        '''   I_SRCCHAN bit 10 (&amp;1024) = 0 → source mono   (1 canal de la piste)
        '''
        '''   Output 1 mono    → I_DSTCHAN = 1024 + 0 = 1024   I_SRCCHAN = 0
        '''   Output 2 mono    → I_DSTCHAN = 1024 + 1 = 1025   I_SRCCHAN = 0
        '''   Output 1/2 stéro → I_DSTCHAN = 0                 I_SRCCHAN = 1024
        '''   Output 3/4 stéro → I_DSTCHAN = 2                 I_SRCCHAN = 1024
        ''' </code>
        ''' </remarks>
        Public Function BuildAudioOutputList() As IReadOnlyList(Of ReaperOutputChannel)
            Dim result As New List(Of ReaperOutputChannel)

            Dim outputs = GetAllAudioOutputs()
            If outputs.Count = 0 Then Return result

            ' ── Sorties mono ──────────────────────────────────────────────────
            ' I_DSTCHAN = channelIndex | 1024  (bit 10 = canal hardware unique, mix to mono)
            ' I_SRCCHAN = 1024                 (prend les 2 canaux consécutifs de la piste
            '                                   et les mixe en mono vers le canal destination)
            For i = 0 To outputs.Count - 1
                result.Add(New ReaperOutputChannel(
                    i Or 1024,
                    1024,
                    $"{i + 1}: {outputs(i).Name}",
                    False,
                    i))
            Next

            ' ── Paires stéréo consécutives (0/1, 2/3, 4/5…) ─────────────────
            ' I_DSTCHAN = leftChannelIndex     (bit 10 NOT set = paire stéréo)
            ' I_SRCCHAN = 0                    (canal source 0 de la piste)
            Dim n = 0
            While n + 1 < outputs.Count
                result.Add(New ReaperOutputChannel(
                    n,
                    0,
                    $"{n + 1}/{n + 2}: {outputs(n).Name}/{outputs(n + 1).Name}",
                    True,
                    n))
                n += 2
            End While

            Return result
        End Function


        ''' <summary>
        ''' Recherche l'index 0-based d'une sortie dans <paramref name="outputs"/>
        ''' d'après sa valeur <c>I_DSTCHAN</c> complète et son mode stéréo.
        ''' Retourne -1 si non trouvé.
        ''' </summary>
        ''' <remarks>
        ''' La valeur <paramref name="dstChan"/> doit être brute (avec bit 10 inclus) :
        ''' <list type="bullet">
        ''' <item>Mono  : <c>dstChan &amp; 1024 ≠ 0</c> → <paramref name="isStereo"/> = False</item>
        ''' <item>Stéréo : <c>dstChan &amp; 1024 = 0</c>  → <paramref name="isStereo"/> = True</item>
        ''' </list>
        ''' </remarks>
        Public Function FindOutputIndex(dstChan As Integer,
                                         isStereo As Boolean,
                                         outputs As IReadOnlyList(Of ReaperOutputChannel)) As Integer
            For i = 0 To outputs.Count - 1
                If outputs(i).DstChan = dstChan AndAlso outputs(i).IsStereo = isStereo Then
                    Return i
                End If
            Next
            Return -1
        End Function
        ''' <summary>
        ''' Impose un état de transport Reaper par sa valeur numérique.
        ''' 0=stop  1=play  2=pause  4=rec  5=rec+play  6=pause+rec
        ''' Retourne True si Reaper a accepté la commande.
        ''' </summary>
        ''' <param name="playState">Valeur cible de l'état de transport.</param>


        ''' <summary>
        ''' Retourne le tempo Reaper en BPM à la position courante du curseur.
        ''' Utilise <c>Master_GetTempo</c> via le bridge.
        ''' Retourne 120 en cas d'erreur.
        ''' </summary>
        Public Function GetBpm() As Double
            Dim ic = Globalization.CultureInfo.InvariantCulture
            Dim raw = CallReaperBridge("GET_MASTER_TEMPO")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return 120.0
            End If
            Dim bpm As Double = 120.0
            If Double.TryParse(raw, Globalization.NumberStyles.Float, ic, bpm) Then
                Return bpm
            End If
            Return 120.0
        End Function

        ''' <summary>
        ''' Retourne le numérateur de la signature rythmique (beats per bar)
        ''' à la position courante du curseur.
        ''' Utilise <c>TimeMap_GetTimeSigAtTime</c> via le bridge.
        ''' Retourne 4 en cas d'erreur.
        ''' </summary>
        Public Function GetBeatsPerBar() As Integer
            Dim raw = CallReaperBridge("GET_TIMESIG_NUMERATOR")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return 4
            End If
            Dim num As Integer = 4
            If Integer.TryParse(raw, num) Then
                Return num
            End If
            Return 4
        End Function

        ''' <summary>
        ''' Retourne True si la fenêtre flottante d'un FX de piste est actuellement ouverte.
        ''' Équivalent de <c>TrackFX_GetOpen</c> via le bridge.
        ''' Retourne False si le bridge est indisponible ou si la piste/FX est introuvable.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
        Public Function TrackFX_IsOpen(trackIndex As Integer,
                                       fxIndex As Integer) As Boolean
            Dim raw = CallReaperBridge($"GET_FX_OPEN|{trackIndex}|{fxIndex}")
            Return raw = "1"
        End Function
        Public Function TrackFX_SetOpen(trackIndex As Integer,
                                       fxIndex As Integer, Open As Boolean) As Boolean
            Dim raw = CallReaperBridge($"SET_FX_OPEN|{trackIndex}|{fxIndex}|{If(Open, "1", "0")}")
            Return If(raw = "OK", True, False)
        End Function

        ''' <summary>
        ''' Ouvre ou ferme la fenêtre flottante d'un FX de piste.
        ''' Utilise <c>TrackFX_Show</c> (API stable) et non <c>TrackFX_SetOpen</c>
        ''' qui provoque un crash Reaper.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
        ''' <param name="open">True = ouvrir la fenêtre flottante  False = fermer.</param>
        Public Sub TrackFX_Show(trackIndex As Integer,
                                fxIndex As Integer,
                                open As Boolean)
            'showflag=0 for hidechain, =1 for show chain(index valid), =2 for hide floating window(index valid), =3 for show floating window (index valid)
            ' showFlag 3 = ouvrir flottant (déterministe, pas de toggle)
            ' showFlag 2 = cacher
            Dim showFlag = If(open, 3, 2)
            CallReaperBridge($"TRACKFX_SHOW|{trackIndex}|{fxIndex}|{showFlag}")
        End Sub

        Public Structure FxParamValues
            Public RawValue As Double
            Public minValue As Double
            Public maxValue As Double
            Public midValue As Double
        End Structure
        ''' <summary>
        ''' Lit la valeur brute (plage native du plugin) d'un paramètre FX.
        ''' Contrairement à <see cref="GetFxParamNormalized"/>, la plage de retour
        ''' dépend du plugin (ex. fréquence en Hz, gain en dB…).
        ''' Retourne <see cref="Double.MinValue"/> si le bridge est indisponible.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
        ''' <param name="paramIndex">Index 0-based du paramètre sur le FX.</param>
        Public Function GetFxParamEx(trackIndex As Integer,
                                   fxIndex As Integer,
                                   paramIndex As Integer) As FxParamValues
            Dim _FxParamValues As New FxParamValues
            Dim exRaw = utility.CallReaperBridge(
               $"GET_FX_PARAM_EX|{trackIndex}|{fxIndex}|{paramIndex}")
            If Not String.IsNullOrEmpty(exRaw) AndAlso Not exRaw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Dim p = exRaw.Split("|"c)
                If p.Length >= 4 Then

                    Dim Ic = CultureInfo.CurrentCulture
                    Dim new_raw As Double = 0
                    Double.TryParse(p(0), NumberStyles.Any, Ic, new_raw)
                    Dim dbl As Double = 0
                    _FxParamValues.RawValue = If(Double.TryParse(p(0), NumberStyles.Any, Ic, dbl), dbl, Nothing)
                    _FxParamValues.minValue = If(Double.TryParse(p(1), NumberStyles.Any, Ic, dbl), dbl, Nothing)
                    _FxParamValues.maxValue = If(Double.TryParse(p(2), NumberStyles.Any, Ic, dbl), dbl, Nothing)
                    _FxParamValues.midValue = If(Double.TryParse(p(3), NumberStyles.Any, Ic, dbl), dbl, Nothing)
                End If
            End If
            Return _FxParamValues
        End Function

        ''' <summary>
        ''' Écrit un paramètre de configuration nommé sur un FX de piste.
        ''' <br/>
        ''' Clés courantes :
        ''' <list type="bullet">
        ''' <item><c>"wet"</c> — niveau wet/dry [0..1]</item>
        ''' <item><c>"bypass"</c> — bypass ("0" ou "1")</item>
        ''' <item><c>"renamed_name"</c> — nom affiché dans la chaîne FX</item>
        ''' <item><c>"floatpos"</c> — position fenêtre flottante "x y w h"</item>
        ''' <item><c>"online"</c> — en ligne / hors ligne ("0" ou "1")</item>
        ''' </list>
        ''' Retourne True si Reaper a accepté la valeur.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
        ''' <param name="parmName">Nom du paramètre de configuration.</param>
        ''' <param name="value">Valeur chaîne à appliquer.</param>
        Public Function SetFxNamedConfigParm(trackIndex As Integer,
                                             fxIndex As Integer,
                                             parmName As String,
                                             value As String) As Boolean
            Dim raw = CallReaperBridge(
                $"SET_FX_NAMED_CONFIG_PARM|{trackIndex}|{fxIndex}|{parmName}|{value}")
            Return raw = "OK"
        End Function

        ''' <summary>
        ''' Lit un paramètre de configuration nommé d'un FX de piste.
        ''' Retourne <see cref="String.Empty"/> si la clé n'existe pas ou si le bridge est indisponible.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
        ''' <param name="parmName">Nom du paramètre de configuration.</param>
        Public Function GetFxNamedConfigParm(trackIndex As Integer,
                                             fxIndex As Integer,
                                             parmName As String) As String
            Dim raw = CallReaperBridge(
                $"GET_FX_NAMED_CONFIG_PARM|{trackIndex}|{fxIndex}|{parmName}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return String.Empty
            End If
            Return raw
        End Function

        ' ── State Chunk ───────────────────────────────────────────────────────

        ''' <summary>
        ''' Sérialise l'état complet d'une piste au format .rpp.
        ''' <br/>
        ''' Équivalent de <c>GetTrackStateChunk</c> dans l'API Reaper.
        ''' Utile pour sauvegarder/restaurer l'état d'une piste (FX, routing,
        ''' paramètres) ou pour comparer deux états.
        ''' <br/>
        ''' Retourne <see cref="String.Empty"/> si le bridge est indisponible.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        Public Function GetTrackStateChunk(trackIndex As Integer) As String
            Dim raw = CallReaperBridge($"GET_TRACK_STATE_CHUNK|{trackIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return String.Empty
            End If
            ' Décoder \n littéral → saut de ligne réel
            Return raw.Replace("\n", vbLf)
        End Function

        ''' <summary>
        ''' Restaure l'état d'une piste depuis un state chunk .rpp.
        ''' <br/>
        ''' Passer le chunk retourné par <see cref="GetTrackStateChunk"/> —
        ''' les sauts de ligne sont encodés/décodés automatiquement.
        ''' <br/>
        ''' Retourne True si Reaper a accepté le chunk.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="chunk">State chunk .rpp (avec sauts de ligne réels).</param>
        Public Function SetTrackStateChunk(trackIndex As Integer,
                                           chunk As String) As Boolean

            ' Encoder LF → \n littéral pour le transport pipe
            Dim encoded = chunk.Replace(vbCrLf, "\n").Replace(vbLf, "\n").Replace(vbCr, "")
            Dim raw = CallReaperBridge($"SET_TRACK_STATE_CHUNK|{trackIndex}|{encoded}")
            Return raw = "OK"
        End Function

        ''' <summary>
        ''' Retourne l'index 0-based de la piste se trouvant aux coordonnées écran données.
        ''' <br/>
        ''' Équivalent de <c>GetTrackFromPoint</c> dans l'API Reaper.
        ''' Utile pour détecter quelle piste l'utilisateur survole avec la souris.
        ''' <list type="table">
        ''' <item><term>trackIndex = -1</term><description>Piste maître ou aucune piste à cette position.</description></item>
        ''' <item><term>infoOut</term><description>Bits d'information sur la zone pointée
        ''' (ex. : &amp;1 = bouton/contrôle TCP, &amp;2 = zone de volume…).</description></item>
        ''' </list>
        ''' Retourne (-1, 0) si le bridge est indisponible.
        ''' </summary>
        ''' <param name="screenX">Coordonnée X écran.</param>
        ''' <param name="screenY">Coordonnée Y écran.</param>
        Public Function GetTrackFromPoint(screenX As Integer,
                                          screenY As Integer) As (trackIndex As Integer, infoOut As Integer)
            Dim raw = CallReaperBridge($"GET_TRACK_FROM_POINT|{screenX}|{screenY}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return (-1, 0)
            End If
            Dim p = raw.Split("|"c)
            If p.Length < 2 Then
                Return (-1, 0)
            End If
            Dim idx As Integer = -1
            Dim info As Integer = 0
            Integer.TryParse(p(0), idx)
            Integer.TryParse(p(1), info)
            Return (idx, info)
        End Function

        ''' <summary>
        ''' Retourne le HWND de la fenêtre principale Reaper.
        ''' <br/>
        ''' Équivalent de <c>GetMainHwnd</c> dans l'API Reaper.
        ''' Utile pour interagir avec la fenêtre Reaper (SendMessage, SetForeground…).
        ''' Retourne <see cref="IntPtr.Zero"/> si le bridge est indisponible.
        ''' </summary>
        Public Function GetMainHwnd() As IntPtr
            Dim raw = CallReaperBridge("GET_MAIN_HWND")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return IntPtr.Zero
            End If
            Dim value As Long = 0
            If Long.TryParse(raw, Globalization.NumberStyles.Integer,
                             Globalization.CultureInfo.InvariantCulture, value) Then
                Return New IntPtr(value)
            End If
            Return IntPtr.Zero
        End Function



        ' ── Win32 (Windows) ───────────────────────────────────────────────────
        <Runtime.InteropServices.DllImport("user32.dll")>
        Private Function GetCursorPos_Win32(ByRef lpPoint As MousePoint) As Boolean
        End Function

        ' ── CoreGraphics (macOS) ──────────────────────────────────────────────
        <Runtime.InteropServices.DllImport(
            "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")>
        Private Function CGEventCreate(source As IntPtr) As IntPtr
        End Function

        <Runtime.InteropServices.DllImport(
            "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")>
        Private Function CGEventGetLocation(event_ As IntPtr) As MacPoint
        End Function

        <Runtime.InteropServices.DllImport(
            "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")>
        Private Sub CFRelease(cf As IntPtr)
        End Sub

        <Runtime.InteropServices.StructLayout(
            Runtime.InteropServices.LayoutKind.Sequential)>
        Private Structure MousePoint
            Public X As Integer
            Public Y As Integer
        End Structure

        <Runtime.InteropServices.StructLayout(
            Runtime.InteropServices.LayoutKind.Sequential)>
        Private Structure MacPoint
            Public X As Double
            Public Y As Double
        End Structure

        ''' <summary>
        ''' Retourne les coordonnées écran courantes du curseur souris.
        ''' Compatible Windows et macOS — aucun round-trip bridge.
        ''' Retourne (-1, -1) si la lecture échoue.
        ''' </summary>
        Public Function GetMousePosition() As (x As Integer, y As Integer)
            If OperatingSystem.IsWindows() Then
                Dim pt As MousePoint
                If Not GetCursorPos_Win32(pt) Then
                    Return (-1, -1)
                End If
                Return (pt.X, pt.Y)
            ElseIf OperatingSystem.IsMacOS() Then
                Dim ev = CGEventCreate(IntPtr.Zero)
                If ev = IntPtr.Zero Then Return (-1, -1)
                Try
                    Dim pt = CGEventGetLocation(ev)
                    Return (CInt(pt.X), CInt(pt.Y))
                Finally
                    CFRelease(ev)
                End Try
            End If
            Return (-1, -1)
        End Function

        ''' <summary>
        ''' Retourne l'index 0-based d'une enveloppe de piste par son nom exact.
        ''' Retourne -1 si introuvable ou si le bridge est indisponible.
        ''' </summary>
        Public Function GetTrackEnvelopeByName(trackIndex As Integer,
                                               envelopeName As String) As Integer
            Dim raw = CallReaperBridge($"GET_TRACK_ENVELOPE_BY_NAME|{trackIndex}|{envelopeName}")

            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return -1
            End If

            Dim idx As Integer = -1
            Integer.TryParse(raw, idx)
            Return idx
        End Function


        ''' <summary>
        ''' Crée une enveloppe de piste via son nom en sélectionnant d'abord la piste cible.
        ''' Retourne True si la commande a été acceptée par le bridge.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="envName">
        ''' Nom de l'enveloppe :
        ''' "Volume", "Pan", "Mute", "Width", "Volume (pre-FX)", "Pan (pre-FX)", "Trim Volume".
        ''' </param>
        Public Function CreateTrackEnvelopeByName(trackIndex As Integer,
                                                  envName As String) As Boolean
            If String.IsNullOrEmpty(envName) Then
                Return False
            End If

            Dim raw = CallReaperBridge($"CREATE_TRACK_ENVELOPE|{trackIndex}|{envName}")
            Return raw = "OK"
        End Function



        ''' <summary>
        ''' Lit une valeur d'état projet persistante (extName/key).
        ''' Retourne String.Empty si la clé n'existe pas ou si le bridge est indisponible.
        ''' </summary>
        Public Function GetProjectExtState(extName As String, key As String, Optional InitValue As String = Nothing) As String
            Dim raw As String = CallReaperBridge($"GET_PROJECT_EXT_STATE|{extName}|{key}")
            If String.IsNullOrEmpty(raw) Then
                If InitValue IsNot Nothing Then
                    SetProjectExtState(extName, key, InitValue)
                    Return InitValue
                Else
                    Return String.Empty
                End If

            End If
            If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return String.Empty
            End If
            Return raw
        End Function

        ''' <summary>
        ''' Écrit une valeur d'état projet persistante (extName/key/value).
        ''' Retourne True si l'écriture a réussi.
        ''' </summary>
        Public Function SetProjectExtState(extName As String, key As String, value As String) As Boolean
            Dim safeValue As String = value
            If safeValue Is Nothing Then
                safeValue = String.Empty
            End If

            Dim raw As String = CallReaperBridge($"SET_PROJECT_EXT_STATE|{extName}|{key}|{safeValue}")
            If raw = "OK" Then
                Return True
            End If
            Return False
        End Function

        ''' <summary>
        ''' Retourne le nombre de sends Reaper existants pour la piste/catégorie courantes.
        ''' </summary>
        Public Function GetSendCount(trackIndex As Integer, Optional category As SendCategory = SendCategory.Sends) As Integer
            Dim rawCount As String = utility.CallReaperBridge($"GET_NUM_SENDS|{trackIndex}|{category}")

            If String.IsNullOrEmpty(rawCount) Then
                Return 0
            End If

            If rawCount.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return 0
            End If

            Dim sendCount As Integer = 0
            If Integer.TryParse(rawCount, sendCount) = False Then
                Return 0
            End If

            If sendCount < 0 Then
                Return 0
            End If

            Return sendCount
        End Function

        ''' <summary>Retourne les index (0-based) des pistes sélectionnées du projet. projectIndex = -1 pour le projet actif.</summary>
        Public Function GetSelectedTracks(Optional projectIndex As Integer = -1) As List(Of Integer)
            Dim result As New List(Of Integer)
            Dim answer As String = CallReaperBridge("GET_SELECTED_TRACKS|" & projectIndex.ToString(CultureInfo.InvariantCulture))
            If String.IsNullOrEmpty(answer) OrElse answer.StartsWith("ERROR:", StringComparison.Ordinal) Then
                Return result
            End If
            For Each s In answer.Split("|"c)
                Dim idx As Integer
                If Integer.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, idx) Then
                    result.Add(idx)
                End If
            Next
            Return result
        End Function



    End Module

#End Region

#Region "Monitors"
    Namespace Monitors


        Public Module ReaperEventPipeMonitor

            Private _thread As Thread
            Private _isRunning As Boolean = False

            Public Event On_HookPostCommand2(command As Integer, val As Integer, valHw As Integer, relMode As Integer)


            Public Sub Start()
                If _isRunning Then
                    Return
                End If

                _isRunning = True
                _thread = New Thread(AddressOf ListenLoop)
                _thread.IsBackground = True
                _thread.Name = "ReaperEventPipeMonitor"
                _thread.Start()
            End Sub

            Public Sub [Stop]()
                _isRunning = False

                Try
                    Using client As New IO.Pipes.NamedPipeClientStream(
                ".",
                "c24_reaper_events",
                IO.Pipes.PipeDirection.Out)

                        client.Connect(50)
                    End Using
                Catch
                End Try

                If _thread IsNot Nothing Then
                    If _thread.IsAlive Then
                        _thread.Join(200)
                    End If
                End If
            End Sub

            Private Sub ListenLoop()
                While _isRunning
                    Try
                        Using server As New IO.Pipes.NamedPipeServerStream(
                    "c24_reaper_events",
                    IO.Pipes.PipeDirection.In,
                    IO.Pipes.NamedPipeServerStream.MaxAllowedServerInstances,
                    IO.Pipes.PipeTransmissionMode.Byte)

                            server.WaitForConnection()

                            If _isRunning = False Then
                                Exit While
                            End If

                            Using reader As New IO.StreamReader(server)
                                While _isRunning AndAlso server.IsConnected
                                    Dim line As String = reader.ReadLine()

                                    If String.IsNullOrEmpty(line) Then
                                        Exit While
                                    End If

                                    DispatchEventLine(line)
                                End While
                            End Using
                        End Using
                    Catch
                        If _isRunning Then
                            Thread.Sleep(50)
                        End If
                    End Try
                End While
            End Sub

            Private Sub DispatchEventLine(line As String)
                Dim parts() As String = line.Split("|"c)

                If parts.Length < 4 Then
                    Return
                End If

                Dim command As Integer = 0
                Dim val As Integer = 0
                Dim valHw As Integer = 0
                Dim relMode As Integer = 0

                If Integer.TryParse(parts(0), Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, command) Then
                    If Integer.TryParse(parts(1), Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, val) Then
                        If Integer.TryParse(parts(2), Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, valHw) Then
                            If Integer.TryParse(parts(3), Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, relMode) Then
                                'Console.WriteLine("Command : " & command & "|" & val & "|" & valHw & "|" & relMode)
                                RaiseEvent On_HookPostCommand2(command, val, valHw, relMode)



                            End If
                        End If
                    End If
                End If
            End Sub

            Private Function GetCommandStatus(Command As Integer) As Boolean
                Return Reaper.utility.GetToggleCommandStateBool(Command)
            End Function


        End Module


        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER MOUSE CLICK MONITOR — clic souris dans la fenêtre Reaper
        ' ══════════════════════════════════════════════════════════════════════════

        Public Class ReaperMouseClickMonitor
            Implements IDisposable

            ' ── Win32 (Windows) ───────────────────────────────────────────────
            <Runtime.InteropServices.DllImport("user32.dll")>
            Private Shared Function GetAsyncKeyState(vKey As Integer) As Short
            End Function

            <Runtime.InteropServices.DllImport("user32.dll")>
            Private Shared Function GetCursorPos(ByRef lpPoint As ClickPoint) As Boolean
            End Function

            <Runtime.InteropServices.DllImport("user32.dll")>
            Private Shared Function WindowFromPoint(pt As ClickPoint) As IntPtr
            End Function

            <Runtime.InteropServices.DllImport("user32.dll")>
            Private Shared Function GetAncestor(hwnd As IntPtr, gaFlags As UInteger) As IntPtr
            End Function

            ' ── CoreGraphics (macOS) ──────────────────────────────────────────
            <Runtime.InteropServices.DllImport(
                "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")>
            Private Shared Function CGEventSourceButtonState(
                stateID As Integer, button As Integer) As Boolean
            End Function

            <Runtime.InteropServices.DllImport(
                "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")>
            Private Shared Function CGEventCreate(source As IntPtr) As IntPtr
            End Function

            <Runtime.InteropServices.DllImport(
                "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")>
            Private Shared Function CGEventGetLocation(event_ As IntPtr) As MacClickPoint
            End Function

            <Runtime.InteropServices.DllImport(
                "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")>
            Private Shared Sub CFRelease(cf As IntPtr)
            End Sub

            Private Const VK_LBUTTON As Integer = &H1
            Private Const GA_ROOT As UInteger = 2UI
            ' kCGEventSourceStateCombinedSessionState=0, kCGMouseButtonLeft=0
            Private Const CG_COMBINED_SESSION As Integer = 0
            Private Const CG_MOUSE_LEFT As Integer = 0

            <Runtime.InteropServices.StructLayout(
                Runtime.InteropServices.LayoutKind.Sequential)>
            Private Structure ClickPoint
                Public X As Integer
                Public Y As Integer
            End Structure

            <Runtime.InteropServices.StructLayout(
                Runtime.InteropServices.LayoutKind.Sequential)>
            Private Structure MacClickPoint
                Public X As Double
                Public Y As Double
            End Structure

            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 16
            Private _reaperHwnd As IntPtr = IntPtr.Zero
            Private _wasDown As Boolean = False
            Private _lastClickedTrackIndex As Integer = -1



            ''' <summary>
            ''' Déclenché sur le front montant du bouton gauche de la souris
            ''' lorsque le curseur se trouve dans la fenêtre principale Reaper.
            ''' Compatible Windows et macOS.
            ''' </summary>
            Public Event On_MouseClick(x As Integer, y As Integer)

            ''' <summary>
            ''' Intervalle de polling en millisecondes (défaut : 16 ms ≈ 60 Hz).
            ''' </summary>
            Public Property Interval As Integer
                Get
                    Return _interval
                End Get
                Set
                    _interval = Value
                End Set
            End Property

            ''' <summary>
            ''' True si le monitor de clic est actif.
            ''' </summary>
            Public ReadOnly Property IsRunning As Boolean
                Get
                    Return Threading.Volatile.Read(_isRunning)
                End Get
            End Property

            ''' <summary>
            ''' Index 0-based de la piste sous le curseur lors du dernier clic détecté.
            ''' -1 si aucun clic n'a encore été détecté ou si aucune piste n'était sous le curseur.
            ''' </summary>
            Public ReadOnly Property LastClickedTrackIndex As Integer
                Get
                    Return _lastClickedTrackIndex
                End Get
            End Property



            ''' <summary>
            ''' Démarre le monitor de clic souris.
            ''' </summary>
            Public Sub Start()
                If IsRunning Then Return
                If OperatingSystem.IsWindows() Then
                    _reaperHwnd = utility.GetMainHwnd()
                End If
                _wasDown = False
                _lastClickedTrackIndex = -1

                Threading.Volatile.Write(_isRunning, True)
                _thread = New Thread(AddressOf MonitorLoop) With {
                    .Name = "ReaperMouseClickMonitor",
                    .IsBackground = True,
                    .Priority = ThreadPriority.Normal
                }
                _thread.Start()
            End Sub

            ''' <summary>
            ''' Arrête le monitor de clic souris.
            ''' </summary>
            Public Sub [Stop]()
                Threading.Volatile.Write(_isRunning, False)
            End Sub

            Private Sub MonitorLoop()
                While Threading.Volatile.Read(_isRunning)
                    PollClick()
                    Thread.Sleep(_interval)
                End While
            End Sub

            Private Sub PollClick()
                Dim isDown As Boolean = False



                If OperatingSystem.IsWindows() Then
                    isDown = (GetAsyncKeyState(VK_LBUTTON) And &H8000S) <> 0
                ElseIf OperatingSystem.IsMacOS() Then
                    isDown = CGEventSourceButtonState(CG_COMBINED_SESSION, CG_MOUSE_LEFT)
                End If

                If isDown AndAlso Not _wasDown Then
                    Dim x As Integer = -1
                    Dim y As Integer = -1

                    If OperatingSystem.IsWindows() Then
                        Dim pt As ClickPoint
                        If GetCursorPos(pt) AndAlso IsInsideReaperWindowWin(pt) Then
                            x = pt.X
                            y = pt.Y
                        End If
                    ElseIf OperatingSystem.IsMacOS() Then
                        Dim ev = CGEventCreate(IntPtr.Zero)
                        If ev <> IntPtr.Zero Then
                            Try
                                Dim pt = CGEventGetLocation(ev)
                                x = CInt(pt.X)
                                y = CInt(pt.Y)
                            Finally
                                CFRelease(ev)
                            End Try
                        End If
                    End If

                    If x >= 0 AndAlso y >= 0 Then
                        ' ── Mémoriser la piste cliquée et le changement d'état projet ─
                        Dim trkInfo = utility.GetTrackFromPoint(x, y)
                        _lastClickedTrackIndex = trkInfo.trackIndex

                        RaiseEvent On_MouseClick(x, y)
                    End If
                End If

                _wasDown = isDown
            End Sub

            ''' <summary>Vérifie si le point est dans la fenêtre Reaper (Windows uniquement).</summary>
            Private Function IsInsideReaperWindowWin(pt As ClickPoint) As Boolean
                If _reaperHwnd = IntPtr.Zero Then
                    _reaperHwnd = utility.GetMainHwnd()
                End If
                If _reaperHwnd = IntPtr.Zero Then Return False
                Dim windowUnderCursor = WindowFromPoint(pt)
                If windowUnderCursor = IntPtr.Zero Then Return False
                Dim rootWindow = GetAncestor(windowUnderCursor, GA_ROOT)
                Return rootWindow = _reaperHwnd
            End Function

            Protected Overridable Sub Dispose(disposing As Boolean)
                If Not _disposed Then
                    If disposing Then
                        [Stop]()
                    End If
                    _disposed = True
                End If
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                Dispose(disposing:=True)
                GC.SuppressFinalize(Me)
            End Sub

        End Class

        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER TRACK SENDS MONITOR — tous les sends d'une seule piste
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Surveille vol/pan/mute/sendMode de N envois d'une piste fixe.
        ''' Symétrique de <see cref="ReaperSendMonitor"/> :
        ''' ici la piste est fixe et les <c>sendIndex</c> varient.
        ''' Une seule requête <c>GET_SEND_ALL_INFO</c> par envoi et par cycle.
        ''' </summary>
        Public Class ReaperTrackSendsMonitor
            Implements IDisposable

            Private ReadOnly _pipe As New ReaperPollPipe()
            Private ReadOnly _trackIndex As Integer
            Private ReadOnly _category As Integer
            Private _stateChanged As Boolean = True
            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 50
            Private ReadOnly _lock As New Object()

            ' ── Clé = sendIndex, Valeur = suspendu ────────────────────────────
            Private ReadOnly _sends As New Dictionary(Of Integer, Boolean)

            ' ── Caches par sendIndex ──────────────────────────────────────────
            Private ReadOnly _volCache As New Dictionary(Of Integer, Double)
            Private ReadOnly _panCache As New Dictionary(Of Integer, Double)
            Private ReadOnly _muteCache As New Dictionary(Of Integer, Boolean)
            Private ReadOnly _sendModeCache As New Dictionary(Of Integer, Integer)

            ' ── Marque les sends dont le cache n'a encore jamais été peuplé ───
            ' Évite de lever un événement de "changement" lors de la toute
            ' première lecture d'un send (WatchSend), qui n'est qu'une
            ' initialisation et non une modification réelle.
            Private ReadOnly _seeded As New HashSet(Of Integer)

            ' ── Événements ────────────────────────────────────────────────────
            Public Event On_Connected()
            Public Event On_Disconnected()
            Public Event On_VolChange(sendIndex As Integer, vol As Double)
            Public Event On_PanChange(sendIndex As Integer, pan As Double)
            Public Event On_MuteChange(sendIndex As Integer, mute As Boolean)
            Public Event On_SendModeChange(sendIndex As Integer, sendMode As Integer)

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Initialise le moniteur pour une piste fixe.
            ''' </summary>
            ''' <param name="trackIndex">Index 0-based de la piste surveillée.</param>
            ''' <param name="category"><see cref="SendCategory"/> (défaut : Sends).</param>
            Public Sub New(trackIndex As Integer,
                   Optional category As SendCategory = SendCategory.Sends)
                _trackIndex = trackIndex
                _category = CInt(category)
            End Sub

            ' ── Propriétés ────────────────────────────────────────────────────

            ''' <summary>Index 0-based de la piste surveillée.</summary>
            Public ReadOnly Property TrackIndex As Integer
                Get
                    Return _trackIndex
                End Get
            End Property

            ''' <summary>Catégorie des envois.</summary>
            Public ReadOnly Property Category As SendCategory
                Get
                    Return CType(_category, SendCategory)
                End Get
            End Property

            ''' <summary>Intervalle de polling en ms (défaut : 50).</summary>
            Public Property Interval As Integer
                Get
                    Return _interval
                End Get
                Set
                    _interval = Value
                End Set
            End Property

            ''' <summary>True si le thread de monitoring tourne.</summary>
            Public ReadOnly Property IsRunning As Boolean
                Get
                    Return Threading.Volatile.Read(_isRunning)
                End Get
            End Property

            ' ── Gestion des envois surveillés ─────────────────────────────────

            ''' <summary>Ajoute un envoi à surveiller.</summary>
            Public Sub WatchSend(sendIndex As Integer)
                SyncLock _lock
                    If Not _sends.ContainsKey(sendIndex) Then
                        _sends(sendIndex) = False
                        _seeded.Remove(sendIndex)
                    End If
                End SyncLock
            End Sub

            ''' <summary>Retire un envoi de la surveillance.</summary>
            Public Sub UnwatchSend(sendIndex As Integer)
                SyncLock _lock
                    _sends.Remove(sendIndex)
                    _volCache.Remove(sendIndex)
                    _panCache.Remove(sendIndex)
                    _muteCache.Remove(sendIndex)
                    _sendModeCache.Remove(sendIndex)
                    _seeded.Remove(sendIndex)
                End SyncLock
            End Sub

            ''' <summary>Vide tous les envois surveillés.</summary>
            Public Sub ClearWatched()
                SyncLock _lock
                    _sends.Clear()
                    _volCache.Clear()
                    _panCache.Clear()
                    _muteCache.Clear()
                    _sendModeCache.Clear()
                    _seeded.Clear()
                End SyncLock
            End Sub

            ''' <summary>Suspend le polling pour un envoi (anti-feedback écriture).</summary>
            Public Sub SuspendSend(sendIndex As Integer)
                SyncLock _lock
                    If _sends.ContainsKey(sendIndex) Then
                        _sends(sendIndex) = True
                    End If
                End SyncLock
            End Sub

            ''' <summary>
            ''' Réactive le polling pour un envoi et invalide son cache.
            ''' Le prochain poll re-seedera silencieusement la valeur courante
            ''' <b>sans lever d'événement</b>, car cette valeur vient d'être écrite
            ''' volontairement par le contrôleur (pas une modification externe).
            ''' </summary>
            Public Sub ResumeSend(sendIndex As Integer)
                SyncLock _lock
                    If _sends.ContainsKey(sendIndex) Then
                        _sends(sendIndex) = False
                        _volCache.Remove(sendIndex)
                        _panCache.Remove(sendIndex)
                        _muteCache.Remove(sendIndex)
                        _sendModeCache.Remove(sendIndex)
                        _seeded.Remove(sendIndex)
                    End If
                End SyncLock
            End Sub

            ' ── Cycle de vie ──────────────────────────────────────────────────

            ''' <summary>Démarre le thread de monitoring.</summary>
            Public Sub Start()
                If IsRunning Then Return
                AddHandler ReaperPollHub.Instance.On_ProjectStateChange,
            AddressOf OnProjectStateChange
                Threading.Volatile.Write(_isRunning, True)
                _thread = New Thread(AddressOf MonitorLoop) With {
            .Name = $"TrackSendsMonitor_T{_trackIndex}_Cat{_category}",
            .IsBackground = True,
            .Priority = ThreadPriority.Normal
        }
                _thread.Start()
            End Sub

            ''' <summary>Arrête le thread de monitoring.</summary>
            Public Sub [Stop]()
                RemoveHandler ReaperPollHub.Instance.On_ProjectStateChange,
            AddressOf OnProjectStateChange
                Threading.Volatile.Write(_isRunning, False)
                _pipe.Disconnect()
            End Sub

            ' ── Boucle interne ────────────────────────────────────────────────

            Private Sub MonitorLoop()
                RaiseEvent On_Connected()
                While Threading.Volatile.Read(_isRunning)
                    If Threading.Volatile.Read(_stateChanged) Then
                        Threading.Volatile.Write(_stateChanged, False)
                        PollSends()
                    End If
                    Thread.Sleep(_interval)
                End While
                RaiseEvent On_Disconnected()
            End Sub

            Private Sub OnProjectStateChange(stateCount As Integer)
                Threading.Volatile.Write(_stateChanged, True)
            End Sub

            Private Sub PollSends()
                Dim snapshot As (sendIndex As Integer, isSuspended As Boolean)()
                SyncLock _lock
                    Dim tmp As New List(Of (Integer, Boolean))
                    For Each kvp In _sends
                        tmp.Add((kvp.Key, kvp.Value))
                    Next
                    snapshot = tmp.ToArray()
                End SyncLock

                Dim ic = CultureInfo.InvariantCulture

                For Each entry In snapshot
                    Dim sIdx = entry.sendIndex
                    If entry.isSuspended Then Continue For

                    ' ── Vol + Pan + Mute + SendMode en 1 seule requête ────────
                    ' GET_SEND_ALL_INFO → idx 0=vol  1=pan  3=mute  6=sendMode
                    Dim raw = _pipe.Request(
                $"GET_SEND_ALL_INFO|{_trackIndex}|{_category}|{sIdx}")
                    If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Continue For

                    Dim p = raw.Split("|"c)
                    If p.Length < 7 Then Continue For

                    Dim newVol As Double = 1
                    Dim newPan As Double = 0
                    Dim newMute As Boolean = False
                    Dim newMode As Integer = 0
                    Double.TryParse(p(0), NumberStyles.Any, ic, newVol)
                    Double.TryParse(p(1), NumberStyles.Any, ic, newPan)
                    newMute = p(3) = "1"
                    Integer.TryParse(p(6), newMode)

                    Dim volChg As Boolean, panChg As Boolean
                    Dim muteChg As Boolean, modeChg As Boolean
                    Dim wasAlreadySeeded As Boolean

                    SyncLock _lock
                        wasAlreadySeeded = _seeded.Contains(sIdx)

                        If Not wasAlreadySeeded Then
                            ' ── Première observation : seed silencieux, pas d'event ───
                            _volCache(sIdx) = newVol
                            _panCache(sIdx) = newPan
                            _muteCache(sIdx) = newMute
                            _sendModeCache(sIdx) = newMode
                            _seeded.Add(sIdx)
                            volChg = False
                            panChg = False
                            muteChg = False
                            modeChg = False
                        Else
                            Dim pv As Double = Double.MinValue
                            volChg = Not _volCache.TryGetValue(sIdx, pv) OrElse pv <> newVol
                            If volChg Then _volCache(sIdx) = newVol
                            panChg = Not _panCache.TryGetValue(sIdx, pv) OrElse pv <> newPan
                            If panChg Then _panCache(sIdx) = newPan
                            Dim pm As Boolean = False
                            muteChg = Not _muteCache.TryGetValue(sIdx, pm) OrElse pm <> newMute
                            If muteChg Then _muteCache(sIdx) = newMute
                            Dim pmode As Integer = -1
                            modeChg = Not _sendModeCache.TryGetValue(sIdx, pmode) OrElse pmode <> newMode
                            If modeChg Then _sendModeCache(sIdx) = newMode
                        End If
                    End SyncLock

                    If volChg Then
                        RaiseEvent On_VolChange(sIdx, newVol)
                        'Console.WriteLine("SendId:" & sIdx & "|" & newVol)
                    End If


                    If panChg Then RaiseEvent On_PanChange(sIdx, newPan)
                    If muteChg Then RaiseEvent On_MuteChange(sIdx, newMute)
                    If modeChg Then RaiseEvent On_SendModeChange(sIdx, newMode)
                Next
            End Sub

            ' ── IDisposable ───────────────────────────────────────────────────

            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
                    _pipe.Dispose()
                    _disposed = True
                End If
            End Sub

        End Class

        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER SEND MONITOR — surveillance vol/pan/mute d'un envoi sur N pistes
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Surveille les changements de vol, pan et mute de l'envoi
        ''' <see cref="TracksSendIndex"/> pour une collection de pistes.
        ''' <br/>
        ''' Polling <b>gated</b> sur <c>ProjectStateChangeCount</c> — aucune
        ''' requête superflue quand le projet est statique.
        ''' <br/>
        ''' Sémantique de suspension identique à
        ''' <see cref="ReaperTrackMonitor"/> : pendant la suspension le cache
        ''' est NON mis à jour ; à la reprise il est effacé → resync forcé.
        ''' </summary>
        Public Class ReaperSendMonitor
            Implements IDisposable

            ' ── Pipe propre — aucune contention avec les autres monitors ──────
            Private ReadOnly _pipe As New ReaperPollPipe()

            Private ReadOnly _TracksSendIndex As Dictionary(Of Integer, Integer?)
            Private ReadOnly _category As Integer
            Private _stateChanged As Boolean = True
            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 50
            Private ReadOnly _lock As New Object()
            Private ReadOnly _tracks As New Dictionary(Of Integer, Boolean)
            Private ReadOnly _volCache As New Dictionary(Of Integer, Double)
            Private ReadOnly _panCache As New Dictionary(Of Integer, Double)
            Private ReadOnly _muteCache As New Dictionary(Of Integer, Boolean)
            Private ReadOnly _sendModeCache As New Dictionary(Of Integer, Integer)
            Private _SendLetter As String
            Public Event On_Connected()
            Public Event On_Disconnected()
            Public Event On_VolChange(trackIndex As Integer, vol As Double)
            Public Event On_PanChange(trackIndex As Integer, pan As Double)
            Public Event On_MuteChange(trackIndex As Integer, mute As Boolean)
            Public Event On_SendModeChange(trackIndex As Integer, sendMode As Integer)
            Public Event On_PlayStateChange(playState As Integer)

            Public Sub New(TracksSendIndex As Dictionary(Of Integer, Integer?), SendLetter As String,
                           Optional category As SendCategory = SendCategory.Sends)
                _SendLetter = SendLetter
                _TracksSendIndex = TracksSendIndex
                _category = CInt(category)
            End Sub

            Public ReadOnly Property SendLetter As String
                Get
                    Return _SendLetter
                End Get
            End Property

            Public ReadOnly Property TracksSendIndex As Dictionary(Of Integer, Integer?)
                Get
                    Return _TracksSendIndex
                End Get
            End Property

            Public ReadOnly Property Category As SendCategory
                Get
                    Return CType(_category, SendCategory)
                End Get
            End Property

            Public Property Interval As Integer
                Get
                    Return _interval
                End Get
                Set
                    _interval = Value
                End Set
            End Property

            Public ReadOnly Property IsRunning As Boolean
                Get
                    Return Threading.Volatile.Read(_isRunning)
                End Get
            End Property

            Public Sub WatchTrack(trackIndex As Integer)
                SyncLock _lock
                    If Not _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = False
                    End If
                End SyncLock
            End Sub

            Public Sub WatchTracks(tracks As IEnumerable(Of ReaperTrack))
                For Each trk In tracks
                    WatchTrack(trk.TrackIndex)
                Next
            End Sub

            Public Sub UnwatchTrack(trackIndex As Integer)
                SyncLock _lock
                    _tracks.Remove(trackIndex)
                    _volCache.Remove(trackIndex)
                    _panCache.Remove(trackIndex)
                    _muteCache.Remove(trackIndex)
                    _sendModeCache.Remove(trackIndex)
                End SyncLock
            End Sub

            Public Sub ClearWatched()
                SyncLock _lock
                    _tracks.Clear()
                    _volCache.Clear()
                    _panCache.Clear()
                    _muteCache.Clear()
                    _sendModeCache.Clear()
                End SyncLock
            End Sub

            Public Sub SuspendTrack(trackIndex As Integer)
                SyncLock _lock
                    If _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = True
                    End If
                End SyncLock
            End Sub

            Public Sub ResumeTrack(trackIndex As Integer)
                SyncLock _lock
                    If _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = False

                    End If
                End SyncLock
            End Sub

            Public Function IsTrackSuspended(trackIndex As Integer) As Boolean
                SyncLock _lock
                    Dim suspended As Boolean = False
                    _tracks.TryGetValue(trackIndex, suspended)
                    Return suspended
                End SyncLock
            End Function

            Public Sub Start()
                If IsRunning Then Return
                AddHandler ReaperPollHub.Instance.On_ProjectStateChange,
                    AddressOf OnProjectStateChange
                Threading.Volatile.Write(_isRunning, True)
                _thread = New Thread(AddressOf MonitorLoop) With {
                    .Name = $"SendMonitor_S{SendLetter}_Cat{_category}",
                    .IsBackground = True,
                    .Priority = ThreadPriority.Normal
                }
                _thread.Start()
            End Sub

            Public Sub [Stop]()
                RemoveHandler ReaperPollHub.Instance.On_ProjectStateChange,
                    AddressOf OnProjectStateChange
                Threading.Volatile.Write(_isRunning, False)
                _pipe.Disconnect()
            End Sub

            Private Sub MonitorLoop()
                RaiseEvent On_Connected()
                While Threading.Volatile.Read(_isRunning)
                    If Threading.Volatile.Read(_stateChanged) Then
                        Threading.Volatile.Write(_stateChanged, False)
                        PollSends()
                    End If
                    Thread.Sleep(_interval)
                End While
                RaiseEvent On_Disconnected()
            End Sub

            Private Sub OnProjectStateChange(stateCount As Integer)
                Threading.Volatile.Write(_stateChanged, True)
            End Sub

            Private Sub PollSends()
                Dim snapshot As (trackIndex As Integer, isSuspended As Boolean)()
                SyncLock _lock
                    Dim tmp As New List(Of (Integer, Boolean))
                    For Each kvp In _tracks
                        tmp.Add((kvp.Key, kvp.Value))
                    Next
                    snapshot = tmp.ToArray()
                End SyncLock

                Dim ic = CultureInfo.InvariantCulture

                For Each entry In snapshot
                    Dim tIdx = entry.trackIndex
                    If _TracksSendIndex(tIdx) Is Nothing Then Continue For
                    ' ── Vol + Pan + Mute + SendMode en 1 seule requête ────────
                    ' GET_SEND_ALL_INFO → idx 0=vol  1=pan  3=mute  6=sendMode
                    Dim raw = _pipe.Request($"GET_SEND_ALL_INFO|{tIdx}|{_category}|{_TracksSendIndex(tIdx).Value}")
                    If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Continue For

                    Dim p = raw.Split("|"c)
                    If p.Length < 7 Then Continue For

                    Dim newVol As Double = 1
                    Dim newPan As Double = 0
                    Dim newMute As Boolean = False
                    Dim newMode As Integer = 0
                    Double.TryParse(p(0), NumberStyles.Any, ic, newVol)
                    Double.TryParse(p(1), NumberStyles.Any, ic, newPan)
                    newMute = p(3) = "1"
                    Integer.TryParse(p(6), newMode)

                    If entry.isSuspended Then Continue For

                    Dim volChg As Boolean, panChg As Boolean
                    Dim muteChg As Boolean, modeChg As Boolean
                    SyncLock _lock
                        Dim pv As Double = Double.MinValue
                        volChg = Not _volCache.TryGetValue(tIdx, pv) OrElse pv <> newVol
                        If volChg Then _volCache(tIdx) = newVol
                        panChg = Not _panCache.TryGetValue(tIdx, pv) OrElse pv <> newPan
                        If panChg Then _panCache(tIdx) = newPan
                        Dim pm As Boolean = False
                        muteChg = Not _muteCache.TryGetValue(tIdx, pm) OrElse pm <> newMute
                        If muteChg Then _muteCache(tIdx) = newMute
                        Dim pmode As Integer = -1
                        modeChg = Not _sendModeCache.TryGetValue(tIdx, pmode) OrElse pmode <> newMode
                        If modeChg Then _sendModeCache(tIdx) = newMode
                    End SyncLock

                    If volChg Then
                        RaiseEvent On_VolChange(tIdx, newVol)
                        'Console.WriteLine("TrkId:" & tIdx & "|" & newVol)
                    End If

                    If panChg Then RaiseEvent On_PanChange(tIdx, newPan)
                    If muteChg Then RaiseEvent On_MuteChange(tIdx, newMute)
                    If modeChg Then RaiseEvent On_SendModeChange(tIdx, newMode)
                Next
            End Sub
            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
                    _pipe.Dispose()
                    _disposed = True
                End If
            End Sub

        End Class
        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER PROJECT MONITOR — surveillance du nombre de pistes + Sends
        ' ══════════════════════════════════════════════════════════════════════════

        Public Class ReaperProjectMonitor
            Implements IDisposable

            ' ── Pipe propre — aucune contention avec les autres monitors ──────────
            Private ReadOnly _pipe As New ReaperPollPipe()

            Private _stateChanged As Boolean = True
            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 1000
            Private ReadOnly _lock As New Object()
            Private _lastPollUtc As DateTime = DateTime.MinValue
            Private ReadOnly _minPollInterval As TimeSpan = TimeSpan.FromSeconds(5)
            Private _SelectedItemsManager As Reaper.Managers.SelectedItemsManager
            ' ── Historique des GUIDs par chemin de projet ──────────────────────────
            ' Clé : chemin .rpp (ou String.Empty pour projet sans nom)
            ' Valeur : dernière liste de GUIDs connue POUR CE PROJET
            Private ReadOnly _guidHistory As New Dictionary(Of String, List(Of String))()

            ' ── Historique des "GUIDs" de sends par piste ─────────────────────────
            ' Clé : "projectKey|trackIndex"
            ' Valeur : liste de GUIDs des pistes destination des sends (catégorie Sends)
            ' Les sends n'ayant pas de GUID natif dans Reaper, on utilise le GUID de
            ' la piste destination de chaque send comme identité stable, indépendante
            ' de l'ordre/index des sends.
            Private ReadOnly _sendGuidHistory As New Dictionary(Of String, List(Of String))()
            ' ── Historique des GUIDs d'enveloppes par piste ─────────────────────────
            ' Clé : "projectKey|trackIndex"
            ' Valeur : liste ordonnée des GUIDs d'enveloppes de la piste
            Private ReadOnly _envGuidHistory As New Dictionary(Of String, List(Of String))()
            Private _SelectionActive As Boolean = False
            Private _SelectionStartTime As Double = -1
            Private _SelectionEndTime As Double = -1
            'Private _activeProjectKey As String = String.Empty
            Private _ReaperSession As ReaperSession
            Public Event On_Connected()
            Public Event On_Disconnected()
            ''' <summary>
            ''' Levé uniquement quand les GUIDs du projet actif ont vraiment changé
            ''' par rapport à l'historique connu <b>pour ce même projet</b>.
            ''' </summary>
            Public Event On_TrackGuidsChanged(guids As IReadOnlyList(Of String))
            Public Event On_ProjectDirty(State As Boolean)

            ''' <summary>
            ''' Levé quand les sends d'une piste ont changé (ajout, suppression,
            ''' ou re-routage vers une piste destination différente) par rapport
            ''' à l'historique connu <b>pour cette piste, dans ce projet</b>.
            ''' </summary>
            ''' <param name="TrackId">Index 0-based de la piste dont les sends ont changé.</param>
            Public Event On_SendGuidsChanged(TrackId As Integer)
            ''' <summary>
            ''' Levé quand les enveloppes d'une piste ont changé (ajout/suppression/
            ''' remplacement/réordonnancement) par rapport à l'historique connu
            ''' pour cette piste dans ce projet.
            ''' </summary>
            ''' <param name="TrackId">Index 0-based de la piste dont les enveloppes ont changé.</param>
            Public Event On_EnvGuidsChanged(TrackId As Integer)
            Public Event On_SelectionChange(IsSelectionActive As Boolean)
            Public Event SomeSelectedItemLocked(Status As Boolean)
            Public Event On_ItemSelected()


            Public Property Interval As Integer
                Get
                    Return _interval
                End Get
                Set
                    _interval = Value
                End Set
            End Property

            Public ReadOnly Property IsRunning As Boolean
                Get
                    Return Threading.Volatile.Read(_isRunning)
                End Get
            End Property
            Public ReadOnly Property ReaperSession As ReaperSession
                Get
                    Return _ReaperSession
                End Get

            End Property

            Public Property SelectionStartTime As Double
                Get
                    Return _SelectionStartTime
                End Get
                Set(value As Double)
                    _SelectionStartTime = value
                End Set
            End Property
            Public Property SelectionEndTime As Double
                Get
                    Return _SelectionEndTime
                End Get
                Set(value As Double)
                    _SelectionEndTime = value
                End Set
            End Property
            Public Property SelectionActive As Boolean
                Get
                    Return _SelectionActive
                End Get
                Set(value As Boolean)
                    _SelectionActive = value
                End Set
            End Property

            ' ── API publique ────────────────────────────────────────────────────────

            ''' <summary>
            ''' Initialise l'historique des GUIDs pour un projet donné
            ''' <b>sans lever <see cref="On_TrackGuidsChanged"/></b>.
            ''' À appeler après le chargement initial d'un projet ou lors de son
            ''' activation depuis le cache, pour éviter un faux positif.
            ''' </summary>
            Public Sub SeedGuids(projectFilePath As String, guids As IReadOnlyList(Of String))
                Dim key = If(projectFilePath, String.Empty)
                SyncLock _lock
                    Dim copy As New List(Of String)(guids.Count)
                    For Each g In guids
                        copy.Add(g)
                    Next
                    _guidHistory(key) = copy
                End SyncLock
            End Sub

            ''' <summary>
            ''' Retourne une copie des derniers GUIDs connus pour un projet donné.
            ''' Retourne une liste vide si le projet n'a pas encore d'historique.
            ''' </summary>
            Public Function GetKnownGuids(projectFilePath As String) As IReadOnlyList(Of String)
                Dim key = If(projectFilePath, String.Empty)
                SyncLock _lock
                    Dim hist As List(Of String) = Nothing
                    If _guidHistory.TryGetValue(key, hist) Then
                        Return hist.ToList()
                    End If
                    Return Array.Empty(Of String)()
                End SyncLock
            End Function

            ''' <summary>
            ''' Initialise l'historique des "GUIDs" de sends d'une piste donnée
            ''' <b>sans lever <see cref="On_SendGuidsChanged"/></b>.
            ''' À appeler après le chargement initial d'un projet pour éviter un faux positif.
            ''' </summary>
            Public Sub SeedSendGuids(projectFilePath As String, trackIndex As Integer, sendGuids As IReadOnlyList(Of String))
                Dim key = BuildSendKey(projectFilePath, trackIndex)
                SyncLock _lock
                    Dim copy As New List(Of String)(sendGuids.Count)
                    For Each g In sendGuids
                        copy.Add(g)
                    Next
                    _sendGuidHistory(key) = copy
                End SyncLock
            End Sub

            ''' <summary>
            ''' Retourne une copie des derniers "GUIDs" de sends connus pour une piste donnée.
            ''' Retourne une liste vide si la piste n'a pas encore d'historique.
            ''' </summary>
            Public Function GetKnownSendGuids(projectFilePath As String, trackIndex As Integer) As IReadOnlyList(Of String)
                Dim key = BuildSendKey(projectFilePath, trackIndex)
                SyncLock _lock
                    Dim hist As List(Of String) = Nothing
                    If _sendGuidHistory.TryGetValue(key, hist) Then
                        Return hist.ToList()
                    End If
                    Return Array.Empty(Of String)()
                End SyncLock
            End Function

            ''' <summary>
            ''' Supprime l'historique d'un projet fermé pour libérer la mémoire.
            ''' Retire également tout l'historique des sends associé à ce projet.
            ''' </summary>
            Public Sub RemoveProject(projectFilePath As String)
                Dim key = If(projectFilePath, String.Empty)
                SyncLock _lock
                    _guidHistory.Remove(key)

                    Dim prefix = key & "|"

                    Dim toRemoveSend As New List(Of String)
                    For Each sendKey In _sendGuidHistory.Keys
                        If sendKey.StartsWith(prefix, StringComparison.Ordinal) Then
                            toRemoveSend.Add(sendKey)
                        End If
                    Next
                    For Each sendKey In toRemoveSend
                        _sendGuidHistory.Remove(sendKey)
                    Next

                    Dim toRemoveEnv As New List(Of String)
                    For Each envKey In _envGuidHistory.Keys
                        If envKey.StartsWith(prefix, StringComparison.Ordinal) Then
                            toRemoveEnv.Add(envKey)
                        End If
                    Next
                    For Each envKey In toRemoveEnv
                        _envGuidHistory.Remove(envKey)
                    Next
                End SyncLock
            End Sub

            Sub New(ReaperSession As ReaperSession, SelectedItemsManager As Reaper.Managers.SelectedItemsManager)
                _ReaperSession = ReaperSession
                _SelectedItemsManager = SelectedItemsManager
            End Sub
            ' ── Cycle de vie ────────────────────────────────────────────────────────

            Public Sub Start()
                If IsRunning Then Return
                AddHandler ReaperPollHub.Instance.On_ProjectStateChange,
            AddressOf OnProjectStateChange
                Threading.Volatile.Write(_isRunning, True)
                _thread = New Thread(AddressOf MonitorLoop) With {
            .Name = "ReaperProjectMonitor",
            .IsBackground = True,
            .Priority = ThreadPriority.Normal
        }
                _thread.Start()
            End Sub

            Public Sub [Stop]()
                RemoveHandler ReaperPollHub.Instance.On_ProjectStateChange,
            AddressOf OnProjectStateChange
                Threading.Volatile.Write(_isRunning, False)
                _pipe.Disconnect()
            End Sub

            Private Sub MonitorLoop()
                RaiseEvent On_Connected()
                While Threading.Volatile.Read(_isRunning)
                    If Threading.Volatile.Read(_stateChanged) Then
                        Dim now = DateTime.UtcNow
                        If now - _lastPollUtc >= _minPollInterval Then
                            Threading.Volatile.Write(_stateChanged, False)
                            _lastPollUtc = now
                            PollGuids()
                        End If
                    End If
                    Thread.Sleep(_interval)
                End While
                RaiseEvent On_Disconnected()
            End Sub

            Private Sub OnProjectStateChange(stateCount As Integer)
                Threading.Volatile.Write(_stateChanged, True)
            End Sub

            ' ── Helpers clé de dictionnaire ──────────────────────────────────────
            ''' <summary>
            ''' Retourne la signature des enveloppes d'une piste sous forme de liste
            ''' ordonnée de GUIDs d'enveloppes.
            ''' </summary>
            Private Function GetTrackEnvelopeGuids(trackIndex As Integer) As List(Of String)
                Dim result As New List(Of String)

                Dim rawCount = _pipe.Request($"COUNT_TRACK_ENVELOPES|{trackIndex}")
                If String.IsNullOrEmpty(rawCount) Then
                    Return result
                End If

                If rawCount.StartsWith("ERROR", StringComparison.Ordinal) Then
                    Return result
                End If

                Dim envCount As Integer = 0
                If Integer.TryParse(rawCount, envCount) = False Then
                    Return result
                End If

                If envCount <= 0 Then
                    Return result
                End If

                For e = 0 To envCount - 1
                    Dim rawGuid = _pipe.Request($"GET_ENVELOPE_GUID|{trackIndex}|{e}")
                    If String.IsNullOrEmpty(rawGuid) OrElse rawGuid.StartsWith("ERROR", StringComparison.Ordinal) Then
                        result.Add("err")
                    Else
                        result.Add(rawGuid)
                    End If
                Next

                Return result
            End Function
            Private Function BuildSendKey(projectFilePath As String, trackIndex As Integer) As String
                Return If(projectFilePath, String.Empty) & "|" & trackIndex.ToString(CultureInfo.InvariantCulture)
            End Function

            ' ── Lecture des "GUIDs" de sends d'une piste ─────────────────────────

            ''' <summary>
            ''' Construit la signature des sends (catégorie Sends) d'une piste sous
            ''' forme de liste de GUIDs des pistes destination, dans l'ordre des index
            ''' de send. Utilisé comme proxy de "GUID de send" car Reaper n'expose
            ''' aucun identifiant stable natif pour les sends.
            ''' </summary>
            Private Function GetTrackSendGuids(trackIndex As Integer) As List(Of String)
                Dim result As New List(Of String)

                Dim rawCount = _pipe.Request($"GET_NUM_SENDS|{trackIndex}|{CInt(SendCategory.Sends)}")
                If String.IsNullOrEmpty(rawCount) OrElse rawCount.StartsWith("ERROR", StringComparison.Ordinal) Then Return result
                Dim sendCount As Integer = 0
                If Not Integer.TryParse(rawCount, sendCount) OrElse sendCount <= 0 Then Return result

                For s = 0 To sendCount - 1
                    Dim rawInfo = _pipe.Request($"GET_SEND_ALL_INFO|{trackIndex}|{CInt(SendCategory.Sends)}|{s}")
                    If String.IsNullOrEmpty(rawInfo) OrElse rawInfo.StartsWith("ERROR", StringComparison.Ordinal) Then Continue For
                    Dim p = rawInfo.Split("|"c)
                    If p.Length < 11 Then Continue For

                    Dim destTrackIndex As Integer = -1
                    Integer.TryParse(p(10), destTrackIndex)
                    If destTrackIndex < 0 Then
                        result.Add("HW")   ' sortie hardware — pas de piste destination
                        Continue For
                    End If

                    Dim destGuid = _pipe.Request($"GET_TRACK_GUID|{destTrackIndex}")
                    If String.IsNullOrEmpty(destGuid) OrElse destGuid.StartsWith("ERROR", StringComparison.Ordinal) Then
                        destGuid = "err"
                    End If
                    result.Add(destGuid)
                Next

                Return result
            End Function

            ' ── Polling et comparaison par projet ───────────────────────────────────

            Private Sub PollGuids()
                Thread.Sleep(500)

                Dim raw = _pipe.Request("IS_PROJECT_DIRTY")
                RaiseEvent On_ProjectDirty(If(raw = "1", True, False))
                ' ── 1. Clé du projet actif (snapshot thread-safe) ─────────────────
                Dim currentKey As String
                SyncLock _lock
                    currentKey = _ReaperSession.Project.FilePath
                End SyncLock

                ' ── 2. Nombre de pistes ───────────────────────────────────────────
                Dim rawCount = _pipe.Request("COUNT_TRACKS")
                If String.IsNullOrEmpty(rawCount) OrElse rawCount.StartsWith("ERROR", StringComparison.Ordinal) Then Return

                Dim newCount As Integer = -1
                If Not Integer.TryParse(rawCount, newCount) Then Return
                If newCount < 0 Then Return

                ' ── 3. GUIDs depuis Reaper ────────────────────────────────────────
                Dim newGuids As New List(Of String)(newCount)
                For i = 0 To newCount - 1
                    Dim rawGuid = _pipe.Request($"GET_TRACK_GUID|{i}")
                    If String.IsNullOrEmpty(rawGuid) OrElse rawGuid.StartsWith("ERROR", StringComparison.Ordinal) Then Return
                    newGuids.Add(rawGuid)
                Next

                ' ── 4. Comparaison contre l'historique du projet actif ────────────
                Dim changed As Boolean = False
                SyncLock _lock
                    Dim knownGuids As List(Of String) = Nothing
                    If Not _guidHistory.TryGetValue(currentKey, knownGuids) Then
                        ' Aucun historique pour ce projet → première observation, on seed sans event
                        _guidHistory(currentKey) = newGuids
                        Return
                    End If

                    If newCount <> knownGuids.Count Then
                        changed = True
                    Else
                        For i = 0 To newCount - 1
                            If newGuids(i) <> knownGuids(i) Then
                                changed = True
                                Exit For
                            End If
                        Next
                    End If

                    If changed Then
                        _guidHistory(currentKey) = newGuids
                    End If
                End SyncLock

                If changed Then
                    RaiseEvent On_TrackGuidsChanged(newGuids)
                End If

                ' ── 5. Surveillance des sends par piste ───────────────────────────
                For i = 0 To newCount - 1
                    Dim newSendGuids = GetTrackSendGuids(i)
                    Dim sendKey = BuildSendKey(currentKey, i)

                    Dim sendChanged As Boolean = False
                    Dim isFirstObservation As Boolean = False

                    SyncLock _lock
                        Dim knownSendGuids As List(Of String) = Nothing
                        If Not _sendGuidHistory.TryGetValue(sendKey, knownSendGuids) Then
                            ' Première observation pour cette piste → seed sans event
                            _sendGuidHistory(sendKey) = newSendGuids
                            isFirstObservation = True
                        Else
                            If newSendGuids.Count <> knownSendGuids.Count Then
                                sendChanged = True
                            Else
                                For j = 0 To newSendGuids.Count - 1
                                    If newSendGuids(j) <> knownSendGuids(j) Then
                                        sendChanged = True
                                        Exit For
                                    End If
                                Next
                            End If

                            If sendChanged Then
                                _sendGuidHistory(sendKey) = newSendGuids
                            End If
                        End If
                    End SyncLock

                    If Not isFirstObservation AndAlso sendChanged Then
                        RaiseEvent On_SendGuidsChanged(i)
                    End If
                Next
                ' ── 6. Surveillance des enveloppes par piste ───────────────────────────
                For i = 0 To newCount - 1
                    Dim newEnvGuids = GetTrackEnvelopeGuids(i)
                    Dim envKey = BuildSendKey(currentKey, i)

                    Dim envChanged As Boolean = False
                    Dim isFirstObservation As Boolean = False

                    SyncLock _lock
                        Dim knownEnvGuids As List(Of String) = Nothing
                        If _envGuidHistory.TryGetValue(envKey, knownEnvGuids) = False Then
                            _envGuidHistory(envKey) = newEnvGuids
                            isFirstObservation = True
                        Else
                            If newEnvGuids.Count <> knownEnvGuids.Count Then
                                envChanged = True
                            Else
                                For j = 0 To newEnvGuids.Count - 1
                                    If newEnvGuids(j) <> knownEnvGuids(j) Then
                                        envChanged = True
                                        Exit For
                                    End If
                                Next
                            End If

                            If envChanged Then
                                _envGuidHistory(envKey) = newEnvGuids
                            End If
                        End If
                    End SyncLock

                    If isFirstObservation = False AndAlso envChanged Then
                        RaiseEvent On_EnvGuidsChanged(i)
                    End If
                Next
                ' ── 7. Surveillance des Items selectionnés ───────────────────────────
                _SelectedItemsManager.Refresh()


                ' ── 8. Surveillance de la zone de sélection ───────────────────────────
                Dim StartTime As Double = -1
                Dim EndTime As Double = -1
                If Reaper.utility.GetSet_LoopTimeRange(False, False, False, StartTime, EndTime) Then
                    Dim change As Boolean = False
                    If SelectionStartTime <> StartTime Then
                        change = True
                        _SelectionStartTime = StartTime
                    End If
                    If SelectionEndTime <> EndTime Then
                        change = True
                        _SelectionEndTime = EndTime
                    End If
                    If StartTime > 0 AndAlso EndTime > 0 Then
                        _SelectionActive = True
                    Else
                        _SelectionActive = False
                    End If
                    If change Then
                        RaiseEvent On_SelectionChange(SelectionActive)
                    End If
                End If


            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
                    _pipe.Dispose()
                    _disposed = True
                End If
            End Sub

        End Class
        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER SESSION MONITOR — détection de changement de projet actif
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Surveille le changement de projet actif dans Reaper (changement d'onglet)
        ''' et les ouvertures/fermetures de projets.
        ''' <br/>
        ''' Utilise <see cref="utility.GetProjectFilePath"/> et
        ''' <see cref="utility.EnumAllProjects"/> sur un thread dédié à faible priorité,
        ''' indépendant du <see cref="ReaperPollPipe"/> (pas de charge sur la connexion principale).
        ''' <br/>
        ''' Intervalle recommandé : 500 ms — les changements de projet sont rares.
        ''' </summary>
        ''' <summary>
        ''' Surveille le changement de projet actif dans Reaper et les ouvertures/fermetures
        ''' de projets. Polling sur un thread dédié à faible priorité, via son propre
        ''' <see cref="ReaperPollPipe"/> persistant — indépendant du hub.
        ''' Intervalle recommandé : 500 ms.
        ''' </summary>
        Public Class ReaperSessionMonitor
            Implements IDisposable

            ' ── Pipe propre — connexion persistante indépendante du hub ──────
            Private ReadOnly _pipe As New ReaperPollPipe()

            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 1000

            Private _lastFilePath As String = String.Empty
            Private ReadOnly _lastOpenProjects As New List(Of String)

            Public Event On_ActiveProjectChanged(newFilePath As String, previousFilePath As String)
            Public Event On_OpenProjectsChanged(projects As IReadOnlyList(Of String))

            Public Property Interval As Integer
                Get
                    Return _interval
                End Get
                Set
                    _interval = Value
                End Set
            End Property

            Public ReadOnly Property IsRunning As Boolean
                Get
                    Return Threading.Volatile.Read(_isRunning)
                End Get
            End Property

            Public ReadOnly Property CurrentFilePath As String
                Get
                    Return _lastFilePath
                End Get
            End Property

            Public Sub Start()
                If IsRunning Then Return

                ' ── Snapshot initial via le pipe propre ───────────────────────

                _lastFilePath = _pipe.Request("GET_PROJECT_FILE_PATH")
                If _lastFilePath.StartsWith("ERROR", StringComparison.Ordinal) Then _lastFilePath = String.Empty

                Dim projectsRaw = _pipe.Request("ENUM_PROJECTS")
                _lastOpenProjects.Clear()
                If Not String.IsNullOrEmpty(projectsRaw) AndAlso
                   Not projectsRaw.StartsWith("ERROR", StringComparison.Ordinal) Then
                    _lastOpenProjects.AddRange(projectsRaw.Split("|"c))
                End If

                Threading.Volatile.Write(_isRunning, True)
                _thread = New Thread(AddressOf MonitorLoop) With {
                    .Name = "ReaperSessionMonitor",
                    .IsBackground = True,
                    .Priority = ThreadPriority.BelowNormal
                }
                _thread.Start()
            End Sub

            Public Sub [Stop]()
                Threading.Volatile.Write(_isRunning, False)
                _pipe.Disconnect()
            End Sub

            Private Sub MonitorLoop()
                While Threading.Volatile.Read(_isRunning)
                    Thread.Sleep(_interval)
                    PollProject()
                End While
            End Sub

            Private Sub PollProject()
                ' ── Projet actif ──────────────────────────────────────────────
                Dim currentPath = _pipe.Request("GET_PROJECT_FILE_PATH")
                If String.IsNullOrEmpty(currentPath) OrElse currentPath.StartsWith("ERROR", StringComparison.Ordinal) Then
                    currentPath = String.Empty
                End If

                If currentPath <> _lastFilePath Then
                    Dim previous = _lastFilePath
                    _lastFilePath = currentPath
                    RaiseEvent On_ActiveProjectChanged(currentPath, previous)
                End If

                ' ── Projets ouverts ───────────────────────────────────────────
                Dim projectsRaw = _pipe.Request("ENUM_PROJECTS")
                Dim openProjects As String() = Array.Empty(Of String)()
                If Not String.IsNullOrEmpty(projectsRaw) AndAlso
                   Not projectsRaw.StartsWith("ERROR", StringComparison.Ordinal) Then
                    openProjects = projectsRaw.Split("|"c)
                End If

                Dim changed = openProjects.Length <> _lastOpenProjects.Count
                If Not changed Then
                    For i = 0 To openProjects.Length - 1
                        If openProjects(i) <> _lastOpenProjects(i) Then
                            changed = True
                            Exit For
                        End If
                    Next
                End If

                If changed Then
                    _lastOpenProjects.Clear()
                    _lastOpenProjects.AddRange(openProjects)
                    RaiseEvent On_OpenProjectsChanged(openProjects)
                End If
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
                    _pipe.Dispose()
                    _disposed = True
                End If
            End Sub

        End Class

        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER FX PARAM MONITOR — surveillance ciblée des paramètres d'un FX
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Surveille les changements de valeurs de paramètres pour une piste et un FX donnés.
        ''' <br/>
        ''' Chaque paramètre peut être suspendu individuellement pour éviter les
        ''' effets de feedback (ex. : l'utilisateur tourne un vpot → SuspendParam
        ''' empêche la valeur envoyée par Reaper de reboucler sur la surface).
        ''' <br/>
        ''' Pendant la suspension d'un paramètre, le cache est maintenu à jour :
        ''' à la reprise (<see cref="ResumeParam"/>), seule une vraie nouvelle valeur
        ''' déclenchera l'événement <see cref="On_ParamChange"/>.
        ''' </summary>
        Public Class ReaperFxParamMonitor
            Implements IDisposable
            ' ── Pipe propre — aucune contention avec les autres monitors ──────
            Private ReadOnly _pipe As New ReaperPollPipe()

            ' ── Flag de changement d'état projet ─────────────────────────────────
            ' Initialisé à True pour forcer un premier poll au démarrage (sync initiale).
            ' Mis à True par OnProjectStateChange (appelé synchroniquement par _pipe.Poll()).
            ' Remis à False dans MonitorLoop après chaque cycle de polling FX.
            Private _stateChanged As Boolean = True
            ' ── Identité FX ───────────────────────────────────────────────────────
            Private ReadOnly _trackIndex As Integer
            Private ReadOnly _fxIndex As Integer


            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 50

            ' ── Paramètres surveillés ─────────────────────────────────────────────
            ' Clé   = paramIndex
            ' Valeur = True si le paramètre est actuellement suspendu
            Private ReadOnly _lock As New Object()
            Private ReadOnly _params As New Dictionary(Of Integer, Boolean)
            Private ReadOnly _cache As New Dictionary(Of Integer, Double)

            ' ── Événements ────────────────────────────────────────────────────────
            ''' <summary>Déclenché quand la connexion au bridge démarre.</summary>
            Public Event On_Connected()
            ''' <summary>Déclenché quand le thread de monitoring s'arrête.</summary>
            Public Event On_Disconnected()
            ''' <summary>
            ''' Déclenché quand la valeur normalisée d'un paramètre surveillé
            ''' (et non suspendu) change.
            ''' </summary>
            Public Event On_ParamChange(paramIndex As Integer, normValue As Double)

            ' ── Constructeur ──────────────────────────────────────────────────────

            ''' <summary>
            ''' Initialise le moniteur pour une piste et un FX donnés.
            ''' </summary>
            ''' <param name="trackIndex">Index 0-based de la piste.</param>
            ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
            Public Sub New(trackIndex As Integer, fxIndex As Integer)
                _trackIndex = trackIndex
                _fxIndex = fxIndex
            End Sub

            ' ── Propriétés ────────────────────────────────────────────────────────

            ''' <summary>Index 0-based de la piste surveillée.</summary>
            Public ReadOnly Property TrackIndex As Integer
                Get
                    Return _trackIndex
                End Get
            End Property

            ''' <summary>Index 0-based du FX surveillé.</summary>
            Public ReadOnly Property FxIndex As Integer
                Get
                    Return _fxIndex
                End Get
            End Property

            ''' <summary>Intervalle de polling en ms (défaut : 50 ms).</summary>
            Public Property Interval As Integer
                Get
                    Return _interval
                End Get
                Set
                    _interval = Value
                End Set
            End Property

            ''' <summary>True si le thread de monitoring est actif.</summary>
            Public ReadOnly Property IsRunning As Boolean
                Get
                    Return Threading.Volatile.Read(_isRunning)
                End Get
            End Property

            ' ── Abonnement aux paramètres ─────────────────────────────────────────

            ''' <summary>
            ''' Ajoute un paramètre à la surveillance.
            ''' Sans effet si le paramètre est déjà surveillé.
            ''' </summary>
            Public Sub WatchParam(paramIndex As Integer)
                SyncLock _lock
                    If Not _params.ContainsKey(paramIndex) Then
                        _params(paramIndex) = False
                    End If
                End SyncLock
            End Sub

            ''' <summary>
            ''' Ajoute une plage de paramètres consécutifs à la surveillance.
            ''' </summary>
            ''' <param name="firstIndex">Premier index 0-based.</param>
            ''' <param name="count">Nombre de paramètres.</param>
            Public Sub WatchParams(firstIndex As Integer, count As Integer)
                SyncLock _lock
                    For i = firstIndex To firstIndex + count - 1
                        If Not _params.ContainsKey(i) Then
                            _params(i) = False
                        End If
                    Next
                End SyncLock
            End Sub

            ''' <summary>
            ''' Retire un paramètre de la surveillance et efface son cache.
            ''' </summary>
            Public Sub UnwatchParam(paramIndex As Integer)
                SyncLock _lock
                    _params.Remove(paramIndex)
                    _cache.Remove(paramIndex)
                End SyncLock
            End Sub

            ''' <summary>Retire tous les paramètres et efface les caches.</summary>
            Public Sub ClearWatched()
                SyncLock _lock
                    _params.Clear()
                    _cache.Clear()
                End SyncLock
            End Sub

            ' ── Suspension par paramètre ──────────────────────────────────────────

            ''' <summary>
            ''' Suspend la détection de changement pour un paramètre spécifique.
            ''' Le polling continue et le cache est maintenu à jour, mais
            ''' <see cref="On_ParamChange"/> n'est plus déclenché pour ce paramètre.
            ''' <br/>
            ''' Usage typique : appeler avant d'envoyer une valeur depuis la surface
            ''' pour éviter le feedback (la valeur envoyée reboucle en retour).
            ''' </summary>
            Public Sub SuspendParam(paramIndex As Integer)
                SyncLock _lock
                    If _params.ContainsKey(paramIndex) Then
                        _params(paramIndex) = True
                    End If
                End SyncLock
            End Sub
            Public Sub SuspendAllParams()
                SyncLock _lock
                    For Each index In _params.Keys
                        _params(index) = True
                    Next
                End SyncLock
            End Sub
            ''' <summary>
            ''' Réactive la détection de changement pour un paramètre suspendu.
            ''' Le prochain changement de valeur dans Reaper déclenchera à nouveau
            ''' <see cref="On_ParamChange"/>.
            ''' </summary>
            Public Sub ResumeParam(paramIndex As Integer)
                SyncLock _lock
                    If _params.ContainsKey(paramIndex) Then
                        _params(paramIndex) = False
                    End If
                End SyncLock
            End Sub
            Public Sub ResumeAllParams()
                SyncLock _lock
                    For Each index In _params.Keys
                        _params(index) = False
                    Next
                End SyncLock
            End Sub
            ''' <summary>
            ''' Retourne True si le paramètre est actuellement suspendu.
            ''' Retourne False si le paramètre n'est pas surveillé.
            ''' </summary>
            Public Function IsParamSuspended(paramIndex As Integer) As Boolean
                SyncLock _lock
                    Dim suspended As Boolean = False
                    _params.TryGetValue(paramIndex, suspended)
                    Return suspended
                End SyncLock
            End Function

            ' ── Cycle de vie ──────────────────────────────────────────────────────

            ''' <summary>Démarre le thread de monitoring. Sans effet si déjà démarré.</summary>
            Public Sub Start()
                If IsRunning Then Return
                ' ── S'abonner au hub avant de démarrer ────────────────────────
                AddHandler ReaperPollHub.Instance.On_ProjectStateChange,
                    AddressOf OnProjectStateChange
                Threading.Volatile.Write(_isRunning, True)
                _thread = New Thread(AddressOf MonitorLoop) With {
                .Name = $"FxParamMonitor_T{_trackIndex}_FX{_fxIndex}",
                .IsBackground = True,
                .Priority = ThreadPriority.Normal
            }
                _thread.Start()
            End Sub

            Public Sub [Stop]()
                RemoveHandler ReaperPollHub.Instance.On_ProjectStateChange,
                    AddressOf OnProjectStateChange
                Threading.Volatile.Write(_isRunning, False)
                ' ── Fermer le pipe propre ─────────────────────────────────────
                _pipe.Disconnect()
            End Sub

            ' ── Boucle de monitoring ──────────────────────────────────────────────

            Private Sub MonitorLoop()

                RaiseEvent On_Connected()

                While Threading.Volatile.Read(_isRunning)

                    ' ── PollParams uniquement si Reaper a modifié l'état projet ─────
                    ' Évite N requêtes GET_FX_PARAM_NORM par cycle quand rien ne change.
                    If Threading.Volatile.Read(_stateChanged) Then
                        Threading.Volatile.Write(_stateChanged, False)
                        PollParams()
                    End If

                    Thread.Sleep(_interval)
                End While


                RaiseEvent On_Disconnected()
            End Sub

            ''' <summary>
            ''' Handler interne déclenché par <see cref="ReaperPollPipe.On_ProjectStateChange"/>.
            ''' Positionne le flag <c>_stateChanged</c> pour activer le prochain cycle de polling FX.
            ''' </summary>
            Private Sub OnProjectStateChange(stateCount As Integer)
                Threading.Volatile.Write(_stateChanged, True)
            End Sub

            ''' <summary>
            ''' Interroge tous les paramètres surveillés via le pipe persistant.
            ''' 1 requête <c>GET_FX_PARAM_NORM</c> par paramètre par cycle.
            ''' Les paramètres suspendus sont quand même pollés (cache maintenu à jour)
            ''' mais leur événement n'est pas déclenché.
            ''' </summary>
            Private Sub PollParams()
                Dim snapshot As (paramIndex As Integer, isSuspended As Boolean)()
                SyncLock _lock
                    Dim tmp As New List(Of (Integer, Boolean))
                    For Each kvp In _params
                        tmp.Add((kvp.Key, kvp.Value))
                    Next
                    snapshot = tmp.ToArray()
                End SyncLock

                Dim ic = Globalization.CultureInfo.InvariantCulture
                For Each entry In snapshot
                    ' ── Utilise le pipe propre du monitor ─────────────────────
                    Dim raw = _pipe.Request(
                    $"GET_FX_PARAM_NORM|{_trackIndex}|{_fxIndex}|{entry.paramIndex}")
                    If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                        Continue For
                    End If

                    Dim newVal As Double = Double.MinValue
                    If Not Double.TryParse(raw, Globalization.NumberStyles.Any,
                                       ic, newVal) Then
                        Continue For
                    End If

                    Dim changed As Boolean = False
                    Dim isSuspended As Boolean = False
                    Dim stillWatched As Boolean = False

                    SyncLock _lock
                        ' ── Fix Bug 1 + Bug 2 : vérifier que le param est toujours
                        ' surveillé. ClearWatched() peut avoir vidé _params ET _cache
                        ' entre la prise du snapshot et ici. On lit aussi isSuspended
                        ' en direct (Fix Bug 3 mineur : valeur stale du snapshot).
                        stillWatched = _params.TryGetValue(entry.paramIndex, isSuspended)
                        If stillWatched Then
                            Dim prevVal As Double = Double.MinValue
                            changed = Not _cache.TryGetValue(entry.paramIndex, prevVal) OrElse
                                  prevVal <> newVal

                            If changed Then
                                _cache(entry.paramIndex) = newVal
                            End If
                        End If
                    End SyncLock

                    If stillWatched AndAlso changed AndAlso Not isSuspended Then
                        RaiseEvent On_ParamChange(entry.paramIndex, newVal)
                    End If
                Next
            End Sub
            ' ── IDisposable ───────────────────────────────────────────────────────

            ''' <summary>Arrête le monitoring et libère toutes les ressources.</summary>
            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
                    ' ── Libérer le pipe propre ────────────────────────────────
                    _pipe.Dispose()
                    _disposed = True
                End If
            End Sub

        End Class


        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER FX ENVELOPE PARAM MONITOR — suivi continu d'une collection de FX params
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Surveille en continu une collection de paramètres FX pour détecter
        ''' les changements induits par les enveloppes d'automation pendant la lecture.
        ''' <br/>
        ''' Différence clé avec <see cref="ReaperFxParamMonitor"/> :
        ''' <list type="bullet">
        ''' <item>Polling <b>inconditionnel</b> à chaque cycle — pas d'attente du
        '''       <c>ProjectStateChangeCount</c> — idéal pour les valeurs animées
        '''       par les enveloppes d'automation.</item>
        ''' <item>Pendant la suspension d'un paramètre, le cache n'est <b>pas</b>
        '''       mis à jour. À la reprise (<see cref="ResumeParam"/>), le cache
        '''       est réinitialisé → resync immédiat avec la valeur courante de Reaper.</item>
        ''' </list>
        ''' </summary>
        Public Class ReaperFxParamEnvelopeMonitor
            Implements IDisposable

            ' ── Pipe propre — aucune contention avec les autres monitors ──────
            Private ReadOnly _pipe As New ReaperPollPipe()

            Private ReadOnly _trackIndex As Integer
            Private ReadOnly _fxIndex As Integer
            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 50
            Private _isPlaying As Boolean = False
            Private ReadOnly _lock As New Object()
            Private ReadOnly _params As New Dictionary(Of Integer, Boolean)
            Private ReadOnly _cache As New Dictionary(Of Integer, Double)

            Public Event On_Connected()
            Public Event On_Disconnected()
            Public Event On_ValueChange(paramIndex As Integer, normValue As Double)
            Public Event On_PlayStateChange(playState As Integer)
            Public Event On_PlayPositionChange(playPos As Double)

            Public Sub New(trackIndex As Integer, fxIndex As Integer)
                _trackIndex = trackIndex
                _fxIndex = fxIndex
            End Sub

            Public ReadOnly Property TrackIndex As Integer
                Get
                    Return _trackIndex
                End Get
            End Property

            Public ReadOnly Property FxIndex As Integer
                Get
                    Return _fxIndex
                End Get
            End Property

            Public Property Interval As Integer
                Get
                    Return _interval
                End Get
                Set
                    _interval = Value
                End Set
            End Property

            Public ReadOnly Property IsRunning As Boolean
                Get
                    Return Threading.Volatile.Read(_isRunning)
                End Get
            End Property

            Public Sub WatchParam(paramIndex As Integer)
                SyncLock _lock
                    If Not _params.ContainsKey(paramIndex) Then
                        _params(paramIndex) = False
                    End If
                End SyncLock
            End Sub

            Public Sub WatchParams(firstIndex As Integer, count As Integer)
                SyncLock _lock
                    For i = firstIndex To firstIndex + count - 1
                        If Not _params.ContainsKey(i) Then
                            _params(i) = False
                        End If
                    Next
                End SyncLock
            End Sub

            Public Sub UnwatchParam(paramIndex As Integer)
                SyncLock _lock
                    _params.Remove(paramIndex)
                    _cache.Remove(paramIndex)
                End SyncLock
            End Sub

            Public Sub ClearWatched()
                SyncLock _lock
                    _params.Clear()
                    _cache.Clear()
                End SyncLock
            End Sub

            Public Sub SuspendParam(paramIndex As Integer)
                SyncLock _lock
                    If _params.ContainsKey(paramIndex) Then
                        _params(paramIndex) = True
                    End If
                End SyncLock
            End Sub

            Public Sub ResumeParam(paramIndex As Integer)
                SyncLock _lock
                    If _params.ContainsKey(paramIndex) Then
                        _params(paramIndex) = False
                        _cache.Remove(paramIndex)
                    End If
                End SyncLock
            End Sub

            Public Function IsParamSuspended(paramIndex As Integer) As Boolean
                SyncLock _lock
                    Dim suspended As Boolean = False
                    _params.TryGetValue(paramIndex, suspended)
                    Return suspended
                End SyncLock
            End Function

            Public Sub Start()
                If IsRunning Then Return
                ' ── Hub : événements transport uniquement (pas de Request) ────
                AddHandler ReaperPollHub.Instance.On_PlayStateChange,
                    AddressOf OnHubPlayStateChange
                AddHandler ReaperPollHub.Instance.On_PlayPositionChange,
                    AddressOf OnHubPlayPositionChange
                Threading.Volatile.Write(_isRunning, True)
                _thread = New Thread(AddressOf MonitorLoop) With {
                    .Name = $"FxEnvMonitor_T{_trackIndex}_FX{_fxIndex}",
                    .IsBackground = True,
                    .Priority = ThreadPriority.Normal
                }
                _thread.Start()
            End Sub

            Public Sub [Stop]()
                RemoveHandler ReaperPollHub.Instance.On_PlayStateChange,
                    AddressOf OnHubPlayStateChange
                RemoveHandler ReaperPollHub.Instance.On_PlayPositionChange,
                    AddressOf OnHubPlayPositionChange
                Threading.Volatile.Write(_isRunning, False)
                ' ── Fermer le pipe propre ─────────────────────────────────────
                _pipe.Disconnect()
            End Sub

            Private Sub OnHubPlayStateChange(playState As Integer)
                Threading.Volatile.Write(_isPlaying, playState <> 0)
                RaiseEvent On_PlayStateChange(playState)
            End Sub

            Private Sub OnHubPlayPositionChange(playPos As Double)
                RaiseEvent On_PlayPositionChange(playPos)
            End Sub

            Private Sub MonitorLoop()
                RaiseEvent On_Connected()
                While Threading.Volatile.Read(_isRunning)
                    If Threading.Volatile.Read(_isPlaying) Then
                        PollParams()
                    End If
                    Thread.Sleep(_interval)
                End While
                RaiseEvent On_Disconnected()
            End Sub

            Private Sub PollParams()
                Dim snapshot As (paramIndex As Integer, isSuspended As Boolean)()
                SyncLock _lock
                    Dim tmp As New List(Of (Integer, Boolean))
                    For Each kvp In _params
                        tmp.Add((kvp.Key, kvp.Value))
                    Next
                    snapshot = tmp.ToArray()
                End SyncLock

                Dim ic = CultureInfo.InvariantCulture
                For Each entry In snapshot
                    ' ── Utilise le pipe propre du monitor ─────────────────────
                    Dim raw = _pipe.Request(
                        $"GET_FX_PARAM_NORM|{_trackIndex}|{_fxIndex}|{entry.paramIndex}")
                    If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Continue For

                    Dim newVal As Double = Double.MinValue
                    If Not Double.TryParse(raw, Globalization.NumberStyles.Any,
                                           ic, newVal) Then Continue For

                    If entry.isSuspended Then Continue For

                    Dim changed As Boolean
                    SyncLock _lock
                        Dim prevVal As Double = Double.MinValue
                        changed = Not _cache.TryGetValue(entry.paramIndex, prevVal) OrElse
                                  prevVal <> newVal
                        If changed Then _cache(entry.paramIndex) = newVal
                    End SyncLock

                    If changed Then
                        RaiseEvent On_ValueChange(entry.paramIndex, newVal)
                        'Console.WriteLine(entry.paramIndex & ";" & newVal)
                    End If
                Next
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
                    ' ── Libérer le pipe propre ────────────────────────────────
                    _pipe.Dispose()
                    _disposed = True
                End If
            End Sub

        End Class

        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER TRACK MONITOR — surveillance ciblée des pistes sur ProjectStateChange
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Surveille les changements de vol/pan/mute/autoMode pour une collection
        ''' de pistes, en interrogeant Reaper <b>uniquement quand le
        ''' <c>ProjectStateChangeCount</c> a évolué</b>.
        ''' <br/>
        ''' Même principe que <see cref="ReaperFxParamMonitor"/> :
        ''' <list type="bullet">
        ''' <item>Pendant la suspension d'une piste, le cache est maintenu à jour
        '''       mais <c>On_XxxChange</c> n'est pas déclenché.</item>
        ''' <item>À la reprise (<see cref="ResumeTrack"/>), seule une vraie
        '''       nouvelle valeur déclenchera l'événement.</item>
        ''' </list>
        ''' Pour le suivi POST-automation en lecture continue, utiliser
        ''' <see cref="ReaperTrackEnvelopeMonitor"/>.
        ''' </summary>
        Public Class ReaperTrackMonitor
            Implements IDisposable

            ' ── Pipe propre — aucune contention avec les autres monitors ──────
            Private ReadOnly _pipe As New ReaperPollPipe()

            Private _stateChanged As Boolean = True
            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 50
            Private _Suspend As Boolean = False
            Private ReadOnly _lock As New Object()
            Private ReadOnly _tracks As New Dictionary(Of Integer, (Track As ReaperTrack, IsSuspended As Boolean))
            Private ReadOnly _cleanup As New Dictionary(Of Integer, Action)

            ' ── Événements ────────────────────────────────────────────────────
            ''' <summary>Déclenché quand la connexion au bridge démarre.</summary>
            Public Event On_Connected()
            ''' <summary>Déclenché quand le thread de monitoring s'arrête.</summary>
            Public Event On_Disconnected()
            ''' <summary>
            ''' Bubblé depuis <see cref="ReaperTrack.On_VolChange"/> avec le
            ''' <paramref name="trackIndex"/> injecté. Déclenché uniquement quand
            ''' <c>D_VOL</c> a réellement changé (comparaison via le cache de
            ''' <see cref="ReaperTrack"/>).
            ''' </summary>
            Public Event On_VolChange(trackIndex As Integer, vol As Double)
            ''' <summary>Bubblé depuis <see cref="ReaperTrack.On_PanChange"/>.</summary>
            Public Event On_PanChange(trackIndex As Integer, pan As Double)
            ''' <summary>Bubblé depuis <see cref="ReaperTrack.On_MuteChange"/>.</summary>
            Public Event On_MuteChange(trackIndex As Integer, mute As Boolean)
            ''' <summary>Bubblé depuis <see cref="ReaperTrack.On_AutoModeChange"/>.</summary>
            Public Event On_AutoModeChange(trackIndex As Integer, autoMode As Integer)
            ''' <summary>Bubblé depuis <see cref="ReaperTrack.On_SoloChange"/>.</summary>
            Public Event On_SoloChange(trackIndex As Integer, solo As Integer)
            ''' <summary>Bubblé depuis <see cref="ReaperTrack.On_SelectedChange"/>.</summary>
            Public Event On_SelectedChange(trackIndex As Integer, selected As Integer)
            ''' <summary>Bubblé depuis <see cref="ReaperTrack.On_RecArmChange"/>.</summary>
            Public Event On_RecArmChange(trackIndex As Integer, recArm As Integer)
            ' ── Propriétés ────────────────────────────────────────────────────

            ''' <summary>Intervalle de polling en ms (défaut : 50 ms).</summary>
            Public Property Interval As Integer
                Get
                    Return _interval
                End Get
                Set
                    _interval = Value
                End Set
            End Property

            ''' <summary>True si le thread de monitoring est actif.</summary>
            Public ReadOnly Property IsRunning As Boolean
                Get
                    Return Threading.Volatile.Read(_isRunning)
                End Get
            End Property
            Public Property Suspend As Boolean
                Get
                    Return _Suspend
                End Get
                Set
                    _Suspend = Value
                End Set
            End Property

            ' ── Gestion de la collection ──────────────────────────────────────

            ''' <summary>
            ''' Ajoute une piste à la surveillance et câble ses événements vers
            ''' les événements de ce moniteur (avec injection du trackIndex).
            ''' Les lambdas de <c>RemoveHandler</c> sont stockées pour un nettoyage
            ''' propre dans <see cref="UnwatchTrack"/> ou <see cref="Dispose"/>.
            ''' </summary>
            ''' <summary>
            ''' Ajoute une piste à la surveillance et câble ses événements vers
            ''' les événements de ce moniteur (avec injection du trackIndex).
            ''' Les lambdas de <c>RemoveHandler</c> sont stockées pour un nettoyage
            ''' propre dans <see cref="UnwatchTrack"/> ou <see cref="Dispose"/>.
            ''' </summary>
            Public Sub WatchTrack(track As ReaperTrack)
                SyncLock _lock
                    If _tracks.ContainsKey(track.TrackIndex) Then Return
                    _tracks(track.TrackIndex) = (track, False)

                    Dim ti = track.TrackIndex

                    Dim volH As [Delegate] = New ReaperTrack.On_VolChangeEventHandler(
                        Sub(v As Double)
                            If Not IsTrackSuspended(ti) Then RaiseEvent On_VolChange(ti, v)
                        End Sub)

                    Dim panH As [Delegate] = New ReaperTrack.On_PanChangeEventHandler(
                        Sub(p As Double)
                            If Not IsTrackSuspended(ti) Then RaiseEvent On_PanChange(ti, p)
                        End Sub)

                    Dim muteH As [Delegate] = New ReaperTrack.On_MuteChangeEventHandler(
                        Sub(m As Boolean)
                            If Not IsTrackSuspended(ti) Then RaiseEvent On_MuteChange(ti, m)
                        End Sub)

                    Dim autoH As [Delegate] = New ReaperTrack.On_AutoModeChangeEventHandler(
                        Sub(a As Integer)
                            If Not IsTrackSuspended(ti) Then RaiseEvent On_AutoModeChange(ti, a)
                        End Sub)

                    Dim soloH As [Delegate] = New ReaperTrack.On_SoloChangeEventHandler(
                        Sub(s As Integer)
                            If Not IsTrackSuspended(ti) Then RaiseEvent On_SoloChange(ti, s)
                        End Sub)

                    Dim selectH As [Delegate] = New ReaperTrack.On_SelectedChangeEventHandler(
                        Sub(s As Integer)
                            If Not IsTrackSuspended(ti) Then RaiseEvent On_SelectedChange(ti, s)
                        End Sub)

                    Dim recArmH As [Delegate] = New ReaperTrack.On_RecArmChangeEventHandler(
                        Sub(r As Integer)
                            If Not IsTrackSuspended(ti) Then RaiseEvent On_RecArmChange(ti, r)
                        End Sub)

                    AddHandler track.On_VolChange, volH
                    AddHandler track.On_PanChange, panH
                    AddHandler track.On_MuteChange, muteH
                    AddHandler track.On_AutoModeChange, autoH
                    AddHandler track.On_SoloChange, soloH
                    AddHandler track.On_SelectedChange, selectH
                    AddHandler track.On_RecArmChange, recArmH

                    _cleanup(ti) = Sub()
                                       RemoveHandler track.On_VolChange, volH
                                       RemoveHandler track.On_PanChange, panH
                                       RemoveHandler track.On_MuteChange, muteH
                                       RemoveHandler track.On_AutoModeChange, autoH
                                       RemoveHandler track.On_SoloChange, soloH
                                       RemoveHandler track.On_SelectedChange, selectH
                                       RemoveHandler track.On_RecArmChange, recArmH
                                   End Sub
                End SyncLock
            End Sub
            ''' <summary>Ajoute une collection de pistes à la surveillance.</summary>
            Public Sub WatchTracks(tracks As IEnumerable(Of ReaperTrack))
                For Each trk In tracks
                    WatchTrack(trk)
                Next
            End Sub

            ''' <summary>Retire une piste, désabonne ses événements et efface son entrée.</summary>
            Public Sub UnwatchTrack(trackIndex As Integer)
                SyncLock _lock
                    Dim cleanupAction As Action = Nothing
                    If _cleanup.TryGetValue(trackIndex, cleanupAction) Then
                        cleanupAction()
                        _cleanup.Remove(trackIndex)
                    End If
                    _tracks.Remove(trackIndex)
                End SyncLock
            End Sub

            ''' <summary>Retire toutes les pistes et désabonne tous leurs événements.</summary>
            Public Sub ClearWatched()
                SyncLock _lock
                    For Each c In _cleanup.Values
                        c()
                    Next
                    _cleanup.Clear()
                    _tracks.Clear()
                End SyncLock
            End Sub

            ' ── Suspension par piste ──────────────────────────────────────────

            ''' <summary>
            ''' Suspend la piste : <see cref="ReaperTrack.Refresh"/> ne sera pas
            ''' appelé → cache non mis à jour, événements muets.
            ''' </summary>
            Public Sub SuspendTrack(trackIndex As Integer)
                SyncLock _lock
                    If _tracks.ContainsKey(trackIndex) Then
                        Dim e = _tracks(trackIndex)
                        _tracks(trackIndex) = (e.Track, True)
                    End If
                End SyncLock
            End Sub

            ''' <summary>
            ''' Réactive la piste. Le prochain cycle appellera <see cref="ReaperTrack.Refresh"/>,
            ''' qui comparera avec les caches internes de la piste et ne déclenchera
            ''' que les vrais changements.
            ''' </summary>
            Public Sub ResumeTrack(trackIndex As Integer)
                SyncLock _lock
                    If _tracks.ContainsKey(trackIndex) Then
                        Dim e = _tracks(trackIndex)
                        _tracks(trackIndex) = (e.Track, False)
                    End If
                End SyncLock
            End Sub

            ''' <summary>Retourne True si la piste est suspendue.</summary>
            Public Function IsTrackSuspended(trackIndex As Integer) As Boolean
                SyncLock _lock
                    If _tracks.ContainsKey(trackIndex) Then Return _tracks(trackIndex).IsSuspended
                    Return False
                End SyncLock
            End Function

            ' ── Cycle de vie ──────────────────────────────────────────────────

            Public Sub Start()
                If IsRunning Then Return
                AddHandler ReaperPollHub.Instance.On_ProjectStateChange,
                    AddressOf OnProjectStateChange
                Threading.Volatile.Write(_isRunning, True)
                _thread = New Thread(AddressOf MonitorLoop) With {
                    .Name = "TrackMonitor",
                    .IsBackground = True,
                    .Priority = ThreadPriority.Normal
                }
                _thread.Start()
            End Sub

            Public Sub [Stop]()
                RemoveHandler ReaperPollHub.Instance.On_ProjectStateChange,
                    AddressOf OnProjectStateChange
                Threading.Volatile.Write(_isRunning, False)
                _pipe.Disconnect()
            End Sub

            Private Sub OnProjectStateChange(stateCount As Integer)
                Threading.Volatile.Write(_stateChanged, True)
            End Sub

            Private Sub MonitorLoop()
                RaiseEvent On_Connected()
                While Threading.Volatile.Read(_isRunning)
                    If Threading.Volatile.Read(_Suspend) = False Then
                        If Threading.Volatile.Read(_stateChanged) Then
                            Threading.Volatile.Write(_stateChanged, False)
                            PollTracks()
                        End If
                    End If
                    Thread.Sleep(_interval)
                End While
                RaiseEvent On_Disconnected()
            End Sub

            Private Sub PollTracks()
                Dim snapshot As (Track As ReaperTrack, IsSuspended As Boolean)()
                SyncLock _lock
                    Dim tmp As New List(Of (ReaperTrack, Boolean))
                    For Each entry In _tracks.Values
                        tmp.Add(entry)
                    Next
                    snapshot = tmp.ToArray()
                End SyncLock
                For Each entry In snapshot
                    If entry.IsSuspended Then Continue For
                    ' ── Utilise le pipe propre du monitor ─────────────────────
                    entry.Track.Refresh(_pipe)
                Next
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
                    ClearWatched()
                    _pipe.Dispose()
                    _disposed = True
                End If
            End Sub

        End Class
        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER TRACK ENVELOPE MONITOR — suivi continu vol/pan/mute POST-automation
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Surveille en continu le volume, le pan et le mute POST-automation
        ''' d'une collection de pistes pour détecter les changements induits par
        ''' les enveloppes d'automation pendant la lecture.
        ''' <br/>
        ''' Lit les valeurs via <c>GET_TRACK_UI_VOL_PAN</c> (vol+pan en 1 requête)
        ''' et <c>GET_TRACK_UI_MUTE</c> — pas d'attente du
        ''' <c>ProjectStateChangeCount</c>.
        ''' <br/>
        ''' Sémantique de suspension identique à
        ''' <see cref="ReaperFxParamEnvelopeMonitor"/> : pendant la suspension,
        ''' les caches ne sont pas mis à jour. À la reprise, les caches sont effacés
        ''' → resync immédiat avec les valeurs courantes de Reaper.
        ''' </summary>

        Public Class ReaperTrackEnvelopeMonitor
            Implements IDisposable

            ' ── Pipe propre — aucune contention avec les autres monitors ──────
            Private ReadOnly _pipe As New ReaperPollPipe()

            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 50
            Private _isPlaying As Boolean = False  ' ← AJOUT

            Private ReadOnly _lock As New Object()
            Private ReadOnly _tracks As New Dictionary(Of Integer, Boolean)
            Private ReadOnly _volCache As New Dictionary(Of Integer, Double)
            Private ReadOnly _panCache As New Dictionary(Of Integer, Double)
            Private ReadOnly _muteCache As New Dictionary(Of Integer, Boolean)

            Public Event On_Connected()
            Public Event On_Disconnected()
            Public Event On_VolChange(trackIndex As Integer, vol As Double)
            Public Event On_PanChange(trackIndex As Integer, pan As Double)
            Public Event On_MuteChange(trackIndex As Integer, mute As Boolean)
            Public Event On_PlayStateChange(playState As Integer)
            Public Event On_PlayPositionChange(playPos As Double)

            Public Property Interval As Integer
                Get
                    Return _interval
                End Get
                Set
                    _interval = Value
                End Set
            End Property

            Public ReadOnly Property IsRunning As Boolean
                Get
                    Return Threading.Volatile.Read(_isRunning)
                End Get
            End Property

            Public Sub WatchTrack(trackIndex As Integer)
                SyncLock _lock
                    If Not _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = False
                    End If
                End SyncLock
            End Sub

            Public Sub WatchTracks(Tracks As List(Of ReaperTrack))
                SyncLock _lock
                    For Each trk In Tracks
                        If Not _tracks.ContainsKey(trk.TrackIndex) Then
                            _tracks(trk.TrackIndex) = False
                        End If
                    Next
                End SyncLock
            End Sub

            Public Sub UnwatchTrack(trackIndex As Integer)
                SyncLock _lock
                    _tracks.Remove(trackIndex)
                    _volCache.Remove(trackIndex)
                    _panCache.Remove(trackIndex)
                    _muteCache.Remove(trackIndex)
                End SyncLock
            End Sub

            Public Sub ClearWatched()
                SyncLock _lock
                    _tracks.Clear()
                    _volCache.Clear()
                    _panCache.Clear()
                    _muteCache.Clear()
                End SyncLock
            End Sub

            Public Sub SuspendTrack(trackIndex As Integer)
                SyncLock _lock
                    If _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = True
                    End If
                End SyncLock
            End Sub

            Public Sub ResumeTrack(trackIndex As Integer)
                SyncLock _lock
                    If _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = False
                        _volCache.Remove(trackIndex)
                        _panCache.Remove(trackIndex)
                        _muteCache.Remove(trackIndex)
                    End If
                End SyncLock
            End Sub

            Public Function IsTrackSuspended(trackIndex As Integer) As Boolean
                SyncLock _lock
                    Dim suspended As Boolean = False
                    _tracks.TryGetValue(trackIndex, suspended)
                    Return suspended
                End SyncLock
            End Function

            Public Sub Start()
                If IsRunning Then Return
                AddHandler ReaperPollHub.Instance.On_PlayStateChange,
                    AddressOf OnHubPlayStateChange
                AddHandler ReaperPollHub.Instance.On_PlayPositionChange,
                    AddressOf OnHubPlayPositionChange
                Threading.Volatile.Write(_isRunning, True)
                _thread = New Thread(AddressOf MonitorLoop) With {
                    .Name = "TrackEnvMonitor",
                    .IsBackground = True,
                    .Priority = ThreadPriority.Normal
                }
                _thread.Start()
            End Sub

            Public Sub [Stop]()
                RemoveHandler ReaperPollHub.Instance.On_PlayStateChange,
                    AddressOf OnHubPlayStateChange
                RemoveHandler ReaperPollHub.Instance.On_PlayPositionChange,
                    AddressOf OnHubPlayPositionChange
                Threading.Volatile.Write(_isRunning, False)
                ' ── Fermer le pipe propre ─────────────────────────────────────
                _pipe.Disconnect()
            End Sub

            Private Sub OnHubPlayStateChange(playState As Integer)
                Threading.Volatile.Write(_isPlaying, playState <> 0)
                RaiseEvent On_PlayStateChange(playState)
            End Sub

            Private Sub OnHubPlayPositionChange(playPos As Double)
                RaiseEvent On_PlayPositionChange(playPos)
            End Sub

            Private Sub MonitorLoop()
                RaiseEvent On_Connected()
                While Threading.Volatile.Read(_isRunning)
                    ' ── PollTracks uniquement si Reaper est en lecture ─────────
                    If Threading.Volatile.Read(_isPlaying) Then
                        PollTracks()
                    End If
                    Thread.Sleep(_interval)
                End While
                RaiseEvent On_Disconnected()
            End Sub

            Private Sub PollTracks()
                Dim snapshot As (trackIndex As Integer, isSuspended As Boolean)()
                SyncLock _lock
                    Dim tmp As New List(Of (Integer, Boolean))
                    For Each kvp In _tracks
                        tmp.Add((kvp.Key, kvp.Value))
                    Next
                    snapshot = tmp.ToArray()
                End SyncLock

                Dim ic = CultureInfo.InvariantCulture
                For Each entry In snapshot
                    Dim tIdx = entry.trackIndex

                    ' ── Vol + Pan (1 requête via le pipe propre) ──────────────
                    Dim rawVP = _pipe.Request($"GET_TRACK_UI_VOL_PAN|{tIdx}")
                    If Not String.IsNullOrEmpty(rawVP) AndAlso
                       Not rawVP.StartsWith("ERROR", StringComparison.Ordinal) Then
                        Dim p = rawVP.Split("|"c)
                        If p.Length >= 2 Then
                            Dim newVol As Double = Double.MinValue
                            Dim newPan As Double = Double.MinValue
                            Double.TryParse(p(0), Globalization.NumberStyles.Any, ic, newVol)
                            Double.TryParse(p(1), Globalization.NumberStyles.Any, ic, newPan)

                            If Not entry.isSuspended Then
                                Dim volChg As Boolean, panChg As Boolean
                                SyncLock _lock
                                    Dim pv As Double = Double.MinValue
                                    volChg = Not _volCache.TryGetValue(tIdx, pv) OrElse pv <> newVol
                                    If volChg Then _volCache(tIdx) = newVol
                                    panChg = Not _panCache.TryGetValue(tIdx, pv) OrElse pv <> newPan
                                    If panChg Then _panCache(tIdx) = newPan
                                End SyncLock
                                If volChg Then RaiseEvent On_VolChange(tIdx, newVol)
                                If panChg Then RaiseEvent On_PanChange(tIdx, newPan)
                            End If
                        End If
                    End If

                    ' ── Mute (1 requête via le pipe propre) ───────────────────
                    Dim rawMute = _pipe.Request($"GET_TRACK_UI_MUTE|{tIdx}")
                    If Not String.IsNullOrEmpty(rawMute) AndAlso
                       Not rawMute.StartsWith("ERROR", StringComparison.Ordinal) Then
                        Dim newMute = rawMute = "1"
                        If Not entry.isSuspended Then
                            Dim muteChg As Boolean
                            SyncLock _lock
                                Dim pm As Boolean = False
                                muteChg = Not _muteCache.TryGetValue(tIdx, pm) OrElse pm <> newMute
                                If muteChg Then _muteCache(tIdx) = newMute
                            End SyncLock
                            If muteChg Then RaiseEvent On_MuteChange(tIdx, newMute)
                        End If
                    End If
                Next
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
                    ' ── Libérer le pipe propre ────────────────────────────────
                    _pipe.Dispose()
                    _disposed = True
                End If
            End Sub

        End Class

        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER SEND ENVELOPE MONITOR — suivi continu vol/pan/mute UI d'un send sur N pistes
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Surveille en continu le volume, le pan et le mute UI (post-automation)
        ''' d'un envoi donné (<see cref="SendIndex"/>) pour une collection de pistes.
        ''' <br/>
        ''' Poll uniquement pendant la lecture Reaper.
        ''' <br/>
        ''' Lit les valeurs via :
        ''' <list type="bullet">
        ''' <item><c>GET_SEND_VOL_PAN|trackIndex|category|sendIndex</c></item>
        ''' <item><c>GET_SEND_MUTE|trackIndex|category|sendIndex</c></item>
        ''' </list>
        ''' <br/>
        ''' Sémantique de suspension identique à <see cref="ReaperTrackEnvelopeMonitor"/> :
        ''' pendant la suspension, les caches ne sont pas mis à jour.
        ''' À la reprise, les caches de la piste sont invalidés pour resynchronisation.
        ''' </summary>
        Public Class ReaperSendEnvelopeMonitor
            Implements IDisposable

            Private ReadOnly _pipe As New ReaperPollPipe()


            Private ReadOnly _category As Integer
            Private _refreshRequestVersion As Integer = 0

            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 50
            Private _isPlaying As Boolean = False

            Private ReadOnly _lock As New Object()
            Private ReadOnly _tracks As New Dictionary(Of Integer, Boolean)
            Private ReadOnly _volCache As New Dictionary(Of Integer, Double)
            Private ReadOnly _panCache As New Dictionary(Of Integer, Double)
            Private ReadOnly _muteCache As New Dictionary(Of Integer, Boolean)
            Private _SendLetter As String
            Private _TrackSendIndexes As Dictionary(Of Integer, Integer?)
            Public Event On_Connected()
            Public Event On_Disconnected()
            Public Event On_VolChange(trackIndex As Integer, vol As Double)
            Public Event On_PanChange(trackIndex As Integer, pan As Double)
            Public Event On_MuteChange(trackIndex As Integer, mute As Boolean)
            Public Event On_PlayStateChange(playState As Integer)
            Public Event On_PlayPositionChange(playPos As Double)

            ''' <summary>
            ''' Initialise le monitor pour un index d'envoi donné.
            ''' </summary>
            ''' <param name="sendIndex">Index 0-based de l'envoi surveillé.</param>
            ''' <param name="category"><see cref="SendCategory"/> (défaut : Sends).</param>
            Public Sub New(TrackSendIndexes As Dictionary(Of Integer, Integer?), SendLetter As String,
                   Optional category As SendCategory = SendCategory.Sends)
                _TrackSendIndexes = TrackSendIndexes
                _SendLetter = SendLetter
                _category = CInt(category)
            End Sub

            Public ReadOnly Property TrackSendIndexes As Dictionary(Of Integer, Integer?)
                Get
                    Return _TrackSendIndexes
                End Get
            End Property

            Public ReadOnly Property SendLetter As String
                Get
                    Return _SendLetter
                End Get
            End Property

            ''' <summary>Catégorie des envois surveillés.</summary>
            Public ReadOnly Property Category As SendCategory
                Get
                    Return CType(_category, SendCategory)
                End Get
            End Property

            ''' <summary>Intervalle de polling en millisecondes (défaut : 50).</summary>
            Public Property Interval As Integer
                Get
                    Return _interval
                End Get
                Set
                    _interval = Value
                End Set
            End Property

            ''' <summary>True si le thread de monitoring est actif.</summary>
            Public ReadOnly Property IsRunning As Boolean
                Get
                    Return Threading.Volatile.Read(_isRunning)
                End Get
            End Property

            ''' <summary>Ajoute une piste à surveiller.</summary>
            Public Sub WatchTrack(trackIndex As Integer)
                SyncLock _lock
                    If Not _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = False
                    End If
                End SyncLock
            End Sub
            ''' <summary>
            ''' Demande un rafraîchissement explicite du monitor, même si Reaper n'est pas en lecture.
            ''' Le prochain cycle de monitoring exécutera un PollTracks.
            ''' </summary>
            Public Sub RequestRefresh()
                Threading.Interlocked.Increment(_refreshRequestVersion)
            End Sub
            ''' <summary>
            ''' Lit l'état de lecture courant via POLL_STATE pour initialiser _isPlaying.
            ''' </summary>
            Private Sub InitializePlayState()
                Dim raw As String = _pipe.Request("POLL_STATE")
                If String.IsNullOrEmpty(raw) Then
                    Threading.Volatile.Write(_isPlaying, False)
                    Return
                End If

                If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                    Threading.Volatile.Write(_isPlaying, False)
                    Return
                End If

                Dim p As String() = raw.Split("|"c)
                If p.Length < 2 Then
                    Threading.Volatile.Write(_isPlaying, False)
                    Return
                End If

                Dim playState As Integer = 0
                If Integer.TryParse(p(1), playState) Then
                    Threading.Volatile.Write(_isPlaying, playState <> 0)
                Else
                    Threading.Volatile.Write(_isPlaying, False)
                End If
            End Sub
            ''' <summary>Ajoute une collection de pistes à surveiller.</summary>
            Public Sub WatchTracks(tracks As List(Of ReaperTrack))
                SyncLock _lock
                    For Each trk In tracks
                        If Not _tracks.ContainsKey(trk.TrackIndex) Then
                            _tracks(trk.TrackIndex) = False
                        End If
                    Next
                End SyncLock
            End Sub

            ''' <summary>Retire une piste de la surveillance et purge ses caches.</summary>
            Public Sub UnwatchTrack(trackIndex As Integer)
                SyncLock _lock
                    _tracks.Remove(trackIndex)
                    _volCache.Remove(trackIndex)
                    _panCache.Remove(trackIndex)
                    _muteCache.Remove(trackIndex)
                End SyncLock
            End Sub

            ''' <summary>Retire toutes les pistes surveillées et purge les caches.</summary>
            Public Sub ClearWatched()
                SyncLock _lock
                    _tracks.Clear()
                    _volCache.Clear()
                    _panCache.Clear()
                    _muteCache.Clear()
                End SyncLock
            End Sub

            ''' <summary>Suspend la surveillance d'une piste.</summary>
            Public Sub SuspendTrack(trackIndex As Integer)
                SyncLock _lock
                    If _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = True
                    End If
                End SyncLock
            End Sub

            ''' <summary>
            ''' Reprend la surveillance d'une piste et invalide ses caches.
            ''' </summary>
            Public Sub ResumeTrack(trackIndex As Integer)
                SyncLock _lock
                    If _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = False
                        _volCache.Remove(trackIndex)
                        _panCache.Remove(trackIndex)
                        _muteCache.Remove(trackIndex)
                    End If
                End SyncLock
            End Sub

            ''' <summary>Retourne True si la piste est suspendue.</summary>
            Public Function IsTrackSuspended(trackIndex As Integer) As Boolean
                SyncLock _lock
                    Dim suspended As Boolean = False
                    _tracks.TryGetValue(trackIndex, suspended)
                    Return suspended
                End SyncLock
            End Function

            ''' <summary>Démarre le monitoring.</summary>
            Public Sub Start()
                If IsRunning Then
                    Return
                End If

                AddHandler ReaperPollHub.Instance.On_PlayStateChange,
        AddressOf OnHubPlayStateChange
                AddHandler ReaperPollHub.Instance.On_PlayPositionChange,
        AddressOf OnHubPlayPositionChange

                ' ── Important : seed de l'état Play au moment de la création ──
                InitializePlayState()

                Threading.Volatile.Write(_isRunning, True)
                _thread = New Thread(AddressOf MonitorLoop) With {
        .Name = $"SendEnvMonitor_S{SendLetter}_Cat{_category}",
        .IsBackground = True,
        .Priority = ThreadPriority.Normal
    }
                _thread.Start()
            End Sub

            ''' <summary>Arrête le monitoring.</summary>
            Public Sub [Stop]()
                RemoveHandler ReaperPollHub.Instance.On_PlayStateChange,
            AddressOf OnHubPlayStateChange
                RemoveHandler ReaperPollHub.Instance.On_PlayPositionChange,
            AddressOf OnHubPlayPositionChange

                Threading.Volatile.Write(_isRunning, False)
                _pipe.Disconnect()
            End Sub

            Private Sub OnHubPlayStateChange(playState As Integer)
                Threading.Volatile.Write(_isPlaying, playState <> 0)
                RaiseEvent On_PlayStateChange(playState)
            End Sub

            Private Sub OnHubPlayPositionChange(playPos As Double)
                RaiseEvent On_PlayPositionChange(playPos)
            End Sub

            Private Sub MonitorLoop()
                RaiseEvent On_Connected()

                Dim lastProcessedRefreshVersion As Integer = -1

                While Threading.Volatile.Read(_isRunning)
                    Dim isPlayingNow As Boolean = Threading.Volatile.Read(_isPlaying)
                    Dim requestVersion As Integer = Threading.Volatile.Read(_refreshRequestVersion)

                    Dim shouldPoll As Boolean = False
                    If isPlayingNow Then
                        shouldPoll = True
                    Else
                        If requestVersion <> lastProcessedRefreshVersion Then
                            shouldPoll = True
                        End If
                    End If

                    If shouldPoll Then
                        PollTracks()
                        lastProcessedRefreshVersion = requestVersion
                    End If

                    Thread.Sleep(_interval)
                End While

                RaiseEvent On_Disconnected()
            End Sub

            Private Sub PollTracks()
                Dim snapshot As (trackIndex As Integer, isSuspended As Boolean)()
                SyncLock _lock
                    Dim tmp As New List(Of (Integer, Boolean))
                    For Each kvp In _tracks
                        tmp.Add((kvp.Key, kvp.Value))
                    Next
                    snapshot = tmp.ToArray()
                End SyncLock

                Dim ic As CultureInfo = CultureInfo.InvariantCulture

                For Each entry In snapshot
                    Dim tIdx As Integer = entry.trackIndex

                    If entry.isSuspended Then
                        Continue For
                    End If
                    If _TrackSendIndexes(tIdx) Is Nothing Then Continue For
                    Dim rawVP As String = _pipe.Request($"GET_SEND_VOL_PAN|{tIdx}|{_category}|{_TrackSendIndexes(tIdx).Value}")
                    If String.IsNullOrEmpty(rawVP) OrElse rawVP.StartsWith("ERROR", StringComparison.Ordinal) Then
                        Continue For
                    End If

                    Dim p As String() = rawVP.Split("|"c)
                    If p.Length < 2 Then
                        Continue For
                    End If

                    Dim newVol As Double = Double.MinValue
                    Dim newPan As Double = Double.MinValue

                    If Not Double.TryParse(p(0), NumberStyles.Any, ic, newVol) Then
                        Continue For
                    End If

                    If Not Double.TryParse(p(1), NumberStyles.Any, ic, newPan) Then
                        Continue For
                    End If

                    Dim rawMute As String = _pipe.Request($"GET_SEND_MUTE|{tIdx}|{_category}|{_TrackSendIndexes(tIdx).Value}")
                    If String.IsNullOrEmpty(rawMute) OrElse rawMute.StartsWith("ERROR", StringComparison.Ordinal) Then
                        Continue For
                    End If

                    Dim newMute As Boolean = rawMute = "1"

                    Dim volChg As Boolean = False
                    Dim panChg As Boolean = False
                    Dim muteChg As Boolean = False
                    Dim stillWatched As Boolean = False
                    Dim isSuspended As Boolean = False

                    SyncLock _lock
                        stillWatched = _tracks.TryGetValue(tIdx, isSuspended)

                        If stillWatched Then
                            If isSuspended = False Then
                                Dim prevVol As Double = Double.MinValue
                                volChg = Not _volCache.TryGetValue(tIdx, prevVol) OrElse prevVol <> newVol
                                If volChg Then
                                    _volCache(tIdx) = newVol
                                End If

                                Dim prevPan As Double = Double.MinValue
                                panChg = Not _panCache.TryGetValue(tIdx, prevPan) OrElse prevPan <> newPan
                                If panChg Then
                                    _panCache(tIdx) = newPan
                                End If

                                Dim prevMute As Boolean = False
                                muteChg = Not _muteCache.TryGetValue(tIdx, prevMute) OrElse prevMute <> newMute
                                If muteChg Then
                                    _muteCache(tIdx) = newMute
                                End If
                            End If
                        End If
                    End SyncLock

                    If stillWatched AndAlso isSuspended = False Then
                        If volChg Then
                            RaiseEvent On_VolChange(tIdx, newVol)
                        End If
                        If panChg Then
                            RaiseEvent On_PanChange(tIdx, newPan)
                        End If
                        If muteChg Then
                            RaiseEvent On_MuteChange(tIdx, newMute)
                        End If
                    End If
                Next
            End Sub

            ''' <summary>Libère les ressources du monitor.</summary>
            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
                    _pipe.Dispose()
                    _disposed = True
                End If
            End Sub

        End Class
        ''' <summary>
        ''' Surveille en continu les valeurs UI (post-automation) de tous les sends
        ''' d'une piste fixe : volume, pan et mute.
        ''' Le polling est actif uniquement quand Reaper est en mode Play.
        ''' </summary>
        Public Class ReaperTrackSendEnveloppeMonitor
            Implements IDisposable

            Private ReadOnly _pipe As New ReaperPollPipe()
            Private ReadOnly _trackIndex As Integer
            Private ReadOnly _category As Integer
            Private _refreshRequestVersion As Integer = 0

            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 50
            Private _isPlaying As Boolean = False

            Private ReadOnly _lock As New Object()

            Private ReadOnly _sends As New Dictionary(Of Integer, Boolean)

            Private ReadOnly _volCache As New Dictionary(Of Integer, Double)
            Private ReadOnly _panCache As New Dictionary(Of Integer, Double)
            Private ReadOnly _muteCache As New Dictionary(Of Integer, Boolean)

            Public Event On_Connected()
            Public Event On_Disconnected()
            Public Event On_VolChange(sendIndex As Integer, vol As Double)
            Public Event On_PanChange(sendIndex As Integer, pan As Double)
            Public Event On_MuteChange(sendIndex As Integer, mute As Boolean)
            Public Event On_PlayStateChange(playState As Integer)
            Public Event On_PlayPositionChange(playPos As Double)

            Public Sub New(trackIndex As Integer,
                   Optional category As SendCategory = SendCategory.Sends)
                _trackIndex = trackIndex
                _category = CInt(category)
            End Sub

            Public ReadOnly Property TrackIndex As Integer
                Get
                    Return _trackIndex
                End Get
            End Property

            Public ReadOnly Property Category As SendCategory
                Get
                    Return CType(_category, SendCategory)
                End Get
            End Property

            Public Property Interval As Integer
                Get
                    Return _interval
                End Get
                Set
                    _interval = Value
                End Set
            End Property

            Public ReadOnly Property IsRunning As Boolean
                Get
                    Return Threading.Volatile.Read(_isRunning)
                End Get
            End Property

            Public Sub WatchSend(sendIndex As Integer)
                SyncLock _lock
                    If _sends.ContainsKey(sendIndex) = False Then
                        _sends(sendIndex) = False
                    End If
                End SyncLock
            End Sub

            Public Sub WatchSends(sendIndexes As IEnumerable(Of Integer))
                For Each sendIndex In sendIndexes
                    WatchSend(sendIndex)
                Next
            End Sub

            Public Sub UnwatchSend(sendIndex As Integer)
                SyncLock _lock
                    _sends.Remove(sendIndex)
                    _volCache.Remove(sendIndex)
                    _panCache.Remove(sendIndex)
                    _muteCache.Remove(sendIndex)
                End SyncLock
            End Sub

            Public Sub ClearWatched()
                SyncLock _lock
                    _sends.Clear()
                    _volCache.Clear()
                    _panCache.Clear()
                    _muteCache.Clear()
                End SyncLock
            End Sub

            Public Sub SuspendSend(sendIndex As Integer)
                SyncLock _lock
                    If _sends.ContainsKey(sendIndex) Then
                        _sends(sendIndex) = True
                    End If
                End SyncLock
            End Sub

            Public Sub ResumeSend(sendIndex As Integer)
                SyncLock _lock
                    If _sends.ContainsKey(sendIndex) Then
                        _sends(sendIndex) = False
                        _volCache.Remove(sendIndex)
                        _panCache.Remove(sendIndex)
                        _muteCache.Remove(sendIndex)
                    End If
                End SyncLock
            End Sub

            Public Function IsSendSuspended(sendIndex As Integer) As Boolean
                SyncLock _lock
                    Dim suspended As Boolean = False
                    _sends.TryGetValue(sendIndex, suspended)
                    Return suspended
                End SyncLock
            End Function

            Public Sub RequestRefresh()
                Threading.Interlocked.Increment(_refreshRequestVersion)
            End Sub

            Private Sub InitializePlayState()
                Dim raw As String = _pipe.Request("POLL_STATE")
                If String.IsNullOrEmpty(raw) Then
                    Threading.Volatile.Write(_isPlaying, False)
                    Return
                End If

                If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                    Threading.Volatile.Write(_isPlaying, False)
                    Return
                End If

                Dim p As String() = raw.Split("|"c)
                If p.Length < 2 Then
                    Threading.Volatile.Write(_isPlaying, False)
                    Return
                End If

                Dim playState As Integer = 0
                If Integer.TryParse(p(1), playState) Then
                    Threading.Volatile.Write(_isPlaying, playState <> 0)
                Else
                    Threading.Volatile.Write(_isPlaying, False)
                End If
            End Sub

            Public Sub Start()
                If IsRunning Then
                    Return
                End If

                AddHandler ReaperPollHub.Instance.On_PlayStateChange, AddressOf OnHubPlayStateChange
                AddHandler ReaperPollHub.Instance.On_PlayPositionChange, AddressOf OnHubPlayPositionChange

                InitializePlayState()

                Threading.Volatile.Write(_isRunning, True)
                _thread = New Thread(AddressOf MonitorLoop) With {
            .Name = $"TrackSendEnvMonitor_T{_trackIndex}_Cat{_category}",
            .IsBackground = True,
            .Priority = ThreadPriority.Normal
        }
                _thread.Start()
            End Sub

            Public Sub [Stop]()
                RemoveHandler ReaperPollHub.Instance.On_PlayStateChange, AddressOf OnHubPlayStateChange
                RemoveHandler ReaperPollHub.Instance.On_PlayPositionChange, AddressOf OnHubPlayPositionChange

                Threading.Volatile.Write(_isRunning, False)
                _pipe.Disconnect()
            End Sub

            Private Sub OnHubPlayStateChange(playState As Integer)
                Threading.Volatile.Write(_isPlaying, playState <> 0)
                RaiseEvent On_PlayStateChange(playState)
            End Sub

            Private Sub OnHubPlayPositionChange(playPos As Double)
                RaiseEvent On_PlayPositionChange(playPos)
            End Sub

            Private Sub MonitorLoop()
                RaiseEvent On_Connected()

                Dim lastProcessedRefreshVersion As Integer = -1

                While Threading.Volatile.Read(_isRunning)
                    Dim isPlayingNow As Boolean = Threading.Volatile.Read(_isPlaying)
                    Dim requestVersion As Integer = Threading.Volatile.Read(_refreshRequestVersion)

                    Dim shouldPoll As Boolean = False
                    If isPlayingNow Then
                        shouldPoll = True
                    Else
                        If requestVersion <> lastProcessedRefreshVersion Then
                            shouldPoll = True
                        End If
                    End If

                    If shouldPoll Then
                        PollSends()
                        lastProcessedRefreshVersion = requestVersion
                    End If

                    Thread.Sleep(_interval)
                End While

                RaiseEvent On_Disconnected()
            End Sub

            Private Sub PollSends()
                Dim snapshot As (sendIndex As Integer, isSuspended As Boolean)()

                SyncLock _lock
                    Dim tmp As New List(Of (Integer, Boolean))
                    For Each kvp In _sends
                        tmp.Add((kvp.Key, kvp.Value))
                    Next
                    snapshot = tmp.ToArray()
                End SyncLock

                Dim ic As CultureInfo = CultureInfo.InvariantCulture

                For Each entry In snapshot
                    Dim sIdx As Integer = entry.sendIndex

                    Dim rawVP As String = _pipe.Request($"GET_SEND_VOL_PAN|{_trackIndex}|{_category}|{sIdx}")
                    If String.IsNullOrEmpty(rawVP) OrElse rawVP.StartsWith("ERROR", StringComparison.Ordinal) Then
                        Continue For
                    End If

                    Dim p As String() = rawVP.Split("|"c)
                    If p.Length < 2 Then
                        Continue For
                    End If

                    Dim newVol As Double = Double.MinValue
                    Dim newPan As Double = Double.MinValue
                    Dim newMute As Boolean = False

                    If Double.TryParse(p(0), NumberStyles.Any, ic, newVol) = False Then
                        Continue For
                    End If

                    If Double.TryParse(p(1), NumberStyles.Any, ic, newPan) = False Then
                        Continue For
                    End If

                    Dim rawMute As String = _pipe.Request($"GET_SEND_MUTE|{_trackIndex}|{_category}|{sIdx}")
                    If String.IsNullOrEmpty(rawMute) OrElse rawMute.StartsWith("ERROR", StringComparison.Ordinal) Then
                        Continue For
                    End If
                    newMute = rawMute = "1"

                    Dim volChg As Boolean = False
                    Dim panChg As Boolean = False
                    Dim muteChg As Boolean = False
                    Dim stillWatched As Boolean = False
                    Dim isSuspended As Boolean = False

                    SyncLock _lock
                        stillWatched = _sends.TryGetValue(sIdx, isSuspended)
                        If stillWatched Then
                            If isSuspended = False Then
                                Dim prevVol As Double = Double.MinValue
                                volChg = Not _volCache.TryGetValue(sIdx, prevVol) OrElse prevVol <> newVol
                                If volChg Then
                                    _volCache(sIdx) = newVol
                                End If

                                Dim prevPan As Double = Double.MinValue
                                panChg = Not _panCache.TryGetValue(sIdx, prevPan) OrElse prevPan <> newPan
                                If panChg Then
                                    _panCache(sIdx) = newPan
                                End If

                                Dim prevMute As Boolean = False
                                muteChg = Not _muteCache.TryGetValue(sIdx, prevMute) OrElse prevMute <> newMute
                                If muteChg Then
                                    _muteCache(sIdx) = newMute
                                End If
                            End If
                        End If
                    End SyncLock

                    If stillWatched AndAlso isSuspended = False Then
                        If volChg Then
                            RaiseEvent On_VolChange(sIdx, newVol)
                        End If

                        If panChg Then
                            RaiseEvent On_PanChange(sIdx, newPan)
                        End If

                        If muteChg Then
                            RaiseEvent On_MuteChange(sIdx, newMute)
                        End If
                    End If
                Next
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                If _disposed = False Then
                    [Stop]()
                    _pipe.Dispose()
                    _disposed = True
                End If
            End Sub

        End Class


        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER POLL PIPE — connexion persistante pour la détection de changement
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Connexion pipe persistante vers le bridge Reaper.
        ''' Envoie <c>POLL_STATE</c> à chaque appel de <see cref="Poll"/> et déclenche
        ''' des événements uniquement si l'état a changé depuis le dernier appel.
        ''' Ne rafraîchit les objets Reaper que lorsque c'est nécessaire.
        ''' Une seule instance par thread de polling — ne pas partager entre threads.
        ''' </summary>
        Public Class ReaperPollPipe
            Implements IDisposable

            Private Shared ReadOnly Ic As CultureInfo = CultureInfo.InvariantCulture

            ' ── Pipe ──────────────────────────────────────────────────────────────
            Private _pipe As IO.Pipes.NamedPipeClientStream
            Private _disposed As Boolean = False

            ' ── Cache état précédent ──────────────────────────────────────────────
            Private _lastStateCount As Integer = -1
            Private _lastPlayState As Integer = -1
            Private _lastPlayPos As Double = -1

            ' ── Événements ────────────────────────────────────────────────────────

            ''' <summary>
            ''' Déclenché quand le compteur d'état Reaper change.
            ''' Indique qu'une modification du projet a eu lieu (pistes, FX, envois…).
            ''' Appeler <see cref="ReaperProject.RefreshTracks"/> ou <see cref="ReaperProject.RefreshInfo"/>
            ''' en réponse à cet événement.
            ''' </summary>
            Public Event On_ProjectStateChange(stateCount As Integer)

            ''' <summary>
            ''' Déclenché quand l'état de lecture change (stop/play/pause/rec).
            ''' 0=stop  1=play  2=pause  4=rec  5=rec+play 6=pause+rec
            ''' </summary>
            Public Event On_PlayStateChange(playState As Integer)

            ''' <summary>
            ''' Déclenché à chaque appel de <see cref="Poll"/> si la position a changé.
            ''' Utiliser pour rafraîchir l'affichage horloge de la surface.
            ''' </summary>
            Public Event On_PlayPositionChange(playPos As Double)

            ' ── Connexion ─────────────────────────────────────────────────────────

            ''' <summary>True si la connexion persistante est active.</summary>
            Public ReadOnly Property IsConnected As Boolean
                Get
                    Return _pipe IsNot Nothing AndAlso _pipe.IsConnected
                End Get
            End Property

            ''' <summary>
            ''' Ouvre la connexion persistante vers le bridge.
            ''' Retourne True si la connexion a réussi.
            ''' </summary>
            Private ReadOnly _connectLock As New Object()

            Public Function Connect() As Boolean
                SyncLock _connectLock
                    Try
                        Disconnect()
                        _pipe = New IO.Pipes.NamedPipeClientStream(
                            ".", "c24_reaper_poll", IO.Pipes.PipeDirection.InOut)
                        _pipe.Connect(500)
                        Return True
                    Catch ex As Exception
                        Disconnect()
                        Return False
                    End Try
                End SyncLock
            End Function

            ''' <summary>
            ''' Ferme la connexion persistante et réinitialise le cache.
            ''' </summary>
            Public Sub Disconnect()
                Try
                    If _pipe IsNot Nothing Then
                        _pipe.Dispose()
                        _pipe = Nothing
                    End If
                Catch
                End Try
                _lastStateCount = -1
                _lastPlayState = -1
                _lastPlayPos = -1
            End Sub

            ' ── Polling ───────────────────────────────────────────────────────────

            ''' <summary>
            ''' Envoie <c>POLL_STATE</c> via la connexion persistante et déclenche
            ''' les événements uniquement pour les valeurs qui ont changé.
            ''' Reconnecte automatiquement si la connexion est perdue.
            ''' À appeler depuis le thread de timing (ex: toutes les 50 ms).
            ''' </summary>
            ''' <returns>True si au moins une valeur (état projet, lecture, position) a changé.</returns>
            Public Function Poll() As Boolean
                Dim changed As Boolean = False

                If Not IsConnected Then
                    If Not Connect() Then Return False
                End If

                Dim raw As String
                Try
                    utility.WriteLineBytes(_pipe, "POLL_STATE")
                    raw = utility.ReadLineBytes(_pipe)
                Catch
                    Disconnect()
                    Return False
                End Try

                If raw Is Nothing Then
                    Disconnect()
                    Return False
                End If
                If raw.Length = 0 OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                    Return False
                End If

                Dim sep1 As Integer = raw.IndexOf("|"c)
                If sep1 < 0 Then
                    Return False
                End If
                Dim sep2 As Integer = raw.IndexOf("|"c, sep1 + 1)
                If sep2 < 0 Then
                    Return False
                End If
                Dim sep3 As Integer = raw.IndexOf("|"c, sep2 + 1)
                Dim posEnd As Integer = raw.Length
                If sep3 >= 0 Then
                    posEnd = sep3
                End If

                ' ── Compteur d'état projet ────────────────────────────────────────
                Dim new_stateCount As Integer = 0
                Integer.TryParse(raw.Substring(0, sep1), NumberStyles.Integer, Ic, new_stateCount)
                If _lastStateCount <> new_stateCount Then
                    _lastStateCount = new_stateCount
                    changed = True
                    RaiseEvent On_ProjectStateChange(_lastStateCount)
                End If

                ' ── État de lecture ───────────────────────────────────────────────
                Dim new_playState As Integer = 0
                Integer.TryParse(raw.Substring(sep1 + 1, sep2 - sep1 - 1), NumberStyles.Integer, Ic, new_playState)
                If _lastPlayState <> new_playState Then
                    _lastPlayState = new_playState
                    changed = True
                    RaiseEvent On_PlayStateChange(_lastPlayState)
                End If

                ' ── Position de lecture ───────────────────────────────────────────
                Dim new_playPos As Double = 0
                Double.TryParse(raw.Substring(sep2 + 1, posEnd - sep2 - 1), NumberStyles.Any, Ic, new_playPos)
                If _lastPlayPos <> new_playPos Then
                    _lastPlayPos = new_playPos
                    changed = True
                    RaiseEvent On_PlayPositionChange(_lastPlayPos)
                End If

                Return changed
            End Function
            Public Sub Log_Poll()
                If Not IsConnected Then
                    Dim ok = Connect()
                    Console.WriteLine($"[PollPipe] Connect → {ok}")
                    If Not ok Then Return
                End If

                Dim raw As String
                Try
                    utility.WriteLineBytes(_pipe, "POLL_STATE")
                    raw = utility.ReadLineBytes(_pipe)
                Catch ex As Exception
                    Console.WriteLine($"[PollPipe] Erreur lecture : {ex.Message}")
                    Disconnect()
                    Return
                End Try

                Console.WriteLine($"[PollPipe] Réponse brute : '{raw}'")

                If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return
                Dim p = raw.Split("|"c)

                Console.WriteLine($"[PollPipe] Champs : {p.Length}  → {String.Join(" | ", p)}")

                If p.Length < 3 Then Return

                Dim new_stateCount As Integer = 0
                Integer.TryParse(p(0), new_stateCount)

                Console.WriteLine($"[PollPipe] stateCount : last={_lastStateCount}  new={new_stateCount}")

                If _lastStateCount <> new_stateCount Then
                    _lastStateCount = new_stateCount
                    RaiseEvent On_ProjectStateChange(_lastStateCount)
                End If

                Dim new_playState As Integer = 0
                Integer.TryParse(p(1), new_playState)
                If _lastPlayState <> new_playState Then
                    _lastPlayState = new_playState
                    RaiseEvent On_PlayStateChange(_lastPlayState)
                End If

                Dim new_playPos As Double = 0
                Double.TryParse(p(2), NumberStyles.Any, Ic, new_playPos)
                If _lastPlayPos <> new_playPos Then
                    _lastPlayPos = new_playPos
                    RaiseEvent On_PlayPositionChange(_lastPlayPos)
                End If
            End Sub

            ''' <summary>
            ''' Envoie une requête arbitraire sur la connexion persistante et retourne la réponse brute.
            ''' Reconnecte automatiquement si la connexion est perdue.
            ''' Permet à <see cref="ReaperMonitor"/> de partager la même connexion que <c>POLL_STATE</c>
            ''' sans ouvrir un nouveau pipe à chaque requête.
            ''' </summary>
            Public Function Request(query As String) As String
                If Not IsConnected Then
                    If Not Connect() Then Return String.Empty
                End If
                Try
                    utility.WriteLineBytes(_pipe, query)
                    Dim response As String = utility.ReadLineBytes(_pipe)
                    If response Is Nothing Then
                        Disconnect()
                        Return String.Empty
                    End If
                    Return response
                Catch
                    Disconnect()
                    Return String.Empty
                End Try
            End Function

            ''' <summary>Libère la connexion pipe.</summary>
            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    Disconnect()
                    _disposed = True
                End If
            End Sub

        End Class

        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER POLL HUB — distributeur central des événements de polling
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Singleton qui possède l'unique connexion persistante vers le pipe
        ''' <c>c24_reaper_poll</c> et redistribue les événements à tous les
        ''' moniteurs abonnés.
        ''' <br/>
        ''' Remplace les <see cref="ReaperPollPipe"/> individuels de chaque moniteur :
        ''' un seul thread de polling, une seule connexion pipe.
        ''' </summary>
        Public Class ReaperPollHub
            Implements IDisposable

            Private Shared ReadOnly _instance As New ReaperPollHub()

            Public Shared ReadOnly Property Instance As ReaperPollHub
                Get
                    Return _instance
                End Get
            End Property

            Private ReadOnly _pipe As New ReaperPollPipe()
            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 50
            Private ReadOnly _lock As New Object()

            ' ── Attente adaptative ────────────────────────────────────────────────
            ' Intervalle nominal tant que l'état change ; au repos (aucun changement
            ' pendant IdlePollsBeforeSlowdown polls) l'intervalle est porté à IdleInterval.
            Private Const IdlePollsBeforeSlowdown As Integer = 20
            Private Const IdleInterval As Integer = 100
            Private ReadOnly _stopEvent As New ManualResetEventSlim(False)

            ' ── Plus de _ioLock
            Public Event On_ProjectStateChange(stateCount As Integer)
            Public Event On_PlayStateChange(playState As Integer)
            Public Event On_PlayPositionChange(playPos As Double)

            Private Sub New()
            End Sub

            Public Property Interval As Integer
                Get
                    Return _interval
                End Get
                Set
                    _interval = Value
                End Set
            End Property

            Public ReadOnly Property IsRunning As Boolean
                Get
                    Return Threading.Volatile.Read(_isRunning)
                End Get
            End Property

            Public ReadOnly Property IsConnected As Boolean
                Get
                    Return _pipe.IsConnected
                End Get
            End Property

            Public Sub Start()
                SyncLock _lock
                    If IsRunning Then Return
                    _stopEvent.Reset()
                    Threading.Volatile.Write(_isRunning, True)
                    _thread = New Thread(AddressOf PollLoop) With {
                        .Name = "ReaperPollHub",
                        .IsBackground = True,
                        .Priority = ThreadPriority.AboveNormal
                    }
                    _thread.Start()
                End SyncLock
            End Sub

            Public Sub [Stop]()
                Threading.Volatile.Write(_isRunning, False)
                _stopEvent.Set()
                _pipe.Disconnect()
            End Sub

            Private Sub PollLoop()
                ' ── Câbler les events du pipe vers les events du hub ──────────
                AddHandler _pipe.On_ProjectStateChange,
                    Sub(sc As Integer) RaiseEvent On_ProjectStateChange(sc)
                AddHandler _pipe.On_PlayStateChange,
                    Sub(ps As Integer) RaiseEvent On_PlayStateChange(ps)
                AddHandler _pipe.On_PlayPositionChange,
                    Sub(pp As Double) RaiseEvent On_PlayPositionChange(pp)

                Dim idlePolls As Integer = 0

                While Threading.Volatile.Read(_isRunning)
                    ' ── Pipe exclusif au hub → aucun lock nécessaire ──────────
                    Dim changed As Boolean = _pipe.Poll()

                    If changed Then
                        idlePolls = 0
                    ElseIf idlePolls < IdlePollsBeforeSlowdown Then
                        idlePolls += 1
                    End If

                    ' ── Attente adaptative : nominale si actif, ralentie au repos ──
                    Dim waitMs As Integer = _interval
                    If idlePolls >= IdlePollsBeforeSlowdown Then
                        waitMs = Math.Max(_interval, IdleInterval)
                    End If

                    ' Interruptible : Stop() réveille immédiatement la boucle
                    _stopEvent.Wait(waitMs)
                End While
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
                    _pipe.Dispose()
                    _disposed = True
                End If
            End Sub

        End Class

    End Namespace
#End Region

#Region "Managers by pages"

    Namespace PageManagers

        ''' <summary>
        ''' Gère une collection de paramètres FX paginés et surveille leurs valeurs
        ''' via <see cref="Monitors.ReaperFxParamMonitor"/> (gated) et
        ''' <see cref="Monitors.ReaperFxParamEnvelopeMonitor"/> (continu, automation).
        ''' </summary>
        Public Class ReaperTrackFxParamsManager
            Implements IDisposable

            ' ── Identité FX (fixe à la construction) ──────────────────────────
            Private ReadOnly _trackIndex As Integer
            Private ReadOnly _fxIndex As Integer
            Private ReadOnly _MaxParamNbPerPage As Integer

            ' ── Moniteurs ─────────────────────────────────────────────────────
            Private _FxMonitor As Monitors.ReaperFxParamMonitor
            Private _EnvMonitor As Monitors.ReaperFxParamEnvelopeMonitor

            ' ── Pages ─────────────────────────────────────────────────────────
            ' _ReaperTrackFxParams et _ReaperFxParamEnvelopes pointent sur la
            ' page courante — réassignés à chaque appel de Set_ReaperFxParams.
            Private _All_ReaperFxParams As New List(Of List(Of ReaperFXParam))
            Private _All_ReaperFxParamEnvelopes As New List(Of List(Of ReaperFxParamEnvelope))
            Private _ReaperTrackFxParams As List(Of ReaperFXParam)         ' ← non ReadOnly (bug corrigé)
            Private _ReaperFxParamEnvelopes As List(Of ReaperFxParamEnvelope)
            Private _Actual_PageNb As Integer = 0

            ' ── Verrou ────────────────────────────────────────────────────────
            ' Protège Set_ReaperFxParams (WriteLock) contre les accès surface
            ' concurrents (ReadLock).
            Private ReadOnly _pageLock As New ReaderWriterLockSlim()
            Private disposedValue As Boolean

            ' ── Événements ────────────────────────────────────────────────────

            ''' <summary>
            ''' Déclenché quand la page courante change et que les paramètres /
            ''' enveloppes ont été reconfigurés.
            ''' Abonnez-vous pour recâbler la surface (ré-affecter les vpots…).
            ''' </summary>
            Public Event On_ParamsRebuilt()

            ' ── Propriétés ────────────────────────────────────────────────────

            Public ReadOnly Property TrackIndex As Integer
                Get
                    Return _trackIndex
                End Get
            End Property

            Public ReadOnly Property FxIndex As Integer
                Get
                    Return _fxIndex
                End Get
            End Property

            Public ReadOnly Property MaxParamNbPerPage As Integer
                Get
                    Return _MaxParamNbPerPage
                End Get
            End Property

            ''' <summary>Paramètres FX de la page courante.</summary>
            Public ReadOnly Property ReaperTrackFxParams As List(Of ReaperFXParam)
                Get
                    Return _ReaperTrackFxParams
                End Get
            End Property

            ''' <summary>Enveloppes d'automation de la page courante (Nothing si absente).</summary>
            Public ReadOnly Property ReaperFxParamEnvelopes As List(Of ReaperFxParamEnvelope)
                Get
                    Return _ReaperFxParamEnvelopes
                End Get
            End Property

            ''' <summary>Index 0-based de la page courante.</summary>
            Public ReadOnly Property CurrentPage As Integer
                Get
                    Return _Actual_PageNb
                End Get
            End Property

            ''' <summary>Nombre total de pages déjà créées.</summary>
            Public ReadOnly Property PageCount As Integer
                Get
                    Return _All_ReaperFxParams.Count
                End Get
            End Property

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Initialise le manager pour un FX donné, crée la page 0 et démarre
            ''' les deux moniteurs.
            ''' </summary>
            Public Sub New(trackIndex As Integer,
                           fxIndex As Integer,
                           Optional maxParamNbPerPage As Integer = 24)
                _trackIndex = trackIndex
                _fxIndex = fxIndex
                _MaxParamNbPerPage = maxParamNbPerPage

                ' ── Création initiale de la page 0 ────────────────────────────
                Set_ReaperFxParams(0)
            End Sub

            ' ── Changement de page interne ────────────────────────────────────

            ''' <summary>
            ''' Dispose et recrée les deux moniteurs, crée la page si elle n'existe
            ''' pas encore, puis démarre la surveillance.
            ''' Protégé par <see cref="_pageLock"/> (WriteLock).
            ''' </summary>
            Private Sub Set_ReaperFxParams(pageNb As Integer)
                _pageLock.EnterWriteLock()
                Try
                    ' ── Recréer le FxMonitor (gated sur ProjectStateChange) ────
                    _FxMonitor?.Dispose()
                    _FxMonitor = New Monitors.ReaperFxParamMonitor(_trackIndex, _fxIndex)
                    AddHandler _FxMonitor.On_ParamChange, AddressOf On_FXParamChange

                    ' ── Recréer l'EnvMonitor (continu, automation) ────────────
                    _EnvMonitor?.Dispose()
                    _EnvMonitor = New Monitors.ReaperFxParamEnvelopeMonitor(_trackIndex, _fxIndex)
                    AddHandler _EnvMonitor.On_ValueChange, AddressOf On_EnvMonitorValueChange
                    AddHandler _EnvMonitor.On_PlayStateChange, AddressOf OnPlayStateChange

                    ' ── Créer la page si elle n'existe pas encore ─────────────
                    ' Condition corrigée : ≥ Count au lieu de < pageNb
                    If pageNb >= _All_ReaperFxParams.Count Then
                        Dim paramPage As New List(Of ReaperFXParam)
                        Dim envPage As New List(Of ReaperFxParamEnvelope)

                        For i = 0 To _MaxParamNbPerPage - 1
                            Dim paramIndex = i + (pageNb * _MaxParamNbPerPage)

                            Dim fxParam As New ReaperFXParam(_trackIndex, _fxIndex, paramIndex)
                            ' Anti-feedback : suspendre le FxMonitor quand la
                            ' surface écrit une valeur
                            AddHandler fxParam.SuspendMonitoring, AddressOf Suspend_Monitoring
                            paramPage.Add(fxParam)

                            ' Enveloppe d'automation si elle existe
                            Dim envIdx = Reaper.GetFXEnvelopeIndex(_trackIndex, _fxIndex, paramIndex)
                            If envIdx <> -1 Then
                                envPage.Add(New ReaperFxParamEnvelope(
                                                _trackIndex, _fxIndex, paramIndex, envIdx))
                            Else
                                envPage.Add(Nothing)
                            End If
                        Next

                        _All_ReaperFxParams.Add(paramPage)
                        _All_ReaperFxParamEnvelopes.Add(envPage)
                    End If

                    ' ── Pointer sur la page courante ──────────────────────────
                    _ReaperTrackFxParams = _All_ReaperFxParams(pageNb)       ' ← bug corrigé
                    _ReaperFxParamEnvelopes = _All_ReaperFxParamEnvelopes(pageNb)

                    ' ── Câbler les moniteurs aux paramètres de la page ─────────
                    For i = 0 To _ReaperTrackFxParams.Count - 1
                        Dim paramIndex = i + (pageNb * _MaxParamNbPerPage)
                        _FxMonitor.WatchParam(paramIndex)
                        If _ReaperFxParamEnvelopes(i) IsNot Nothing Then
                            _EnvMonitor.WatchParam(paramIndex)
                        End If
                    Next

                    _FxMonitor.Start()     ' ← bug corrigé (jamais appelé avant)
                    _EnvMonitor.Start()

                    ' ── Suspendre l'EnvMonitor au démarrage (transport arrêté) ─
                    SyncAllEnvMonitorToPlayState(isPlaying:=False)

                Finally
                    _pageLock.ExitWriteLock()
                End Try
            End Sub

            ' ── Navigation ────────────────────────────────────────────────────

            Public Sub Set_Next_Page()
                _Actual_PageNb += 1
                Set_ReaperFxParams(_Actual_PageNb)
                RaiseEvent On_ParamsRebuilt()
            End Sub

            Public Sub Set_Prev_Page()
                _Actual_PageNb = Math.Max(0, _Actual_PageNb - 1)
                Set_ReaperFxParams(_Actual_PageNb)
                RaiseEvent On_ParamsRebuilt()
            End Sub

            ' ── Suspension par paramètre ──────────────────────────────────────

            ''' <summary>
            ''' Suspend les deux moniteurs pour un paramètre de la page donnée.
            ''' Appeler avant d'écrire une valeur depuis la surface.
            ''' </summary>
            Public Sub SuspendParam(pageParamIndex As Integer)
                Dim paramIndex = GetParmIndex(pageParamIndex)
                _FxMonitor?.SuspendParam(paramIndex)
                _EnvMonitor?.SuspendParam(paramIndex)
            End Sub

            ''' <summary>Réactive les deux moniteurs pour un paramètre de la page donnée.</summary>
            Public Sub ResumeParam(pageParamIndex As Integer)
                Dim paramIndex = GetParmIndex(pageParamIndex)
                _FxMonitor?.ResumeParam(paramIndex)
                _EnvMonitor?.ResumeParam(paramIndex)
            End Sub

            ' ── Gestion des enveloppes ────────────────────────────────────────

            Public Sub AddFxParamEnv(pageParamIndex As Integer)
                _pageLock.EnterReadLock()
                Try
                    If _ReaperFxParamEnvelopes(pageParamIndex) IsNot Nothing Then Return
                    Dim paramIndex = GetParmIndex(pageParamIndex)
                    Dim envIndex = Reaper.GetFXEnvelopeIndex(_trackIndex, _fxIndex, paramIndex, True)
                    _ReaperFxParamEnvelopes(pageParamIndex) =
                        New ReaperFxParamEnvelope(_trackIndex, _fxIndex, paramIndex, envIndex)
                    _EnvMonitor.WatchParam(paramIndex)
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Sub

            Public Sub RemoveFxParamEnv(pageParamIndex As Integer)
                _pageLock.EnterReadLock()
                Try
                    If _ReaperFxParamEnvelopes(pageParamIndex) Is Nothing Then Return
                    Dim paramIndex = GetParmIndex(pageParamIndex)
                    utility.DeleteFXEnvelope(_trackIndex, _fxIndex, paramIndex)
                    _ReaperFxParamEnvelopes(pageParamIndex) = Nothing
                    _EnvMonitor.UnwatchParam(paramIndex)
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Sub

            Public Sub Set_ArmedFxParam(pageParamIndex As Integer, status As Boolean)
                _pageLock.EnterReadLock()
                Try
                    SuspendParam(pageParamIndex)
                    _ReaperFxParamEnvelopes(pageParamIndex).Armed = status
                    ResumeParam(pageParamIndex)
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Sub

            Public Function Get_ArmedFxParam(pageParamIndex As Integer) As Boolean
                _pageLock.EnterReadLock()
                Try
                    Return _ReaperFxParamEnvelopes(pageParamIndex)?.Armed
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Function

            Public Function Get_FxParamEnvStatus() As List(Of Boolean)
                _pageLock.EnterReadLock()
                Try
                    Dim status As New List(Of Boolean)
                    For Each env In _ReaperFxParamEnvelopes
                        status.Add(env IsNot Nothing)
                    Next
                    Return status
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Function

            ' ── Gestion du transport ──────────────────────────────────────────

            ''' <summary>
            ''' Suspend ou réactive l'<see cref="Monitors.ReaperFxParamEnvelopeMonitor"/>
            ''' pour tous les paramètres de la page selon l'état de lecture.
            ''' Quand le transport est arrêté, les valeurs d'enveloppe sont statiques
            ''' → les requêtes <c>GET_FX_PARAM_NORM</c> sont inutiles.
            ''' </summary>
            Private Sub SyncAllEnvMonitorToPlayState(isPlaying As Boolean)
                If _ReaperTrackFxParams Is Nothing Then Return
                For i = 0 To _ReaperTrackFxParams.Count - 1
                    Dim paramIndex = GetParmIndex(i)
                    If isPlaying Then
                        _EnvMonitor?.ResumeParam(paramIndex)
                    Else
                        _EnvMonitor?.SuspendParam(paramIndex)
                    End If
                Next
            End Sub

            ''' <summary>
            ''' Réactive ou suspend l'EnvMonitor selon l'état de lecture Reaper.
            ''' 0=stop → suspension | 1=play / 4=rec / 5=rec+play → activation
            ''' </summary>
            Private Sub OnPlayStateChange(playState As Integer)
                SyncAllEnvMonitorToPlayState(isPlaying:=playState <> 0)
            End Sub

            ' ── Handlers internes ─────────────────────────────────────────────

            ''' <summary>
            ''' Déclenché par <see cref="ReaperFXParam.SuspendMonitoring"/> :
            ''' suspend les deux moniteurs pendant qu'une valeur est écrite
            ''' depuis la surface (anti-feedback).
            ''' </summary>
            Private Sub Suspend_Monitoring(paramIndex As Integer, status As Boolean)
                If status Then
                    _FxMonitor?.SuspendParam(paramIndex)
                    _EnvMonitor?.SuspendParam(paramIndex)
                Else
                    _FxMonitor?.ResumeParam(paramIndex)
                    _EnvMonitor?.ResumeParam(paramIndex)
                End If
            End Sub

            Private Sub On_EnvMonitorValueChange(paramIndex As Integer, normValue As Double)
                Dim pageIdx = GetPageParmIndex(paramIndex)
                If pageIdx < 0 OrElse pageIdx >= _ReaperTrackFxParams?.Count Then Return
                _ReaperTrackFxParams(pageIdx).SetNormValueFromMonitor(normValue)
            End Sub

            Private Sub On_FXParamChange(paramIndex As Integer, normValue As Double)
                Dim pageIdx = GetPageParmIndex(paramIndex)
                If pageIdx < 0 OrElse pageIdx >= _ReaperTrackFxParams?.Count Then Return
                _ReaperTrackFxParams(pageIdx).SetNormValueFromMonitor(normValue)
                If _ReaperFxParamEnvelopes(pageIdx) IsNot Nothing Then
                    _ReaperFxParamEnvelopes(pageIdx).Value = normValue
                End If
            End Sub

            ' ── Utilitaires ───────────────────────────────────────────────────

            Private Function GetPageParmIndex(paramIndex As Integer) As Integer
                Return paramIndex - (_Actual_PageNb * _MaxParamNbPerPage)
            End Function

            Private Function GetParmIndex(pageParamIndex As Integer) As Integer
                Return pageParamIndex + (_Actual_PageNb * _MaxParamNbPerPage)
            End Function

            ' ── IDisposable ───────────────────────────────────────────────────

            Protected Overridable Sub Dispose(disposing As Boolean)
                If Not disposedValue Then
                    If disposing Then
                        _FxMonitor?.Dispose()
                        _EnvMonitor?.Dispose()
                        _pageLock.Dispose()
                    End If
                    disposedValue = True
                End If
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                Dispose(disposing:=True)
                GC.SuppressFinalize(Me)
            End Sub

        End Class

        ''' <summary>
        ''' Gère une collection de pistes paginées et surveille en continu
        ''' leur volume, pan et mute POST-automation via
        ''' <see cref="Monitors.ReaperTrackEnvelopeMonitor"/>.
        ''' </summary>
        Public Class ReaperTracksManager
            Implements IDisposable
            Private _Tracks As New List(Of ReaperTrack)
            Private ReadOnly _project As ReaperProject
            Private _TrackMonitor As Monitors.ReaperTrackMonitor

            Private _EnvMonitor As Monitors.ReaperTrackEnvelopeMonitor
            Private _FxManager As ReaperTrackFxManager         ' ← AJOUT
            Private ReadOnly _maxTracksNbPerPage As Integer
            Private ReadOnly _all_Tracks As New List(Of List(Of ReaperTrack))
            Private ReadOnly _all_VolEnvelopes As New Dictionary(Of Integer, Dictionary(Of Integer, ReaperTrackVolEnvelope))
            Private ReadOnly _all_PanEnvelopes As New Dictionary(Of Integer, Dictionary(Of Integer, ReaperTrackPanEnvelope))
            Private ReadOnly _all_MuteEnvelopes As New Dictionary(Of Integer, Dictionary(Of Integer, ReaperTrackMuteEnvelope))

            Private _VolEnvelopes As New Dictionary(Of Integer, ReaperTrackVolEnvelope)
            Private _PanEnvelopes As New Dictionary(Of Integer, ReaperTrackPanEnvelope)
            Private _MuteEnvelopes As New Dictionary(Of Integer, ReaperTrackMuteEnvelope)
            Private _Actual_PageNb As Integer = 0
            Private ReadOnly _trackPageMap As New Dictionary(Of Integer, Integer)
            Private disposedValue As Boolean
            Private ReadOnly _pageLock As New ReaderWriterLockSlim()
            Private _sessionConfig As ReaperSessionConfig

            Public Event On_TracksRebuilt()

            ' ── Propriétés ────────────────────────────────────────────────────

            Public ReadOnly Property TrackMonitor As Monitors.ReaperTrackMonitor
                Get
                    Return _TrackMonitor
                End Get
            End Property
            Public ReadOnly Property EnvMonitor As Monitors.ReaperTrackEnvelopeMonitor
                Get
                    Return _EnvMonitor
                End Get
            End Property

            Public ReadOnly Property Tracks As List(Of ReaperTrack)
                Get
                    Return _Tracks
                End Get
            End Property

            Public ReadOnly Property VolEnvelopes As Dictionary(Of Integer, ReaperTrackVolEnvelope)
                Get
                    Return _VolEnvelopes
                End Get
            End Property

            Public ReadOnly Property PanEnvelopes As Dictionary(Of Integer, ReaperTrackPanEnvelope)
                Get
                    Return _PanEnvelopes
                End Get
            End Property

            Public ReadOnly Property MuteEnvelopes As Dictionary(Of Integer, ReaperTrackMuteEnvelope)
                Get
                    Return _MuteEnvelopes
                End Get
            End Property

            ''' <summary>
            ''' Gestionnaire FX de la page courante.
            ''' Permet de lire/modifier les FX de chaque piste visible.
            ''' Nothing si <see cref="SessionConfig"/> n'est pas défini.
            ''' </summary>
            Public ReadOnly Property FxManager As ReaperTrackFxManager    ' ← AJOUT
                Get
                    Return _FxManager
                End Get
            End Property

            Public ReadOnly Property CurrentPage As Integer
                Get
                    Return _Actual_PageNb
                End Get
            End Property

            Public ReadOnly Property PageCount As Integer
                Get
                    Return _all_Tracks.Count
                End Get
            End Property
            ''' <summary>True si une page précédente existe (navigation gauche possible).</summary>
            Public ReadOnly Property IsPageLeftExist As Boolean
                Get
                    Return _Actual_PageNb > 0
                End Get
            End Property

            ''' <summary>True si une page suivante existe (navigation droite possible).</summary>
            Public ReadOnly Property IsPageRightExist As Boolean
                Get
                    Return _Actual_PageNb < _all_Tracks.Count - 1
                End Get
            End Property
            Public Property SessionConfig As ReaperSessionConfig
                Get
                    Return _sessionConfig
                End Get
                Set
                    _sessionConfig = Value
                End Set
            End Property

            Public Function IsTrackEqFxEmpty(trackIndex As Integer) As Boolean
                If _sessionConfig Is Nothing Then Return True
                Dim fxCollection = If(trackIndex = -1, _project.MasterTrackFx, _project.TrackFXList(trackIndex))
                fxCollection.Refresh()
                If fxCollection Is Nothing Then Return True
                For i = 0 To fxCollection.Count - 1
                    If _sessionConfig.IsEQPlugin(fxCollection(i).Name) Then
                        Return False
                    End If
                Next
                Return True
            End Function
            Public Enum FXBypassStatus
                AllBypass
                SomeBypass
                NoBypass
            End Enum
            Public Function IsTrackEqFxBypassed(trackIndex As Integer) As FXBypassStatus
                If _sessionConfig Is Nothing Then Return FXBypassStatus.NoBypass
                Dim fxCollection = If(trackIndex = -1, _project.MasterTrackFx, _project.TrackFXList(trackIndex))
                fxCollection.Refresh()
                If fxCollection Is Nothing Then Return FXBypassStatus.NoBypass
                Dim FxEnabled As Integer = 0
                Dim FxDisabled As Integer = 0
                For i = 0 To fxCollection.Count - 1
                    If _sessionConfig.IsEQPlugin(fxCollection(i).Name) Then
                        If fxCollection(i).Enabled Then
                            FxEnabled += 1
                        Else
                            FxDisabled += 1
                        End If
                    End If
                Next

                If FxEnabled >= 0 AndAlso FxDisabled = 0 Then
                    Return FXBypassStatus.NoBypass
                ElseIf FxEnabled = 0 AndAlso FxDisabled > 0 Then
                    Return FXBypassStatus.AllBypass
                Else
                    Return FXBypassStatus.SomeBypass
                End If

            End Function
            Public Sub SetTrackEqFxActive(trackIndex As Integer, Enable As Boolean)
                If _sessionConfig Is Nothing Then Exit Sub
                Dim fxCollection = If(trackIndex = -1, _project.MasterTrackFx, _project.TrackFXList(trackIndex))
                fxCollection.Refresh()
                If fxCollection Is Nothing Then Exit Sub

                For i = 0 To fxCollection.Count - 1
                    If _sessionConfig.IsEQPlugin(fxCollection(i).Name) Then
                        fxCollection(i).Enabled = Enable

                    End If
                Next


            End Sub

            Public Function IsTrackDynFxEmpty(trackIndex As Integer) As Boolean
                If _sessionConfig Is Nothing Then Return True
                Dim fxCollection = If(trackIndex = -1, _project.MasterTrackFx, _project.TrackFXList(trackIndex))
                fxCollection.Refresh()
                If fxCollection Is Nothing Then Return True
                For i = 0 To fxCollection.Count - 1
                    If _sessionConfig.IsDynPlugin(fxCollection(i).Name) Then
                        Return False
                    End If
                Next
                Return True
            End Function
            Public Function IsTrackDynFxBypassed(trackIndex As Integer) As FXBypassStatus
                If _sessionConfig Is Nothing Then Return FXBypassStatus.NoBypass
                Dim fxCollection = If(trackIndex = -1, _project.MasterTrackFx, _project.TrackFXList(trackIndex))
                fxCollection.Refresh()
                If fxCollection Is Nothing Then Return FXBypassStatus.NoBypass
                Dim FxEnabled As Integer = 0
                Dim FxDisabled As Integer = 0
                For i = 0 To fxCollection.Count - 1
                    If _sessionConfig.IsDynPlugin(fxCollection(i).Name) Then
                        If fxCollection(i).Enabled Then
                            FxEnabled += 1
                        Else
                            FxDisabled += 1
                        End If
                    End If
                Next

                If FxEnabled >= 0 AndAlso FxDisabled = 0 Then
                    Return FXBypassStatus.NoBypass
                ElseIf FxEnabled = 0 AndAlso FxDisabled > 0 Then
                    Return FXBypassStatus.AllBypass
                Else
                    Return FXBypassStatus.SomeBypass
                End If

            End Function
            Public Sub SetTrackDynFxActive(trackIndex As Integer, Enable As Boolean)
                If _sessionConfig Is Nothing Then Exit Sub
                Dim fxCollection = If(trackIndex = -1, _project.MasterTrackFx, _project.TrackFXList(trackIndex))
                fxCollection.Refresh()
                If fxCollection Is Nothing Then Exit Sub

                For i = 0 To fxCollection.Count - 1
                    If _sessionConfig.IsDynPlugin(fxCollection(i).Name) Then
                        fxCollection(i).Enabled = Enable

                    End If
                Next


            End Sub
            Public Function IsTrackGenericFxEmpty(trackIndex As Integer) As Boolean
                If _sessionConfig Is Nothing Then Return True
                Dim fxCollection = If(trackIndex = -1, _project.MasterTrackFx, _project.TrackFXList(trackIndex))
                fxCollection.Refresh()

                If fxCollection.Count > 0 Then
                    Return False
                Else
                    Return True
                End If

            End Function

            Public Function IsTrackGenericFxBypassed(trackIndex As Integer) As FXBypassStatus
                If _sessionConfig Is Nothing Then Return FXBypassStatus.NoBypass
                Dim fxCollection = If(trackIndex = -1, _project.MasterTrackFx, _project.TrackFXList(trackIndex))
                fxCollection.Refresh()
                If fxCollection Is Nothing Then Return FXBypassStatus.NoBypass
                Dim FxEnabled As Integer = 0
                Dim FxDisabled As Integer = 0
                For i = 0 To fxCollection.Count - 1
                    If _sessionConfig.IsEQPlugin(fxCollection(i).Name) OrElse _sessionConfig.IsDynPlugin(fxCollection(i).Name) Then
                        Continue For
                    End If

                    If fxCollection(i).Enabled Then
                        FxEnabled += 1
                    Else
                        FxDisabled += 1
                    End If

                Next

                If FxEnabled >= 0 AndAlso FxDisabled = 0 Then
                    Return FXBypassStatus.NoBypass
                ElseIf FxEnabled = 0 AndAlso FxDisabled > 0 Then
                    Return FXBypassStatus.AllBypass
                Else
                    Return FXBypassStatus.SomeBypass
                End If

            End Function
            Public Sub SetTrackGenericFxActive(trackIndex As Integer, Enable As Boolean)
                If _sessionConfig Is Nothing Then Exit Sub
                Dim fxCollection = If(trackIndex = -1, _project.MasterTrackFx, _project.TrackFXList(trackIndex))
                fxCollection.Refresh()
                If fxCollection Is Nothing Then Exit Sub

                For i = 0 To fxCollection.Count - 1
                    If _sessionConfig.IsEQPlugin(fxCollection(i).Name) OrElse _sessionConfig.IsDynPlugin(fxCollection(i).Name) Then
                        Continue For
                    End If
                    fxCollection(i).Enabled = Enable
                Next


            End Sub
            ' ── Constructeur ──────────────────────────────────────────────────

            Public Sub New(proj As ReaperProject,
                           Optional maxTracksNb As Integer = 24,
                           Optional sessionConfig As ReaperSessionConfig = Nothing)



                _project = proj
                _maxTracksNbPerPage = maxTracksNb
                _sessionConfig = sessionConfig

                ' ── Moniteur toujours présent, même si aucune piste ───────────
                _TrackMonitor = New Monitors.ReaperTrackMonitor()
                _TrackMonitor.Start()

                _EnvMonitor = New Monitors.ReaperTrackEnvelopeMonitor()
                _EnvMonitor.Start()

                'AddHandler _project.On_TrackGuidsChanged, AddressOf OnTrackGuidsChanged

                RebuildPages()
                If _all_Tracks.Count > 0 Then Set_Tracks(0)
            End Sub

            ' ── Handler projet ────────────────────────────────────────────────

            Private Sub OnTrackGuidsChanged(guids As IReadOnlyList(Of String))
                RebuildPages()
                Dim targetPage = Math.Min(_Actual_PageNb, Math.Max(0, _all_Tracks.Count - 1))
                If _all_Tracks.Count > 0 Then
                    _Actual_PageNb = targetPage
                    Set_Tracks(_Actual_PageNb)
                End If

            End Sub

            ' ── Construction des pages ────────────────────────────────────────

            Private Sub RebuildPages()
                _all_Tracks.Clear()
                _all_VolEnvelopes.Clear()
                _all_PanEnvelopes.Clear()
                _all_MuteEnvelopes.Clear()

                Dim trk_list As New List(Of ReaperTrack)
                For i = 0 To _project.Tracks.Count - 1
                    Dim trk = _project.Tracks(i)
                    If trk.Spacer = 1 OrElse trk_list.Count = _maxTracksNbPerPage Then
                        If trk_list.Count > 0 Then _all_Tracks.Add(trk_list)
                        trk_list = New List(Of ReaperTrack)
                    End If
                    trk_list.Add(trk)
                Next
                If trk_list.Count > 0 Then _all_Tracks.Add(trk_list)

                'For i = 0 To _all_Tracks.Count - 1
                '    _all_VolEnvelopes.Add(Nothing)
                '    _all_PanEnvelopes.Add(Nothing)
                '    _all_MuteEnvelopes.Add(Nothing)
                'Next
            End Sub

            ' ── Lecture d'état ────────────────────────────────────────────────

            Public Function Get_MuteStatus() As List(Of Boolean)
                Dim status As New List(Of Boolean)
                For Each trk In Tracks
                    status.Add(trk.Mute)
                Next
                Return status
            End Function

            Public Function Get_SoloStatus() As List(Of Integer)
                Dim status As New List(Of Integer)
                For Each trk In Tracks
                    status.Add(trk.Solo)
                Next
                Return status
            End Function

            ' ── Écriture depuis la surface ────────────────────────────────────

            Public Sub Set_MuteStatus(pageTrackIndex As Integer, status As Boolean)
                If pageTrackIndex < 0 OrElse pageTrackIndex >= _Tracks.Count Then Return
                SuspendTrack(pageTrackIndex)
                Dim trk = Tracks(pageTrackIndex)
                'Reaper.utility.SetTrackUIMute(trk.TrackIndex, status)
                trk.Mute = status
                ResumeTrack(pageTrackIndex)
            End Sub

            Public Sub SetVolume(pageTrackIndex As Integer, vol As Double)
                If pageTrackIndex < 0 OrElse pageTrackIndex >= _Tracks.Count Then Return
                SuspendTrack(pageTrackIndex)
                Dim trk = Tracks(pageTrackIndex) '.Vol = vol
                Reaper.utility.SetTrackUIVolume(trk.TrackIndex, vol)
                ResumeTrack(pageTrackIndex)
            End Sub

            Public Sub SetPan(pageTrackIndex As Integer, pan As Double)
                If pageTrackIndex < 0 OrElse pageTrackIndex >= _Tracks.Count Then Return
                SuspendTrack(pageTrackIndex)
                Dim trk = Tracks(pageTrackIndex)
                Reaper.utility.SetTrackUIPan(trk.TrackIndex, pan)
                ResumeTrack(pageTrackIndex)
            End Sub

            Public Sub SetSelect(pageTrackIndex As Integer, selected As Integer)
                If pageTrackIndex < 0 OrElse pageTrackIndex >= _Tracks.Count Then Return
                SuspendTrack(pageTrackIndex)
                Tracks(pageTrackIndex).Selected = selected
                ResumeTrack(pageTrackIndex)
            End Sub

            Public Sub ToggleSelect(pageTrackIndex As Integer)
                If pageTrackIndex < 0 OrElse pageTrackIndex >= _Tracks.Count Then Return
                Dim current = Tracks(pageTrackIndex).Selected
                SetSelect(pageTrackIndex, If(current = 0, 1, 0))
            End Sub

            Public Sub SetAutoMode(pageTrackIndex As Integer, autoMode As Integer)
                If pageTrackIndex < 0 OrElse pageTrackIndex >= _Tracks.Count Then Return
                SuspendTrack(pageTrackIndex)
                Tracks(pageTrackIndex).AutoMode = autoMode
                ResumeTrack(pageTrackIndex)
            End Sub

            Public Sub CycleAutoMode(pageTrackIndex As Integer)
                If pageTrackIndex < 0 OrElse pageTrackIndex >= _Tracks.Count Then Return
                Dim current = Tracks(pageTrackIndex).AutoMode
                Dim next_mode As Integer
                Select Case current
                    Case 0 : next_mode = 1
                    Case 1 : next_mode = 2
                    Case 2 : next_mode = 3
                    Case 3 : next_mode = 4
                    Case Else : next_mode = 0
                End Select
                SetAutoMode(pageTrackIndex, next_mode)
            End Sub

            Public Sub SetRecArm(pageTrackIndex As Integer, recArm As Integer)
                If pageTrackIndex < 0 OrElse pageTrackIndex >= _Tracks.Count Then Return
                SuspendTrack(pageTrackIndex)
                Tracks(pageTrackIndex).RecArm = recArm
                ResumeTrack(pageTrackIndex)
            End Sub
            Public Sub SetRecMode(pageTrackIndex As Integer, recMode As Integer)
                If pageTrackIndex < 0 OrElse pageTrackIndex >= _Tracks.Count Then Return
                SuspendTrack(pageTrackIndex)
                Tracks(pageTrackIndex).RecMode = recMode
                ResumeTrack(pageTrackIndex)
            End Sub

            Public Sub ToggleRecArm(pageTrackIndex As Integer)
                If pageTrackIndex < 0 OrElse pageTrackIndex >= _Tracks.Count Then Return
                Dim current = Tracks(pageTrackIndex).RecArm
                SetRecArm(pageTrackIndex, If(current = 0, 1, 0))
            End Sub

            Public Sub Set_SoloStatus(pageTrackIndex As Integer, soloState As SoloState)
                If pageTrackIndex < 0 OrElse pageTrackIndex >= _Tracks.Count Then Return
                SuspendTrack(pageTrackIndex)
                Tracks(pageTrackIndex).Solo = CInt(soloState)
                ResumeTrack(pageTrackIndex)
            End Sub

            Public Sub Set_SoloDefeat(pageTrackIndex As Integer, soloDefeatState As Boolean)
                If pageTrackIndex < 0 OrElse pageTrackIndex >= _Tracks.Count Then Return
                SuspendTrack(pageTrackIndex)
                Tracks(pageTrackIndex).SoloDefeat = soloDefeatState
                ResumeTrack(pageTrackIndex)
            End Sub

            Public Sub ToggleSolo(pageTrackIndex As Integer)
                If pageTrackIndex < 0 OrElse pageTrackIndex >= _Tracks.Count Then Return
                SuspendTrack(pageTrackIndex)
                Dim current = Tracks(pageTrackIndex).Solo
                Dim next_state = If(current = 0, SoloState.Solo, SoloState.Off)
                Set_SoloStatus(pageTrackIndex, next_state)
                ResumeTrack(pageTrackIndex)
            End Sub

            ' ── Navigation ────────────────────────────────────────────────────

            Public Sub Set_Next_Page()
                If _all_Tracks.Count = 0 Then Return
                _Actual_PageNb = Math.Min(_all_Tracks.Count - 1, _Actual_PageNb + 1)
                Set_Tracks(_Actual_PageNb)
            End Sub

            Public Sub Set_Prev_Page()
                If _all_Tracks.Count = 0 Then Return
                _Actual_PageNb = Math.Max(0, _Actual_PageNb - 1)
                Set_Tracks(_Actual_PageNb)
            End Sub

            Public Sub Set_Page(TrackIndex)
                Dim page As Integer = -1
                For i = 0 To _all_Tracks.Count - 1
                    For Each _trk In _all_Tracks(i)
                        If _trk.TrackIndex = TrackIndex Then
                            _Actual_PageNb = i
                            Set_Tracks(i)
                            Exit Sub
                        End If
                    Next

                Next
            End Sub

            ' ── Suspension par piste ──────────────────────────────────────────

            Public Sub SuspendTrack(pageTrackIndex As Integer)
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                If tIdx < 0 Then Return
                _TrackMonitor?.SuspendTrack(tIdx)
                _EnvMonitor?.SuspendTrack(tIdx)
            End Sub

            Public Sub ResumeTrack(pageTrackIndex As Integer)
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                If tIdx < 0 Then Return
                _TrackMonitor?.ResumeTrack(tIdx)
                _EnvMonitor?.ResumeTrack(tIdx)
            End Sub

            Private Sub SyncAllEnvMonitorToPlayState(isPlaying As Boolean)
                If _Tracks Is Nothing Then Return
                For Each trk In _Tracks
                    If isPlaying Then
                        _EnvMonitor?.ResumeTrack(trk.TrackIndex)
                    Else
                        _EnvMonitor?.SuspendTrack(trk.TrackIndex)
                    End If
                Next
            End Sub

            Private Sub OnPlayStateChange(playState As Integer)
                SyncAllEnvMonitorToPlayState(isPlaying:=playState <> 0)
            End Sub

            Private Sub Set_Tracks(pageNb As Integer)
                _pageLock.EnterWriteLock()
                Try
                    _TrackMonitor?.Dispose()
                    _TrackMonitor = New Monitors.ReaperTrackMonitor()


                    If _EnvMonitor IsNot Nothing Then
                        RemoveHandler _EnvMonitor.On_VolChange, AddressOf On_EnvVolChange
                        RemoveHandler _EnvMonitor.On_PanChange, AddressOf On_EnvPanChange
                        RemoveHandler _EnvMonitor.On_MuteChange, AddressOf On_EnvMuteChange
                        _EnvMonitor.Dispose()
                    End If
                    _EnvMonitor = New Monitors.ReaperTrackEnvelopeMonitor()
                    AddHandler _EnvMonitor.On_VolChange, AddressOf On_EnvVolChange
                    AddHandler _EnvMonitor.On_PanChange, AddressOf On_EnvPanChange
                    AddHandler _EnvMonitor.On_MuteChange, AddressOf On_EnvMuteChange

                    ' ── Recréer le FxManager ──────────────────────────────────   ← AJOUT
                    _FxManager?.Dispose()
                    If _sessionConfig IsNot Nothing Then
                        _FxManager = New ReaperTrackFxManager(
                            _all_Tracks(pageNb), _sessionConfig, _maxTracksNbPerPage)
                    Else
                        _FxManager = Nothing
                    End If

                    _Tracks = _all_Tracks(pageNb)

                    _trackPageMap.Clear()
                    For i = 0 To _Tracks.Count - 1
                        _trackPageMap(_Tracks(i).TrackIndex) = i
                    Next



                    If _all_VolEnvelopes.ContainsKey(pageNb) = False Then
                        Dim volPage As New Dictionary(Of Integer, ReaperTrackVolEnvelope)
                        Dim panPage As New Dictionary(Of Integer, ReaperTrackPanEnvelope)
                        Dim mutePage As New Dictionary(Of Integer, ReaperTrackMuteEnvelope)
                        For Each trk In _Tracks
                            For Each env In trk.Envelopes.Envelopes
                                Select Case env.Name
                                    Case "Volume"
                                        volPage.Add(trk.TrackIndex, env)
                                    Case "Pan"
                                        panPage.Add(trk.TrackIndex, env)
                                    Case "Mute"
                                        mutePage.Add(trk.TrackIndex, env)
                                End Select
                            Next



                        Next
                        _all_VolEnvelopes(pageNb) = volPage
                        _all_PanEnvelopes(pageNb) = panPage
                        _all_MuteEnvelopes(pageNb) = mutePage
                    End If

                    _VolEnvelopes = _all_VolEnvelopes(pageNb)
                    _PanEnvelopes = _all_PanEnvelopes(pageNb)
                    _MuteEnvelopes = _all_MuteEnvelopes(pageNb)

                    _TrackMonitor.WatchTracks(_Tracks)
                    _EnvMonitor.WatchTracks(_Tracks)




                    _TrackMonitor.Start()
                    _EnvMonitor.Start()

                Finally
                    _pageLock.ExitWriteLock()
                End Try
                RaiseEvent On_TracksRebuilt()
            End Sub
            Public Sub UnSet_MasterTrack()
                Set_Tracks(_Actual_PageNb)
            End Sub
            Public Sub Set_MasterTrack()
                _pageLock.EnterWriteLock()
                Try
                    _TrackMonitor?.Dispose()
                    _TrackMonitor = New Monitors.ReaperTrackMonitor()

                    _Tracks = New List(Of ReaperTrack)
                    _Tracks.Add(_project.MasterTrack)

                    If _EnvMonitor IsNot Nothing Then
                        RemoveHandler _EnvMonitor.On_VolChange, AddressOf On_EnvVolChange
                        RemoveHandler _EnvMonitor.On_PanChange, AddressOf On_EnvPanChange
                        RemoveHandler _EnvMonitor.On_MuteChange, AddressOf On_EnvMuteChange
                        _EnvMonitor.Dispose()
                    End If
                    _EnvMonitor = New Monitors.ReaperTrackEnvelopeMonitor()
                    AddHandler _EnvMonitor.On_VolChange, AddressOf On_EnvVolChange
                    AddHandler _EnvMonitor.On_PanChange, AddressOf On_EnvPanChange
                    AddHandler _EnvMonitor.On_MuteChange, AddressOf On_EnvMuteChange

                    ' ── Recréer le FxManager ──────────────────────────────────   ← AJOUT
                    _FxManager?.Dispose()
                    If _sessionConfig IsNot Nothing Then
                        _FxManager = New ReaperTrackFxManager(
                            _Tracks, _sessionConfig, _maxTracksNbPerPage)
                    Else
                        _FxManager = Nothing
                    End If



                    _trackPageMap.Clear()
                    For i = 0 To _Tracks.Count - 1
                        _trackPageMap(_Tracks(i).TrackIndex) = i
                    Next



                    If _all_VolEnvelopes.ContainsKey(-1) = False Then
                        Dim volPage As New Dictionary(Of Integer, ReaperTrackVolEnvelope)
                        Dim panPage As New Dictionary(Of Integer, ReaperTrackPanEnvelope)
                        Dim mutePage As New Dictionary(Of Integer, ReaperTrackMuteEnvelope)
                        For Each trk In _Tracks
                            For Each env In trk.Envelopes.Envelopes
                                Select Case env.Name
                                    Case "Volume"
                                        volPage.Add(trk.TrackIndex, env)
                                    Case "Pan"
                                        panPage.Add(trk.TrackIndex, env)
                                    Case "Mute"
                                        mutePage.Add(trk.TrackIndex, env)
                                End Select
                            Next



                        Next
                        _all_VolEnvelopes(-1) = volPage
                        _all_PanEnvelopes(-1) = panPage
                        _all_MuteEnvelopes(-1) = mutePage
                    End If

                    _VolEnvelopes = _all_VolEnvelopes(-1)
                    _PanEnvelopes = _all_PanEnvelopes(-1)
                    _MuteEnvelopes = _all_MuteEnvelopes(-1)

                    _TrackMonitor.WatchTracks(_Tracks)
                    _EnvMonitor.WatchTracks(_Tracks)




                    _TrackMonitor.Start()
                    _EnvMonitor.Start()

                Finally
                    _pageLock.ExitWriteLock()
                End Try
                RaiseEvent On_TracksRebuilt()
            End Sub

            ' ── Handlers EnvMonitor ───────────────────────────────────────────

            Private Sub On_EnvVolChange(trackIndex As Integer, vol As Double)
                Dim value As ReaperTrackVolEnvelope = Nothing
                If _VolEnvelopes.TryGetValue(trackIndex, value) Then
                    value.SetValueFromMonitor(vol)
                End If

            End Sub

            Private Sub On_EnvPanChange(trackIndex As Integer, pan As Double)
                Dim value As ReaperTrackPanEnvelope = Nothing
                If _PanEnvelopes.TryGetValue(trackIndex, value) Then
                    value.SetValueFromMonitor(pan)
                End If

            End Sub

            Private Sub On_EnvMuteChange(trackIndex As Integer, mute As Boolean)
                Dim value As ReaperTrackMuteEnvelope = Nothing
                If _MuteEnvelopes.TryGetValue(trackIndex, value) Then
                    value.SetValueFromMonitor(If(mute, 1.0, 0.0))
                End If

            End Sub


            Public Function GetVolEnvelope(TrackIndex As Integer) As ReaperTrackVolEnvelope
                For Each pageVolenvs In _all_VolEnvelopes.Values
                    If pageVolenvs.ContainsKey(TrackIndex) Then
                        Return pageVolenvs(TrackIndex)
                    End If
                Next
                Return Nothing
            End Function

            Public Function GetPanEnvelope(TrackIndex As Integer) As ReaperTrackPanEnvelope
                For Each pagePanenvs In _all_PanEnvelopes.Values
                    If pagePanenvs.ContainsKey(TrackIndex) Then
                        Return pagePanenvs(TrackIndex)
                    End If
                Next
                Return Nothing
            End Function

            Public Function GetMuteEnvelope(TrackIndex As Integer) As ReaperTrackMuteEnvelope
                For Each pageMuteenvs In _all_MuteEnvelopes.Values
                    If pageMuteenvs.ContainsKey(TrackIndex) Then
                        Return pageMuteenvs(TrackIndex)
                    End If
                Next
                Return Nothing
            End Function

            ' ── Utilitaires ───────────────────────────────────────────────────

            Public Function GetAbsoluteTrackIndex(pageTrackIndex As Integer) As Integer
                If pageTrackIndex < 0 OrElse pageTrackIndex >= _Tracks.Count Then Return -1
                Return _Tracks(pageTrackIndex).TrackIndex
            End Function

            Public Function GetRelativeTrackIndex(ReaperAbsoluteTrackIndex As Integer) As Integer?
                For i = 0 To Tracks.Count - 1
                    Dim track = Tracks(i)
                    If track.TrackIndex = ReaperAbsoluteTrackIndex Then
                        Return i
                    End If
                Next
                Return Nothing
            End Function

            ' ── IDisposable ───────────────────────────────────────────────────

            Protected Overridable Sub Dispose(disposing As Boolean)
                If Not disposedValue Then
                    If disposing Then
                        ' RemoveHandler _project.On_TrackGuidsChanged,
                        'AddressOf OnTrackGuidsChanged
                        _TrackMonitor?.Dispose()

                        If _EnvMonitor IsNot Nothing Then
                            RemoveHandler _EnvMonitor.On_VolChange, AddressOf On_EnvVolChange
                            RemoveHandler _EnvMonitor.On_PanChange, AddressOf On_EnvPanChange
                            RemoveHandler _EnvMonitor.On_MuteChange, AddressOf On_EnvMuteChange
                            _EnvMonitor.Dispose()
                        End If

                        _FxManager?.Dispose()                          ' ← AJOUT
                        _pageLock.Dispose()
                    End If
                    disposedValue = True
                End If
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                Dispose(disposing:=True)
                GC.SuppressFinalize(Me)
            End Sub

            Protected Overrides Sub Finalize()
                MyBase.Finalize()
            End Sub
        End Class

        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER TRACK SEND MANAGER — envoi N sur une liste fixe de pistes
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Surveille l'envoi <see cref="SendIndex"/> pour une liste fixe de
        ''' <see cref="ReaperTrack"/> fournie à la construction (page unique).
        ''' <br/>
        ''' Un <see cref="Monitors.ReaperSendMonitor"/> surveille vol/pan/mute et
        ''' propage les changements vers les <see cref="ReaperSend"/> via
        ''' <c>SetXxxFromMonitor</c>, déclenchant leurs propres événements.
        ''' </summary>
        Public Class ReaperTracksSendManager
            Implements IDisposable

            Private ReadOnly _VolEnvelopes As New Dictionary(Of Integer, ReaperSendVolEnvelope)
            Private ReadOnly _PanEnvelopes As New Dictionary(Of Integer, ReaperSendPanEnvelope)
            Private ReadOnly _MuteEnvelopes As New Dictionary(Of Integer, ReaperSendMuteEnvelope)
            ' ── Identité de l'envoi (fixe) ────────────────────────────────────

            Private ReadOnly _category As Integer

            ' ── Moniteur ──────────────────────────────────────────────────────
            Public _SendMonitor As Monitors.ReaperSendMonitor
            ' ── Nouveau moniteur "enveloppe/UI" ──────────────────────────────────
            Private _SendEnvMonitor As Monitors.ReaperSendEnvelopeMonitor

            ' ── Envois (un par piste fournie) ─────────────────────────────────
            Private ReadOnly _Sends As New List(Of ReaperSend)
            Private ReadOnly _trackIndexMap As New Dictionary(Of Integer, Integer)

            ' ── Verrou ────────────────────────────────────────────────────────
            Private ReadOnly _lock As New ReaderWriterLockSlim()
            Private _TrackSendIndexes As Dictionary(Of Integer, Integer?)
            Private _TrackSendNames As Dictionary(Of Integer, String)
            Private _ReaperTracksSendsEditor As Dictionary(Of Integer, ReaperTrackSendsEditor)
            Private _SendLetter As String
            Private _Tracks As List(Of ReaperTrack)
            Private disposedValue As Boolean

            ' ── Propriétés ────────────────────────────────────────────────────
            ''' <summary>
            ''' Enveloppes de volume d'envoi indexées par TrackIndex absolu.
            ''' </summary>
            Public ReadOnly Property VolEnvelopes As IReadOnlyDictionary(Of Integer, ReaperSendVolEnvelope)
                Get
                    Return _VolEnvelopes
                End Get
            End Property

            ''' <summary>
            ''' Enveloppes de pan d'envoi indexées par TrackIndex absolu.
            ''' </summary>
            Public ReadOnly Property PanEnvelopes As IReadOnlyDictionary(Of Integer, ReaperSendPanEnvelope)
                Get
                    Return _PanEnvelopes
                End Get
            End Property

            ''' <summary>
            ''' Enveloppes de mute d'envoi indexées par TrackIndex absolu.
            ''' </summary>
            Public ReadOnly Property MuteEnvelopes As IReadOnlyDictionary(Of Integer, ReaperSendMuteEnvelope)
                Get
                    Return _MuteEnvelopes
                End Get
            End Property
            ''' <summary>
            ''' Moniteur d'enveloppe d'envoi (vol/pan/mute UI, polling en lecture).
            ''' </summary>
            Public ReadOnly Property SendEnvMonitor As Monitors.ReaperSendEnvelopeMonitor
                Get
                    Return _SendEnvMonitor
                End Get
            End Property
            Public ReadOnly Property SendLetter As String
                Get
                    Return _SendLetter
                End Get
            End Property

            ''' <summary>Catégorie des envois (Sends/Receives/Hardware).</summary>
            Public ReadOnly Property Category As SendCategory
                Get
                    Return CType(_category, SendCategory)
                End Get
            End Property

            ''' <summary>Liste des envois (un par piste fournie à la construction).</summary>
            Public ReadOnly Property Sends As List(Of ReaperSend)
                Get
                    Return _Sends
                End Get
            End Property
            Public ReadOnly Property Tracks As List(Of ReaperTrack)
                Get
                    Return _Tracks
                End Get
            End Property
            Public ReadOnly Property ReaperTracksSendsEditor As Dictionary(Of Integer, ReaperTrackSendsEditor)
                Get
                    Return _ReaperTracksSendsEditor
                End Get
            End Property
            Public ReadOnly Property TrackSendNames As Dictionary(Of Integer, String)
                Get
                    Return _TrackSendNames
                End Get
            End Property
            Public ReadOnly Property TrackSendIndexes As Dictionary(Of Integer, Integer?)
                Get
                    Return _TrackSendIndexes
                End Get
            End Property

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Initialise le manager et démarre la surveillance pour les pistes données.
            ''' </summary>
            ''' <param name="tracks">Liste fixe de pistes à surveiller.</param>
            ''' <param name="sendLetter">Index 0-based de l'envoi à surveiller.</param>
            ''' <param name="category"><see cref="SendCategory"/> (défaut : Sends).</param>
            Public Sub New(tracks As IReadOnlyList(Of ReaperTrack), AllTracks As IReadOnlyList(Of Reaper.ReaperTrack),
                           sendLetter As String,
                           Optional category As SendCategory = SendCategory.Sends)
                _SendLetter = sendLetter
                _category = CInt(category)
                _Tracks = tracks

                _ReaperTracksSendsEditor = New Dictionary(Of Integer, ReaperTrackSendsEditor)
                _TrackSendNames = New Dictionary(Of Integer, String)
                _TrackSendIndexes = New Dictionary(Of Integer, Integer?)

                Console.WriteLine("sendManager:" & sendLetter)
                For Each trk In tracks

                    Dim _ReaperTrackSendsEditor As New ReaperTrackSendsEditor(trk.TrackIndex, AllTracks)
                    _ReaperTracksSendsEditor(trk.TrackIndex) = _ReaperTrackSendsEditor


                    Dim send_name As String = _ReaperTrackSendsEditor.GetSendName(sendLetter)

                    _TrackSendNames(trk.TrackIndex) = send_name

                    Dim sendIndex = _ReaperTrackSendsEditor.SendsIndexByName(send_name)

                    _TrackSendIndexes(trk.TrackIndex) = Nothing
                    Dim send As ReaperSend = Nothing
                    If sendIndex <> -1 Then
                        send = New ReaperSend(trk.TrackIndex, _category, sendIndex)
                        send.Refresh()
                        send.Name = send_name
                        _TrackSendIndexes(trk.TrackIndex) = sendIndex
                    End If
                    _Sends.Add(send)
                    Console.WriteLine("SndMng:" & If(send IsNot Nothing, send.Name, "Nothing"))
                Next

                If _SendMonitor IsNot Nothing Then
                    _SendMonitor.Dispose()
                End If

                _SendMonitor = New Monitors.ReaperSendMonitor(
    _TrackSendIndexes, sendLetter, CType(_category, SendCategory))

                If _SendEnvMonitor IsNot Nothing Then
                    RemoveHandler _SendEnvMonitor.On_VolChange, AddressOf On_EnvVolChange
                    RemoveHandler _SendEnvMonitor.On_PanChange, AddressOf On_EnvPanChange
                    RemoveHandler _SendEnvMonitor.On_MuteChange, AddressOf On_EnvMuteChange
                    _SendEnvMonitor.Dispose()
                End If
                _SendEnvMonitor = New Monitors.ReaperSendEnvelopeMonitor(
    _TrackSendIndexes, sendLetter, CType(_category, SendCategory))

                ' ── Le monitor enveloppe pilote Vol/Pan/Mute ─────────────────────────
                AddHandler _SendEnvMonitor.On_VolChange, AddressOf On_EnvVolChange
                AddHandler _SendEnvMonitor.On_PanChange, AddressOf On_EnvPanChange
                AddHandler _SendEnvMonitor.On_MuteChange, AddressOf On_EnvMuteChange

                For Each send In _Sends
                    If send IsNot Nothing Then
                        _SendMonitor.WatchTrack(send.TrackIndex)
                        _SendEnvMonitor.WatchTrack(send.TrackIndex)
                    End If
                Next
                BuildSendEnvelopeDictionaries()
                _SendMonitor.Start()
                _SendEnvMonitor.Start()
                _SendEnvMonitor.RequestRefresh()
            End Sub

            ''' <summary>
            ''' Construit les dictionnaires d'enveloppes SendVol/SendPan/SendMute
            ''' pour le send index géré par ce manager.
            ''' </summary>
            Private Sub BuildSendEnvelopeDictionaries()
                _VolEnvelopes.Clear()
                _PanEnvelopes.Clear()
                _MuteEnvelopes.Clear()

                For Each trk In _Tracks
                    If trk Is Nothing Then
                        Continue For
                    End If

                    trk.Envelopes.Refresh()

                    For Each env In trk.Envelopes.AllOfType(Of ReaperSendVolEnvelope)()
                        If env IsNot Nothing Then
                            If env.SendId = TrackSendIndexes(trk.TrackIndex) Then
                                _VolEnvelopes(trk.TrackIndex) = env
                            End If
                        End If
                    Next

                    For Each env In trk.Envelopes.AllOfType(Of ReaperSendPanEnvelope)()
                        If env IsNot Nothing Then
                            If env.SendId = TrackSendIndexes(trk.TrackIndex) Then
                                _PanEnvelopes(trk.TrackIndex) = env
                            End If
                        End If
                    Next

                    For Each env In trk.Envelopes.AllOfType(Of ReaperSendMuteEnvelope)()
                        If env IsNot Nothing Then
                            If env.SendId = TrackSendIndexes(trk.TrackIndex) Then
                                _MuteEnvelopes(trk.TrackIndex) = env
                            End If
                        End If
                    Next
                Next
            End Sub
            ' ── Suspension par piste ──────────────────────────────────────────

            ''' <summary>Suspend le moniteur pour la piste à l'index donné.</summary>
            Public Sub SuspendTrack(pageTrackIndex As Integer)
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                If tIdx < 0 Then
                    Return
                End If

                If _SendMonitor IsNot Nothing Then
                    _SendMonitor.SuspendTrack(tIdx)
                End If

                If _SendEnvMonitor IsNot Nothing Then
                    _SendEnvMonitor.SuspendTrack(tIdx)
                End If
            End Sub

            ''' <summary>Réactive le moniteur pour la piste à l'index donné.</summary>
            Public Sub ResumeTrack(pageTrackIndex As Integer)
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                If tIdx < 0 Then
                    Return
                End If

                If _SendMonitor IsNot Nothing Then
                    _SendMonitor.ResumeTrack(tIdx)
                End If

                If _SendEnvMonitor IsNot Nothing Then
                    _SendEnvMonitor.ResumeTrack(tIdx)
                    _SendEnvMonitor.RequestRefresh()
                End If
            End Sub

            ' ── Écriture depuis la surface ────────────────────────────────────

            ''' <summary>
            ''' Écrit le volume de l'envoi pour la piste à l'index donné.
            ''' Suspend le moniteur pendant l'écriture (anti-feedback).
            ''' </summary>
            ''' <param name="vol">Volume 0..4  (1.0 = 0 dB).</param>
            Public Sub Set_Vol(pageTrackIndex As Integer, vol As Double)
                _lock.EnterReadLock()
                Try
                    If pageTrackIndex < 0 OrElse pageTrackIndex >= _Sends.Count Then
                        Return
                    End If

                    Dim send = _Sends(pageTrackIndex)
                    If send Is Nothing Then
                        Return
                    End If

                    SuspendTrack(pageTrackIndex)
                    utility.SetTrackSendUIVol(send.TrackIndex, TrackSendIndexes(send.TrackIndex), vol)
                    ResumeTrack(pageTrackIndex)
                Finally
                    _lock.ExitReadLock()
                End Try
            End Sub

            ''' <summary>
            ''' Écrit le pan de l'envoi pour la piste à l'index donné.
            ''' Suspend le moniteur pendant l'écriture (anti-feedback).
            ''' </summary>
            ''' <param name="pan">Pan -1=Gauche  0=Centre  1=Droite.</param>
            Public Sub Set_Pan(pageTrackIndex As Integer, pan As Double)
                _lock.EnterReadLock()
                Try
                    If pageTrackIndex < 0 OrElse pageTrackIndex >= _Sends.Count Then
                        Return
                    End If

                    Dim send = _Sends(pageTrackIndex)
                    If send Is Nothing Then
                        Return
                    End If

                    SuspendTrack(pageTrackIndex)
                    utility.SetTrackSendUIPan(send.TrackIndex, TrackSendIndexes(send.TrackIndex), pan, CType(_category, SendCategory))
                    ResumeTrack(pageTrackIndex)
                Finally
                    _lock.ExitReadLock()
                End Try
            End Sub

            ''' <summary>
            ''' Impose l'état mute de l'envoi pour la piste à l'index donné.
            ''' Suspend le moniteur pendant l'écriture (anti-feedback).
            ''' </summary>
            Public Sub Set_Mute(pageTrackIndex As Integer, mute As Boolean)
                _lock.EnterReadLock()
                Try
                    If pageTrackIndex < 0 OrElse pageTrackIndex >= _Sends.Count Then
                        Return
                    End If

                    Dim send = _Sends(pageTrackIndex)
                    If send Is Nothing Then
                        Return
                    End If

                    SuspendTrack(pageTrackIndex)
                    utility.SetTrackSendUIMute(send.TrackIndex, TrackSendIndexes(send.TrackIndex), mute)
                    ResumeTrack(pageTrackIndex)
                Finally
                    _lock.ExitReadLock()
                End Try
            End Sub

            ''' <summary>Bascule le mute de l'envoi pour la piste à l'index donné.</summary>
            Public Sub ToggleMute(pageTrackIndex As Integer)
                _lock.EnterReadLock()
                Try
                    If pageTrackIndex < 0 OrElse pageTrackIndex >= _Sends.Count Then
                        Return
                    End If

                    Dim send = _Sends(pageTrackIndex)
                    If send Is Nothing Then
                        Return
                    End If

                    Dim current = send.Mute
                    Set_Mute(pageTrackIndex, Not current)
                Finally
                    _lock.ExitReadLock()
                End Try
            End Sub

            ' ── Lecture d'état ────────────────────────────────────────────────

            ''' <summary>Retourne la liste des volumes courants.</summary>
            Public Function Get_VolStatus() As List(Of Double)
                Dim result As New List(Of Double)
                For Each s In _Sends
                    result.Add(s.Vol)
                Next
                Return result
            End Function

            ''' <summary>Retourne la liste des pans courants.</summary>
            Public Function Get_PanStatus() As List(Of Double)
                Dim result As New List(Of Double)
                For Each s In _Sends
                    result.Add(s.Pan)
                Next
                Return result
            End Function

            ''' <summary>Retourne la liste des états mute courants.</summary>
            Public Function Get_MuteStatus() As List(Of Boolean)
                Dim result As New List(Of Boolean)
                For Each s In _Sends
                    result.Add(s.Mute)
                Next
                Return result
            End Function

            ' ── Handlers internes ─────────────────────────────────────────────
            Private Sub On_EnvVolChange(trackIndex As Integer, vol As Double)
                Dim pageIdx As Integer = -1
                If _trackIndexMap.TryGetValue(trackIndex, pageIdx) = False Then
                    Return
                End If

                If pageIdx < 0 OrElse pageIdx >= _Sends.Count Then
                    Return
                End If

                Dim send = _Sends(pageIdx)
                If send Is Nothing Then
                    Return
                End If

                send.SetVolFromMonitor(vol)

                Dim env As ReaperSendVolEnvelope = Nothing
                If _VolEnvelopes.TryGetValue(trackIndex, env) Then
                    env.SetValueFromMonitor(vol)
                    'Console.WriteLine(trackIndex & ";" & pageIdx & ";" & vol)
                End If
            End Sub

            Private Sub On_EnvPanChange(trackIndex As Integer, pan As Double)
                Dim pageIdx As Integer = -1
                If _trackIndexMap.TryGetValue(trackIndex, pageIdx) = False Then
                    Return
                End If

                If pageIdx < 0 OrElse pageIdx >= _Sends.Count Then
                    Return
                End If

                Dim send = _Sends(pageIdx)
                If send Is Nothing Then
                    Return
                End If

                send.SetPanFromMonitor(pan)

                Dim env As ReaperSendPanEnvelope = Nothing
                If _PanEnvelopes.TryGetValue(trackIndex, env) Then
                    env.SetValueFromMonitor(pan)
                End If
            End Sub

            Private Sub On_EnvMuteChange(trackIndex As Integer, mute As Boolean)
                Dim pageIdx As Integer = -1
                If _trackIndexMap.TryGetValue(trackIndex, pageIdx) = False Then
                    Return
                End If

                If pageIdx < 0 OrElse pageIdx >= _Sends.Count Then
                    Return
                End If

                Dim send = _Sends(pageIdx)
                If send Is Nothing Then
                    Return
                End If

                send.SetMuteFromMonitor(mute)

                Dim env As ReaperSendMuteEnvelope = Nothing
                If _MuteEnvelopes.TryGetValue(trackIndex, env) Then
                    env.SetValueFromMonitor(mute)
                End If
            End Sub
            Private Sub On_MonitorVolChange(trackIndex As Integer, vol As Double)
                Dim pageIdx As Integer = -1
                If _trackIndexMap.TryGetValue(trackIndex, pageIdx) = False Then
                    Return
                End If

                If pageIdx < 0 OrElse pageIdx >= _Sends.Count Then
                    Return
                End If

                Dim send = _Sends(pageIdx)
                If send Is Nothing Then
                    Return
                End If

                send.SetVolFromMonitor(vol)
            End Sub

            Private Sub On_MonitorPanChange(trackIndex As Integer, pan As Double)
                Dim pageIdx As Integer = -1
                If _trackIndexMap.TryGetValue(trackIndex, pageIdx) = False Then
                    Return
                End If

                If pageIdx < 0 OrElse pageIdx >= _Sends.Count Then
                    Return
                End If

                Dim send = _Sends(pageIdx)
                If send Is Nothing Then
                    Return
                End If

                send.SetPanFromMonitor(pan)
            End Sub

            Private Sub On_MonitorMuteChange(trackIndex As Integer, mute As Boolean)
                Dim pageIdx As Integer = -1
                If _trackIndexMap.TryGetValue(trackIndex, pageIdx) = False Then
                    Return
                End If

                If pageIdx < 0 OrElse pageIdx >= _Sends.Count Then
                    Return
                End If

                Dim send = _Sends(pageIdx)
                If send Is Nothing Then
                    Return
                End If

                send.SetMuteFromMonitor(mute)
            End Sub

            ' ── Utilitaires ───────────────────────────────────────────────────

            Private Function GetAbsoluteTrackIndex(pageTrackIndex As Integer) As Integer
                If pageTrackIndex < 0 OrElse pageTrackIndex >= _Sends.Count Then
                    Return -1
                End If

                Dim send = _Sends(pageTrackIndex)
                If send Is Nothing Then
                    Return -1
                End If

                Return send.TrackIndex
            End Function

            ' ── IDisposable ───────────────────────────────────────────────────

            Protected Overridable Sub Dispose(disposing As Boolean)
                If Not disposedValue Then
                    If disposing Then
                        If _SendMonitor IsNot Nothing Then
                            _SendMonitor.Dispose()
                        End If

                        If _SendEnvMonitor IsNot Nothing Then
                            RemoveHandler _SendEnvMonitor.On_VolChange, AddressOf On_EnvVolChange
                            RemoveHandler _SendEnvMonitor.On_PanChange, AddressOf On_EnvPanChange
                            RemoveHandler _SendEnvMonitor.On_MuteChange, AddressOf On_EnvMuteChange
                            _SendEnvMonitor.Dispose()
                        End If
                        _VolEnvelopes.Clear()
                        _PanEnvelopes.Clear()
                        _MuteEnvelopes.Clear()
                        _lock.Dispose()
                    End If
                    disposedValue = True
                End If
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                Dispose(disposing:=True)
                GC.SuppressFinalize(Me)
            End Sub

        End Class



        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER TRACK SENDS MANAGER — tous les sends d'une seule piste
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Surveille tous les envois d'une piste fixe fournis à la construction.
        ''' Symétrique de <see cref="ReaperTrackSendManager"/> :
        ''' ici la piste est fixe et les <c>sendIndex</c> varient.
        ''' <br/>
        ''' Un <see cref="ReaperTrackSendsMonitor"/> surveille vol/pan/mute/sendMode
        ''' et propage les changements vers les <see cref="ReaperSend"/> correspondants.
        ''' </summary>
        Public Class ReaperTrackSendsManager
            Implements IDisposable
            Private ReadOnly _VolEnvelopes As New Dictionary(Of Integer, ReaperSendVolEnvelope)
            Private ReadOnly _PanEnvelopes As New Dictionary(Of Integer, ReaperSendPanEnvelope)
            Private ReadOnly _MuteEnvelopes As New Dictionary(Of Integer, ReaperSendMuteEnvelope)
            Private ReadOnly _trackIndex As Integer
            Private ReadOnly _category As Integer
            Private _Monitor As ReaperTrackSendsMonitor
            Private _EnvMonitor As Monitors.ReaperTrackSendEnveloppeMonitor

            ' ── Envois (un par sendIndex fourni) ──────────────────────────────
            Private ReadOnly _Sends As New List(Of ReaperSend)

            Private _TrackSendNames As New List(Of String)
            Private _ReaperTrackSendsEditor As ReaperTrackSendsEditor

            Private ReadOnly _lock As New ReaderWriterLockSlim()
            Private _Reapertrack As ReaperTrack
            Private disposedValue As Boolean

            ' ── Propriétés ────────────────────────────────────────────────────
            ''' <summary>
            ''' Monitor enveloppe des sends de la piste courante (vol/pan/mute UI).
            ''' </summary>
            Public ReadOnly Property EnvMonitor As Monitors.ReaperTrackSendEnveloppeMonitor
                Get
                    Return _EnvMonitor
                End Get
            End Property
            Public ReadOnly Property VolEnvelopes As IReadOnlyDictionary(Of Integer, ReaperSendVolEnvelope)
                Get
                    Return _VolEnvelopes
                End Get
            End Property

            Public ReadOnly Property PanEnvelopes As IReadOnlyDictionary(Of Integer, ReaperSendPanEnvelope)
                Get
                    Return _PanEnvelopes
                End Get
            End Property

            Public ReadOnly Property MuteEnvelopes As IReadOnlyDictionary(Of Integer, ReaperSendMuteEnvelope)
                Get
                    Return _MuteEnvelopes
                End Get
            End Property

            ''' <summary>Index 0-based de la piste surveillée.</summary>
            Public ReadOnly Property TrackIndex As Integer
                Get
                    Return _trackIndex
                End Get
            End Property

            ''' <summary>Catégorie des envois.</summary>
            Public ReadOnly Property Category As SendCategory
                Get
                    Return CType(_category, SendCategory)
                End Get
            End Property

            ''' <summary>Liste des envois surveillés.</summary>
            Public ReadOnly Property Sends As List(Of ReaperSend)
                Get
                    Return _Sends
                End Get
            End Property

            ''' <summary>Moniteur interne (accès direct pour abonnements externes).</summary>
            Public ReadOnly Property Monitor As ReaperTrackSendsMonitor
                Get
                    Return _Monitor
                End Get
            End Property

            Public ReadOnly Property ReaperTrackSendsEditor As ReaperTrackSendsEditor
                Get
                    Return _ReaperTrackSendsEditor
                End Get
            End Property
            Public ReadOnly Property TrackSendNames As List(Of String)
                Get
                    Return _TrackSendNames
                End Get
            End Property
            Public ReadOnly Property Reapertrack As ReaperTrack
                Get
                    Return _Reapertrack
                End Get
            End Property
            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Initialise le manager pour la piste donnée et les sendIndexes fournis.
            ''' </summary>
            ''' <param name="trackIndex">Index 0-based de la piste.</param>
            ''' <param name="sendIndexes">Liste des sendIndex à surveiller.</param>
            ''' <param name="category"><see cref="SendCategory"/> (défaut : Sends).</param>
            Public Sub New(Reapertrack As ReaperTrack, AllTracks As IReadOnlyList(Of Reaper.ReaperTrack),
               sendIndexes As List(Of Integer),
               Optional category As SendCategory = SendCategory.Sends)

                _Reapertrack = Reapertrack
                _trackIndex = Reapertrack.TrackIndex
                _category = CInt(category)

                _ReaperTrackSendsEditor = New ReaperTrackSendsEditor(TrackIndex, AllTracks, _category)
                _TrackSendNames.Clear()

                If sendIndexes Is Nothing Then
                    sendIndexes = New List(Of Integer)
                    Dim nbSends = Reaper.utility.GetSendCount(Reapertrack.TrackIndex, category)
                    For i = 0 To nbSends - 1
                        sendIndexes.Add(i)
                    Next
                End If

                For Each c24SendIndex In sendIndexes


                    If c24SendIndex >= 0 AndAlso c24SendIndex < _ReaperTrackSendsEditor.Sends.Count Then
                        Dim send = _ReaperTrackSendsEditor.Sends(c24SendIndex)
                        _Sends.Add(send)
                        _TrackSendNames.Add(send.Name)
                    Else
                        _Sends.Add(Nothing)
                        _TrackSendNames.Add(Nothing)
                    End If
                Next

                BuildSendEnvelopeDictionaries()

                If _Monitor IsNot Nothing Then
                    RemoveHandler _Monitor.On_SendModeChange, AddressOf On_MonitorSendModeChange
                    _Monitor.Dispose()
                End If

                _Monitor = New ReaperTrackSendsMonitor(_trackIndex, category)
                AddHandler _Monitor.On_SendModeChange, AddressOf On_MonitorSendModeChange

                If _EnvMonitor IsNot Nothing Then
                    RemoveHandler _EnvMonitor.On_VolChange, AddressOf On_EnvMonitorVolChange
                    RemoveHandler _EnvMonitor.On_PanChange, AddressOf On_EnvMonitorPanChange
                    RemoveHandler _EnvMonitor.On_MuteChange, AddressOf On_EnvMonitorMuteChange
                    _EnvMonitor.Dispose()
                End If

                _EnvMonitor = New Monitors.ReaperTrackSendEnveloppeMonitor(_trackIndex, category)
                AddHandler _EnvMonitor.On_VolChange, AddressOf On_EnvMonitorVolChange
                AddHandler _EnvMonitor.On_PanChange, AddressOf On_EnvMonitorPanChange
                AddHandler _EnvMonitor.On_MuteChange, AddressOf On_EnvMonitorMuteChange

                For Each send In _Sends
                    If send IsNot Nothing Then
                        _Monitor.WatchSend(send.SendIndex)
                        _EnvMonitor.WatchSend(send.SendIndex)
                    End If
                Next

                _Monitor.Start()
                _EnvMonitor.Start()
            End Sub


            Private Sub BuildSendEnvelopeDictionaries()
                _VolEnvelopes.Clear()
                _PanEnvelopes.Clear()
                _MuteEnvelopes.Clear()



                For Each env In Reapertrack.Envelopes.AllOfType(Of ReaperSendVolEnvelope)()
                    If env IsNot Nothing Then
                        If env.SendId >= 0 AndAlso env.SendId < _ReaperTrackSendsEditor.Sends.Count Then


                            _VolEnvelopes(env.SendId) = env

                        End If
                    End If
                Next

                For Each env In Reapertrack.Envelopes.AllOfType(Of ReaperSendPanEnvelope)()
                    If env IsNot Nothing Then
                        If env.SendId >= 0 AndAlso env.SendId < _ReaperTrackSendsEditor.Sends.Count Then

                            _PanEnvelopes(env.SendId) = env

                        End If
                    End If
                Next

                For Each env In Reapertrack.Envelopes.AllOfType(Of ReaperSendMuteEnvelope)()
                    If env IsNot Nothing Then
                        If env.SendId >= 0 AndAlso env.SendId < _ReaperTrackSendsEditor.Sends.Count Then

                            _MuteEnvelopes(env.SendId) = env

                        End If
                    End If
                Next
            End Sub
            ' ── Suspension par envoi ──────────────────────────────────────────

            ''' <summary>Suspend le monitoring pour l'envoi à l'index .</summary>
            Public Sub SuspendSend(SendIndex As Integer)
                Dim sIdx As Integer = SendIndex
                If sIdx < 0 Then
                    Return
                End If

                If _Monitor IsNot Nothing Then
                    _Monitor.SuspendSend(sIdx)
                End If

                If _EnvMonitor IsNot Nothing Then
                    _EnvMonitor.SuspendSend(sIdx)
                End If
            End Sub

            ''' <summary>Réactive le monitoring pour l'envoi à l'index de page donné.</summary>
            Public Sub ResumeSend(pageSendIndex As Integer)
                Dim sIdx As Integer = pageSendIndex
                If sIdx < 0 Then
                    Return
                End If

                If _Monitor IsNot Nothing Then
                    _Monitor.ResumeSend(sIdx)
                End If

                If _EnvMonitor IsNot Nothing Then
                    _EnvMonitor.ResumeSend(sIdx)
                End If
            End Sub

            ' ── Écriture depuis la surface ────────────────────────────────────

            ''' <summary>
            ''' Écrit le volume de l'envoi à l'index donné.
            ''' Suspend le moniteur pendant l'écriture (anti-feedback).
            ''' </summary>
            ''' <param name="vol">Volume 0..4  (1.0 = 0 dB).</param>
            Public Sub Set_Vol(pageSendIndex As Integer, vol As Double)
                _lock.EnterReadLock()
                Try
                    If pageSendIndex < 0 OrElse pageSendIndex >= _Sends.Count Then Return
                    SuspendSend(pageSendIndex)
                    utility.SetTrackSendUIVol(_trackIndex, _Sends(pageSendIndex).SendIndex, vol)
                    ResumeSend(pageSendIndex)
                Finally
                    _lock.ExitReadLock()
                End Try
            End Sub

            ''' <summary>
            ''' Écrit le pan de l'envoi à l'index donné.
            ''' Suspend le moniteur pendant l'écriture (anti-feedback).
            ''' </summary>
            ''' <param name="pan">Pan -1=Gauche  0=Centre  1=Droite.</param>
            Public Sub Set_Pan(pageSendIndex As Integer, pan As Double)
                _lock.EnterReadLock()
                Try
                    If pageSendIndex < 0 OrElse pageSendIndex >= _Sends.Count Then Return
                    SuspendSend(pageSendIndex)
                    utility.SetTrackSendUIPan(_trackIndex,
                                              _Sends(pageSendIndex).SendIndex, pan,
                                              CType(_category, SendCategory))
                    ResumeSend(pageSendIndex)
                Finally
                    _lock.ExitReadLock()
                End Try
            End Sub

            ''' <summary>
            ''' Impose l'état mute de l'envoi à l'index donné.
            ''' Suspend le moniteur pendant l'écriture (anti-feedback).
            ''' </summary>
            Public Sub Set_Mute(pageSendIndex As Integer, mute As Boolean)
                _lock.EnterReadLock()
                Try
                    If pageSendIndex < 0 OrElse pageSendIndex >= _Sends.Count Then Return
                    SuspendSend(pageSendIndex)
                    utility.SetTrackSendUIMute(_trackIndex,
                                               _Sends(pageSendIndex).SendIndex, mute)
                    ResumeSend(pageSendIndex)
                Finally
                    _lock.ExitReadLock()
                End Try
            End Sub

            ''' <summary>Bascule le mute de l'envoi à l'index donné.</summary>
            Public Sub ToggleMute(pageSendIndex As Integer)
                _lock.EnterReadLock()
                Try
                    If pageSendIndex < 0 OrElse pageSendIndex >= _Sends.Count Then Return
                    Dim current = _Sends(pageSendIndex).Mute
                    Set_Mute(pageSendIndex, Not current)
                Finally
                    _lock.ExitReadLock()
                End Try
            End Sub

            ' ── Lecture d'état ────────────────────────────────────────────────

            ''' <summary>Retourne la liste des volumes courants.</summary>
            Public Function Get_VolStatus() As List(Of Double)
                Dim result As New List(Of Double)
                For Each s In _Sends
                    result.Add(s.Vol)
                Next
                Return result
            End Function

            ''' <summary>Retourne la liste des pans courants.</summary>
            Public Function Get_PanStatus() As List(Of Double)
                Dim result As New List(Of Double)
                For Each s In _Sends
                    result.Add(s.Pan)
                Next
                Return result
            End Function

            ''' <summary>Retourne la liste des états mute courants.</summary>
            Public Function Get_MuteStatus() As List(Of Boolean)
                Dim result As New List(Of Boolean)
                For Each s In _Sends
                    result.Add(s.Mute)
                Next
                Return result
            End Function

            ' ── Handlers internes ─────────────────────────────────────────────
            Private Sub On_EnvMonitorVolChange(sendIndex As Integer, vol As Double)
                Dim pageIdx As Integer = -1


                If pageIdx < 0 OrElse pageIdx >= _Sends.Count Then
                    Return
                End If

                Dim send = _Sends(pageIdx)
                If send Is Nothing Then
                    Return
                End If

                send.SetVolFromMonitor(vol)

                Dim env As ReaperSendVolEnvelope = Nothing
                If _VolEnvelopes.TryGetValue(sendIndex, env) Then
                    env.SetValueFromMonitor(vol)
                End If
            End Sub

            Private Sub On_EnvMonitorPanChange(sendIndex As Integer, pan As Double)
                Dim pageIdx As Integer = -1


                If pageIdx < 0 OrElse pageIdx >= _Sends.Count Then
                    Return
                End If

                Dim send = _Sends(pageIdx)
                If send Is Nothing Then
                    Return
                End If

                send.SetPanFromMonitor(pan)

                Dim env As ReaperSendPanEnvelope = Nothing
                If _PanEnvelopes.TryGetValue(sendIndex, env) Then
                    env.SetValueFromMonitor(pan)
                End If
            End Sub

            Private Sub On_EnvMonitorMuteChange(sendIndex As Integer, mute As Boolean)
                Dim pageIdx As Integer = -1


                If pageIdx < 0 OrElse pageIdx >= _Sends.Count Then
                    Return
                End If

                Dim send = _Sends(pageIdx)
                If send Is Nothing Then
                    Return
                End If

                send.SetMuteFromMonitor(mute)

                Dim env As ReaperSendMuteEnvelope = Nothing
                If _MuteEnvelopes.TryGetValue(sendIndex, env) Then
                    env.SetValueFromMonitor(mute)
                End If
            End Sub
            Private Sub On_MonitorVolChange(sendIndex As Integer, vol As Double)
                Dim pageIdx As Integer = -1

                _Sends(pageIdx).SetVolFromMonitor(vol)
            End Sub

            Private Sub On_MonitorPanChange(sendIndex As Integer, pan As Double)
                Dim pageIdx As Integer = -1

                _Sends(pageIdx).SetPanFromMonitor(pan)
            End Sub

            Private Sub On_MonitorMuteChange(sendIndex As Integer, mute As Boolean)
                Dim pageIdx As Integer = -1

                _Sends(pageIdx).SetMuteFromMonitor(mute)
            End Sub

            Private Sub On_MonitorSendModeChange(sendIndex As Integer, sendMode As Integer)
                Dim pageIdx As Integer = -1

                ' SendMode est lecture seule dans ReaperSend — pas de SetSendModeFromMonitor
                ' L'abonné externe peut s'abonner à _Monitor.On_SendModeChange directement.
            End Sub

            ' ── Utilitaires ───────────────────────────────────────────────────


            ' ── IDisposable ───────────────────────────────────────────────────

            Protected Overridable Sub Dispose(disposing As Boolean)
                If disposedValue = False Then
                    If disposing Then
                        If _Monitor IsNot Nothing Then
                            RemoveHandler _Monitor.On_SendModeChange, AddressOf On_MonitorSendModeChange
                            _Monitor.Dispose()
                        End If

                        If _EnvMonitor IsNot Nothing Then
                            RemoveHandler _EnvMonitor.On_VolChange, AddressOf On_EnvMonitorVolChange
                            RemoveHandler _EnvMonitor.On_PanChange, AddressOf On_EnvMonitorPanChange
                            RemoveHandler _EnvMonitor.On_MuteChange, AddressOf On_EnvMonitorMuteChange
                            _EnvMonitor.Dispose()
                        End If

                        _VolEnvelopes.Clear()
                        _PanEnvelopes.Clear()
                        _MuteEnvelopes.Clear()
                        _lock.Dispose()
                    End If
                    disposedValue = True
                End If
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                Dispose(disposing:=True)
                GC.SuppressFinalize(Me)
            End Sub

        End Class

        ' ══════════════════════════════════════════════════════════════════════
        '  REAPER TRACK INPUTS MANAGER — affectation paginée des entrées audio
        ' ══════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Gère l'affectation des entrées audio sur une liste fixe de pistes
        ''' paginées par <paramref name="maxTracksNbPerPage"/>.
        ''' <br/>
        ''' Au démarrage, <see cref="utility.BuildAudioInputList"/> construit
        ''' la liste des entrées disponibles (mono + paires stéréo).
        ''' <br/>
        ''' Pour chaque piste, le manager maintient l'index courant dans cette
        ''' liste et permet à la surface de naviguer via
        ''' <see cref="Set_Next_Input"/>/<see cref="Set_Prev_Input"/> ou d'affecter
        ''' directement via <see cref="Set_InputByIndex"/>.
        ''' <br/>
        ''' La liste source est fixe à la construction. Appeler
        ''' <see cref="SyncInputsFromTracks"/> après un <see cref="ReaperTrack.Refresh"/>
        ''' pour resynchroniser le cache depuis Reaper.
        ''' </summary>
        Public Class ReaperTrackInputsManager
            Implements IDisposable

            ' ── Liste fixe de pistes source ───────────────────────────────────
            Private ReadOnly _sourceTracks As IReadOnlyList(Of ReaperTrack)
            Private ReadOnly _maxTracksNbPerPage As Integer

            ' ── Entrées disponibles ───────────────────────────────────────────
            ' Construite 1 fois au démarrage, reconstruite par RefreshInputList.
            ' Index 0  = "-- Aucune --"
            ' Index 1..M = entrées mono
            ' Index M+1..N = paires stéréo
            Private _allInputs As IReadOnlyList(Of ReaperInputChannel)

            ' ── Pages ─────────────────────────────────────────────────────────
            Private ReadOnly _allTrackPages As New List(Of List(Of ReaperTrack))
            Private _Tracks As List(Of ReaperTrack)
            Private _Actual_PageNb As Integer = 0

            ' ── Sélection courante par piste ─────────────────────────────────
            ' Clé   = trackIndex (0-based absolu)
            ' Valeur = index 0-based dans _allInputs
            Private ReadOnly _selectedInputIndex As New Dictionary(Of Integer, Integer)

            ' ── Verrou ────────────────────────────────────────────────────────
            Private ReadOnly _pageLock As New ReaderWriterLockSlim()
            Private disposedValue As Boolean

            ' ── Événements ────────────────────────────────────────────────────

            ''' <summary>Déclenché après chaque changement de page.</summary>
            Public Event On_TracksRebuilt()

            ''' <summary>
            ''' Déclenché quand l'entrée d'une piste est modifiée depuis la surface.
            ''' <paramref name="pageTrackIndex"/> = position dans la page courante.
            ''' </summary>
            Public Event On_InputChanged(pageTrackIndex As Integer,
                                         input As ReaperInputChannel)

            ''' <summary>
            ''' Déclenché quand la liste d'entrées est reconstruite via
            ''' <see cref="RefreshInputList"/>.
            ''' </summary>
            Public Event On_InputListRefreshed(count As Integer)

            ' ── Propriétés ────────────────────────────────────────────────────

            ''' <summary>Pistes de la page courante.</summary>
            Public ReadOnly Property Tracks As List(Of ReaperTrack)
                Get
                    Return _Tracks
                End Get
            End Property

            ''' <summary>Index 0-based de la page courante.</summary>
            Public ReadOnly Property CurrentPage As Integer
                Get
                    Return _Actual_PageNb
                End Get
            End Property

            ''' <summary>Nombre total de pages.</summary>
            Public ReadOnly Property PageCount As Integer
                Get
                    Return _allTrackPages.Count
                End Get
            End Property

            ''' <summary>
            ''' Liste complète des entrées disponibles (mono + paires stéréo).
            ''' Construite à la création, reconstruite par <see cref="RefreshInputList"/>.
            ''' </summary>
            Public ReadOnly Property AllInputs As IReadOnlyList(Of ReaperInputChannel)
                Get
                    Return _allInputs
                End Get
            End Property

            ''' <summary>Nombre d'entrées disponibles dans <see cref="AllInputs"/>.</summary>
            Public ReadOnly Property InputCount As Integer
                Get
                    Return _allInputs.Count
                End Get
            End Property

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Initialise le manager, construit la liste d'entrées et les pages,
            ''' puis lit l'entrée courante de chaque piste depuis <c>I_RECINPUT</c>.
            ''' </summary>
            ''' <param name="tracks">Liste fixe de pistes à paginer.</param>
            ''' <param name="maxTracksNbPerPage">Pistes max par page (défaut : 24).</param>
            Public Sub New(tracks As IReadOnlyList(Of ReaperTrack),
                           Optional maxTracksNbPerPage As Integer = 24)
                _sourceTracks = tracks
                _maxTracksNbPerPage = maxTracksNbPerPage

                ' ── Construire la liste d'entrées (mono + stéréo) ─────────────
                _allInputs = utility.BuildAudioInputList()

                ' ── Construire les pages ──────────────────────────────────────
                RebuildPages()

                ' ── Lire les entrées courantes depuis les pistes ──────────────
                ' (nécessite que ReaperTrack.Refresh() ait déjà été appelé)
                SyncInputsFromTracks()

                ' ── Positionner sur la page 0 ─────────────────────────────────
                If _allTrackPages.Count > 0 Then _Tracks = _allTrackPages(0)
            End Sub

            ' ── Construction des pages ────────────────────────────────────────

            ''' <summary>
            ''' Découpe la liste source en pages de <c>maxTracksNbPerPage</c> pistes.
            ''' </summary>
            Private Sub RebuildPages()
                _allTrackPages.Clear()
                Dim page As New List(Of ReaperTrack)
                For Each trk In _sourceTracks
                    If page.Count = _maxTracksNbPerPage Then
                        _allTrackPages.Add(page)
                        page = New List(Of ReaperTrack)
                    End If
                    page.Add(trk)
                Next
                If page.Count > 0 Then _allTrackPages.Add(page)
            End Sub

            ' ── Synchronisation depuis Reaper ─────────────────────────────────

            ''' <summary>
            ''' Lit la valeur <c>I_RECINPUT</c> de chaque piste source et
            ''' résout l'index correspondant dans <see cref="AllInputs"/>.
            ''' <br/>
            ''' Appeler après un <see cref="ReaperTrack.Refresh"/> pour
            ''' resynchroniser si une modification externe a eu lieu.
            ''' </summary>
            Public Sub SyncInputsFromTracks()
                _selectedInputIndex.Clear()
                For Each trk In _sourceTracks
                    Dim idx = utility.FindInputIndex(trk.RecInput, _allInputs)
                    _selectedInputIndex(trk.TrackIndex) = idx
                Next
            End Sub

            ''' <summary>
            ''' Reconstruit la liste d'entrées depuis Reaper (utile si le périphérique
            ''' audio a changé) et resynchronise les sélections courantes.
            ''' Déclenche <see cref="On_InputListRefreshed"/>.
            ''' </summary>
            Public Sub RefreshInputList()
                _pageLock.EnterWriteLock()
                Try
                    _allInputs = utility.BuildAudioInputList()
                    ' ── Resynchroniser les sélections (les index peuvent avoir changé) ─
                    SyncInputsFromTracks()
                Finally
                    _pageLock.ExitWriteLock()
                End Try
                RaiseEvent On_InputListRefreshed(_allInputs.Count)
            End Sub

            ' ── Navigation pages ──────────────────────────────────────────────

            ''' <summary>Passe à la page suivante si possible.</summary>
            Public Sub Set_Next_Page()
                _Actual_PageNb = Math.Min(_allTrackPages.Count - 1, _Actual_PageNb + 1)
                _Tracks = _allTrackPages(_Actual_PageNb)
                RaiseEvent On_TracksRebuilt()
            End Sub

            ''' <summary>Revient à la page précédente si possible.</summary>
            Public Sub Set_Prev_Page()
                _Actual_PageNb = Math.Max(0, _Actual_PageNb - 1)
                _Tracks = _allTrackPages(_Actual_PageNb)
                RaiseEvent On_TracksRebuilt()
            End Sub

            ' ── Navigation inputs ─────────────────────────────────────────────

            ''' <summary>
            ''' Passe à l'entrée suivante pour la piste de la page donnée et
            ''' l'applique immédiatement dans Reaper.
            ''' Boucle vers "-- Aucune --" après la dernière entrée.
            ''' </summary>
            ''' <param name="pageTrackIndex">Position 0-based dans la page courante.</param>
            Public Sub Set_Next_Input(pageTrackIndex As Integer)
                _pageLock.EnterReadLock()
                Try
                    Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                    If tIdx < 0 Then Return
                    Dim current = GetSelectedInputIndex(tIdx)
                    Dim next_ = (current + 1) Mod _allInputs.Count
                    ApplyInput(pageTrackIndex, tIdx, next_)
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Sub

            ''' <summary>
            ''' Passe à l'entrée précédente pour la piste de la page donnée et
            ''' l'applique immédiatement dans Reaper.
            ''' Boucle vers la dernière entrée depuis "-- Aucune --".
            ''' </summary>
            ''' <param name="pageTrackIndex">Position 0-based dans la page courante.</param>
            Public Sub Set_Prev_Input(pageTrackIndex As Integer)
                _pageLock.EnterReadLock()
                Try
                    Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                    If tIdx < 0 Then Return
                    Dim current = GetSelectedInputIndex(tIdx)
                    Dim prev = (current - 1 + _allInputs.Count) Mod _allInputs.Count
                    ApplyInput(pageTrackIndex, tIdx, prev)
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Sub

            ''' <summary>
            ''' Affecte directement une entrée par son index dans <see cref="AllInputs"/>
            ''' et l'applique dans Reaper.
            ''' </summary>
            ''' <param name="pageTrackIndex">Position 0-based dans la page courante.</param>
            ''' <param name="inputListIndex">Index 0-based dans <see cref="AllInputs"/>.</param>
            Public Sub Set_InputByIndex(pageTrackIndex As Integer,
                                         inputListIndex As Integer)
                If inputListIndex < 0 OrElse inputListIndex >= _allInputs.Count Then Return
                _pageLock.EnterReadLock()
                Try
                    Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                    If tIdx < 0 Then Return
                    ApplyInput(pageTrackIndex, tIdx, inputListIndex)
                Finally
                    _pageLock.ExitReadLock()
                End Try

            End Sub

            ''' <summary>
            ''' Affecte l'entrée correspondant à la valeur <c>I_RECINPUT</c> brute.
            ''' Utile pour rétablir un état sauvegardé ou synchroniser depuis Reaper.
            ''' Si la valeur ne correspond à aucune entrée connue, "-- Aucune --" est appliqué.
            ''' </summary>
            ''' <param name="pageTrackIndex">Position 0-based dans la page courante.</param>
            ''' <param name="recInputValue">Valeur brute <c>I_RECINPUT</c>.</param>
            Public Sub Set_InputByRecValue(pageTrackIndex As Integer,
                                            recInputValue As Integer)
                _pageLock.EnterReadLock()
                Try
                    Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                    If tIdx < 0 Then Return
                    Dim idx = utility.FindInputIndex(recInputValue, _allInputs)
                    ApplyInput(pageTrackIndex, tIdx, idx)
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Sub

            ''' <summary>
            ''' Supprime l'entrée de la piste de la page donnée
            ''' (<c>I_RECINPUT</c> = -1, "-- Aucune --").
            ''' </summary>
            Public Sub Set_NoInput(pageTrackIndex As Integer)
                Set_InputByIndex(pageTrackIndex, 0)   ' index 0 = "-- Aucune --"
            End Sub

            ' ── Lecture d'état ────────────────────────────────────────────────

            ''' <summary>
            ''' Retourne l'index dans <see cref="AllInputs"/> pour la piste
            ''' de la page donnée.
            ''' </summary>
            Public Function Get_CurrentInputIndex(pageTrackIndex As Integer) As Integer
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                If tIdx < 0 Then Return 0
                Return GetSelectedInputIndex(tIdx)
            End Function

            ''' <summary>
            ''' Retourne l'entrée sélectionnée pour la piste de la page donnée.
            ''' </summary>
            Public Function Get_CurrentInput(pageTrackIndex As Integer) As ReaperInputChannel
                Return _allInputs(Get_CurrentInputIndex(pageTrackIndex))
            End Function

            ''' <summary>
            ''' Retourne les entrées sélectionnées pour toutes les pistes
            ''' de la page courante (utile pour un rafraîchissement groupé de la surface).
            ''' </summary>
            Public Function Get_CurrentInputs() As List(Of ReaperInputChannel)
                Dim result As New List(Of ReaperInputChannel)
                If _Tracks Is Nothing Then Return result
                For Each trk In _Tracks
                    result.Add(_allInputs(GetSelectedInputIndex(trk.TrackIndex)))
                Next
                Return result
            End Function

            ''' <summary>
            ''' Retourne les valeurs <c>I_RECINPUT</c> brutes de la page courante.
            ''' </summary>
            Public Function Get_RecInputValues() As List(Of Integer)
                Dim result As New List(Of Integer)
                If _Tracks Is Nothing Then Return result
                For Each trk In _Tracks
                    result.Add(_allInputs(GetSelectedInputIndex(trk.TrackIndex)).RecInputValue)
                Next
                Return result
            End Function

            ' ── Helpers internes ──────────────────────────────────────────────

            ''' <summary>
            ''' Applique l'entrée à l'index <paramref name="inputListIndex"/> sur la piste
            ''' <paramref name="tIdx"/> :
            ''' <list type="number">
            ''' <item>Met à jour le cache <see cref="_selectedInputIndex"/>.</item>
            ''' <item>Écrit <c>I_RECINPUT</c> dans Reaper via <see cref="utility.SetTrackInfoI"/>.</item>
            ''' <item>Déclenche <see cref="On_InputChanged"/>.</item>
            ''' </list>
            ''' </summary>
            Private Sub ApplyInput(pageTrackIndex As Integer,
                                    tIdx As Integer,
                                    inputListIndex As Integer)
                _selectedInputIndex(tIdx) = inputListIndex
                Dim input = _allInputs(inputListIndex)
                utility.SetTrackInfoI(tIdx, "I_RECINPUT", input.RecInputValue)
                RaiseEvent On_InputChanged(pageTrackIndex, input)
            End Sub

            Private Function GetAbsoluteTrackIndex(pageTrackIndex As Integer) As Integer
                If _Tracks Is Nothing OrElse
                   pageTrackIndex < 0 OrElse
                   pageTrackIndex >= _Tracks.Count Then Return -1
                Return _Tracks(pageTrackIndex).TrackIndex
            End Function

            Private Function GetSelectedInputIndex(tIdx As Integer) As Integer
                Dim idx As Integer = 0
                _selectedInputIndex.TryGetValue(tIdx, idx)
                Return idx
            End Function

            ' ── IDisposable ───────────────────────────────────────────────────

            Protected Overridable Sub Dispose(disposing As Boolean)
                If Not disposedValue Then
                    If disposing Then
                        _pageLock.Dispose()
                    End If
                    disposedValue = True
                End If
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                Dispose(disposing:=True)
                GC.SuppressFinalize(Me)
            End Sub

        End Class

        ' ══════════════════════════════════════════════════════════════════════
        '  REAPER TRACK OUTPUTS MANAGER — sorties hardware paginées sur N pistes
        ' ══════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Gère l'affectation des sorties audio hardware sur une liste fixe de pistes
        ''' paginées par <paramref name="maxTracksNbPerPage"/>.
        ''' <br/>
        ''' Chaque piste peut avoir plusieurs envois hardware (<see cref="SendCategory.Hardware"/>).
        ''' Le manager permet d'ajouter, supprimer et modifier les destinations de chaque envoi.
        ''' <br/>
        ''' La liste des sorties disponibles (mono + paires stéréo) est construite via
        ''' <see cref="utility.BuildAudioOutputList"/>.
        ''' Appeler <see cref="SyncOutputsFromTracks"/> après un <see cref="ReaperTrack.Refresh"/>
        ''' pour resynchroniser le cache depuis Reaper.
        ''' </summary>
        Public Class ReaperTrackOutputsManager
            Implements IDisposable

            ' ── Liste fixe de pistes source ───────────────────────────────────
            Private ReadOnly _sourceTracks As IReadOnlyList(Of ReaperTrack)
            Private ReadOnly _maxTracksNbPerPage As Integer

            ' ── Sorties disponibles ───────────────────────────────────────────
            ' Construite au démarrage, reconstruite par RefreshOutputList.
            Private _allOutputs As IReadOnlyList(Of ReaperOutputChannel)

            ' ── Pages ─────────────────────────────────────────────────────────
            Private ReadOnly _allTrackPages As New List(Of List(Of ReaperTrack))
            Private _Tracks As List(Of ReaperTrack)
            Private _Actual_PageNb As Integer = 0

            ' ── Envois hardware par piste ─────────────────────────────────────
            ' Clé   = trackIndex absolu (0-based)
            ' Valeur = liste ordonnée de ReaperSend (SendCategory.Hardware),
            '          index 0-based correspondant à l'ordre dans Reaper.
            Private ReadOnly _trackHWSends As New Dictionary(Of Integer, List(Of ReaperSend))

            ' ── Verrou ────────────────────────────────────────────────────────
            Private ReadOnly _pageLock As New ReaderWriterLockSlim()
            Private _AllOutputsNames As New List(Of String)
            Private disposedValue As Boolean

            ' ── Événements ────────────────────────────────────────────────────

            ''' <summary>Déclenché après chaque changement de page.</summary>
            Public Event On_TracksRebuilt()

            ''' <summary>
            ''' Déclenché quand le canal destination d'un envoi hardware change.
            ''' <paramref name="sendIndex"/> = index 0-based de l'envoi sur la piste.
            ''' </summary>
            Public Event On_OutputChanged(pageTrackIndex As Integer,
                                           sendIndex As Integer,
                                           output As ReaperOutputChannel)

            ''' <summary>
            ''' Déclenché quand un nouvel envoi hardware est créé sur une piste.
            ''' </summary>
            Public Event On_SendAdded(pageTrackIndex As Integer,
                                       sendIndex As Integer,
                                       output As ReaperOutputChannel)

            ''' <summary>
            ''' Déclenché quand un envoi hardware est supprimé d'une piste.
            ''' </summary>
            Public Event On_SendRemoved(pageTrackIndex As Integer)

            ''' <summary>
            ''' Déclenché quand la liste de sorties est reconstruite via
            ''' <see cref="RefreshOutputList"/>.
            ''' </summary>
            Public Event On_OutputListRefreshed(count As Integer)

            ' ── Propriétés ────────────────────────────────────────────────────

            ''' <summary>Pistes de la page courante.</summary>
            Public ReadOnly Property Tracks As List(Of ReaperTrack)
                Get
                    Return _Tracks
                End Get
            End Property

            ''' <summary>Index 0-based de la page courante.</summary>
            Public ReadOnly Property CurrentPage As Integer
                Get
                    Return _Actual_PageNb
                End Get
            End Property

            ''' <summary>Nombre total de pages.</summary>
            Public ReadOnly Property PageCount As Integer
                Get
                    Return _allTrackPages.Count
                End Get
            End Property
            Public ReadOnly Property AllOutputsNames As List(Of String)
                Get
                    Return _AllOutputsNames
                End Get
            End Property

            ''' <summary>
            ''' Liste complète des sorties disponibles (mono + paires stéréo).
            ''' Reconstruite par <see cref="RefreshOutputList"/>.
            ''' </summary>
            Public ReadOnly Property AllOutputs As IReadOnlyList(Of ReaperOutputChannel)
                Get
                    Return _allOutputs
                End Get
            End Property

            ''' <summary>Nombre de sorties disponibles dans <see cref="AllOutputs"/>.</summary>
            Public ReadOnly Property OutputCount As Integer
                Get
                    Return _allOutputs.Count
                End Get
            End Property

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Initialise le manager, construit la liste de sorties et les pages,
            ''' puis charge les envois hardware courants de chaque piste.
            ''' </summary>
            ''' <param name="tracks">Liste fixe de pistes à paginer.</param>
            ''' <param name="maxTracksNbPerPage">Pistes max par page (défaut : 24).</param>
            Public Sub New(tracks As IReadOnlyList(Of ReaperTrack),
                           Optional maxTracksNbPerPage As Integer = 24)
                _sourceTracks = tracks
                _maxTracksNbPerPage = maxTracksNbPerPage

                _allOutputs = utility.BuildAudioOutputList()
                _AllOutputsNames.Clear()

                For Each output In _allOutputs
                    _AllOutputsNames.Add(output.Name)
                Next
                RebuildPages()
                SyncOutputsFromTracks()

                If _allTrackPages.Count > 0 Then _Tracks = _allTrackPages(0)
            End Sub

            ' ── Construction des pages ────────────────────────────────────────

            ''' <summary>Découpe la liste source en pages de <c>maxTracksNbPerPage</c>.</summary>
            Private Sub RebuildPages()
                _allTrackPages.Clear()
                Dim page As New List(Of ReaperTrack)
                For Each trk In _sourceTracks
                    If page.Count = _maxTracksNbPerPage Then
                        _allTrackPages.Add(page)
                        page = New List(Of ReaperTrack)
                    End If
                    page.Add(trk)
                Next
                If page.Count > 0 Then _allTrackPages.Add(page)
            End Sub

            ' ── Synchronisation depuis Reaper ─────────────────────────────────

            ''' <summary>
            ''' Recharge tous les envois hardware de chaque piste source depuis Reaper.
            ''' Appeler après un <see cref="ReaperTrack.Refresh"/> ou si la configuration
            ''' des sorties a changé en dehors du manager.
            ''' </summary>
            Public Sub SyncOutputsFromTracks()
                _trackHWSends.Clear()
                For Each trk In _sourceTracks
                    LoadTrackHWSends(trk.TrackIndex)
                Next
            End Sub

            ''' <summary>
            ''' Interroge Reaper pour les envois hardware d'une piste et met à jour le cache.
            ''' </summary>
            Private Sub LoadTrackHWSends(trackIndex As Integer)
                Dim raw = utility.CallReaperBridge(
                    $"GET_NUM_SENDS|{trackIndex}|{CInt(SendCategory.Hardware)}")
                Dim count As Integer = 0
                If Not String.IsNullOrEmpty(raw) AndAlso Not raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                    Integer.TryParse(raw, count)
                End If

                Dim sends As New List(Of ReaperSend)
                For i = 0 To count - 1
                    Dim send As New ReaperSend(trackIndex, CInt(SendCategory.Hardware), i)
                    send.Refresh()
                    sends.Add(send)
                Next
                _trackHWSends(trackIndex) = sends
            End Sub

            ''' <summary>
            ''' Reconstruit la liste de sorties depuis Reaper (utile si le périphérique
            ''' audio a changé) et resynchronise les envois courants.
            ''' Déclenche <see cref="On_OutputListRefreshed"/>.
            ''' </summary>
            Public Sub RefreshOutputList()
                _pageLock.EnterWriteLock()
                Try
                    _allOutputs = utility.BuildAudioOutputList()
                    _AllOutputsNames.Clear()

                    For Each output In _allOutputs
                        _AllOutputsNames.Add(output.Name)
                    Next
                    SyncOutputsFromTracks()
                Finally
                    _pageLock.ExitWriteLock()
                End Try
                RaiseEvent On_OutputListRefreshed(_allOutputs.Count)
            End Sub

            ' ── Navigation pages ──────────────────────────────────────────────

            ''' <summary>Passe à la page suivante si possible.</summary>
            Public Sub Set_Next_Page()
                _Actual_PageNb = Math.Min(_allTrackPages.Count - 1, _Actual_PageNb + 1)
                _Tracks = _allTrackPages(_Actual_PageNb)
                RaiseEvent On_TracksRebuilt()
            End Sub

            ''' <summary>Revient à la page précédente si possible.</summary>
            Public Sub Set_Prev_Page()
                _Actual_PageNb = Math.Max(0, _Actual_PageNb - 1)
                _Tracks = _allTrackPages(_Actual_PageNb)
                RaiseEvent On_TracksRebuilt()
            End Sub

            ' ── Lecture d'état ────────────────────────────────────────────────

            ''' <summary>
            ''' Retourne le nombre d'envois hardware de la piste de la page donnée.
            ''' </summary>
            Public Function Get_SendCount(pageTrackIndex As Integer) As Integer
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)

                Dim sends As List(Of ReaperSend) = Nothing
                If Not _trackHWSends.TryGetValue(tIdx, sends) Then Return 0
                Return sends.Count
            End Function

            ''' <summary>
            ''' Retourne l'index dans <see cref="AllOutputs"/> du canal destination
            ''' de l'envoi <paramref name="sendIndex"/> pour la piste de la page donnée.
            ''' Retourne -1 si non trouvé.
            ''' </summary>
            Public Function Get_CurrentOutputIndex(pageTrackIndex As Integer,
                                                    sendIndex As Integer) As Integer
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)

                Dim sends As List(Of ReaperSend) = Nothing
                If Not _trackHWSends.TryGetValue(tIdx, sends) Then Return -1
                If sendIndex < 0 OrElse sendIndex >= sends.Count Then Return -1
                Return FindOutputIndexFromSend(sends(sendIndex))
            End Function

            ''' <summary>
            ''' Retourne le <see cref="ReaperOutputChannel"/> du canal destination
            ''' de l'envoi <paramref name="sendIndex"/> pour la piste de la page donnée.
            ''' </summary>
            Public Function Get_CurrentOutput(pageTrackIndex As Integer,
                                               sendIndex As Integer) As ReaperOutputChannel
                Dim idx = Get_CurrentOutputIndex(pageTrackIndex, sendIndex)
                If idx >= _allOutputs.Count Then
                    Return New ReaperOutputChannel(-1, 0, String.Empty, False, -1)
                End If
                Return _allOutputs(idx)
            End Function
            Public Function GetVpotSendsList(trackindex As Integer, Optional NullItem As String = " -  ") As List(Of String)
                Dim SendList As New List(Of String)
                If NullItem IsNot Nothing Then
                    SendList.Add(NullItem)
                End If
                Dim outs = Get_CurrentOutputs(trackindex)
                For Each out In outs
                    SendList.Add(out.Name)
                Next
                For Each out In outs
                    SendList.Add("-" & out.Name)
                Next
                For Each out In AllOutputs
                    SendList.Add("+" & out.Name)
                Next
                Return SendList
            End Function
            ''' <summary>
            ''' Retourne toutes les sorties courantes de la piste de la page donnée
            ''' (un <see cref="ReaperOutputChannel"/> par envoi hardware).
            ''' </summary>
            Public Function Get_CurrentOutputs(pageTrackIndex As Integer) As List(Of ReaperOutputChannel)
                Dim result As New List(Of ReaperOutputChannel)
                Dim count = Get_SendCount(pageTrackIndex)
                For i = 0 To count - 1
                    result.Add(Get_CurrentOutput(pageTrackIndex, i))
                Next
                Return result
            End Function

            ''' <summary>
            ''' Retourne l'objet <see cref="ReaperSend"/> correspondant à un envoi hardware
            ''' de la piste de la page donnée.
            ''' Retourne Nothing si hors limite.
            ''' </summary>
            Public Function Get_Send(pageTrackIndex As Integer,
                                      sendIndex As Integer) As ReaperSend
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)

                Dim sends As List(Of ReaperSend) = Nothing
                If Not _trackHWSends.TryGetValue(tIdx, sends) Then Return Nothing

                Return sends(sendIndex)
            End Function

            ' ── Ajout d'un envoi hardware ─────────────────────────────────────

            ''' <summary>
            ''' Crée un nouvel envoi hardware sur la piste de la page donnée et
            ''' le configure sur le canal de sortie à l'index <paramref name="outputListIndex"/>.
            ''' <br/>
            ''' Retourne True si la création a réussi.
            ''' </summary>
            ''' <param name="pageTrackIndex">Position 0-based dans la page courante.</param>
            ''' <param name="outputListIndex">Index 0-based dans <see cref="AllOutputs"/>.</param>
            Public Function AddOutput(pageTrackIndex As Integer,
                                       outputListIndex As Integer) As Boolean
                If outputListIndex < 0 OrElse outputListIndex >= _allOutputs.Count Then Return False
                _pageLock.EnterReadLock()
                Try
                    Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)


                    Dim output = _allOutputs(outputListIndex)

                    ' ── Créer l'envoi hardware (destTrackIndex = -2) ──────────
                    Dim sendIdx = utility.CreateTrackSend(tIdx, -2)
                    If sendIdx < 0 Then Return False

                    ' ── Configurer le canal destination ───────────────────────
                    Dim send As New ReaperSend(tIdx, CInt(SendCategory.Hardware), sendIdx)
                    send.DstChan = output.DstChan ' 1024=mono  0=stéréo
                    send.SrcChan = output.SrcChanFlag  ' 1024=mono  0=stéréo
                    send.Refresh()

                    If Not _trackHWSends.ContainsKey(tIdx) Then
                        _trackHWSends(tIdx) = New List(Of ReaperSend)
                    End If
                    _trackHWSends(tIdx).Add(send)

                    RaiseEvent On_SendAdded(pageTrackIndex, sendIdx, output)
                    Return True
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Function
            Public Function GetOutputId(pageTrackIndex As Integer, OutputName As String) As Integer
                Dim outs = Get_CurrentOutputs(pageTrackIndex)
                For i = 0 To outs.Count - 1
                    Dim out = outs(i)
                    If out.Name = OutputName Then
                        Return i
                    End If
                Next
                Return -1
            End Function
            ' ── Suppression d'un envoi hardware ──────────────────────────────

            ''' <summary>
            ''' Supprime l'envoi hardware <paramref name="sendIndex"/> de la piste
            ''' de la page donnée.
            ''' Les envois suivants sont décalés et leurs indices reconstruits.
            ''' Retourne True si la suppression a réussi.
            ''' </summary>
            ''' <param name="pageTrackIndex">Position 0-based dans la page courante.</param>
            ''' <param name="sendIndex">Index 0-based de l'envoi à supprimer.</param>
            Public Function RemoveOutput(pageTrackIndex As Integer,
                                          sendIndex As Integer) As Boolean
                _pageLock.EnterReadLock()
                Try
                    Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)

                    Dim sends As List(Of ReaperSend) = Nothing
                    If Not _trackHWSends.TryGetValue(tIdx, sends) Then Return False
                    If sendIndex < 0 OrElse sendIndex >= sends.Count Then Return False

                    Dim ok = utility.RemoveTrackSend(tIdx, SendCategory.Hardware, sendIndex)
                    If Not ok Then Return False

                    sends.RemoveAt(sendIndex)

                    ' ── Reconstruire les indices des envois décalés ───────────
                    For i = sendIndex To sends.Count - 1
                        sends(i) = New ReaperSend(tIdx, CInt(SendCategory.Hardware), i)
                        sends(i).Refresh()
                    Next

                    RaiseEvent On_SendRemoved(pageTrackIndex)
                    Return True
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Function

            ' ── Modification du canal destination ─────────────────────────────

            ''' <summary>
            ''' Affecte directement un canal de sortie par son index dans
            ''' <see cref="AllOutputs"/> à l'envoi <paramref name="sendIndex"/>
            ''' de la piste de la page donnée.
            ''' </summary>
            ''' <param name="pageTrackIndex">Position 0-based dans la page courante.</param>
            ''' <param name="sendIndex">Index 0-based de l'envoi à modifier.</param>
            ''' <param name="outputListIndex">Index 0-based dans <see cref="AllOutputs"/>.</param>
            Public Sub SetOutput(pageTrackIndex As Integer,
                                  sendIndex As Integer,
                                  outputListIndex As Integer)
                If outputListIndex < 0 OrElse outputListIndex >= _allOutputs.Count Then Return
                _pageLock.EnterReadLock()
                Try
                    Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)

                    Dim sends As List(Of ReaperSend) = Nothing
                    If Not _trackHWSends.TryGetValue(tIdx, sends) Then Return
                    If sendIndex < 0 OrElse sendIndex >= sends.Count Then Return

                    Dim output = _allOutputs(outputListIndex)
                    Dim send = sends(sendIndex)
                    send.DstChan = output.DstChan
                    send.SrcChan = output.SrcChanFlag

                    RaiseEvent On_OutputChanged(pageTrackIndex, sendIndex, output)
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Sub

            ' ── Navigation dans les canaux de sortie ─────────────────────────

            ''' <summary>
            ''' Passe à la sortie suivante pour l'envoi <paramref name="sendIndex"/>
            ''' de la piste de la page donnée.
            ''' Boucle après la dernière sortie disponible.
            ''' </summary>
            Public Sub Set_Next_Output(pageTrackIndex As Integer, sendIndex As Integer)
                _pageLock.EnterReadLock()
                Try
                    If _allOutputs.Count = 0 Then Return
                    Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                    If tIdx < 0 Then Return
                    Dim sends As List(Of ReaperSend) = Nothing
                    If Not _trackHWSends.TryGetValue(tIdx, sends) Then Return
                    If sendIndex < 0 OrElse sendIndex >= sends.Count Then Return

                    Dim current = FindOutputIndexFromSend(sends(sendIndex))
                    Dim next_ = If(current < 0, 0, (current + 1) Mod _allOutputs.Count)
                    ApplyOutput(sends(sendIndex), pageTrackIndex, sendIndex, next_)
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Sub

            ''' <summary>
            ''' Passe à la sortie précédente pour l'envoi <paramref name="sendIndex"/>
            ''' de la piste de la page donnée.
            ''' Boucle depuis la première sortie vers la dernière.
            ''' </summary>
            Public Sub Set_Prev_Output(pageTrackIndex As Integer, sendIndex As Integer)
                _pageLock.EnterReadLock()
                Try
                    If _allOutputs.Count = 0 Then Return
                    Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                    If tIdx < 0 Then Return
                    Dim sends As List(Of ReaperSend) = Nothing
                    If Not _trackHWSends.TryGetValue(tIdx, sends) Then Return
                    If sendIndex < 0 OrElse sendIndex >= sends.Count Then Return

                    Dim current = FindOutputIndexFromSend(sends(sendIndex))
                    Dim prev = If(current <= 0, _allOutputs.Count - 1, current - 1)
                    ApplyOutput(sends(sendIndex), pageTrackIndex, sendIndex, prev)
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Sub

            ' ── Helpers internes ──────────────────────────────────────────────

            ''' <summary>
            ''' Applique un canal de sortie sur un envoi et déclenche
            ''' <see cref="On_OutputChanged"/>.
            ''' </summary>
            Private Sub ApplyOutput(send As ReaperSend,
                                     pageTrackIndex As Integer,
                                     sendIndex As Integer,
                                     outputListIndex As Integer)
                Dim output = _allOutputs(outputListIndex)
                send.DstChan = output.DstChan
                send.SrcChan = output.SrcChanFlag
                RaiseEvent On_OutputChanged(pageTrackIndex, sendIndex, output)
            End Sub

            Private Function GetAbsoluteTrackIndex(pageTrackIndex As Integer) As Integer
                If _Tracks Is Nothing OrElse
                   pageTrackIndex < 0 OrElse
                   pageTrackIndex >= _Tracks.Count Then Return -1
                Return _Tracks(pageTrackIndex).TrackIndex
            End Function

            ''' <summary>
            ''' Retrouve l'index dans <see cref="AllOutputs"/> correspondant à un
            ''' <see cref="ReaperSend"/> en lisant <c>I_DSTCHAN</c>.
            ''' <br/>
            ''' Règle d'identification :
            ''' <list type="table">
            ''' <item><term><c>I_DSTCHAN &amp; 1024 ≠ 0</c></term>
            '''       <description>Sortie mono — bit 10 = canal hardware unique.</description></item>
            ''' <item><term><c>I_DSTCHAN &amp; 1024 = 0</c></term>
            '''       <description>Sortie stéréo — pas de bit 10 = paire de canaux consécutifs.</description></item>
            ''' </list>
            ''' </summary>
            Private Function FindOutputIndexFromSend(send As ReaperSend) As Integer
                ' I_DSTCHAN bit 10 (&1024) :
                '   ≠ 0 → canal hardware unique (mono)  : chercher IsStereo = False
                '   = 0 → paire stéréo                  : chercher IsStereo = True
                Dim isStereo = (send.DstChan And 1024) = 0
                Return utility.FindOutputIndex(send.DstChan, isStereo, _allOutputs)
            End Function

            ' ── IDisposable ───────────────────────────────────────────────────

            Protected Overridable Sub Dispose(disposing As Boolean)
                If Not disposedValue Then
                    If disposing Then
                        _pageLock.Dispose()
                    End If
                    disposedValue = True
                End If
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                Dispose(disposing:=True)
                GC.SuppressFinalize(Me)
            End Sub

        End Class

        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER TRACK FX MANAGER — gestion des FX paginée sur N pistes
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Gère la liste des FX de chaque piste d'une liste fixe, paginée par
        ''' <paramref name="maxTracksNbPerPage"/>.
        ''' <br/>
        ''' Pour chaque piste expose :
        ''' <list type="bullet">
        ''' <item>La liste complète des FX avec leur état bypass.</item>
        ''' <item>La classification EQ / DYN / Generic selon la
        '''       <see cref="ReaperSessionConfig"/> de la session.</item>
        ''' <item>Les opérations d'ajout, de bypass et de suppression de FX.</item>
        ''' </list>
        ''' Appeler <see cref="SyncFxFromTracks"/> après tout changement structurel
        ''' du projet pour resynchroniser le cache depuis Reaper.
        ''' </summary>
        Public Class ReaperTrackFxManager
            Implements IDisposable

            ' ── Liste fixe de pistes source ───────────────────────────────────
            Private ReadOnly _sourceTracks As IReadOnlyList(Of ReaperTrack)
            Private ReadOnly _maxTracksNbPerPage As Integer

            ' ── Config session (classification EQ / DYN) ─────────────────────
            Private ReadOnly _sessionConfig As ReaperSessionConfig

            ' ── Pages ─────────────────────────────────────────────────────────
            Private ReadOnly _allTrackPages As New List(Of List(Of ReaperTrack))
            Private _Tracks As List(Of ReaperTrack)
            Private _Actual_PageNb As Integer = 0

            ' ── Cache FX par piste ────────────────────────────────────────────
            ' Clé = trackIndex absolu (0-based)
            ' Valeur = ReaperTrackFX chargé (Refresh appelé)
            Private ReadOnly _trackFX As New Dictionary(Of Integer, ReaperTrackFX)

            ' ── Verrou ────────────────────────────────────────────────────────
            Private ReadOnly _pageLock As New ReaderWriterLockSlim()
            Private disposedValue As Boolean

            ' ── Événements ────────────────────────────────────────────────────

            ''' <summary>Déclenché après chaque changement de page.</summary>
            Public Event On_TracksRebuilt()

            ''' <summary>
            ''' Déclenché quand la liste de FX d'une piste de la page courante
            ''' a changé (ajout ou suppression).
            ''' </summary>
            Public Event On_FxListChanged(pageTrackIndex As Integer)

            ''' <summary>
            ''' Déclenché quand l'état bypass d'un FX change.
            ''' <paramref name="isEnabled"/> = True si le FX est actif (non bypassé).
            ''' </summary>
            Public Event On_FxBypassChanged(pageTrackIndex As Integer,
                                             fxIndex As Integer,
                                             isEnabled As Boolean)

            ''' <summary>Déclenché quand un FX est ajouté à une piste.</summary>
            Public Event On_FxAdded(pageTrackIndex As Integer, fxFullName As String)

            ''' <summary>Déclenché quand un FX est supprimé d'une piste.</summary>
            Public Event On_FxDeleted(pageTrackIndex As Integer)

            ' ── Propriétés ────────────────────────────────────────────────────

            ''' <summary>Pistes de la page courante.</summary>
            Public ReadOnly Property Tracks As List(Of ReaperTrack)
                Get
                    Return _Tracks
                End Get
            End Property

            ''' <summary>Index 0-based de la page courante.</summary>
            Public ReadOnly Property CurrentPage As Integer
                Get
                    Return _Actual_PageNb
                End Get
            End Property

            ''' <summary>Nombre total de pages.</summary>
            Public ReadOnly Property PageCount As Integer
                Get
                    Return _allTrackPages.Count
                End Get
            End Property

            ''' <summary>Config session utilisée pour la classification EQ / DYN.</summary>
            Public ReadOnly Property SessionConfig As ReaperSessionConfig
                Get
                    Return _sessionConfig
                End Get
            End Property

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Initialise le manager, pagine les pistes et charge les FX de chacune.
            ''' </summary>
            ''' <param name="tracks">Liste fixe de pistes à gérer.</param>
            ''' <param name="sessionConfig">
            ''' Config session exposant <see cref="ReaperSessionConfig.IsEQPlugin"/>
            ''' et <see cref="ReaperSessionConfig.IsDynPlugin"/> pour la classification.
            ''' </param>
            ''' <param name="maxTracksNbPerPage">Pistes max par page (défaut : 24).</param>
            Public Sub New(tracks As IReadOnlyList(Of ReaperTrack),
                           sessionConfig As ReaperSessionConfig,
                           Optional maxTracksNbPerPage As Integer = 24)
                _sourceTracks = tracks
                _sessionConfig = sessionConfig
                _maxTracksNbPerPage = maxTracksNbPerPage

                RebuildPages()
                SyncFxFromTracks()

                If _allTrackPages.Count > 0 Then _Tracks = _allTrackPages(0)
            End Sub

            ' ── Construction des pages ────────────────────────────────────────

            ''' <summary>Découpe la liste source en pages de <c>maxTracksNbPerPage</c>.</summary>
            Private Sub RebuildPages()
                _allTrackPages.Clear()
                Dim page As New List(Of ReaperTrack)
                For Each trk In _sourceTracks
                    If page.Count = _maxTracksNbPerPage Then
                        _allTrackPages.Add(page)
                        page = New List(Of ReaperTrack)
                    End If
                    page.Add(trk)
                Next
                If page.Count > 0 Then _allTrackPages.Add(page)
            End Sub

            ' ── Synchronisation depuis Reaper ─────────────────────────────────

            ''' <summary>
            ''' Recharge les FX de toutes les pistes source depuis Reaper.
            ''' Appeler après un changement structurel du projet (ajout/suppression
            ''' de piste) ou après un <see cref="ReaperTrack.Refresh"/>.
            ''' </summary>
            Public Sub SyncFxFromTracks()
                _trackFX.Clear()
                For Each trk In _sourceTracks
                    LoadTrackFX(trk.TrackIndex)
                Next
            End Sub

            ''' <summary>
            ''' Recharge les FX d'une seule piste depuis Reaper.
            ''' Utile après un ajout ou une suppression ciblée.
            ''' </summary>
            Private Sub LoadTrackFX(trackIndex As Integer)
                Dim fx As New ReaperTrackFX(trackIndex)
                fx.Refresh()
                _trackFX(trackIndex) = fx
            End Sub

            ' ── Navigation pages ──────────────────────────────────────────────

            ''' <summary>Passe à la page suivante si possible.</summary>
            Public Sub Set_Next_Page()
                _Actual_PageNb = Math.Min(_allTrackPages.Count - 1, _Actual_PageNb + 1)
                _Tracks = _allTrackPages(_Actual_PageNb)
                RaiseEvent On_TracksRebuilt()
            End Sub

            ''' <summary>Revient à la page précédente si possible.</summary>
            Public Sub Set_Prev_Page()
                _Actual_PageNb = Math.Max(0, _Actual_PageNb - 1)
                _Tracks = _allTrackPages(_Actual_PageNb)
                RaiseEvent On_TracksRebuilt()
            End Sub

            ' ── Lecture d'état ────────────────────────────────────────────────

            ''' <summary>
            ''' Retourne le nombre de FX de la piste de la page donnée.
            ''' Retourne 0 si hors limite.
            ''' </summary>
            ''' <param name="pageTrackIndex">Position 0-based dans la page courante.</param>
            Public Function Get_FxCount(pageTrackIndex As Integer) As Integer
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                If tIdx < 0 Then Return 0
                Dim fx As ReaperTrackFX = Nothing
                If Not _trackFX.TryGetValue(tIdx, fx) Then Return 0
                Return fx.Count
            End Function

            ''' <summary>
            ''' Retourne la liste complète des FX de la piste de la page donnée,
            ''' avec nom, état bypass et classification EQ/DYN/Generic.
            ''' </summary>
            ''' <param name="pageTrackIndex">Position 0-based dans la page courante.</param>
            Public Function Get_FxList(pageTrackIndex As Integer) As List(Of TrackFxInfo)
                Dim result As New List(Of TrackFxInfo)
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                If tIdx < 0 Then Return result
                Dim fx As ReaperTrackFX = Nothing
                If Not _trackFX.TryGetValue(tIdx, fx) Then Return result
                For i = 0 To fx.Count - 1
                    Dim reapFx = fx(i)
                    result.Add(New TrackFxInfo(i, reapFx.Name, reapFx.Enabled,
                                               ClassifyFx(reapFx.Name)))
                Next
                Return result
            End Function

            ''' <summary>
            ''' Retourne les informations d'un FX précis sur la piste de la page donnée.
            ''' Retourne un <see cref="TrackFxInfo"/> invalide (<see cref="TrackFxInfo.IsValid"/> = False)
            ''' si hors limite.
            ''' </summary>
            ''' <param name="pageTrackIndex">Position 0-based dans la page courante.</param>
            ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
            Public Function Get_FxInfo(pageTrackIndex As Integer,
                                        fxIndex As Integer) As TrackFxInfo
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                If tIdx < 0 Then Return New TrackFxInfo(-1, String.Empty, False, TrackFxType.Generic)
                Dim fx As ReaperTrackFX = Nothing
                If Not _trackFX.TryGetValue(tIdx, fx) Then
                    Return New TrackFxInfo(-1, String.Empty, False, TrackFxType.Generic)
                End If
                If fxIndex < 0 OrElse fxIndex >= fx.Count Then
                    Return New TrackFxInfo(-1, String.Empty, False, TrackFxType.Generic)
                End If
                Dim reapFx = fx(fxIndex)
                Return New TrackFxInfo(fxIndex, reapFx.Name, reapFx.Enabled,
                                       ClassifyFx(reapFx.Name))
            End Function

            ''' <summary>
            ''' Retourne True si le FX est actif (non bypassé).
            ''' Retourne False si hors limite.
            ''' </summary>
            Public Function Get_FxEnabled(pageTrackIndex As Integer,
                                           fxIndex As Integer) As Boolean
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                If tIdx < 0 Then Return False
                Dim fx As ReaperTrackFX = Nothing
                If Not _trackFX.TryGetValue(tIdx, fx) Then Return False
                If fxIndex < 0 OrElse fxIndex >= fx.Count Then Return False
                Return fx(fxIndex).Enabled
            End Function

            ''' <summary>
            ''' Retourne la classification <see cref="TrackFxType"/> d'un FX.
            ''' Retourne <see cref="TrackFxType.Generic"/> si hors limite.
            ''' </summary>
            Public Function Get_FxType(pageTrackIndex As Integer,
                                        fxIndex As Integer) As TrackFxType
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                If tIdx < 0 Then Return TrackFxType.Generic
                Dim fx As ReaperTrackFX = Nothing
                If Not _trackFX.TryGetValue(tIdx, fx) Then Return TrackFxType.Generic
                If fxIndex < 0 OrElse fxIndex >= fx.Count Then Return TrackFxType.Generic
                Return ClassifyFx(fx(fxIndex).Name)
            End Function

            ''' <summary>
            ''' Retourne les états bypass de tous les FX de la piste de la page donnée.
            ''' Pratique pour un rafraîchissement groupé des LEDs de la surface.
            ''' </summary>
            Public Function Get_FxEnabledList(pageTrackIndex As Integer) As List(Of Boolean)
                Dim result As New List(Of Boolean)
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                If tIdx < 0 Then Return result
                Dim fx As ReaperTrackFX = Nothing
                If Not _trackFX.TryGetValue(tIdx, fx) Then Return result
                For i = 0 To fx.Count - 1
                    result.Add(fx(i).Enabled)
                Next
                Return result
            End Function

            ' ── Bypass ────────────────────────────────────────────────────────

            ''' <summary>
            ''' Impose l'état bypass d'un FX sur la piste de la page donnée.
            ''' Déclenche <see cref="On_FxBypassChanged"/>.
            ''' </summary>
            ''' <param name="pageTrackIndex">Position 0-based dans la page courante.</param>
            ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
            ''' <param name="enabled">True = actif  False = bypassé.</param>
            Public Sub Set_FxEnabled(pageTrackIndex As Integer,
                                      fxIndex As Integer,
                                      enabled As Boolean)
                _pageLock.EnterReadLock()
                Try
                    Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                    If tIdx < 0 Then Return
                    Dim fx As ReaperTrackFX = Nothing
                    If Not _trackFX.TryGetValue(tIdx, fx) Then Return
                    If fxIndex < 0 OrElse fxIndex >= fx.Count Then Return
                    fx(fxIndex).Enabled = enabled
                    RaiseEvent On_FxBypassChanged(pageTrackIndex, fxIndex, enabled)
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Sub

            ''' <summary>
            ''' Bascule l'état bypass d'un FX (toggle actif/bypassé).
            ''' Déclenche <see cref="On_FxBypassChanged"/>.
            ''' </summary>
            ''' <param name="pageTrackIndex">Position 0-based dans la page courante.</param>
            ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
            Public Sub ToggleFxEnabled(pageTrackIndex As Integer, fxIndex As Integer)
                _pageLock.EnterReadLock()
                Try
                    Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                    If tIdx < 0 Then Return
                    Dim fx As ReaperTrackFX = Nothing
                    If Not _trackFX.TryGetValue(tIdx, fx) Then Return
                    If fxIndex < 0 OrElse fxIndex >= fx.Count Then Return
                    Dim newState = Not fx(fxIndex).Enabled
                    fx(fxIndex).Enabled = newState
                    RaiseEvent On_FxBypassChanged(pageTrackIndex, fxIndex, newState)
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Sub

            ' ── Ajout ─────────────────────────────────────────────────────────

            ''' <summary>
            ''' Ajoute un FX à la piste de la page donnée et recharge la liste locale.
            ''' <br/>
            ''' Utiliser <see cref="InstalledFXInfo.FullName"/> ou la valeur brute
            ''' telle que stockée dans <see cref="ReaperSessionConfig.Plugins"/>
            ''' comme valeur de <paramref name="fxFullName"/>.
            ''' Déclenche <see cref="On_FxAdded"/> puis <see cref="On_FxListChanged"/>.
            ''' Retourne True si l'ajout a réussi.
            ''' </summary>
            ''' <param name="pageTrackIndex">Position 0-based dans la page courante.</param>
            ''' <param name="fxFullName">
            ''' Nom complet du FX ex: "VST3: FabFilter Pro-Q 3 (FabFilter)".
            ''' </param>
            Public Function AddFx(pageTrackIndex As Integer,
                                   fxFullName As String) As Boolean
                _pageLock.EnterReadLock()
                Try
                    Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                    If tIdx < 0 Then Return False
                    Dim fxIdx = utility.AddFXByName(tIdx, fxFullName)
                    If fxIdx < 0 Then Return False
                    LoadTrackFX(tIdx)
                    RaiseEvent On_FxAdded(pageTrackIndex, fxFullName)
                    RaiseEvent On_FxListChanged(pageTrackIndex)
                    Return True
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Function

            ' ── Suppression ───────────────────────────────────────────────────

            ''' <summary>
            ''' Supprime le FX à l'index donné de la piste de la page donnée.
            ''' Les indices des FX suivants sont décalés automatiquement par Reaper.
            ''' Recharge la liste locale après suppression.
            ''' Déclenche <see cref="On_FxDeleted"/> puis <see cref="On_FxListChanged"/>.
            ''' Retourne True si la suppression a réussi.
            ''' </summary>
            ''' <param name="pageTrackIndex">Position 0-based dans la page courante.</param>
            ''' <param name="fxIndex">Index 0-based du FX à supprimer.</param>
            Public Function DeleteFx(pageTrackIndex As Integer,
                                      fxIndex As Integer) As Boolean
                _pageLock.EnterReadLock()
                Try
                    Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                    If tIdx < 0 Then Return False
                    Dim fx As ReaperTrackFX = Nothing
                    If Not _trackFX.TryGetValue(tIdx, fx) Then Return False
                    If fxIndex < 0 OrElse fxIndex >= fx.Count Then Return False
                    Dim ok = fx.DeleteFX(fxIndex)
                    If Not ok Then Return False
                    LoadTrackFX(tIdx)
                    RaiseEvent On_FxDeleted(pageTrackIndex)
                    RaiseEvent On_FxListChanged(pageTrackIndex)
                    Return True
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Function

            ' ── Classification ────────────────────────────────────────────────

            ''' <summary>
            ''' Classifie un FX selon la <see cref="ReaperSessionConfig"/> :
            ''' <see cref="TrackFxType.EQ"/> si dans [EQ],
            ''' <see cref="TrackFxType.Dyn"/> si dans [DYN],
            ''' <see cref="TrackFxType.Generic"/> sinon.
            ''' </summary>
            ''' <param name="fxName">Nom complet du FX (ex: "VST3: ReaEQ (Cockos)").</param>
            Public Function ClassifyFx(fxName As String) As TrackFxType
                If _sessionConfig.IsEQPlugin(fxName) Then Return TrackFxType.EQ
                If _sessionConfig.IsDynPlugin(fxName) Then Return TrackFxType.Dyn
                Return TrackFxType.Generic
            End Function

            ''' <summary>
            ''' Retourne tous les FX EQ de la piste de la page donnée.
            ''' Pratique pour identifier rapidement la chaîne d'égalisation.
            ''' </summary>
            Public Function Get_EqFxList(pageTrackIndex As Integer) As List(Of TrackFxInfo)
                Dim result As New List(Of TrackFxInfo)
                For Each info In Get_FxList(pageTrackIndex)
                    If info.FxType = TrackFxType.EQ Then result.Add(info)
                Next
                Return result
            End Function

            ''' <summary>
            ''' Retourne tous les FX DYN de la piste de la page donnée.
            ''' Pratique pour identifier rapidement la chaîne de dynamique.
            ''' </summary>
            Public Function Get_DynFxList(pageTrackIndex As Integer) As List(Of TrackFxInfo)
                Dim result As New List(Of TrackFxInfo)
                For Each info In Get_FxList(pageTrackIndex)
                    If info.FxType = TrackFxType.Dyn Then result.Add(info)
                Next
                Return result
            End Function

            ' ── Helpers internes ──────────────────────────────────────────────

            Private Function GetAbsoluteTrackIndex(pageTrackIndex As Integer) As Integer
                If _Tracks Is Nothing OrElse
                   pageTrackIndex < 0 OrElse
                   pageTrackIndex >= _Tracks.Count Then Return -1
                Return _Tracks(pageTrackIndex).TrackIndex
            End Function

            ' ── IDisposable ───────────────────────────────────────────────────

            Protected Overridable Sub Dispose(disposing As Boolean)
                If Not disposedValue Then
                    If disposing Then
                        _pageLock.Dispose()
                    End If
                    disposedValue = True
                End If
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                Dispose(disposing:=True)
                GC.SuppressFinalize(Me)
            End Sub

        End Class

        ' ══════════════════════════════════════════════════════════════════════════
        '  PEAK SNAPSHOT — résultat d'un appel Refresh sur le ReaperPeakLevelManager
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Résultat immutable d'un appel <see cref="PageManagers.ReaperPeakLevelManager.Refresh"/>.
        ''' Regroupe en un seul objet :
        ''' <list type="bullet">
        ''' <item>La position de lecture courante en secondes.</item>
        ''' <item>Les peaks stéréo linéaires et le peak-hold de la piste Master.</item>
        ''' <item>Les peaks stéréo linéaires et le peak-hold de toutes les pistes surveillées.</item>
        ''' </list>
        ''' Convertir en dB : <see cref="utility.SLIDER2DB"/>(value).
        ''' </summary>
        Public Structure PeakSnapshot

            ''' <summary>Position de lecture en secondes. -1 si le transport est arrêté.</summary>
            Public ReadOnly PlayPosition As Double

            ''' <summary>Peak linéaire gauche de la piste Master.</summary>
            Public ReadOnly MasterL As Double
            ''' <summary>Peak linéaire droit de la piste Master.</summary>
            Public ReadOnly MasterR As Double
            ''' <summary>Peak-hold linéaire gauche de la piste Master.</summary>
            Public ReadOnly MasterHoldL As Double
            ''' <summary>Peak-hold linéaire droit de la piste Master.</summary>
            Public ReadOnly MasterHoldR As Double

            ''' <summary>
            ''' Peaks et holds des pistes surveillées, 4 valeurs par piste
            ''' (PeakL, PeakR, HoldL, HoldR), à l'indice (trackIndex + 1) * 4.
            ''' Le décalage de 1 permet d'inclure la piste Master (index -1).
            ''' </summary>
            Private ReadOnly _trackData As Double()

            Public Sub New(playPosition As Double,
                       masterL As Double,
                       masterR As Double,
                       masterHoldL As Double,
                       masterHoldR As Double,
                       trackData As Double())
                Me.PlayPosition = playPosition
                Me.MasterL = masterL
                Me.MasterR = masterR
                Me.MasterHoldL = masterHoldL
                Me.MasterHoldR = masterHoldR
                Me._trackData = trackData
            End Sub

            ''' <summary>Peak de la piste Master en dB (L, R).</summary>
            Public ReadOnly Property MasterDB As (L As Double, R As Double)
                Get
                    Return (utility.SLIDER2DB(MasterL), utility.SLIDER2DB(MasterR))
                End Get
            End Property

            ''' <summary>Peak-hold de la piste Master en dB (L, R).</summary>
            Public ReadOnly Property MasterHoldDB As (L As Double, R As Double)
                Get
                    Return (utility.SLIDER2DB(MasterHoldL), utility.SLIDER2DB(MasterHoldR))
                End Get
            End Property

            ''' <summary>Peak d'une piste en dB (L, R). Retourne (-150, -150) si absente.</summary>
            Public Function GetTrackPeakDB(trackIndex As Integer) As (L As Double, R As Double)
                Dim l As Double = 0
                Dim r As Double = 0
                Dim offset As Integer = (trackIndex + 1) * 4
                If _trackData IsNot Nothing AndAlso offset >= 0 AndAlso offset + 3 < _trackData.Length Then
                    l = _trackData(offset)
                    r = _trackData(offset + 1)
                End If
                Return (utility.SLIDER2DB(l), utility.SLIDER2DB(r))
            End Function

            ''' <summary>Peak-hold d'une piste en dB (L, R). Retourne (-150, -150) si absente.</summary>
            Public Function GetTrackHoldDB(trackIndex As Integer) As (L As Double, R As Double)
                Dim l As Double = 0
                Dim r As Double = 0
                Dim offset As Integer = (trackIndex + 1) * 4
                If _trackData IsNot Nothing AndAlso offset >= 0 AndAlso offset + 3 < _trackData.Length Then
                    l = _trackData(offset + 2)
                    r = _trackData(offset + 3)
                End If
                Return (utility.SLIDER2DB(l), utility.SLIDER2DB(r))
            End Function

        End Structure

        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER PEAK LEVEL MANAGER — peaks à la demande, retour PeakSnapshot
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Gère la lecture des niveaux peak stéréo d'une collection de pistes.
        ''' <br/>
        ''' Appeler <see cref="Refresh"/> depuis le timer de la surface (~50 ms) :
        ''' une seule commande pipe <c>GET_PEAKS_STATE</c> retourne simultanément
        ''' la position de lecture, les peaks Master et les peaks de toutes les pistes
        ''' surveillées sous forme d'un <see cref="PeakSnapshot"/> immutable.
        ''' <br/>
        ''' Le peak-hold est calculé côté VB entre les appels — aucun appel pipe
        ''' supplémentaire. Appeler <see cref="ResetHold"/> pour le réinitialiser.
        ''' </summary>
        Public Class ReaperPeakLevelManager
            Implements IDisposable

            Public Const MasterTrackIndex As Integer = -1

            Private ReadOnly _pipe As New ReaperPeakPipe()
            Private ReadOnly _lock As New Object()

            ' ── Pistes surveillées : clé = trackIndex, valeur = isSuspended ───
            Private ReadOnly _tracks As New Dictionary(Of Integer, Boolean)

            ' ── Cache de la requête : reconstruit seulement si l'ensemble change ──
            Private _cacheDirty As Boolean = True
            Private _activeTracks As Integer() = Array.Empty(Of Integer)()
            Private _maxSlot As Integer = -1
            Private _query As String = "GET_PEAKS_STATE|"

            Private _disposed As Boolean = False

            ''' <summary>
            ''' Reconstruit la liste des pistes actives et la requête pipe.
            ''' À appeler sous <c>_lock</c>.
            ''' </summary>
            Private Sub RebuildCache()
                Dim ic = CultureInfo.InvariantCulture
                Dim active As New List(Of Integer)(_tracks.Count)
                Dim maxSlot As Integer = -1
                For Each kvp In _tracks
                    If kvp.Value = False Then
                        active.Add(kvp.Key)
                        If kvp.Key + 1 > maxSlot Then
                            maxSlot = kvp.Key + 1
                        End If
                    End If
                Next

                Dim parts(active.Count - 1) As String
                For i = 0 To active.Count - 1
                    parts(i) = active(i).ToString(ic)
                Next

                _activeTracks = active.ToArray()
                _maxSlot = maxSlot
                _query = "GET_PEAKS_STATE|" & String.Join("|", parts)
                _cacheDirty = False
            End Sub

            ' ── Gestion de la collection ──────────────────────────────────────

            ''' <summary>La piste Master est toujours incluse dans le snapshot.</summary>
            Public Sub WatchMaster()
            End Sub

            Public Sub UnwatchMaster()
            End Sub

            ''' <summary>Ajoute une piste à la surveillance.</summary>
            Public Sub WatchTrack(trackIndex As Integer)
                SyncLock _lock
                    If Not _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = False
                        _cacheDirty = True
                    End If
                End SyncLock
            End Sub

            ''' <summary>Ajoute une collection de pistes à la surveillance.</summary>
            Public Sub WatchTracks(tracks As IEnumerable(Of ReaperTrack))
                For Each trk In tracks
                    WatchTrack(trk.TrackIndex)
                Next
            End Sub

            ''' <summary>Retire une piste.</summary>
            Public Sub UnwatchTrack(trackIndex As Integer)
                SyncLock _lock
                    _tracks.Remove(trackIndex)
                    _cacheDirty = True
                End SyncLock
            End Sub

            ''' <summary>Retire toutes les pistes.</summary>
            Public Sub ClearWatched()
                SyncLock _lock
                    _tracks.Clear()
                    _cacheDirty = True
                End SyncLock
            End Sub

            ''' <summary>Suspend une piste — ignorée dans le snapshot.</summary>
            Public Sub SuspendTrack(trackIndex As Integer)
                SyncLock _lock
                    If _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = True
                        _cacheDirty = True
                    End If
                End SyncLock
            End Sub

            ''' <summary>Réactive une piste.</summary>
            Public Sub ResumeTrack(trackIndex As Integer)
                SyncLock _lock
                    If _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = False
                        _cacheDirty = True
                    End If
                End SyncLock
            End Sub

            ''' <summary>True si la piste est suspendue.</summary>
            Public Function IsTrackSuspended(trackIndex As Integer) As Boolean
                SyncLock _lock
                    Dim s As Boolean = False
                    _tracks.TryGetValue(trackIndex, s)
                    Return s
                End SyncLock
            End Function

            ' ── Hold ─────────────────────────────────────────────────────────
            ' Les valeurs hold sont désormais lues directement depuis Reaper
            ' (canal | 1024 dans Track_GetPeakInfo). Les méthodes ci-dessous
            ' sont conservées pour compatibilité mais sans effet local.

            ''' <summary>
            ''' Sans effet — le peak-hold est géré par Reaper (canal | 1024).
            ''' Conserver uniquement si un reset côté Reaper est implémenté.
            ''' </summary>
            Public Sub ResetHold()
            End Sub

            ''' <summary>Sans effet — voir <see cref="ResetHold"/>.</summary>
            Public Sub ResetHold(trackIndex As Integer)
            End Sub

            ' ── Rafraîchissement ──────────────────────────────────────────────

            ''' <summary>
            ''' Envoie <c>GET_PEAKS_STATE|t0|t1|…</c> au bridge et retourne un
            ''' <see cref="PeakSnapshot"/> immutable.
            ''' <br/>
            ''' Format de réponse :
            ''' <c>playPos|masterL|masterR|masterHoldL|masterHoldR|t0L|t0R|t0HoldL|t0HoldR|…</c>
            ''' </summary>
            Public Function Refresh() As PeakSnapshot
                Dim activeTracks As Integer()
                Dim query As String
                Dim maxSlot As Integer
                SyncLock _lock
                    If _cacheDirty Then
                        RebuildCache()
                    End If
                    activeTracks = _activeTracks
                    query = _query
                    maxSlot = _maxSlot
                End SyncLock

                Dim ic = CultureInfo.InvariantCulture

                Dim raw = _pipe.Request(query)

                ' ── Valeurs par défaut ────────────────────────────────────────
                Dim playPos As Double = -1
                Dim mL As Double = 0, mR As Double = 0
                Dim mHoldL As Double = 0, mHoldR As Double = 0
                Dim trackData(maxSlot * 4 + 3) As Double

                If Not String.IsNullOrEmpty(raw) AndAlso Not raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                    Dim pos As Integer = 0
                    Dim fieldCount As Integer = 1
                    For k = 0 To raw.Length - 1
                        If raw(k) = "|"c Then
                            fieldCount += 1
                        End If
                    Next

                    ' Format : playPos | masterL | masterR | masterHoldL | masterHoldR
                    '          | t0L | t0R | t0HoldL | t0HoldR | t1L | …
                    If fieldCount >= 5 Then
                        playPos = ReadNextDouble(raw, pos, ic)
                        mL = ReadNextDouble(raw, pos, ic)
                        mR = ReadNextDouble(raw, pos, ic)
                        mHoldL = ReadNextDouble(raw, pos, ic)
                        mHoldR = ReadNextDouble(raw, pos, ic)

                        ' ── Peaks pistes (4 champs par piste : L | R | HoldL | HoldR) ─
                        For i = 0 To activeTracks.Length - 1
                            Dim offset = 5 + i * 4
                            If offset + 3 >= fieldCount Then Exit For

                            Dim slot = activeTracks(i) + 1
                            Dim tL As Double = ReadNextDouble(raw, pos, ic)
                            Dim tR As Double = ReadNextDouble(raw, pos, ic)
                            Dim tHL As Double = ReadNextDouble(raw, pos, ic)
                            Dim tHR As Double = ReadNextDouble(raw, pos, ic)

                            If slot >= 0 Then
                                trackData(slot * 4) = tL
                                trackData(slot * 4 + 1) = tR
                                trackData(slot * 4 + 2) = tHL
                                trackData(slot * 4 + 3) = tHR
                            End If
                        Next
                    End If
                End If

                Return New PeakSnapshot(playPos, mL, mR, mHoldL, mHoldR, trackData)
            End Function

            ''' <summary>
            ''' Lit le prochain champ délimité par '|' dans <paramref name="raw"/> à partir de <paramref name="pos"/>,
            ''' le parse en Double (0 si non valide) et avance <paramref name="pos"/> après le séparateur.
            ''' </summary>
            ''' <param name="raw">Réponse complète du pipe.</param>
            ''' <param name="pos">Position de lecture, mise à jour après lecture.</param>
            ''' <param name="provider">Culture de parsing (InvariantCulture).</param>
            ''' <returns>La valeur parsée, ou 0 si le champ n'est pas un nombre valide.</returns>
            Private Shared Function ReadNextDouble(raw As String, ByRef pos As Integer, provider As IFormatProvider) As Double
                Dim start As Integer = pos
                Dim sep As Integer = raw.IndexOf("|"c, start)
                Dim length As Integer
                If sep < 0 Then
                    length = raw.Length - start
                    pos = raw.Length
                Else
                    length = sep - start
                    pos = sep + 1
                End If

                Dim value As Double = 0
                Double.TryParse(raw.AsSpan(start, length), Globalization.NumberStyles.Any, provider, value)
                Return value
            End Function

            Protected Overridable Sub Dispose(disposing As Boolean)
                If Not _disposed Then
                    If disposing Then
                        _pipe.Dispose()
                    End If
                    _disposed = True
                End If
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                Dispose(disposing:=True)
                GC.SuppressFinalize(Me)
            End Sub

        End Class
        ' ── Pipe persistant dédié aux peaks ───────────────────────────────────
        Friend Class ReaperPeakPipe
            Implements IDisposable

            Private _pipe As IO.Pipes.NamedPipeClientStream
            Private _disposed As Boolean = False
            Private ReadOnly _connectLock As New Object()

            Public ReadOnly Property IsConnected As Boolean
                Get
                    Return _pipe IsNot Nothing AndAlso _pipe.IsConnected
                End Get
            End Property

            Public Function Connect() As Boolean
                SyncLock _connectLock
                    Try
                        Disconnect()
                        _pipe = New IO.Pipes.NamedPipeClientStream(
                            ".", "c24_reaper_peaks", IO.Pipes.PipeDirection.InOut)
                        _pipe.Connect(500)
                        Return True
                    Catch
                        Disconnect()
                        Return False
                    End Try
                End SyncLock
            End Function

            Public Sub Disconnect()
                Try
                    If _pipe IsNot Nothing Then
                        _pipe.Dispose()
                        _pipe = Nothing
                    End If
                Catch
                End Try
            End Sub

            Public Function Request(query As String) As String
                If Not IsConnected Then
                    If Not Connect() Then Return String.Empty
                End If
                Try
                    utility.WriteLineBytes(_pipe, query)
                    Dim response As String = utility.ReadLineBytes(_pipe)
                    If response Is Nothing Then
                        Disconnect()
                        Return String.Empty
                    End If
                    Return response
                Catch
                    Disconnect()
                    Return String.Empty
                End Try
            End Function

            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    Disconnect()
                    _disposed = True
                End If
            End Sub

        End Class


        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER AUTOMATION MANAGER — armement et suspension d'enveloppes
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Gère l'armement et la suspension des enveloppes d'automation pour une
        ''' liste fixe de pistes.
        ''' <list type="table">
        ''' <item><term>Vol / Pan / Mute</term>
        '''       <description>Arme et crée si absente (optionnel).</description></item>
        ''' <item><term>Send Vol / Send Mute</term>
        '''       <description>Arme et crée si absente (optionnel).</description></item>
        ''' <item><term>FX params</term>
        '''       <description>Arme uniquement les enveloppes déjà existantes
        '''                    — jamais de création.</description></item>
        ''' </list>
        ''' Appeler <see cref="SuspendAll"/> pour désarmer temporairement toutes les
        ''' enveloppes trackées (contrôle manuel), <see cref="ResumeAll"/> pour
        ''' les réarmer.
        ''' </summary>
        Public Class ReaperAutomationManager

            ' ── Liste source de pistes (fixe) ────────────────────────────────────
            Private ReadOnly _tracks As IReadOnlyList(Of ReaperTrack)

            ' ── Cache des enveloppes armées ───────────────────────────────────────
            ' Chaque entrée = (trackIndex, envIndex) d'une enveloppe armée par ce manager.
            ' Utilisé par SuspendAll / ResumeAll / DisarmAll.
            Private ReadOnly _armed As New List(Of (TrackIndex As Integer, EnvIndex As Integer))
            Private ReadOnly _lock As New Object()
            ' ── Cache état armé par type ──────────────────────────────────────────
            ' Mémorise la dernière valeur publiée pour éviter les doublons d'événements.
            Private _cachedHasArmedVol As Boolean = False
            Private _cachedHasArmedPan As Boolean = False
            Private _cachedHasArmedMute As Boolean = False
            Private _cachedHasArmedSendVol As Boolean = False
            Private _cachedHasArmedSendPan As Boolean = False
            Private _cachedHasArmedSendMute As Boolean = False
            Private _cachedHasArmedFxParam As Boolean = False

            Private _ReaperTrackMonitor As ReaperTrackMonitor

            ' ── Événements par type d'enveloppe ──────────────────────────────────

            ''' <summary>
            ''' Déclenché quand l'état "au moins une enveloppe de volume armée" change.
            ''' </summary>
            Public Event On_VolEnvelopeArmedChange(armed As Boolean)

            ''' <summary>
            ''' Déclenché quand l'état "au moins une enveloppe de pan armée" change.
            ''' </summary>
            Public Event On_PanEnvelopeArmedChange(armed As Boolean)

            ''' <summary>
            ''' Déclenché quand l'état "au moins une enveloppe de mute armée" change.
            ''' </summary>
            Public Event On_MuteEnvelopeArmedChange(armed As Boolean)

            ''' <summary>
            ''' Déclenché quand l'état "au moins une enveloppe de volume d'envoi armée" change.
            ''' </summary>
            Public Event On_SendVolEnvelopeArmedChange(armed As Boolean)

            ''' <summary>
            ''' Déclenché quand l'état "au moins une enveloppe de pan d'envoi armée" change.
            ''' </summary>
            Public Event On_SendPanEnvelopeArmedChange(armed As Boolean)

            ''' <summary>
            ''' Déclenché quand l'état "au moins une enveloppe de mute d'envoi armée" change.
            ''' </summary>
            Public Event On_SendMuteEnvelopeArmedChange(armed As Boolean)

            ''' <summary>
            ''' Déclenché quand l'état "au moins une enveloppe de paramètre FX armée" change.
            ''' </summary>
            Public Event On_FxParamEnvelopeArmedChange(armed As Boolean)

            ' ── Événements ────────────────────────────────────────────────────────

            ''' <summary>Déclenché quand une enveloppe est armée par ce manager.</summary>
            Public Event On_EnvelopeArmed(trackIndex As Integer,
                                               envIndex As Integer,
                                               name As String)

            ''' <summary>Déclenché quand une enveloppe est désarmée définitivement.</summary>
            Public Event On_EnvelopeDisarmed(trackIndex As Integer, envIndex As Integer)

            ''' <summary>Déclenché quand toutes les enveloppes sont suspendues.</summary>
            Public Event On_AllSuspended()

            ''' <summary>Déclenché quand toutes les enveloppes sont réactivées.</summary>
            Public Event On_AllResumed()

            ' ── Constructeur ──────────────────────────────────────────────────────

            ''' <summary>
            ''' Initialise le manager pour une liste fixe de pistes.
            ''' </summary>
            ''' <param name="tracks">Liste fixe de pistes à gérer.</param>
            Public Sub New(tracks As IReadOnlyList(Of ReaperTrack), ReaperTrackMonitor As ReaperTrackMonitor)
                _tracks = tracks
                _ReaperTrackMonitor = ReaperTrackMonitor
            End Sub

            ' ── Propriétés ────────────────────────────────────────────────────────

            ''' <summary>Nombre d'enveloppes actuellement trackées (armées).</summary>
            Public ReadOnly Property ArmedCount As Integer
                Get
                    SyncLock _lock
                        Return _armed.Count
                    End SyncLock
                End Get
            End Property

            ' ── Propriétés d'état par type d'enveloppe ────────────────────────────

            ''' <summary>
            ''' True si au moins une enveloppe de volume est armée dans la liste des pistes.
            ''' </summary>
            Public ReadOnly Property HasArmedVolEnvelope As Boolean
                Get
                    Return HasArmedEnvelopeOfType(Of ReaperTrackVolEnvelope)()
                End Get
            End Property

            ''' <summary>
            ''' True si au moins une enveloppe de pan est armée dans la liste des pistes.
            ''' </summary>
            Public ReadOnly Property HasArmedPanEnvelope As Boolean
                Get
                    Return HasArmedEnvelopeOfType(Of ReaperTrackPanEnvelope)()
                End Get
            End Property

            ''' <summary>
            ''' True si au moins une enveloppe de mute est armée dans la liste des pistes.
            ''' </summary>
            Public ReadOnly Property HasArmedMuteEnvelope As Boolean
                Get
                    Return HasArmedEnvelopeOfType(Of ReaperTrackMuteEnvelope)()
                End Get
            End Property

            ''' <summary>
            ''' True si au moins une enveloppe de volume d'envoi est armée.
            ''' </summary>
            Public ReadOnly Property HasArmedSendVolEnvelope As Boolean
                Get
                    Return HasArmedEnvelopeOfType(Of ReaperSendVolEnvelope)()
                End Get
            End Property

            ''' <summary>
            ''' True si au moins une enveloppe de pan d'envoi est armée.
            ''' </summary>
            Public ReadOnly Property HasArmedSendPanEnvelope As Boolean
                Get
                    Return HasArmedEnvelopeOfType(Of ReaperSendPanEnvelope)()
                End Get
            End Property

            ''' <summary>
            ''' True si au moins une enveloppe de mute d'envoi est armée.
            ''' </summary>
            Public ReadOnly Property HasArmedSendMuteEnvelope As Boolean
                Get
                    Return HasArmedEnvelopeOfType(Of ReaperSendMuteEnvelope)()
                End Get
            End Property

            ''' <summary>
            ''' True si au moins une enveloppe de paramètre FX est armée.
            ''' </summary>
            Public ReadOnly Property HasArmedFxParamEnvelope As Boolean
                Get
                    Return HasArmedEnvelopeOfType(Of ReaperFxParamEnvelope)()
                End Get
            End Property

            ''' <summary>
            ''' True si au moins une enveloppe d'un type quelconque est armée.
            ''' Équivalent de <see cref="ArmedCount"/> &gt; 0.
            ''' </summary>
            Public ReadOnly Property HasArmedAnyEnvelope As Boolean
                Get
                    SyncLock _lock
                        Return _armed.Count > 0
                    End SyncLock
                End Get
            End Property

            ' ── Helper interne ────────────────────────────────────────────────────

            ''' <summary>
            ''' Vérifie si au moins une entrée de <see cref="_armed"/> correspond à
            ''' une enveloppe du type <typeparamref name="T"/> en croisant la liste
            ''' armée avec <see cref="ReaperTrackEnvelopes.AllOfType"/> de chaque piste.
            ''' Résolution par (TrackIndex + EnvIndex) — aucun appel pipe.
            ''' </summary>
            Private Function HasArmedEnvelopeOfType(Of T As ReaperTrackEnvelope)() As Boolean
                SyncLock _lock
                    For Each entry In _armed
                        For Each trk In _tracks
                            If trk.TrackIndex <> entry.TrackIndex Then Continue For
                            For Each env In trk.Envelopes.AllOfType(Of T)()
                                If env.EnvIndex = entry.EnvIndex Then Return True
                            Next
                            Exit For
                        Next
                    Next
                End SyncLock
                Return False
            End Function

            ' ── Armement Vol / Pan / Mute ─────────────────────────────────────────

            ''' <summary>
            ''' Arme l'enveloppe de volume d'une piste.
            ''' Si elle n'existe pas et que <paramref name="create"/> est True,
            ''' la crée avant de l'armer.
            ''' </summary>
            ''' <param name="trackIndex">Index 0-based de la piste.</param>
            ''' <param name="create">True = créer l'enveloppe si absente (défaut : True).</param>
            Public Function ArmVolEnvelope(trackIndex As Integer,
                                                Optional create As Boolean = True) As Boolean
                Return ArmTrackEnvelopeByName(trackIndex, "Volume", create)
            End Function

            ''' <summary>
            ''' Arme l'enveloppe de pan d'une piste.
            ''' Crée l'enveloppe si absente et <paramref name="create"/> est True.
            ''' </summary>
            ''' <param name="trackIndex">Index 0-based de la piste.</param>
            ''' <param name="create">True = créer l'enveloppe si absente (défaut : True).</param>
            Public Function ArmPanEnvelope(trackIndex As Integer,
                                                Optional create As Boolean = True) As Boolean
                Return ArmTrackEnvelopeByName(trackIndex, "Pan", create)
            End Function

            ''' <summary>
            ''' Arme l'enveloppe de mute d'une piste.
            ''' Crée l'enveloppe si absente et <paramref name="create"/> est True.
            ''' </summary>
            ''' <param name="trackIndex">Index 0-based de la piste.</param>
            ''' <param name="create">True = créer l'enveloppe si absente (défaut : True).</param>
            Public Function ArmMuteEnvelope(trackIndex As Integer,
                                                 Optional create As Boolean = True) As Boolean
                Return ArmTrackEnvelopeByName(trackIndex, "Mute", create)
            End Function

            ' ── Armement Send Vol / Send Mute ─────────────────────────────────────

            ''' <summary>
            ''' Arme l'enveloppe de volume de l'envoi <paramref name="sendIndex"/> sur
            ''' la piste <paramref name="trackIndex"/>.
            ''' Crée l'enveloppe si absente et <paramref name="create"/> est True.
            ''' </summary>
            ''' <param name="trackIndex">Index 0-based de la piste.</param>
            ''' <param name="sendIndex">Index 0-based de l'envoi.</param>
            ''' <param name="create">True = créer l'enveloppe si absente (défaut : True).</param>
            Public Function ArmSendVolEnvelope(trackIndex As Integer,
                                                    sendIndex As Integer,
                                                    Optional create As Boolean = True) As Boolean
                Return ArmSendEnvelopeByName(trackIndex, sendIndex, "Volume", create)
            End Function

            ''' <summary>
            ''' Arme l'enveloppe de mute de l'envoi <paramref name="sendIndex"/> sur
            ''' la piste <paramref name="trackIndex"/>.
            ''' Crée l'enveloppe si absente et <paramref name="create"/> est True.
            ''' </summary>
            ''' <param name="trackIndex">Index 0-based de la piste.</param>
            ''' <param name="sendIndex">Index 0-based de l'envoi.</param>
            ''' <param name="create">True = créer l'enveloppe si absente (défaut : True).</param>
            Public Function ArmSendMuteEnvelope(trackIndex As Integer,
                                                     sendIndex As Integer,
                                                     Optional create As Boolean = True) As Boolean
                Return ArmSendEnvelopeByName(trackIndex, sendIndex, "Mute", create)
            End Function

            ' ── Armement FX (pas de création) ─────────────────────────────────────

            ''' <summary>
            ''' Arme toutes les enveloppes existantes du FX
            ''' <paramref name="fxIndex"/> sur la piste <paramref name="trackIndex"/>.
            ''' <br/>
            ''' Aucune enveloppe n'est créée — seules celles déjà présentes
            ''' dans Reaper sont armées.
            ''' </summary>
            ''' <param name="trackIndex">Index 0-based de la piste.</param>
            ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
            ''' <returns>Nombre d'enveloppes effectivement armées.</returns>
            Public Function ArmFxEnvelopes(trackIndex As Integer,
                                                fxIndex As Integer) As Integer
                Dim count = 0
                Dim nParams = GetFxParamCount(trackIndex, fxIndex)
                For paramIndex = 0 To nParams - 1
                    Dim envIdx = utility.GetFXEnvelopeIndex(trackIndex, fxIndex,
                                                                 paramIndex, create:=False)
                    If envIdx >= 0 Then
                        If ArmEnvelope(trackIndex, envIdx) Then
                            count += 1
                        End If
                    End If
                Next
                Return count
            End Function

            ''' <summary>
            ''' Arme l'enveloppe d'un paramètre FX précis si elle existe déjà.
            ''' Retourne False si l'enveloppe n'existe pas (aucune création).
            ''' </summary>
            ''' <param name="trackIndex">Index 0-based de la piste.</param>
            ''' <param name="fxIndex">Index 0-based du FX.</param>
            ''' <param name="paramIndex">Index 0-based du paramètre.</param>
            Public Function ArmFxParamEnvelope(trackIndex As Integer,
                                                    fxIndex As Integer,
                                                    paramIndex As Integer) As Boolean
                Dim envIdx = utility.GetFXEnvelopeIndex(trackIndex, fxIndex,
                                                              paramIndex, create:=False)
                If envIdx < 0 Then Return False
                Return ArmEnvelope(trackIndex, envIdx)
            End Function

            ' ── Armement groupé par piste ─────────────────────────────────────────

            ''' <summary>
            ''' Arme les enveloppes Vol / Pan / Mute d'une piste.
            ''' </summary>
            ''' <param name="trackIndex">Index 0-based de la piste.</param>
            ''' <param name="armVol">Armer l'enveloppe de volume (défaut : True).</param>
            ''' <param name="armPan">Armer l'enveloppe de pan (défaut : True).</param>
            ''' <param name="armMute">Armer l'enveloppe de mute (défaut : True).</param>
            ''' <param name="create">Créer les enveloppes absentes (défaut : True).</param>
            Public Sub ArmTrackEnvelopes(trackIndex As Integer,
                                              Optional armVol As Boolean = True,
                                              Optional armPan As Boolean = True,
                                              Optional armMute As Boolean = True,
                                              Optional create As Boolean = True)
                If armVol Then ArmVolEnvelope(trackIndex, create)
                If armPan Then ArmPanEnvelope(trackIndex, create)
                If armMute Then ArmMuteEnvelope(trackIndex, create)
            End Sub

            ''' <summary>
            ''' Arme les enveloppes Vol / Pan / Mute de toutes les pistes de la liste source.
            ''' </summary>
            ''' <param name="armVol">Armer les enveloppes de volume (défaut : True).</param>
            ''' <param name="armPan">Armer les enveloppes de pan (défaut : True).</param>
            ''' <param name="armMute">Armer les enveloppes de mute (défaut : True).</param>
            ''' <param name="create">Créer les enveloppes absentes (défaut : True).</param>
            Public Sub ArmAllTracks(Optional armVol As Boolean = True,
                                         Optional armPan As Boolean = True,
                                         Optional armMute As Boolean = True,
                                         Optional create As Boolean = True)
                For Each trk In _tracks
                    ArmTrackEnvelopes(trk.TrackIndex, armVol, armPan, armMute, create)
                Next
            End Sub

            ''' <summary>
            ''' Arme les enveloppes de tous les FX existants de toutes les pistes
            ''' de la liste source (aucune création).
            ''' </summary>
            Public Sub ArmAllFxEnvelopes()
                For Each trk In _tracks
                    Dim fxCount = GetFxCount(trk.TrackIndex)
                    For fxIndex = 0 To fxCount - 1
                        ArmFxEnvelopes(trk.TrackIndex, fxIndex)
                    Next
                Next
            End Sub

            ' ── Suspension / Reprise ──────────────────────────────────────────────

            ''' <summary>
            ''' Désarme temporairement toutes les enveloppes trackées par ce manager
            ''' sans les retirer de la liste — le contrôle manuel reprend la main.
            ''' <br/>
            ''' Appeler <see cref="ResumeAll"/> pour les réarmer.
            ''' </summary>
            Public Sub SuspendAll()
                SyncLock _lock
                    For Each entry In _armed
                        _ReaperTrackMonitor.SuspendTrack(entry.TrackIndex)
                        utility.SetEnvelopePropVal(entry.TrackIndex, entry.EnvIndex,
                                                        "ARMED", 0.0)
                        _ReaperTrackMonitor.ResumeTrack(entry.TrackIndex)
                    Next
                End SyncLock
                RaiseEvent On_AllSuspended()
            End Sub

            ''' <summary>
            ''' Réarme toutes les enveloppes précédemment suspendues via
            ''' <see cref="SuspendAll"/>.
            ''' </summary>
            Public Sub ResumeAll()
                SyncLock _lock
                    For Each entry In _armed
                        _ReaperTrackMonitor.SuspendTrack(entry.TrackIndex)
                        utility.SetEnvelopePropVal(entry.TrackIndex, entry.EnvIndex,
                                                        "ARMED", 1.0)
                        _ReaperTrackMonitor.ResumeTrack(entry.TrackIndex)
                    Next
                End SyncLock
                RaiseEvent On_AllResumed()
            End Sub



            ' ── Helpers internes ──────────────────────────────────────────────────

            ''' <summary>
            ''' Arme une enveloppe par son index et l'enregistre dans la liste trackée.
            ''' Sans effet si l'enveloppe est déjà dans la liste.
            ''' Déclenche <see cref="On_EnvelopeArmed"/> et les événements par type si l'état change.
            ''' </summary>
            Private Function ArmEnvelope(trackIndex As Integer, envIndex As Integer) As Boolean
                _ReaperTrackMonitor.SuspendTrack(trackIndex)
                Dim ok = utility.SetEnvelopePropVal(trackIndex, envIndex, "ARMED", 1.0)
                If Not ok Then Return False

                Dim alreadyTracked As Boolean
                SyncLock _lock
                    alreadyTracked = False
                    For Each e In _armed
                        If e.TrackIndex = trackIndex AndAlso e.EnvIndex = envIndex Then
                            alreadyTracked = True
                            Exit For
                        End If
                    Next
                    If Not alreadyTracked Then
                        _armed.Add((trackIndex, envIndex))
                    End If
                End SyncLock

                If Not alreadyTracked Then
                    Dim name = utility.GetEnvelopeName(trackIndex, envIndex)
                    RaiseEvent On_EnvelopeArmed(trackIndex, envIndex, name)
                    ' ── Notifier les changements d'état par type ──────────
                    RaiseEnvelopeTypeChanges()
                End If
                _ReaperTrackMonitor.ResumeTrack(trackIndex)
                Return True
            End Function

            ''' <summary>
            ''' Désarme définitivement toutes les enveloppes trackées et vide la liste.
            ''' Contrairement à <see cref="SuspendAll"/>, un appel à <see cref="ResumeAll"/>
            ''' ne les réarmera pas.
            ''' </summary>
            Public Sub DisarmAll()

                SyncLock _lock
                    For Each entry In _armed
                        _ReaperTrackMonitor.SuspendTrack(entry.TrackIndex)
                        utility.SetEnvelopePropVal(entry.TrackIndex, entry.EnvIndex,
                                                        "ARMED", 0.0)
                        _ReaperTrackMonitor.ResumeTrack(entry.TrackIndex)
                        RaiseEvent On_EnvelopeDisarmed(entry.TrackIndex, entry.EnvIndex)
                    Next
                    _armed.Clear()
                End SyncLock
                ' ── Notifier les changements d'état par type ──────────────
                RaiseEnvelopeTypeChanges()
            End Sub
            ''' <summary>
            ''' Compare l'état armé actuel de chaque type d'enveloppe avec le cache et
            ''' déclenche l'événement correspondant uniquement en cas de changement.
            ''' Appelé après <see cref="ArmEnvelope"/> et <see cref="DisarmAll"/>.
            ''' </summary>
            Private Sub RaiseEnvelopeTypeChanges()
                Dim vol = HasArmedEnvelopeOfType(Of ReaperTrackVolEnvelope)()
                If _cachedHasArmedVol <> vol Then
                    _cachedHasArmedVol = vol
                    RaiseEvent On_VolEnvelopeArmedChange(vol)
                End If

                Dim pan = HasArmedEnvelopeOfType(Of ReaperTrackPanEnvelope)()
                If _cachedHasArmedPan <> pan Then
                    _cachedHasArmedPan = pan
                    RaiseEvent On_PanEnvelopeArmedChange(pan)
                End If

                Dim mute = HasArmedEnvelopeOfType(Of ReaperTrackMuteEnvelope)()
                If _cachedHasArmedMute <> mute Then
                    _cachedHasArmedMute = mute
                    RaiseEvent On_MuteEnvelopeArmedChange(mute)
                End If

                Dim sendVol = HasArmedEnvelopeOfType(Of ReaperSendVolEnvelope)()
                If _cachedHasArmedSendVol <> sendVol Then
                    _cachedHasArmedSendVol = sendVol
                    RaiseEvent On_SendVolEnvelopeArmedChange(sendVol)
                End If

                Dim sendPan = HasArmedEnvelopeOfType(Of ReaperSendPanEnvelope)()
                If _cachedHasArmedSendPan <> sendPan Then
                    _cachedHasArmedSendPan = sendPan
                    RaiseEvent On_SendPanEnvelopeArmedChange(sendPan)
                End If

                Dim sendMute = HasArmedEnvelopeOfType(Of ReaperSendMuteEnvelope)()
                If _cachedHasArmedSendMute <> sendMute Then
                    _cachedHasArmedSendMute = sendMute
                    RaiseEvent On_SendMuteEnvelopeArmedChange(sendMute)
                End If

                Dim fxParam = HasArmedEnvelopeOfType(Of ReaperFxParamEnvelope)()
                If _cachedHasArmedFxParam <> fxParam Then
                    _cachedHasArmedFxParam = fxParam
                    RaiseEvent On_FxParamEnvelopeArmedChange(fxParam)
                End If
            End Sub
            ''' <summary>
            ''' Cherche une enveloppe de piste par nom (Volume / Pan / Mute).
            ''' Retourne l'envIndex 0-based, ou -1 si introuvable.
            ''' </summary>
            Private Function FindEnvelopeByName(trackIndex As Integer,
                                                      name As String) As Integer
                Dim count = utility.CountTrackEnvelopes(trackIndex)
                If count <= 0 Then Return -1
                For i = 0 To count - 1
                    Dim envName = utility.GetEnvelopeName(trackIndex, i)
                    If envName.Equals(name, StringComparison.OrdinalIgnoreCase) Then
                        Return i
                    End If
                Next
                Return -1
            End Function

            ''' <summary>
            ''' Cherche une enveloppe d'envoi par nom ET par index d'envoi
            ''' (<c>I_SEND_IDX</c>).
            ''' Retourne l'envIndex 0-based, ou -1 si introuvable.
            ''' </summary>
            Private Function FindSendEnvelope(trackIndex As Integer,
                                                   sendIndex As Integer,
                                                   name As String) As Integer
                Dim count = utility.CountTrackEnvelopes(trackIndex)
                If count <= 0 Then Return -1
                For i = 0 To count - 1
                    Dim envName = utility.GetEnvelopeName(trackIndex, i)
                    If Not envName.Equals(name, StringComparison.OrdinalIgnoreCase) Then
                        Continue For
                    End If
                    Dim rawSendId = utility.GetEnvelopePropVal2(trackIndex, i, "I_SEND_IDX")
                    If rawSendId = Double.MinValue Then Continue For
                    If CInt(rawSendId) = sendIndex Then Return i
                Next
                Return -1
            End Function

            ''' <summary>
            ''' Arme une enveloppe de piste standard (Volume/Pan/Mute) en la cherchant
            ''' par nom. Si absente et <paramref name="create"/> est True,
            ''' demande sa création au bridge via <c>CREATE_TRACK_ENVELOPE</c>.
            ''' </summary>
            Private Function ArmTrackEnvelopeByName(trackIndex As Integer,
                                                          name As String,
                                                          create As Boolean) As Boolean
                _ReaperTrackMonitor.SuspendTrack(trackIndex)
                Dim envIdx = FindEnvelopeByName(trackIndex, name)
                If envIdx < 0 Then
                    If Not create Then Return False
                    Dim raw = utility.CallReaperBridge(
                            $"CREATE_TRACK_ENVELOPE|{trackIndex}|{name}")
                    If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return False
                    Integer.TryParse(raw, envIdx)
                    If envIdx < 0 Then Return False
                End If
                _ReaperTrackMonitor.ResumeTrack(trackIndex)
                Return ArmEnvelope(trackIndex, envIdx)
            End Function

            ''' <summary>
            ''' Arme une enveloppe d'envoi (Send Volume / Send Mute) en la cherchant
            ''' par nom + sendIndex. Si absente et <paramref name="create"/> est True,
            ''' demande sa création au bridge via <c>CREATE_SEND_ENVELOPE</c>.
            ''' </summary>
            Private Function ArmSendEnvelopeByName(trackIndex As Integer,
                                                         sendIndex As Integer,
                                                         name As String,
                                                         create As Boolean) As Boolean

                _ReaperTrackMonitor.SuspendTrack(trackIndex)
                Dim envIdx = FindSendEnvelope(trackIndex, sendIndex, name)
                If envIdx < 0 Then
                    If Not create Then Return False
                    Dim raw = utility.CallReaperBridge(
                            $"CREATE_SEND_ENVELOPE|{trackIndex}|{sendIndex}|{name}")
                    If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return False
                    Integer.TryParse(raw, envIdx)
                    If envIdx < 0 Then Return False
                End If
                _ReaperTrackMonitor.ResumeTrack(trackIndex)
                Return ArmEnvelope(trackIndex, envIdx)
            End Function

            ''' <summary>
            ''' Retourne le nombre de paramètres d'un FX via <c>GET_FX_ALL_INFO</c>.
            ''' Retourne 0 si le bridge est indisponible.
            ''' </summary>
            Private Function GetFxParamCount(trackIndex As Integer, fxIndex As Integer) As Integer
                Dim raw = utility.CallReaperBridge($"GET_FX_ALL_INFO|{trackIndex}|{fxIndex}")
                If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return 0
                Dim p = raw.Split("|"c)
                If p.Length < 3 Then Return 0
                Dim count As Integer = 0
                Integer.TryParse(p(2), count)
                Return count
            End Function

            ''' <summary>
            ''' Retourne le nombre de FX d'une piste.
            ''' Retourne 0 si le bridge est indisponible.
            ''' </summary>
            Private Function GetFxCount(trackIndex As Integer) As Integer
                Dim raw = utility.CallReaperBridge($"GET_FX_COUNT|{trackIndex}")
                If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return 0
                Dim count As Integer = 0
                Integer.TryParse(raw, count)
                Return count
            End Function

        End Class

        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER TRACK SENDS EDITOR — liste, ajout, suppression de sends sur une piste
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Gère les sends d'une piste source unique :
        ''' <list type="bullet">
        ''' <item>Liste les pistes disponibles comme destinations.</item>
        ''' <item>Crée un envoi vers une piste de destination.</item>
        ''' <item>Supprime un envoi existant.</item>
        ''' </list>
        ''' La liste des pistes disponibles est construite depuis <see cref="ReaperProject"/>
        ''' et exclut automatiquement la piste source.
        ''' </summary>
        Public Class ReaperTrackSendsEditor
            Implements IDisposable

            Private ReadOnly _trackIndex As Integer
            Private ReadOnly _category As Integer
            Private ReadOnly _sourceTracks As IReadOnlyList(Of ReaperTrack)
            Private ReadOnly _sends As New List(Of ReaperSend)
            Private ReadOnly _availableDestinations As New List(Of ReaperTrack)
            Private disposedValue As Boolean
            Private ReadOnly _trackNames As New Dictionary(Of Integer, String)
            Private _OutputNames As New Dictionary(Of String, Integer)
            Private _AvailableOutputs As New List(Of AudioChannelInfo)
            Private _AudioOutputs As New Dictionary(Of Integer, String)
            Private _Sends_Names As New List(Of String)
            Private _TrackSendNamesInOder As List(Of String)
            Public C24SendsLetters As New List(Of String)({"A", "B", "C", "D", "E", "F", "G", "H", "I", "J"})

            ' ── Index → Nom des pistes disponibles comme destinations ─────────
            ''' <summary>
            ''' Dictionnaire indexé par <c>TrackIndex</c> retournant le nom de chaque
            ''' piste disponible comme destination (la piste source est exclue).
            ''' Mis à jour par <see cref="RefreshAvailableTracks"/>.
            ''' </summary>
            Public ReadOnly Property TrackNames As IReadOnlyDictionary(Of Integer, String)
                Get
                    Return _trackNames
                End Get
            End Property

            Public ReadOnly Property TrackSendNamesInOder As List(Of String)
                Get
                    Return _TrackSendNamesInOder
                End Get
            End Property
            Public ReadOnly Property AudioOutputs As Dictionary(Of Integer, String)
                Get
                    Return _AudioOutputs
                End Get
            End Property
            Public ReadOnly Property AvailableOutputs As List(Of AudioChannelInfo)
                Get
                    Return _AvailableOutputs
                End Get
            End Property
            Public ReadOnly Property OutputNames As Dictionary(Of String, Integer)
                Get
                    Return _OutputNames
                End Get
            End Property


            ' ── Événements ────────────────────────────────────────────────────

            ''' <summary>Déclenché quand un send est ajouté avec succès.</summary>
            Public Event On_SendAdded(sendIndex As Integer, destTrackIndex As Integer)

            ''' <summary>Déclenché quand un send est supprimé avec succès.</summary>
            Public Event On_SendRemoved(sendIndex As Integer)

            ''' <summary>Déclenché quand la liste des sends est rechargée.</summary>
            Public Event On_SendsRefreshed(count As Integer)

            ''' <summary>Déclenché quand la liste des destinations disponibles change.</summary>
            Public Event On_AvailableTracksRefreshed(count As Integer)

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Initialise l'éditeur pour la piste source donnée.
            ''' </summary>
            ''' <param name="trackIndex">Index 0-based de la piste source.</param>
            ''' <param name="allTracks">
            ''' Liste complète des pistes du projet — sert à construire la liste
            ''' des destinations disponibles (la piste source est exclue).
            ''' </param>
            ''' <param name="category"><see cref="SendCategory"/> (défaut : Sends).</param>
            Public Sub New(trackIndex As Integer,
                           allTracks As IReadOnlyList(Of ReaperTrack),
                           Optional category As SendCategory = SendCategory.Sends)
                _trackIndex = trackIndex
                _category = CInt(category)
                _sourceTracks = allTracks
                If category = SendCategory.Sends Then
                    RefreshAvailableTracks()
                End If
                If category = SendCategory.Hardware Then
                    RefreshAvailableOutputs
                End If
                Refresh()
                If category = SendCategory.Sends Then
                    RefreshSendOrder()
                End If


            End Sub

            ' ── Propriétés ────────────────────────────────────────────────────
            ''' <summary>
            ''' Noms des pistes destination des sends actifs, dans le même ordre
            ''' que <see cref="Sends"/>.
            ''' Basé sur <see cref="_trackNames"/> — aucun appel pipe.
            ''' Mis à jour après <see cref="Refresh"/> et <see cref="RefreshAvailableTracks"/>.
            ''' </summary>
            Public ReadOnly Property Sends_Names As IReadOnlyList(Of String)
                Get

                    Return _Sends_Names
                End Get
            End Property
            ''' <summary>Index 0-based de la piste source.</summary>
            Public ReadOnly Property TrackIndex As Integer
                Get
                    Return _trackIndex
                End Get
            End Property

            ''' <summary>Catégorie des envois gérés.</summary>
            Public ReadOnly Property Category As SendCategory
                Get
                    Return CType(_category, SendCategory)
                End Get
            End Property

            ''' <summary>
            ''' Sends actifs sur la piste source.
            ''' Mettre à jour via <see cref="Refresh"/>.
            ''' </summary>
            Public ReadOnly Property Sends As IReadOnlyList(Of ReaperSend)
                Get
                    Return _sends
                End Get
            End Property

            ''' <summary>Nombre de sends actifs sur la piste source.</summary>
            Public ReadOnly Property SendCount As Integer
                Get
                    Return _sends.Count
                End Get
            End Property
            ''' <summary>
            ''' Noms des pistes disponibles comme destinations, dans le même ordre
            ''' que <see cref="AvailableDestinations"/>.
            ''' Mis à jour par <see cref="RefreshAvailableTracks"/>.
            ''' </summary>
            Public ReadOnly Property AvailableDestinations_Names As IReadOnlyList(Of String)
                Get
                    Dim names As New List(Of String)
                    If Category = SendCategory.Sends Then
                        For Each trk In _availableDestinations
                            names.Add(trk.Name)
                        Next
                    ElseIf Category = SendCategory.Hardware Then

                        _AudioOutputs.Clear()
                        _OutputNames.Clear()
                        For Each AudioChInfo In _AvailableOutputs
                            names.Add(AudioChInfo.Name)
                            _OutputNames.TryAdd(AudioChInfo.Name, AudioChInfo.Index)
                            _AudioOutputs.Add(AudioChInfo.Index, AudioChInfo.Name)
                        Next
                    End If

                    Return names
                End Get
            End Property
            ''' <summary>
            ''' Pistes disponibles comme destinations d'envoi
            ''' (toutes les pistes sauf la source).
            ''' Mettre à jour via <see cref="RefreshAvailableTracks"/>.
            ''' </summary>
            Public ReadOnly Property AvailableDestinations As IReadOnlyList(Of ReaperTrack)
                Get
                    Return _availableDestinations
                End Get
            End Property

            ''' <summary>Nombre de destinations disponibles.</summary>
            Public ReadOnly Property AvailableDestinationCount As Integer
                Get
                    Return _availableDestinations.Count
                End Get
            End Property

            ' ── Synchronisation ───────────────────────────────────────────────

            ''' <summary>
            ''' Recharge la liste des sends actifs sur la piste source depuis Reaper.
            ''' Déclenche <see cref="On_SendsRefreshed"/>.
            ''' </summary>
            Public Sub Refresh()
                Dim raw = utility.CallReaperBridge(
                    $"GET_NUM_SENDS|{_trackIndex}|{_category}")
                Dim count As Integer = 0
                If Not String.IsNullOrEmpty(raw) AndAlso Not raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                    Integer.TryParse(raw, count)
                End If

                _sends.Clear()
                _Sends_Names.Clear()

                For i = 0 To count - 1
                    Dim send As New ReaperSend(_trackIndex, _category, i)
                    send.Refresh()
                    Dim name As String = Nothing
                    If Category = SendCategory.Sends Then
                        _trackNames.TryGetValue(send.DestTrackIndex, name)
                    ElseIf Category = SendCategory.Hardware Then
                        _AudioOutputs.TryGetValue(send.DstChan, name)
                    End If


                    If _Sends_Names.Contains(name) Then
                        For j = 0 To 100
                            name &= "_" & j
                            If Sends_Names.Contains(name) = False Then
                                Exit For
                            End If
                        Next
                    End If
                    send.Name = name
                    _Sends_Names.Add(name)
                    _sends.Add(send)
                Next


                RaiseEvent On_SendsRefreshed(_sends.Count)
            End Sub

            Public Sub RefreshSendOrder()


                'init           
                Dim _SendNamesInOder = New List(Of String)
                For i = 0 To 9
                    _SendNamesInOder.Add(Nothing)
                Next


                Dim sendOrder = Reaper.utility.GetProjectExtState("C24SENDORDER", TrackIndex.ToString)

                If sendOrder IsNot Nothing AndAlso sendOrder <> "" AndAlso sendOrder.Contains(",") Then

                    Dim orders = sendOrder.Split(",")
                    For j = 0 To 9
                        If orders(j) <> "" Then

                            _SendNamesInOder(j) = Nothing

                            For Each send In Sends
                                If send.Name = orders(j) Then
                                    _SendNamesInOder(j) = send.Name
                                    Exit For
                                End If
                            Next


                        Else

                            _SendNamesInOder(j) = Nothing


                        End If

                    Next
                    _TrackSendNamesInOder = _SendNamesInOder
                Else
                    'init sendorder
                    For i = 0 To Sends.Count - 1
                        _SendNamesInOder(i) = Sends(i).Name
                    Next
                    _TrackSendNamesInOder = _SendNamesInOder
                    SaveSendOrder(TrackIndex)

                End If

            End Sub
            Public Sub SaveSendOrder(_TrackIndex As Integer)
                Dim order = String.Join(",", _TrackSendNamesInOder)
                Reaper.utility.SetProjectExtState("C24SENDORDER", _TrackIndex, order)
            End Sub
            Public Overloads Sub ResetC24SENDORDER(_TrackIndex As Integer)
                Reaper.utility.SetProjectExtState("C24SENDORDER", _TrackIndex, "")
            End Sub
            Public Overloads Sub ResetC24SENDORDER(SendLetter As String)
                Dim SendLetterIndex = C24SendsLetters.IndexOf(SendLetter)
                _TrackSendNamesInOder(SendLetterIndex) = Nothing
                SaveSendOrder(TrackIndex)
            End Sub
            Public Sub AddUpdateSendOrder(C24SendLetter As String, SendName As String)
                If C24SendsLetters.Contains(C24SendLetter) Then
                    Dim C24SendIndex = C24SendsLetters.IndexOf(C24SendLetter)
                    If SendName Is Nothing Then
                        _TrackSendNamesInOder(C24SendIndex) = Nothing
                    Else
                        _TrackSendNamesInOder(C24SendIndex) = SendName
                    End If

                    SaveSendOrder(_trackIndex)
                End If

            End Sub
            Public Function GetSendName(C24SendLetter As String) As String
                If C24SendsLetters.Contains(C24SendLetter) Then
                    Dim C24SendIndex = C24SendsLetters.IndexOf(C24SendLetter)
                    Return _TrackSendNamesInOder(C24SendIndex)
                End If
                Return Nothing
            End Function
            ''' <summary>
            ''' Recalcule la liste des pistes disponibles comme destinations
            ''' en excluant la piste source et en retirant les + et les - si il y en a.
            ''' Déclenche <see cref="On_AvailableTracksRefreshed"/>.
            ''' </summary>
            Public Sub RefreshAvailableTracks()
                _availableDestinations.Clear()
                _trackNames.Clear()
                For Each trk In _sourceTracks
                    If trk.TrackIndex <> _trackIndex Then
                        _availableDestinations.Add(trk)
                        Dim name = trk.Name
                        If name.StartsWith("+") OrElse name.StartsWith("-") Then
                            name = name.Remove(0, 1)
                        End If
                        _trackNames(trk.TrackIndex) = name
                    End If
                Next
                RaiseEvent On_AvailableTracksRefreshed(_availableDestinations.Count)
            End Sub
            Public Sub RefreshAvailableOutputs()
                _AvailableOutputs.Clear()
                _AvailableOutputs.AddRange(Reaper.utility.GetAllAudioOutputs())
                _AudioOutputs.Clear()
                _OutputNames.Clear()

                For Each AudioChInfo In _AvailableOutputs

                    _AudioOutputs.Add(AudioChInfo.Index, AudioChInfo.Name)
                    _OutputNames.TryAdd(AudioChInfo.Name, AudioChInfo.Index)
                Next
            End Sub

            ' ── Ajout d'un send ───────────────────────────────────────────────

            ''' <summary>
            ''' Crée un envoi depuis la piste source vers la piste destination
            ''' à l'index donné dans <see cref="AvailableDestinations"/>.
            ''' <br/>

            ''' Retourne True si la création a réussi.
            ''' </summary>
            ''' <param name="destinationListIndex">
            ''' Index 0-based dans <see cref="AvailableDestinations"/>.
            ''' </param>
            Public Overloads Function AddSend(destinationListIndex As Integer, SendLetter As String) As Boolean
                If destinationListIndex < 0 OrElse
                   destinationListIndex >= _availableDestinations.Count Then Return False

                Dim destTrack = _availableDestinations(destinationListIndex)
                Dim destIndex = destTrack.TrackIndex



                Dim sendIdx = utility.CreateTrackSend(_trackIndex, destIndex)
                If sendIdx < 0 Then Return False

                Dim send As New ReaperSend(_trackIndex, _category, sendIdx)
                send.Refresh()
                Dim name As String = Nothing
                _trackNames.TryGetValue(send.DestTrackIndex, name)
                If _Sends_Names.Contains(name) Then
                    For i = 0 To 100
                        name &= "_" & i
                        If Sends_Names.Contains(name) = False Then
                            Exit For
                        End If
                    Next
                End If
                send.Name = name
                If sendIdx >= _Sends_Names.Count Then
                    _Sends_Names.Add(name)
                Else
                    _Sends_Names(sendIdx) = name
                End If
                If sendIdx >= _sends.Count Then
                    _sends.Add(send)
                Else
                    _sends(sendIdx) = send
                End If


                AddUpdateSendOrder(SendLetter, send.Name)

                RaiseEvent On_SendAdded(sendIdx, destIndex)
                Return True
            End Function
            Public Overloads Function AddSend(OutputChannel As Integer) As Boolean

                Dim sendIdx = utility.CreateTrackOutput(_trackIndex, OutputChannel)
                If sendIdx < 0 Then Return False
                RefreshAvailableOutputs()

                Dim send As New ReaperSend(_trackIndex, _category, sendIdx)
                send.Refresh()
                Dim name As String = Nothing
                _AudioOutputs.TryGetValue(send.DstChan, name)
                send.Name = name
                _sends(OutputChannel) = send


                Return True
            End Function

            ''' <summary>
            ''' Crée un envoi depuis la piste source vers la piste destination
            ''' identifiée par son index absolu dans le projet.
            ''' Retourne True si la création a réussi.
            ''' </summary>
            ''' <param name="destTrackIndex">Index 0-based absolu de la piste destination.</param>
            Public Function AddSendToTrack(destTrackIndex As Integer, SendLetter As String) As Boolean
                If destTrackIndex = _trackIndex Then Return False
                If SendExistsToTrack(destTrackIndex) Then Return False

                Dim sendIdx = utility.CreateTrackSend(_trackIndex, destTrackIndex)
                If sendIdx < 0 Then Return False

                Dim send As New ReaperSend(_trackIndex, _category, sendIdx)
                send.Refresh()

                Dim name As String = Nothing
                _trackNames.TryGetValue(send.DestTrackIndex, name)
                If _Sends_Names.Contains(name) Then
                    For i = 0 To 100
                        name &= "_" & i
                        If Sends_Names.Contains(name) = False Then
                            Exit For
                        End If
                    Next
                End If


                send.Name = name
                _Sends_Names(sendIdx) = name
                _sends(sendIdx) = send

                AddUpdateSendOrder(SendLetter, send.Name)

                RaiseEvent On_SendAdded(sendIdx, destTrackIndex)
                Return True
            End Function

            ' ── Suppression d'un send ─────────────────────────────────────────

            ''' <summary>
            ''' Supprime le send à l'index donné dans <see cref="Sends"/>.
            ''' Les indices des sends suivants sont décalés et la liste interne
            ''' est reconstruite.
            ''' Retourne True si la suppression a réussi.
            ''' </summary>
            ''' <param name="sendIndex">Index 0-based dans <see cref="Sends"/>.</param>
            Public Overloads Function RemoveSend(sendIndex As Integer, SendLetter As String) As Boolean
                If sendIndex < 0 OrElse sendIndex >= _sends.Count Then Return False

                Dim removeSendName As String = Sends_Names(sendIndex)
                Dim ok = utility.RemoveTrackSend(
                    _trackIndex, CType(_category, SendCategory), sendIndex)
                If Not ok Then Return False

                If _TrackSendNamesInOder.Contains(RemoveSend) Then
                    _TrackSendNamesInOder(RemoveSend) = Nothing
                    SaveSendOrder(_trackIndex)
                End If

                Refresh()

                RaiseEvent On_SendRemoved(sendIndex)
                Return True
            End Function
            Public Overloads Function RemoveSend(OutputIndex As Integer) As Boolean
                If OutputIndex < 0 OrElse OutputIndex >= _sends.Count Then Return False

                Dim removeSendName As String = _AudioOutputs(OutputIndex)
                Dim ok = utility.RemoveTrackSend(
                    _trackIndex, CType(_category, SendCategory), OutputIndex)
                If Not ok Then Return False



                Refresh()

                RaiseEvent On_SendRemoved(OutputIndex)
                Return True
            End Function
            ''' <summary>
            ''' Supprime le send vers la piste destination identifiée par son index
            ''' absolu dans le projet.
            ''' Retourne True si la suppression a réussi.
            ''' </summary>
            ''' <param name="destTrackIndex">Index 0-based absolu de la piste destination.</param>
            Public Function RemoveSendToTrack(destTrackIndex As Integer, SendLetter As String) As Boolean
                Dim sendIndex = FindSendIndexToTrack(destTrackIndex)
                If sendIndex < 0 Then Return False
                Return RemoveSend(sendIndex, SendLetter)
            End Function

            ' ── Lecture d'état ────────────────────────────────────────────────

            ''' <summary>
            ''' Retourne True si un send vers la piste destination existe déjà.
            ''' </summary>
            ''' <param name="destTrackIndex">Index 0-based absolu de la piste destination.</param>
            Public Function SendExistsToTrack(destTrackIndex As Integer) As Boolean
                Return FindSendIndexToTrack(destTrackIndex) >= 0
            End Function

            ''' <summary>
            ''' Retourne l'index 0-based du send vers la piste destination,
            ''' ou -1 si aucun send n'existe vers cette piste.
            ''' </summary>
            ''' <param name="destTrackIndex">Index 0-based absolu de la piste destination.</param>
            Public Function FindSendIndexToTrack(destTrackIndex As Integer) As Integer
                ' I_DSTCHAN ne suffit pas — on lit la piste destination via le bridge
                For i = 0 To _sends.Count - 1
                    Dim raw = utility.CallReaperBridge(
                        $"GET_SEND_INFO|{_trackIndex}|{_category}|{i}|IP_DSTTRKIDX")
                    If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Continue For
                    Dim idx As Integer = -1
                    Integer.TryParse(raw, idx)
                    If idx = destTrackIndex Then Return i
                Next
                Return -1
            End Function

            ''' <summary>
            ''' Retourne le send à l'index donné, ou Nothing si hors limite.
            ''' </summary>
            Public Function GetSend(SendLetter As String) As ReaperSend
                Dim SendName = GetSendName(SendLetter)
                For Each send In Sends
                    If send.Name = SendName Then
                        Return send
                    End If
                Next
                Return Nothing
            End Function
            ''' <summary>
            ''' Recherche la première piste disponible comme destination dont le nom
            ''' correspond à <paramref name="trackName"/> et retourne son index
            ''' 0-based dans <see cref="AvailableDestinations"/>.
            ''' La comparaison est insensible à la casse.
            ''' Retourne -1 si aucune piste ne correspond.
            ''' </summary>
            ''' <param name="trackName">Nom de la piste destination à rechercher.</param>
            Public Function AvailableDestinationsIndexByName(trackName As String) As Integer
                For i = 0 To _availableDestinations.Count - 1
                    If _availableDestinations(i).Name.Equals(trackName, StringComparison.OrdinalIgnoreCase) Then
                        Return i
                    End If
                Next
                Return -1
            End Function
            ''' <summary>
            ''' Recherche le premier send dont la piste destination correspond à
            ''' <paramref name="trackName"/> et retourne son index 0-based dans
            ''' <see cref="Sends"/>.
            ''' La comparaison est insensible à la casse.
            ''' Utilise <see cref="_trackNames"/> — aucun appel pipe.
            ''' Retourne -1 si aucun send ne correspond.
            ''' </summary>
            ''' <param name="trackName">Nom de la piste destination à rechercher.</param>
            Public Function SendsIndexByName(trackName As String) As Integer
                For i = 0 To _sends.Count - 1
                    Dim name As String = String.Empty
                    _trackNames.TryGetValue(_sends(i).DestTrackIndex, name)
                    If name.Equals(trackName, StringComparison.OrdinalIgnoreCase) Then
                        Return i
                    End If
                Next
                Return -1
            End Function
            ''' <summary>
            ''' Renvois la liste complète des nom de sends possible en sélection/ajout/supression
            ''' </summary>
            ''' <returns></returns>
            Public Function GetVpotSendsList(Optional NullItem As String = " -  ") As List(Of String)
                Dim SendListNames As New List(Of String)
                If NullItem IsNot Nothing Then
                    SendListNames.Add(NullItem)
                End If
                For Each send In Sends
                    SendListNames.Add(send.Name)
                Next
                For Each send In Sends
                    SendListNames.Add("-" & send.Name)
                Next
                If Category = SendCategory.Sends Then
                    For Each DestName In AvailableDestinations_Names
                        SendListNames.Add("+" & DestName)
                    Next
                    Return SendListNames

                ElseIf Category = SendCategory.Hardware Then
                    For Each OutputName In AudioOutputs.Values
                        SendListNames.Add("+" & OutputName)
                    Next
                    Return SendListNames

                End If
                Return Nothing
            End Function
            ' ── IDisposable ───────────────────────────────────────────────────

            Protected Overridable Sub Dispose(disposing As Boolean)
                If Not disposedValue Then
                    disposedValue = True
                End If
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                Dispose(disposing:=True)
                GC.SuppressFinalize(Me)
            End Sub

        End Class

        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER TRACK FX EDITOR — édition directe des FX d'une piste unique
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Permet l'édition des FX d'une seule piste (ajout, suppression, bypass).
        ''' Utilisé depuis la vue Insert de la surface : pas de pagination,
        ''' opère directement sur la piste <paramref name="trackIndex"/>.
        ''' <br/>
        ''' La liste des FX est rechargée depuis Reaper à la construction
        ''' et après chaque modification structurelle.
        ''' </summary>
        Public Class ReaperTrackFxEditor

            Private ReadOnly _trackIndex As Integer
            Private ReadOnly _sessionConfig As ReaperSessionConfig
            Private _fxList As ReaperTrackFX

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Crée un éditeur de FX pour la piste <paramref name="trackIndex"/>.
            ''' Charge immédiatement la liste des FX depuis Reaper.
            ''' </summary>
            ''' <param name="trackIndex">Index 0-based de la piste cible.</param>
            ''' <param name="sessionConfig">
            ''' Config session utilisée pour la classification EQ / DYN / Generic.
            ''' Peut être Nothing si la classification n'est pas requise.
            ''' </param>
            Public Sub New(trackIndex As Integer,
                           Optional sessionConfig As ReaperSessionConfig = Nothing)
                _trackIndex = trackIndex
                _sessionConfig = sessionConfig
                _fxList = New ReaperTrackFX(trackIndex)
                _fxList.Refresh()
            End Sub

            ' ── Propriétés ────────────────────────────────────────────────────

            ''' <summary>Index 0-based de la piste éditée.</summary>
            Public ReadOnly Property TrackIndex As Integer
                Get
                    Return _trackIndex
                End Get
            End Property

            ''' <summary>Nombre de FX sur la piste.</summary>
            Public ReadOnly Property Count As Integer
                Get
                    Return _fxList.Count
                End Get
            End Property

            ''' <summary>
            ''' Noms des FX de la piste dans l'ordre de la chaîne.
            ''' </summary>
            Public ReadOnly Property FxNames As IReadOnlyList(Of String)
                Get
                    Dim result(Count - 1) As String
                    For i = 0 To Count - 1
                        result(i) = _fxList(i).Name
                    Next
                    Return result
                End Get
            End Property

            ''' <summary>
            ''' Retourne la liste complète des FX avec nom, état bypass et type.
            ''' </summary>
            Public Function GetFxList() As List(Of TrackFxInfo)
                Dim result As New List(Of TrackFxInfo)
                For i = 0 To Count - 1
                    Dim reapFx = _fxList(i)
                    result.Add(New TrackFxInfo(i, reapFx.Name, reapFx.Enabled, ClassifyFx(reapFx.Name)))
                Next
                Return result
            End Function

            ''' <summary>
            ''' Retourne les informations d'un FX par index.
            ''' Retourne un <see cref="TrackFxInfo"/> invalide si hors limite.
            ''' </summary>
            ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
            Public Function GetFxInfo(fxIndex As Integer) As TrackFxInfo
                If fxIndex < 0 OrElse fxIndex >= Count Then
                    Return New TrackFxInfo(-1, String.Empty, False, TrackFxType.Generic)
                End If
                Dim reapFx = _fxList(fxIndex)
                Return New TrackFxInfo(fxIndex, reapFx.Name, reapFx.Enabled, ClassifyFx(reapFx.Name))
            End Function

            ''' <summary>
            ''' Retourne True si le FX est actif (non bypassé).
            ''' Retourne False si l'index est hors limite.
            ''' </summary>
            ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
            Public Function GetFxEnabled(fxIndex As Integer) As Boolean
                If fxIndex < 0 OrElse fxIndex >= Count Then Return False
                Return _fxList(fxIndex).Enabled
            End Function

            ''' <summary>
            ''' Retourne le type du FX selon la classification EQ / DYN / Generic.
            ''' Retourne <see cref="TrackFxType.Generic"/> si hors limite ou si
            ''' <c>sessionConfig</c> est Nothing.
            ''' </summary>
            ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
            Public Function GetFxType(fxIndex As Integer) As TrackFxType
                If fxIndex < 0 OrElse fxIndex >= Count Then Return TrackFxType.Generic
                Return ClassifyFx(_fxList(fxIndex).Name)
            End Function

            ' ── Bypass ────────────────────────────────────────────────────────

            ''' <summary>
            ''' Impose l'état bypass du FX à l'index donné.
            ''' Sans effet si l'index est hors limite.
            ''' </summary>
            ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
            ''' <param name="enabled">True = actif  False = bypassé.</param>
            Public Sub SetFxEnabled(fxIndex As Integer, enabled As Boolean)
                If fxIndex < 0 OrElse fxIndex >= Count Then Return
                _fxList(fxIndex).Enabled = enabled
            End Sub

            ''' <summary>
            ''' Bascule l'état bypass du FX à l'index donné (toggle actif/bypassé).
            ''' Sans effet si l'index est hors limite.
            ''' Retourne le nouvel état (True = actif).
            ''' </summary>
            ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
            Public Function ToggleFxEnabled(fxIndex As Integer) As Boolean
                If fxIndex < 0 OrElse fxIndex >= Count Then Return False
                Dim newState = Not _fxList(fxIndex).Enabled
                _fxList(fxIndex).Enabled = newState
                Return newState
            End Function

            ' ── Ajout ─────────────────────────────────────────────────────────

            ''' <summary>
            ''' Ajoute un FX à la piste par son nom complet et recharge la liste.
            ''' Utiliser <see cref="InstalledFXInfo.FullName"/> comme valeur.
            ''' Retourne l'index 0-based du FX ajouté, ou -1 en cas d'échec.
            ''' </summary>
            ''' <param name="fxFullName">
            ''' Nom complet du FX ex: "VST3: FabFilter Pro-Q 3 (FabFilter)".
            ''' </param>
            Public Function AddFx(fxFullName As String) As Integer
                Dim fxIdx = utility.AddFXByName(_trackIndex, fxFullName)
                If fxIdx >= 0 Then
                    _fxList = New ReaperTrackFX(_trackIndex)
                    _fxList.Refresh()
                End If
                Return fxIdx
            End Function

            ' ── Suppression ───────────────────────────────────────────────────

            ''' <summary>
            ''' Supprime le FX à l'index donné et recharge la liste.
            ''' Retourne True si la suppression a réussi.
            ''' </summary>
            ''' <param name="fxIndex">Index 0-based du FX à supprimer.</param>
            Public Function DeleteFx(fxIndex As Integer) As Boolean
                If fxIndex < 0 OrElse fxIndex >= Count Then Return False
                Dim ok = _fxList.DeleteFX(fxIndex)
                If ok Then
                    _fxList = New ReaperTrackFX(_trackIndex)
                    _fxList.Refresh()
                End If
                Return ok
            End Function

            ' ── Rechargement ──────────────────────────────────────────────────

            ''' <summary>
            ''' Recharge la liste des FX depuis Reaper.
            ''' Appeler après une modification externe (ex : ajout via l'UI Reaper).
            ''' </summary>
            Public Sub Refresh()
                _fxList = New ReaperTrackFX(_trackIndex)
                _fxList.Refresh()
            End Sub

            ' ── Classification ────────────────────────────────────────────────

            ''' <summary>
            ''' Classifie un FX selon la <see cref="ReaperSessionConfig"/> :
            ''' <see cref="TrackFxType.EQ"/>, <see cref="TrackFxType.Dyn"/>
            ''' ou <see cref="TrackFxType.Generic"/>.
            ''' Retourne <see cref="TrackFxType.Generic"/> si <c>sessionConfig</c> est Nothing.
            ''' </summary>
            ''' <param name="fxName">Nom complet du FX.</param>
            Public Function ClassifyFx(fxName As String) As TrackFxType
                If _sessionConfig Is Nothing Then Return TrackFxType.Generic
                If _sessionConfig.IsEQPlugin(fxName) Then Return TrackFxType.EQ
                If _sessionConfig.IsDynPlugin(fxName) Then Return TrackFxType.Dyn
                Return TrackFxType.Generic
            End Function

            ''' <summary>Retourne tous les FX de type EQ de la piste.</summary>
            Public Function GetEqFxList() As List(Of TrackFxInfo)
                Dim result As New List(Of TrackFxInfo)
                For Each info In GetFxList()
                    If info.FxType = TrackFxType.EQ Then result.Add(info)
                Next
                Return result
            End Function

            ''' <summary>Retourne tous les FX de type DYN de la piste.</summary>
            Public Function GetDynFxList() As List(Of TrackFxInfo)
                Dim result As New List(Of TrackFxInfo)
                For Each info In GetFxList()
                    If info.FxType = TrackFxType.Dyn Then result.Add(info)
                Next
                Return result
            End Function

        End Class
    End Namespace

#End Region
#Region "Other Managers"

    Namespace Managers

        ''' <summary>
        ''' Référence stable vers un item Reaper : index de piste + index de l'item sur cette piste.
        ''' </summary>
        Public Structure ReaperItemRef
            Implements IEquatable(Of ReaperItemRef)

            ''' <summary>Index 0-based de la piste.</summary>
            Public ReadOnly Property TrackIndex As Integer

            ''' <summary>Index 0-based de l'item sur la piste.</summary>
            Public ReadOnly Property ItemIndex As Integer

            ''' <summary>Crée une référence d'item.</summary>
            ''' <param name="trackIndex">Index 0-based de la piste.</param>
            ''' <param name="itemIndex">Index 0-based de l'item sur la piste.</param>
            Public Sub New(trackIndex As Integer, itemIndex As Integer)
                Me.TrackIndex = trackIndex
                Me.ItemIndex = itemIndex
            End Sub

            ''' <summary>Format pipe : "trackIndex:itemIndex".</summary>
            Public Overrides Function ToString() As String
                Return $"{TrackIndex.ToString(CultureInfo.InvariantCulture)}:{ItemIndex.ToString(CultureInfo.InvariantCulture)}"
            End Function

            ''' <summary>Égalité par valeur.</summary>
            Public Overloads Function Equals(other As ReaperItemRef) As Boolean Implements IEquatable(Of ReaperItemRef).Equals
                Return TrackIndex = other.TrackIndex AndAlso ItemIndex = other.ItemIndex
            End Function

            ''' <summary>Égalité par valeur.</summary>
            Public Overrides Function Equals(obj As Object) As Boolean
                If TypeOf obj Is ReaperItemRef Then
                    Return Equals(DirectCast(obj, ReaperItemRef))
                End If

                Return False
            End Function

            ''' <summary>Code de hachage basé sur les deux index.</summary>
            Public Overrides Function GetHashCode() As Integer
                Return HashCode.Combine(TrackIndex, ItemIndex)
            End Function
        End Structure

        ''' <summary>
        ''' Lit et modifie la liste des items sélectionnés, par projet Reaper.
        ''' projectIndex = -1 désigne le projet actif.
        ''' Les lectures passent par GET_SELECTED_MEDIA_ITEMS, les écritures par
        ''' SET_MEDIA_ITEMS_SELECTED (exécutées sur le thread principal de Reaper).
        ''' </summary>
        Public Class SelectedItemsManager

            ''' <summary>Index de projet désignant le projet actif.</summary>
            Public Const ActiveProject As Integer = -1

            Private ReadOnly _cache As New Dictionary(Of Integer, List(Of ReaperItemRef))
            Private ReadOnly _lock As New Object()

            ''' <summary>Levé quand la sélection d'un projet change après un Refresh ou une écriture.</summary>
            Public Event On_SelectionChange(projectIndex As Integer, items As IReadOnlyList(Of ReaperItemRef))

            ''' <summary>
            ''' Lit la liste des items sélectionnés d'un projet directement dans Reaper.
            ''' Retourne une liste vide si le bridge est indisponible.
            ''' </summary>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function GetSelectedItems(Optional projectIndex As Integer = ActiveProject) As IReadOnlyList(Of ReaperItemRef)
                Dim raw As String = CallReaperBridge($"GET_SELECTED_MEDIA_ITEMS|{projectIndex.ToString(CultureInfo.InvariantCulture)}")
                Dim result As New List(Of ReaperItemRef)

                If String.IsNullOrEmpty(raw) Then
                    Return result
                End If

                If raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                    Return result
                End If

                Dim p As String() = raw.Split("|"c)
                For k As Integer = 1 To p.Length - 1
                    Dim sep As Integer = p(k).IndexOf(":"c)
                    If sep <= 0 Then
                        Continue For
                    End If

                    Dim t As Integer = 0
                    Dim i As Integer = 0
                    If Integer.TryParse(p(k).Substring(0, sep), NumberStyles.Integer, CultureInfo.InvariantCulture, t) = False Then
                        Continue For
                    End If

                    If Integer.TryParse(p(k).Substring(sep + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, i) = False Then
                        Continue For
                    End If

                    result.Add(New ReaperItemRef(t, i))
                Next

                Return result
            End Function

            ''' <summary>Nombre d'items sélectionnés dans un projet.</summary>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function CountSelectedItems(Optional projectIndex As Integer = ActiveProject) As Integer
                Return GetSelectedItems(projectIndex).Count
            End Function

            ''' <summary>Indique si un item donné est sélectionné.</summary>
            ''' <param name="item">Référence de l'item.</param>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function IsSelected(item As ReaperItemRef, Optional projectIndex As Integer = ActiveProject) As Boolean
                Return GetSelectedItems(projectIndex).Contains(item)
            End Function

            ''' <summary>Ajoute des items à la sélection existante.</summary>
            ''' <param name="items">Items à sélectionner.</param>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function SelectItems(items As IEnumerable(Of ReaperItemRef), Optional projectIndex As Integer = ActiveProject) As Boolean
                Return Apply(projectIndex, False, True, items)
            End Function

            ''' <summary>Retire des items de la sélection existante.</summary>
            ''' <param name="items">Items à désélectionner.</param>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function DeselectItems(items As IEnumerable(Of ReaperItemRef), Optional projectIndex As Integer = ActiveProject) As Boolean
                Return Apply(projectIndex, False, False, items)
            End Function

            ''' <summary>Remplace entièrement la sélection du projet par la liste donnée.</summary>
            ''' <param name="items">Nouvelle sélection.</param>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function SetSelectedItems(items As IEnumerable(Of ReaperItemRef), Optional projectIndex As Integer = ActiveProject) As Boolean
                Return Apply(projectIndex, True, True, items)
            End Function

            ''' <summary>Désélectionne tous les items du projet.</summary>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function ClearSelection(Optional projectIndex As Integer = ActiveProject) As Boolean
                Return Apply(projectIndex, True, True, Array.Empty(Of ReaperItemRef)())
            End Function

            ''' <summary>
            ''' Relit la sélection depuis Reaper et lève <see cref="On_SelectionChange"/> si elle a changé
            ''' par rapport au dernier état connu de ce projet.
            ''' </summary>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Sub Refresh(Optional projectIndex As Integer = ActiveProject)
                Dim new_items As List(Of ReaperItemRef) = New List(Of ReaperItemRef)(GetSelectedItems(projectIndex))
                Dim changed As Boolean = False

                SyncLock _lock
                    Dim old_items As List(Of ReaperItemRef) = Nothing
                    If _cache.TryGetValue(projectIndex, old_items) = False Then
                        changed = True
                    ElseIf old_items.SequenceEqual(new_items) = False Then
                        changed = True
                    End If

                    If changed Then
                        _cache(projectIndex) = new_items
                    End If
                End SyncLock

                If changed Then
                    RaiseEvent On_SelectionChange(projectIndex, new_items)
                End If
            End Sub

            ''' <summary>Dernier état connu de la sélection d'un projet (sans appel pipe).</summary>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function GetCachedSelection(Optional projectIndex As Integer = ActiveProject) As IReadOnlyList(Of ReaperItemRef)
                SyncLock _lock
                    Dim cached As List(Of ReaperItemRef) = Nothing
                    If _cache.TryGetValue(projectIndex, cached) Then
                        Return New List(Of ReaperItemRef)(cached)
                    End If
                End SyncLock

                Return Array.Empty(Of ReaperItemRef)()
            End Function



            ' ── Paramètres des items ─────────────────────────────────────────────

            ''' <summary>
            ''' Lit un paramètre numérique pour une liste d'items.
            ''' Paramètres courants : "D_POSITION", "D_LENGTH", "D_VOL", "D_FADEINLEN",
            ''' "D_FADEOUTLEN", "D_SNAPOFFSET", "B_MUTE", "B_LOOPSRC", "C_LOCK",
            ''' "I_CUSTOMCOLOR", "I_GROUPID".
            ''' Une valeur <see cref="Double.NaN"/> est retournée pour un item introuvable
            ''' ou si le bridge est indisponible.
            ''' </summary>
            ''' <param name="items">Items à lire.</param>
            ''' <param name="param">Nom du paramètre Reaper.</param>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function GetItemsInfoD(items As IEnumerable(Of ReaperItemRef),
                                      param As String,
                                      Optional projectIndex As Integer = ActiveProject) As IReadOnlyDictionary(Of ReaperItemRef, Double)
                Dim result As New Dictionary(Of ReaperItemRef, Double)
                Dim list As List(Of ReaperItemRef) = New List(Of ReaperItemRef)(items)
                If list.Count = 0 Then
                    Return result
                End If

                Dim tokens As New List(Of String)
                For Each it In list
                    tokens.Add(it.ToString())
                Next

                Dim raw As String = CallReaperBridge(
                $"GET_MEDIA_ITEMS_INFO|{projectIndex.ToString(CultureInfo.InvariantCulture)}|{param}|{String.Join("|", tokens)}")

                Dim values As String() = Array.Empty(Of String)()
                If String.IsNullOrEmpty(raw) = False Then
                    If raw.StartsWith("ERROR", StringComparison.Ordinal) = False Then
                        values = raw.Split("|"c)
                    End If
                End If

                For k As Integer = 0 To list.Count - 1
                    Dim v As Double = Double.NaN
                    If k < values.Length Then
                        If Double.TryParse(values(k), NumberStyles.Any, CultureInfo.InvariantCulture, v) = False Then
                            v = Double.NaN
                        End If
                    End If

                    result(list(k)) = v
                Next

                Return result
            End Function

            ''' <summary>Lit un paramètre numérique pour tous les items sélectionnés du projet.</summary>
            ''' <param name="param">Nom du paramètre Reaper.</param>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function GetSelectedItemsInfoD(param As String,
                                              Optional projectIndex As Integer = ActiveProject) As IReadOnlyDictionary(Of ReaperItemRef, Double)
                Return GetItemsInfoD(GetSelectedItems(projectIndex), param, projectIndex)
            End Function

            ''' <summary>Lit un paramètre numérique d'un seul item (NaN si indisponible).</summary>
            ''' <param name="item">Item à lire.</param>
            ''' <param name="param">Nom du paramètre Reaper.</param>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function GetItemInfoD(item As ReaperItemRef,
                                     param As String,
                                     Optional projectIndex As Integer = ActiveProject) As Double
                Dim d As IReadOnlyDictionary(Of ReaperItemRef, Double) = GetItemsInfoD({item}, param, projectIndex)
                Dim v As Double = Double.NaN
                If d.TryGetValue(item, v) Then
                    Return v
                End If

                Return Double.NaN
            End Function

            ''' <summary>Écrit la même valeur numérique sur une liste d'items.</summary>
            ''' <param name="items">Items à modifier.</param>
            ''' <param name="param">Nom du paramètre Reaper.</param>
            ''' <param name="value">Valeur à écrire.</param>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            ''' <returns>True si la commande a été acceptée (application différée sur le thread Reaper).</returns>
            Public Function SetItemsInfoD(items As IEnumerable(Of ReaperItemRef),
                                      param As String,
                                      value As Double,
                                      Optional projectIndex As Integer = ActiveProject) As Boolean
                Dim tokens As New List(Of String)
                For Each it In items
                    tokens.Add(it.ToString())
                Next

                If tokens.Count = 0 Then
                    Return True
                End If

                Dim raw As String = CallReaperBridge(
                $"SET_MEDIA_ITEMS_INFO|{projectIndex.ToString(CultureInfo.InvariantCulture)}|{param}|{value.ToString("G", CultureInfo.InvariantCulture)}|{String.Join("|", tokens)}")

                If raw = "OK" Then
                    Return True
                End If

                Return False
            End Function

            ''' <summary>Écrit la même valeur numérique sur tous les items sélectionnés du projet.</summary>
            ''' <param name="param">Nom du paramètre Reaper.</param>
            ''' <param name="value">Valeur à écrire.</param>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function SetSelectedItemsInfoD(param As String,
                                              value As Double,
                                              Optional projectIndex As Integer = ActiveProject) As Boolean
                Return SetItemsInfoD(GetSelectedItems(projectIndex), param, value, projectIndex)
            End Function

            ''' <summary>Écrit un paramètre <see cref="Boolean"/> (B_MUTE, B_LOOPSRC…) sur une liste d'items.</summary>
            ''' <param name="items">Items à modifier.</param>
            ''' <param name="param">Nom du paramètre Reaper.</param>
            ''' <param name="value">Valeur booléenne.</param>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function SetItemsInfoB(items As IEnumerable(Of ReaperItemRef),
                                      param As String,
                                      value As Boolean,
                                      Optional projectIndex As Integer = ActiveProject) As Boolean
                Dim d As Double = 0.0
                If value Then
                    d = 1.0
                End If

                Return SetItemsInfoD(items, param, d, projectIndex)
            End Function

            ''' <summary>Écrit un paramètre <see cref="Integer"/> (I_CUSTOMCOLOR, I_GROUPID…) sur une liste d'items.</summary>
            ''' <param name="items">Items à modifier.</param>
            ''' <param name="param">Nom du paramètre Reaper.</param>
            ''' <param name="value">Valeur entière.</param>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function SetItemsInfoI(items As IEnumerable(Of ReaperItemRef),
                                      param As String,
                                      value As Integer,
                                      Optional projectIndex As Integer = ActiveProject) As Boolean
                Return SetItemsInfoD(items, param, CDbl(value), projectIndex)
            End Function

            ''' <summary>
            ''' Écrit un paramètre de type char (préfixe C_) sur une liste d'items.
            ''' Paramètres courants :
            ''' "C_LOCK" (&amp;1 = verrouillé),
            ''' "C_BEATATTACHMODE" (-1 = défaut, 0 = temps, 1 = position/longueur/offset en beats, 2 = position seule en beats),
            ''' "C_AUTOSTRETCH" (1 = étirement automatique en mode beats).
            ''' </summary>
            ''' <param name="items">Items à modifier.</param>
            ''' <param name="param">Nom du paramètre Reaper (préfixe C_).</param>
            ''' <param name="value">Valeur char signée (-128 à 127).</param>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            ''' <returns>True si la commande a été acceptée (application différée sur le thread Reaper).</returns>
            Public Function SetItemsInfoC(items As IEnumerable(Of ReaperItemRef),
                                      param As String,
                                      value As SByte,
                                      Optional projectIndex As Integer = ActiveProject) As Boolean
                Return SetItemsInfoD(items, param, CDbl(value), projectIndex)
            End Function

            ''' <summary>Écrit un paramètre char (préfixe C_) sur tous les items sélectionnés du projet.</summary>
            ''' <param name="param">Nom du paramètre Reaper (préfixe C_).</param>
            ''' <param name="value">Valeur char signée (-128 à 127).</param>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function SetSelectedItemsInfoC(param As String,
                                              value As SByte,
                                              Optional projectIndex As Integer = ActiveProject) As Boolean
                Return SetItemsInfoC(GetSelectedItems(projectIndex), param, value, projectIndex)
            End Function

            ''' <summary>
            ''' Lit un paramètre de type char (préfixe C_) pour une liste d'items.
            ''' Une valeur <see cref="Double.NaN"/> est retournée pour un item introuvable.
            ''' </summary>
            ''' <param name="items">Items à lire.</param>
            ''' <param name="param">Nom du paramètre Reaper (préfixe C_).</param>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function GetItemsInfoC(items As IEnumerable(Of ReaperItemRef),
                                      param As String,
                                      Optional projectIndex As Integer = ActiveProject) As IReadOnlyDictionary(Of ReaperItemRef, Double)
                Return GetItemsInfoD(items, param, projectIndex)
            End Function
            ''' <summary>
            ''' Lit un paramètre de type char (préfixe C_) pour tous les items sélectionnés du projet.
            ''' Paramètres courants : "C_LOCK", "C_BEATATTACHMODE", "C_AUTOSTRETCH".
            ''' Une valeur <see cref="Double.NaN"/> est retournée pour un item introuvable
            ''' ou si le bridge est indisponible.
            ''' </summary>
            ''' <param name="param">Nom du paramètre Reaper (préfixe C_).</param>
            ''' <param name="projectIndex">Index du projet, ou -1 pour le projet actif.</param>
            Public Function GetSelectedItemsInfoC(param As String,
                                              Optional projectIndex As Integer = ActiveProject) As IReadOnlyDictionary(Of ReaperItemRef, Double)
                Return GetItemsInfoC(GetSelectedItems(projectIndex), param, projectIndex)
            End Function
            Private Function Apply(projectIndex As Integer,
                               clearOthers As Boolean,
                               selected As Boolean,
                               items As IEnumerable(Of ReaperItemRef)) As Boolean
                Dim tokens As New List(Of String)
                For Each it In items
                    tokens.Add(it.ToString())
                Next

                Dim head As String = String.Join("|",
                "SET_MEDIA_ITEMS_SELECTED",
                projectIndex.ToString(CultureInfo.InvariantCulture),
                If(clearOthers, "1", "0"),
                If(selected, "1", "0"))

                Dim request As String = head
                If tokens.Count > 0 Then
                    request = head & "|" & String.Join("|", tokens)
                End If

                Dim raw As String = CallReaperBridge(request)
                If raw <> "OK" Then
                    Return False
                End If

                Return True
            End Function

        End Class

        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER TRANSPORT MANAGER — pilotage complet du transport Reaper
        ' ══════════════════════════════════════════════════════════════════════════


        Public Class ReaperTransportManager
            Implements IDisposable

            Private _playState As Integer = 0
            Private _playPos As Double = 0
            Private _started As Boolean = False
            Private _suspended As Boolean = False
            Private _disposed As Boolean = False

            Public Event On_PlayStateChange(playState As Integer)
            Public Event On_PositionChange(pos As Double)

            Public Sub New()
            End Sub

            Public ReadOnly Property PlayState As Integer
                Get
                    Return _playState
                End Get
            End Property

            Public ReadOnly Property PlayPosition As Double
                Get
                    Return _playPos
                End Get
            End Property

            Public ReadOnly Property IsPlaying As Boolean
                Get
                    If _playState = 1 Then
                        Return True
                    Else
                        Return False
                    End If
                End Get
            End Property

            Public ReadOnly Property IsPaused As Boolean
                Get
                    If _playState = 2 OrElse _playState = 6 Then
                        Return True
                    Else
                        Return False
                    End If
                End Get
            End Property

            Public ReadOnly Property IsRecording As Boolean
                Get
                    If _playState = 5 OrElse _playState = 6 Then
                        Return True
                    Else
                        Return False
                    End If

                End Get
            End Property

            Public Sub Start()
                If _started Then
                    Return
                End If
                _started = True
                AddHandler ReaperPollHub.Instance.On_PlayStateChange, AddressOf OnPlayStateChange
                AddHandler ReaperPollHub.Instance.On_PlayPositionChange, AddressOf OnPlayPositionChange
            End Sub

            Public Sub [Stop]()
                If Not _started Then
                    Return
                End If
                _started = False
                RemoveHandler ReaperPollHub.Instance.On_PlayStateChange, AddressOf OnPlayStateChange
                RemoveHandler ReaperPollHub.Instance.On_PlayPositionChange, AddressOf OnPlayPositionChange
            End Sub

            Public Sub SuspendMonitor()
                _suspended = True
            End Sub

            Public Sub ResumeMonitor()
                _suspended = False
            End Sub

            Private Sub OnPlayStateChange(playState As Integer)
                _playState = playState
                If Not _suspended Then
                    RaiseEvent On_PlayStateChange(_playState)
                End If
            End Sub

            Private Sub OnPlayPositionChange(playPos As Double)
                _playPos = playPos
                If Not _suspended Then
                    RaiseEvent On_PositionChange(_playPos)
                End If
            End Sub

            Protected Overridable Sub Dispose(disposing As Boolean)
                If Not _disposed Then
                    If disposing Then
                        [Stop]()
                    End If
                    _disposed = True
                End If
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                Dispose(disposing:=True)
                GC.SuppressFinalize(Me)
            End Sub

        End Class
        ' ══════════════════════════════════════════════════════════════════════════
        '  JOG WHEEL MODE — modes de la jogwheel
        ' ══════════════════════════════════════════════════════════════════════════

        '''' <summary>Modes de fonctionnement de la jogwheel de la surface de contrôle.</summary>
        'Public Enum JogWheelMode
        '    ''' <summary>Déplace le curseur d'édition avant/arrière sur la timeline.</summary>
        '    Cursor = 0
        '    ''' <summary>Déplace le point In de la sélection temporelle.</summary>
        '    SelectionIn = 1
        '    ''' <summary>Déplace le point Out de la sélection temporelle.</summary>
        '    SelectionOut = 2
        '    ''' <summary>Zoom horizontal de la timeline (in/out).</summary>
        '    ZoomHorizontal = 3
        '    ''' <summary>Zoom vertical de la piste sélectionnée (in/out).</summary>
        '    ZoomVertical = 4
        'End Enum



        '' ══════════════════════════════════════════════════════════════════════════
        ''  REAPER JOG WHEEL MANAGER — interface jogwheel multi-modes
        '' ══════════════════════════════════════════════════════════════════════════

        '''' <summary>
        '''' Interface entre la jogwheel d'une surface de contrôle et Reaper.
        '''' Gère plusieurs modes :
        '''' <list type="table">
        '''' <item><term><see cref="JogWheelMode.Cursor"/></term>
        ''''       <description>Déplace le curseur d'édition avant/arrière.</description></item>
        '''' <item><term><see cref="JogWheelMode.SelectionIn"/></term>
        ''''       <description>Déplace le point In de la sélection temporelle.</description></item>
        '''' <item><term><see cref="JogWheelMode.SelectionOut"/></term>
        ''''       <description>Déplace le point Out de la sélection temporelle.</description></item>
        '''' <item><term><see cref="JogWheelMode.ZoomHorizontal"/></term>
        ''''       <description>Zoom horizontal de la timeline (vue arrangement).</description></item>
        '''' <item><term><see cref="JogWheelMode.ZoomVertical"/></term>
        ''''       <description>Zoom vertical de la piste sélectionnée.</description></item>
        '''' </list>
        '''' Appeler <see cref="Jog"/> à chaque tick détecté de la jogwheel.
        '''' Un tick positif correspond à une rotation dans le sens horaire
        '''' (forward / zoom in / point +) ; un tick négatif à une rotation
        '''' anti-horaire (backward / zoom out / point -).
        '''' <br/>
        '''' Fournir un <see cref="ReaperTransportManager"/> via le constructeur ou
        '''' <see cref="TransportManager"/> pour lire <c>IsPlaying</c> et la sélection
        '''' temporelle depuis le cache (évite une requête pipe).
        '''' </summary>
        'Public Class ReaperJogWheelManager

        '    Private Shared ReadOnly Ic As CultureInfo = CultureInfo.InvariantCulture

        '    ' ── Mode courant ──────────────────────────────────────────────────
        '    Private _mode As JogWheelMode = JogWheelMode.Cursor

        '    ' ── Référence optionnelle au transport manager ────────────────────
        '    Private _transportManager As ReaperTransportManager

        '    ' ── Pas de déplacement par tick ───────────────────────────────────
        '    Private _cursorStepSeconds As Double = 0.1
        '    Private _selectionStepSeconds As Double = 0.1

        '    ' ── Mode fin (Ctrl/Shift sur la surface) ──────────────────────────
        '    Private _fineMode As Boolean = False
        '    Private _fineDivider As Double = 10.0

        '    ' ── Événements ────────────────────────────────────────────────────

        '    ''' <summary>Déclenché quand le mode change.</summary>
        '    Public Event On_ModeChanged(mode As JogWheelMode)

        '    ''' <summary>
        '    ''' Déclenché après chaque déplacement du curseur d'édition.
        '    ''' <paramref name="newPosition"/> = nouvelle position en secondes.
        '    ''' </summary>
        '    Public Event On_CursorMoved(newPosition As Double)

        '    ''' <summary>
        '    ''' Déclenché après chaque modification de la sélection temporelle.
        '    ''' </summary>
        '    Public Event On_SelectionChanged(selIn As Double, selOut As Double)

        '    ''' <summary>
        '    ''' Déclenché après chaque action de zoom.
        '    ''' <paramref name="direction"/> : +1 = zoom in   -1 = zoom out.
        '    ''' </summary>
        '    Public Event On_Zoomed(mode As JogWheelMode, direction As Integer)

        '    ' ── Constructeur ──────────────────────────────────────────────────

        '    ''' <summary>
        '    ''' Initialise le manager.
        '    ''' </summary>
        '    ''' <param name="transportManager">
        '    ''' Optionnel — permet de lire <c>IsPlaying</c>, <c>TimeSelIn</c> et
        '    ''' <c>TimeSelOut</c> depuis le cache. Peut être défini plus tard via
        '    ''' <see cref="TransportManager"/>.
        '    ''' </param>
        '    Public Sub New(Optional transportManager As ReaperTransportManager = Nothing)
        '        _transportManager = transportManager
        '    End Sub

        '    ' ── Propriétés ────────────────────────────────────────────────────

        '    ''' <summary>Mode de fonctionnement courant.</summary>
        '    Public Property Mode As JogWheelMode
        '        Get
        '            Return _mode
        '        End Get
        '        Set
        '            If _mode = Value Then Return
        '            _mode = Value
        '            RaiseEvent On_ModeChanged(_mode)
        '        End Set
        '    End Property

        '    ''' <summary>
        '    ''' Référence au <see cref="ReaperTransportManager"/>.
        '    ''' Peut être définie après construction.
        '    ''' </summary>
        '    Public Property TransportManager As ReaperTransportManager
        '        Get
        '            Return _transportManager
        '        End Get
        '        Set
        '            _transportManager = Value
        '        End Set
        '    End Property

        '    ''' <summary>
        '    ''' Pas de déplacement du curseur par tick en mode
        '    ''' <see cref="JogWheelMode.Cursor"/> (secondes, défaut : 0.1 s).
        '    ''' </summary>
        '    Public Property CursorStepSeconds As Double
        '        Get
        '            Return _cursorStepSeconds
        '        End Get
        '        Set
        '            _cursorStepSeconds = Math.Max(0.001, Value)
        '        End Set
        '    End Property

        '    ''' <summary>
        '    ''' Pas de déplacement des points de sélection par tick en modes
        '    ''' <see cref="JogWheelMode.SelectionIn"/> et
        '    ''' <see cref="JogWheelMode.SelectionOut"/> (secondes, défaut : 0.1 s).
        '    ''' </summary>
        '    Public Property SelectionStepSeconds As Double
        '        Get
        '            Return _selectionStepSeconds
        '        End Get
        '        Set
        '            _selectionStepSeconds = Math.Max(0.001, Value)
        '        End Set
        '    End Property

        '    ''' <summary>
        '    ''' Quand True, le pas effectif est divisé par <see cref="FineDivider"/>.
        '    ''' Activer via un modificateur (Ctrl/Shift) sur la surface.
        '    ''' </summary>
        '    Public Property FineMode As Boolean
        '        Get
        '            Return _fineMode
        '        End Get
        '        Set
        '            _fineMode = Value
        '        End Set
        '    End Property

        '    ''' <summary>
        '    ''' Facteur de division du pas en mode fin (défaut : 10 → pas ÷ 10).
        '    ''' Minimum : 2.
        '    ''' </summary>
        '    Public Property FineDivider As Double
        '        Get
        '            Return _fineDivider
        '        End Get
        '        Set
        '            _fineDivider = Math.Max(2.0, Value)
        '        End Set
        '    End Property

        '    ' ── Point d'entrée principal ──────────────────────────────────────

        '    ''' <summary>
        '    ''' À appeler à chaque tick détecté de la jogwheel.
        '    ''' <list type="table">
        '    ''' <item><term><paramref name="ticks"/> &gt; 0</term>
        '    '''       <description>Sens horaire : forward / zoom in / point +</description></item>
        '    ''' <item><term><paramref name="ticks"/> &lt; 0</term>
        '    '''       <description>Sens anti-horaire : backward / zoom out / point -</description></item>
        '    ''' </list>
        '    ''' </summary>
        '    ''' <param name="ticks">
        '    ''' Nombre de crans (positif = horaire).
        '    ''' Une valeur de ±1 correspond à un cran standard de la roue.
        '    ''' </param>
        '    Public Sub Jog(ticks As Integer)
        '        If ticks = 0 Then Return
        '        Select Case _mode
        '            Case JogWheelMode.Cursor
        '                JogCursor(ticks)
        '            Case JogWheelMode.SelectionIn
        '                JogSelectionIn(ticks)
        '            Case JogWheelMode.SelectionOut
        '                JogSelectionOut(ticks)
        '            Case JogWheelMode.ZoomHorizontal
        '                JogZoomHorizontal(ticks)
        '            Case JogWheelMode.ZoomVertical
        '                JogZoomVertical(ticks)
        '        End Select
        '    End Sub

        '    ' ── Raccourcis de mode ────────────────────────────────────────────

        '    ''' <summary>Passe en mode <see cref="JogWheelMode.Cursor"/>.</summary>
        '    Public Sub SetCursorMode()
        '        Mode = JogWheelMode.Cursor
        '    End Sub

        '    ''' <summary>Passe en mode <see cref="JogWheelMode.SelectionIn"/>.</summary>
        '    Public Sub SetSelectionInMode()
        '        Mode = JogWheelMode.SelectionIn
        '    End Sub

        '    ''' <summary>Passe en mode <see cref="JogWheelMode.SelectionOut"/>.</summary>
        '    Public Sub SetSelectionOutMode()
        '        Mode = JogWheelMode.SelectionOut
        '    End Sub

        '    ''' <summary>Passe en mode <see cref="JogWheelMode.ZoomHorizontal"/>.</summary>
        '    Public Sub SetZoomHorizontalMode()
        '        Mode = JogWheelMode.ZoomHorizontal
        '    End Sub

        '    ''' <summary>Passe en mode <see cref="JogWheelMode.ZoomVertical"/>.</summary>
        '    Public Sub SetZoomVerticalMode()
        '        Mode = JogWheelMode.ZoomVertical
        '    End Sub

        '    ''' <summary>
        '    ''' Cycle vers le mode suivant dans l'ordre :
        '    ''' Cursor → SelectionIn → SelectionOut → ZoomHorizontal → ZoomVertical → Cursor…
        '    ''' Utile pour un bouton "Mode" dédié sur la surface.
        '    ''' </summary>
        '    Public Sub CycleMode()
        '        Select Case _mode
        '            Case JogWheelMode.Cursor
        '                Mode = JogWheelMode.SelectionIn
        '            Case JogWheelMode.SelectionIn
        '                Mode = JogWheelMode.SelectionOut
        '            Case JogWheelMode.SelectionOut
        '                Mode = JogWheelMode.ZoomHorizontal
        '            Case JogWheelMode.ZoomHorizontal
        '                Mode = JogWheelMode.ZoomVertical
        '            Case Else
        '                Mode = JogWheelMode.Cursor
        '        End Select
        '    End Sub

        '    ' ── Mode Cursor ───────────────────────────────────────────────────

        '    ''' <summary>
        '    ''' Déplace le curseur d'édition de <c>ticks × <see cref="CursorStepSeconds"/></c>.
        '    ''' Si Reaper est en lecture (<see cref="ReadIsPlaying"/>), cherche la
        '    ''' nouvelle position sans s'arrêter (seek).
        '    ''' </summary>
        '    Private Sub JogCursor(ticks As Integer)
        '        Dim currentPos = utility.GetCursorPosition()
        '        If currentPos < 0 Then currentPos = 0

        '        Dim _step = EffectiveStep(_cursorStepSeconds)
        '        Dim newPos = Math.Max(0.0, currentPos + ticks * _step)

        '        utility.SetCursorPosition(newPos,
        '                                          moveView:=True,
        '                                          seekPlay:=ReadIsPlaying())
        '        RaiseEvent On_CursorMoved(newPos)
        '    End Sub

        '    ' ── Mode SelectionIn ──────────────────────────────────────────────

        '    ''' <summary>
        '    ''' Déplace le point In de la sélection temporelle.
        '    ''' Clampé à [0, selOut].
        '    ''' </summary>
        '    Private Sub JogSelectionIn(ticks As Integer)
        '        Dim sel = ReadSelectionState()

        '        Dim _step = EffectiveStep(_selectionStepSeconds)
        '        Dim newIn = Math.Max(0.0, sel.selIn + ticks * _step)
        '        If newIn > sel.selOut Then newIn = sel.selOut

        '        utility.CallReaperBridge(
        '                    $"TRANSPORT_SET_TIME_SEL_IN|{newIn.ToString("G", Ic)}")
        '        RaiseEvent On_SelectionChanged(newIn, sel.selOut)
        '    End Sub

        '    ' ── Mode SelectionOut ─────────────────────────────────────────────

        '    ''' <summary>
        '    ''' Déplace le point Out de la sélection temporelle.
        '    ''' Clampé à [selIn, +∞[.
        '    ''' </summary>
        '    Private Sub JogSelectionOut(ticks As Integer)
        '        Dim sel = ReadSelectionState()

        '        Dim _step = EffectiveStep(_selectionStepSeconds)
        '        Dim newOut = Math.Max(sel.selIn, sel.selOut + ticks * _step)

        '        utility.CallReaperBridge(
        '                    $"TRANSPORT_SET_TIME_SEL_OUT|{newOut.ToString("G", Ic)}")
        '        RaiseEvent On_SelectionChanged(sel.selIn, newOut)
        '    End Sub

        '    ' ── Mode ZoomHorizontal ───────────────────────────────────────────

        '    ''' <summary>
        '    ''' Zoom horizontal de la timeline.
        '    ''' Chaque tick émet une commande <c>ZOOM_H_IN</c> ou <c>ZOOM_H_OUT</c>
        '    ''' vers le bridge.
        '    ''' </summary>
        '    Private Sub JogZoomHorizontal(ticks As Integer)
        '        Dim dir = Math.Sign(ticks)
        '        Dim count = Math.Abs(ticks)
        '        For i = 0 To count - 1
        '            If dir > 0 Then
        '                utility.CallReaperBridge("ZOOM_H_IN")
        '            Else
        '                utility.CallReaperBridge("ZOOM_H_OUT")
        '            End If
        '        Next
        '        RaiseEvent On_Zoomed(JogWheelMode.ZoomHorizontal, dir)
        '    End Sub

        '    ' ── Mode ZoomVertical ─────────────────────────────────────────────

        '    ''' <summary>
        '    ''' Zoom vertical sur la piste sélectionnée.
        '    ''' Chaque tick émet une commande <c>ZOOM_TRACK_IN</c> ou
        '    ''' <c>ZOOM_TRACK_OUT</c> vers le bridge.
        '    ''' </summary>
        '    Private Sub JogZoomVertical(ticks As Integer)
        '        Dim dir = Math.Sign(ticks)
        '        Dim count = Math.Abs(ticks)
        '        For i = 0 To count - 1
        '            If dir > 0 Then
        '                utility.CallReaperBridge("ZOOM_TRACK_IN")
        '            Else
        '                utility.CallReaperBridge("ZOOM_TRACK_OUT")
        '            End If
        '        Next
        '        RaiseEvent On_Zoomed(JogWheelMode.ZoomVertical, dir)
        '    End Sub

        '    ' ── Helpers internes ──────────────────────────────────────────────

        '    ''' <summary>
        '    ''' Calcule le pas effectif :
        '    ''' en mode normal → <paramref name="baseStep"/> ;
        '    ''' en mode fin → <paramref name="baseStep"/> ÷ <see cref="FineDivider"/>.
        '    ''' </summary>
        '    Private Function EffectiveStep(baseStep As Double) As Double
        '        If _fineMode Then
        '            Return baseStep / _fineDivider
        '        End If
        '        Return baseStep
        '    End Function

        '    ''' <summary>
        '    ''' Lit la sélection temporelle courante.
        '    ''' Utilise le cache du <see cref="TransportManager"/> si disponible,
        '    ''' sinon effectue une requête pipe directe <c>GET_TIME_SELECTION</c>.
        '    ''' Retourne (0, 0) si indisponible.
        '    ''' </summary>
        '    Private Function ReadSelectionState() As (selIn As Double, selOut As Double)
        '        If _transportManager IsNot Nothing Then
        '            Return (_transportManager.TimeSelIn, _transportManager.TimeSelOut)
        '        End If

        '        Dim raw = utility.CallReaperBridge("GET_TIME_SELECTION")
        '        If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
        '            Return (0, 0)
        '        End If

        '        Dim p = raw.Split("|"c)
        '        If p.Length < 2 Then Return (0, 0)

        '        Dim selIn As Double = 0, selOut As Double = 0
        '        Double.TryParse(p(0), Globalization.NumberStyles.Any, Ic, selIn)
        '        Double.TryParse(p(1), Globalization.NumberStyles.Any, Ic, selOut)
        '        Return (selIn, selOut)
        '    End Function

        '    ''' <summary>
        '    ''' Retourne True si Reaper est en lecture.
        '    ''' Utilise le <see cref="TransportManager"/> si disponible,
        '    ''' sinon retourne False (pas de requête pipe supplémentaire).
        '    ''' </summary>
        '    Private Function ReadIsPlaying() As Boolean
        '        If _transportManager IsNot Nothing Then
        '            Return _transportManager.IsPlaying
        '        End If
        '        Return False
        '    End Function

        'End Class




    End Namespace

#End Region
#Region "Envelope"

    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER TRACK ENVELOPES — collection d'enveloppes d'une piste
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Gère la collection d'enveloppes d'automation d'une piste Reaper.
    ''' Appeler <see cref="Refresh"/> pour synchroniser la liste et l'état de chaque enveloppe.
    ''' <br/>
    ''' Les enveloppes standard (Volume / Pan / Mute / Send) sont instanciées
    ''' comme sous-classes typées.
    ''' Les <b>enveloppes de paramètres FX</b> sont détectées automatiquement via
    ''' <see cref="utility.GetFXEnvelopeIndex"/> et instanciées en
    ''' <see cref="ReaperFxParamEnvelope"/>.
    ''' </summary>
    Public Class ReaperTrackEnvelopes

        Private ReadOnly _trackIndex As Integer
        Private ReadOnly _envelopes As New List(Of ReaperTrackEnvelope)

        ' ── Mapping envIndex → (fxIndex, paramIndex) ──────────────────────────
        ' Construit dans BuildFxParamMap() avant chaque reconstruction de liste.
        ' Permet à CreateEnvelope() d'identifier les enveloppes FX sans
        ' faire de requête supplémentaire par enveloppe.
        Private ReadOnly _fxParamMap As New Dictionary(Of Integer, (FxIndex As Integer, ParamIndex As Integer))

        ' ── Dernier compte brut retourné par Reaper ───────────────────────────
        ' Comparé à chaque Refresh() pour ne reconstruire la liste que lorsque
        ' le nombre d'enveloppes a réellement changé (ajout/suppression d'env FX).
        Private _lastRawCount As Integer = -1

        ' ── Événement ─────────────────────────────────────────────────────────

        ''' <summary>
        ''' Déclenché quand le nombre brut d'enveloppes Reaper change.
        ''' <paramref name="count"/> = nouveau nombre total retourné par
        ''' <c>COUNT_TRACK_ENVELOPES</c>.
        ''' </summary>
        Public Event On_CountChange(count As Integer)

        ' ── Constructeur ───────────────────────────────────────────────────────

        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        Public Sub New(trackIndex As Integer)
            _trackIndex = trackIndex
            Refresh()
        End Sub

        ' ── Propriétés ─────────────────────────────────────────────────────────

        ''' <summary>Index 0-based de la piste parente.</summary>
        Public ReadOnly Property TrackIndex As Integer
            Get
                Return _trackIndex
            End Get
        End Property

        ''' <summary>Nombre d'enveloppes effectivement instanciées (après Refresh).</summary>
        Public ReadOnly Property Count As Integer
            Get
                Return _envelopes.Count
            End Get
        End Property

        ''' <summary>Accès direct à une enveloppe par index 0-based dans la liste interne.</summary>
        Default Public ReadOnly Property Item(index As Integer) As ReaperTrackEnvelope
            Get
                Return _envelopes(index)
            End Get
        End Property

        ''' <summary>Liste en lecture seule des enveloppes instanciées.</summary>
        Public ReadOnly Property Envelopes As IReadOnlyList(Of ReaperTrackEnvelope)
            Get
                Return _envelopes
            End Get
        End Property

        ''' <summary>
        ''' Retourne la première enveloppe du type <typeparamref name="T"/>, ou Nothing.
        ''' Exemple : <c>envs.FirstOfType(Of ReaperTrackVolEnvelope)()</c>
        ''' </summary>
        Public Function FirstOfType(Of T As ReaperTrackEnvelope)() As T
            Return _envelopes.OfType(Of T)().FirstOrDefault()
        End Function

        ''' <summary>
        ''' Retourne toutes les enveloppes du type <typeparamref name="T"/>.
        ''' Utile pour itérer sur tous les <see cref="ReaperFxParamEnvelope"/>.
        ''' </summary>
        Public Function AllOfType(Of T As ReaperTrackEnvelope)() As IEnumerable(Of T)
            Return _envelopes.OfType(Of T)()
        End Function

        ' ── Chargement ─────────────────────────────────────────────────────────

        ''' <summary>
        ''' Charge le nombre d'enveloppes Reaper.
        ''' Si le nombre a changé depuis le dernier appel :
        ''' <list type="number">
        ''' <item>Construit le mapping envIndex → FX/param via
        '''      <see cref="utility.GetFXEnvelopeIndex"/> (create=False).</item>
        ''' <item>Reconstruit la liste en créant l'instance typée correcte pour
        '''      chaque enveloppe (standard OU <see cref="ReaperFxParamEnvelope"/>).</item>
        ''' <item>Déclenche <see cref="On_CountChange"/>.</item>
        ''' </list>
        ''' Puis rafraîchit chaque enveloppe déjà connue.
        ''' </summary>
        Public Sub Refresh()
            Dim newCount = utility.CountTrackEnvelopes(_trackIndex)
            If newCount < 0 Then Return

            ' ── Reconstruction uniquement si le compte brut a changé ──────────
            If newCount <> _lastRawCount Then
                _lastRawCount = newCount

                ' ── 1. Lire les noms et compter les enveloppes non standard ───
                Dim names(newCount - 1) As String
                Dim fxEnvCount As Integer = 0
                For i = 0 To newCount - 1
                    names(i) = utility.GetEnvelopeName(_trackIndex, i)
                    If IsStandardEnvelopeName(names(i)) = False Then
                        fxEnvCount += 1
                    End If
                Next

                ' ── 2. Cartographier les enveloppes FX seulement si nécessaire ─
                ' Doit précéder CreateEnvelope — utilisé dans le Select Case Else.
                _fxParamMap.Clear()
                If fxEnvCount > 0 Then
                    BuildFxParamMap(fxEnvCount)
                End If

                ' ── 3. Reconstruire la liste ──────────────────────────────────
                _envelopes.Clear()
                For i = 0 To newCount - 1
                    Dim envName = names(i)
                    Dim new_env = CreateEnvelope(i, envName)
                    If new_env IsNot Nothing Then
                        new_env.SetKnownName(envName)
                        _envelopes.Add(new_env)
                    End If
                Next
                RaiseEvent On_CountChange(newCount)
            End If

            ' ── Rafraîchissement des enveloppes connues ───────────────────────
            For Each env In _envelopes
                env?.Refresh()
            Next
        End Sub

        ' ── Cartographie des enveloppes FX ─────────────────────────────────────

        ''' <summary>
        ''' Parcourt tous les FX de la piste et, pour chaque paramètre, interroge
        ''' Reaper avec <c>create=False</c> via <see cref="utility.GetFXEnvelopeIndex"/>.
        ''' Si une enveloppe existe, son envIndex est enregistré dans
        ''' <c>_fxParamMap</c> avec le couple (fxIndex, paramIndex) correspondant.
        ''' <br/>
        ''' Complexité : O(FX × Params) appels pipe — appelé uniquement quand
        ''' le nombre total d'enveloppes change.
        ''' </summary>
        Private Sub BuildFxParamMap(expectedCount As Integer)
            _fxParamMap.Clear()
            Dim fxCount = GetFxCount()
            For fxIdx = 0 To fxCount - 1
                Dim paramCount = GetFxParamCount(fxIdx)
                For paramIdx = 0 To paramCount - 1
                    Dim envIdx = utility.GetFXEnvelopeIndex(
                                     _trackIndex, fxIdx, paramIdx, create:=False)
                    If envIdx >= 0 Then
                        _fxParamMap(envIdx) = (fxIdx, paramIdx)
                        If _fxParamMap.Count >= expectedCount Then
                            Return
                        End If
                    End If
                Next
            Next
        End Sub

        ''' <summary>
        ''' Indique si le nom correspond à une enveloppe standard (non FX).
        ''' </summary>
        Private Shared Function IsStandardEnvelopeName(name As String) As Boolean
            Select Case name.ToUpperInvariant()
                Case "VOLUME", "PAN", "MUTE", "SEND VOLUME", "SEND PAN", "SEND MUTE"
                    Return True
                Case Else
                    Return False
            End Select
        End Function

        ' ── Factory ────────────────────────────────────────────────────────────

        ''' <summary>
        ''' Instancie le type d'enveloppe approprié selon son nom et la
        ''' cartographie FX construite par <see cref="BuildFxParamMap"/>.
        ''' <list type="table">
        ''' <item><term>"Volume"</term>
        '''       <description><see cref="ReaperTrackVolEnvelope"/></description></item>
        ''' <item><term>"Pan"</term>
        '''       <description><see cref="ReaperTrackPanEnvelope"/></description></item>
        ''' <item><term>"Mute"</term>
        '''       <description><see cref="ReaperTrackMuteEnvelope"/></description></item>
        ''' <item><term>"Send Volume"</term>
        '''       <description><see cref="ReaperSendVolEnvelope"/></description></item>
        ''' <item><term>"Send Pan"</term>
        '''       <description><see cref="ReaperSendPanEnvelope"/></description></item>
        ''' <item><term>"Send Mute"</term>
        '''       <description><see cref="ReaperSendMuteEnvelope"/></description></item>
        ''' <item><term>Présent dans <c>_fxParamMap</c></term>
        '''       <description><see cref="ReaperFxParamEnvelope"/> avec fxIndex
        '''       et paramIndex résolus.</description></item>
        ''' <item><term>Inconnu</term>
        '''       <description>Nothing — l'entrée est ignorée.</description></item>
        ''' </list>
        ''' </summary>
        Private Function CreateEnvelope(envIndex As Integer, name As String) As ReaperTrackEnvelope
            Select Case name.ToUpperInvariant()
                Case "VOLUME"
                    Return New ReaperTrackVolEnvelope(_trackIndex, envIndex)
                Case "PAN"
                    Return New ReaperTrackPanEnvelope(_trackIndex, envIndex)
                Case "MUTE"
                    Return New ReaperTrackMuteEnvelope(_trackIndex, envIndex)
                Case "SEND VOLUME"
                    Return New ReaperSendVolEnvelope(_trackIndex, envIndex)
                Case "SEND PAN"
                    Return New ReaperSendPanEnvelope(_trackIndex, envIndex)
                Case "SEND MUTE"
                    Return New ReaperSendMuteEnvelope(_trackIndex, envIndex)
                Case Else
                    ' ── Enveloppe de paramètre FX identifiée via BuildFxParamMap ─
                    Dim fxParam As (FxIndex As Integer, ParamIndex As Integer)
                    If _fxParamMap.TryGetValue(envIndex, fxParam) Then
                        Return New ReaperFxParamEnvelope(
                                   _trackIndex, fxParam.FxIndex, fxParam.ParamIndex, envIndex)
                    End If
                    Return Nothing
            End Select
        End Function

        Public Sub CreatFxParamEnvelope(FxIndex As Integer, ParamIndex As Integer)
            Reaper.utility.GetFXEnvelopeParam(TrackIndex, FxIndex, ParamIndex, True)
            Refresh()
        End Sub

        ' ── Helpers pipe ───────────────────────────────────────────────────────

        ''' <summary>
        ''' Retourne le nombre de FX sur la piste via <c>GET_FX_COUNT</c>.
        ''' Retourne 0 si le bridge est indisponible.
        ''' </summary>
        Private Function GetFxCount() As Integer
            Dim raw = utility.CallReaperBridge($"GET_FX_COUNT|{_trackIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return 0
            Dim count As Integer = 0
            Integer.TryParse(raw, count)
            Return count
        End Function

        ''' <summary>
        ''' Retourne le nombre de paramètres du FX <paramref name="fxIndex"/>
        ''' via <c>GET_FX_ALL_INFO</c> (champ index 2).
        ''' Retourne 0 si le bridge est indisponible.
        ''' </summary>
        Private Function GetFxParamCount(fxIndex As Integer) As Integer
            Dim raw = utility.CallReaperBridge($"GET_FX_ALL_INFO|{_trackIndex}|{fxIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return 0
            Dim p = raw.Split("|"c)
            If p.Length < 3 Then Return 0
            Dim count As Integer = 0
            Integer.TryParse(p(2), count)
            Return count
        End Function

    End Class


    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER AUTO FX PARAM — enveloppe d'automation d'un paramètre FX
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Spécialisation de <see cref="ReaperTrackEnvelope"/> pour l'enveloppe
    ''' d'automation d'un paramètre FX de piste.
    ''' Ajoute la propriété <see cref="NormalizedValue"/> (valeur normalisée [0..1])
    ''' et étend <see cref="Refresh"/> pour la charger en 1 appel pipe supplémentaire.
    ''' </summary>
    Public Class ReaperFxParamEnvelope
        Inherits ReaperTrackEnvelope

        ' ── Cache ──────────────────────────────────────────────────────────────
        Private ReadOnly _fxIndex As Integer
        Private ReadOnly _paramIndex As Integer
        Private _normalizedValue As Double = 0

        ' ── Événement ─────────────────────────────────────────────────────────
        ''' <summary>Déclenché quand la valeur normalisée [0..1] du paramètre change.</summary>
        Public Event On_NormalizedValueChange(_value As Double, _paramIndex As Integer)

        ' ── Constructeur ───────────────────────────────────────────────────────

        ''' <summary>
        ''' Initialise l'enveloppe d'automation d'un paramètre FX.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="fxIndex">Index 0-based du FX sur la piste.</param>
        ''' <param name="paramIndex">Index 0-based du paramètre sur le FX.</param>
        ''' <param name="envIndex">
        ''' Index 0-based de l'enveloppe sur la piste.
        ''' Les enveloppes de paramètres FX suivent les enveloppes de piste
        ''' (vol/pan/mute) — l'index exact est dynamique selon la configuration.
        ''' </param>
        Public Sub New(trackIndex As Integer,
                       fxIndex As Integer,
                       paramIndex As Integer,
                       envIndex As Integer)
            MyBase.New(trackIndex, envIndex)
            _fxIndex = fxIndex
            _paramIndex = paramIndex
            Refresh()
        End Sub

        ' ── Propriétés identité ────────────────────────────────────────────────

        ''' <summary>Index 0-based du FX sur la piste.</summary>
        Public ReadOnly Property FxIndex As Integer
            Get
                Return _fxIndex
            End Get
        End Property

        ''' <summary>Index 0-based du paramètre sur le FX.</summary>
        Public ReadOnly Property ParamIndex As Integer
            Get
                Return _paramIndex
            End Get
        End Property

        ' ── MustOverride ───────────────────────────────────────────────────────

        Protected Overrides Function GetValue() As Double
            Return _normalizedValue
        End Function

        Protected Overrides Sub SetValue(value As Double)
            _normalizedValue = value
            utility.SetFxParamNormalized(TrackIndex, _fxIndex, _paramIndex, value)
        End Sub

        ' ── Chargement ─────────────────────────────────────────────────────────

        ''' <summary>
        ''' Charge les propriétés de l'enveloppe (1 appel pipe via base)
        ''' puis la valeur normalisée du paramètre FX via <c>GET_FX_PARAM_NORM</c>
        ''' (1 appel pipe supplémentaire).
        ''' Déclenche <see cref="On_NormalizedValueChange"/> si la valeur a changé.
        ''' </summary>
        Public Overrides Sub Refresh()
            MyBase.Refresh()
            Dim new_val = utility.GetFxParamNormalized(TrackIndex, _fxIndex, _paramIndex)
            If new_val = Double.MinValue Then Return
            If _normalizedValue <> new_val Then
                _normalizedValue = new_val
                If Suspend_events = False Then
                    RaiseOn_ValueChange(new_val)
                    RaiseEvent On_NormalizedValueChange(_normalizedValue, _paramIndex)
                End If

            End If
        End Sub

        ''' <summary>
        ''' Met à jour le cache depuis le moniteur d'enveloppes sans écrire dans Reaper.
        ''' Déclenche <see cref="On_NormalizedValueChange"/> si la valeur a changé.
        ''' </summary>
        Public Sub SetValueFromMonitor(ParamIndex As Integer, NormValue As Double)
            If _normalizedValue <> NormValue Then
                _normalizedValue = NormValue
                If Suspend_events = False Then
                    RaiseOn_ValueChange(_normalizedValue)
                    RaiseEvent On_NormalizedValueChange(_normalizedValue, ParamIndex)
                End If

            End If
        End Sub

    End Class

    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER AUTO SEND VOL — enveloppe de volume d'un envoi
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Spécialisation de <see cref="ReaperAutoSend"/> pour l'enveloppe de volume d'un envoi.
    ''' Ajoute la propriété <see cref="SendUIVol"/> (volume POST-automation de l'envoi)
    ''' et étend <see cref="Refresh"/> pour la charger en 1 appel pipe supplémentaire.
    ''' Vol et pan sont obtenus en 1 seul échange via <c>GET_SEND_VOL_PAN</c>.
    ''' </summary>
    Public Class ReaperSendVolEnvelope
        Inherits ReaperTrackEnvelope

        ' ── Cache ──────────────────────────────────────────────────────────────
        Private _sendUIVol As Double = 1

        ' ── Événement ─────────────────────────────────────────────────────────
        ''' <summary>Déclenché quand le volume UI de l'envoi change.</summary>
        Public Event On_SendUIVolChange(_vol As Double)

        ' ── Constructeur ───────────────────────────────────────────────────────

        ''' <summary>
        ''' Initialise l'enveloppe de volume d'un envoi.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste source.</param>

        ''' <param name="envIndex">Index 0-based de l'enveloppe sur la piste.</param>
        Public Sub New(trackIndex As Integer, envIndex As Integer)
            MyBase.New(trackIndex, envIndex)

        End Sub

        Protected Overrides Function GetValue() As Double
            Return _sendUIVol
        End Function

        Protected Overrides Sub SetValue(value As Double)
            _sendUIVol = value
            'utility.SetTrackSendUIVol(TrackIndex, SendId, value)
            utility.SetTrackSendUIVolPan(TrackIndex, SendId, value, 0)
        End Sub

        ' ── Chargement ─────────────────────────────────────────────────────────

        ''' <summary>
        ''' Charge les propriétés de l'enveloppe (1 appel pipe via base)
        ''' puis le volume UI de l'envoi via <c>GET_SEND_VOL_PAN</c>
        ''' (1 appel pipe supplémentaire — vol et pan en un seul échange).
        ''' Déclenche <see cref="On_SendUIVolChange"/> si le volume a changé.
        ''' </summary>
        Public Overrides Sub Refresh()
            MyBase.Refresh()
            Dim vp = utility.GetTrackSendUIVolPan(TrackIndex, SendId)
            If vp.Vol = Double.MinValue Then Return
            If _sendUIVol <> vp.Vol Then
                _sendUIVol = vp.Vol
                If Suspend_events = False Then
                    RaiseOn_ValueChange(vp.Vol)
                    RaiseEvent On_SendUIVolChange(_sendUIVol)
                End If

            End If
        End Sub
        Public Sub SetValueFromMonitor(vol As Double)
            If _sendUIVol <> vol Then
                _sendUIVol = vol
                If Suspend_events = False Then
                    RaiseOn_ValueChange(vol)
                    RaiseEvent On_SendUIVolChange(_sendUIVol)
                End If
            End If
        End Sub
    End Class

    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER AUTO SEND PAN — enveloppe de pan d'un envoi
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Spécialisation de <see cref="ReaperAutoSend"/> pour l'enveloppe de pan d'un envoi.
    ''' Ajoute la propriété <see cref="SendUIPan"/> (pan POST-automation de l'envoi)
    ''' et étend <see cref="Refresh"/> pour la charger en 1 appel pipe supplémentaire.
    ''' Vol et pan sont obtenus en 1 seul échange via <c>GET_SEND_VOL_PAN</c>.
    ''' </summary>
    Public Class ReaperSendPanEnvelope
        Inherits ReaperTrackEnvelope

        ' ── Cache ──────────────────────────────────────────────────────────────
        Private _sendUIPan As Double = 0

        ' ── Événement ─────────────────────────────────────────────────────────
        ''' <summary>Déclenché quand le pan UI de l'envoi change.</summary>
        Public Event On_SendUIPanChange(_pan As Double)

        ' ── Constructeur ───────────────────────────────────────────────────────

        ''' <summary>
        ''' Initialise l'enveloppe de pan d'un envoi.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste source.</param>
        ''' <param name="sendId">Index 0-based de l'envoi sur la piste source.</param>
        ''' <param name="envIndex">Index 0-based de l'enveloppe sur la piste.</param>
        Public Sub New(trackIndex As Integer, envIndex As Integer)
            MyBase.New(trackIndex, envIndex)
        End Sub
        Protected Overrides Function GetValue() As Double
            Return _sendUIPan
        End Function

        Protected Overrides Sub SetValue(value As Double)
            _sendUIPan = value
            utility.SetTrackSendUIPan(TrackIndex, SendId, value)
        End Sub

        ' ── Chargement ─────────────────────────────────────────────────────────

        ''' <summary>
        ''' Charge les propriétés de l'enveloppe (1 appel pipe via base)
        ''' puis le pan UI de l'envoi via <c>GET_SEND_VOL_PAN</c>
        ''' (1 appel pipe supplémentaire — vol et pan en un seul échange).
        ''' Déclenche <see cref="On_SendUIPanChange"/> si le pan a changé.
        ''' </summary>
        Public Overrides Sub Refresh()
            MyBase.Refresh()
            Dim vp = utility.GetTrackSendUIVolPan(TrackIndex, SendId)
            If vp.Vol = Double.MinValue Then Return
            If _sendUIPan <> vp.Pan Then
                _sendUIPan = vp.Pan
                If Suspend_events = False Then
                    RaiseOn_ValueChange(vp.Pan)
                    RaiseEvent On_SendUIPanChange(_sendUIPan)
                End If

            End If
        End Sub
        Public Sub SetValueFromMonitor(pan As Double)
            If _sendUIPan <> pan Then
                _sendUIPan = pan
                If Suspend_events = False Then
                    RaiseOn_ValueChange(pan)
                    RaiseEvent On_SendUIPanChange(_sendUIPan)
                End If
            End If
        End Sub
    End Class

    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER AUTO SEND MUTE — enveloppe de mute d'un envoi
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Spécialisation de <see cref="ReaperAutoSend"/> pour l'enveloppe de mute d'un envoi.
    ''' Ajoute la propriété <see cref="SendUIMute"/> (mute POST-automation de l'envoi)
    ''' et étend <see cref="Refresh"/> pour le charger en 1 appel pipe supplémentaire.
    ''' </summary>
    Public Class ReaperSendMuteEnvelope
        Inherits ReaperTrackEnvelope

        ' ── Cache ──────────────────────────────────────────────────────────────
        Private _sendUIMute As Boolean = False

        ' ── Événement ─────────────────────────────────────────────────────────
        ''' <summary>Déclenché quand l'état mute UI de l'envoi change.</summary>
        Public Event On_SendUIMuteChange(_mute As Boolean)

        ' ── Constructeur ───────────────────────────────────────────────────────

        ''' <summary>
        ''' Initialise l'enveloppe de mute d'un envoi.
        ''' </summary>
        ''' <param name="trackIndex">Index 0-based de la piste source.</param>
        ''' <param name="sendId">Index 0-based de l'envoi sur la piste source.</param>
        ''' <param name="envIndex">Index 0-based de l'enveloppe sur la piste.</param>
        Public Sub New(trackIndex As Integer, envIndex As Integer)
            MyBase.New(trackIndex, envIndex)
        End Sub

        Protected Overrides Function GetValue() As Double
            Return If(_sendUIMute, 1.0, 0.0)
        End Function

        Protected Overrides Sub SetValue(value As Double)
            _sendUIMute = value <> 0.0
            utility.SetTrackSendUIMute(TrackIndex, SendId, _sendUIMute)
        End Sub

        ' ── Chargement ─────────────────────────────────────────────────────────

        ''' <summary>
        ''' Charge les propriétés de l'enveloppe (1 appel pipe via base)
        ''' puis l'état mute UI de l'envoi via <c>GET_SEND_MUTE</c>
        ''' (1 appel pipe supplémentaire).
        ''' Déclenche <see cref="On_SendUIMuteChange"/> si l'état a changé.
        ''' </summary>
        Public Overrides Sub Refresh()
            MyBase.Refresh()
            Dim new_mute = utility.GetTrackSendUIMute(TrackIndex, SendId)
            If _sendUIMute <> new_mute Then
                _sendUIMute = new_mute
                If Suspend_events = False Then
                    RaiseOn_ValueChange(GetValue())
                    RaiseEvent On_SendUIMuteChange(_sendUIMute)
                End If

            End If
        End Sub
        Public Sub SetValueFromMonitor(mute As Boolean)
            If _sendUIMute <> mute Then
                _sendUIMute = mute
                If Suspend_events = False Then
                    Dim normValue As Double = 0.0
                    If _sendUIMute Then
                        normValue = 1.0
                    End If

                    RaiseOn_ValueChange(normValue)
                    RaiseEvent On_SendUIMuteChange(_sendUIMute)
                End If
            End If
        End Sub
    End Class



    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER AUTO MUTE — enveloppe de mute + mute POST-automation
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Spécialisation de <see cref="ReaperTrackEnvelope"/> pour l'enveloppe de mute.
    ''' Ajoute la propriété <see cref="TrackUIMute"/> (mute POST-automation de la piste)
    ''' et étend <see cref="Refresh"/> pour la charger en 1 appel pipe supplémentaire.
    ''' </summary>
    Public Class ReaperTrackMuteEnvelope
        Inherits ReaperTrackEnvelope

        Private _trackUIMute As Boolean = False
        Public Event On_TrackUIMuteChange(_mute As Boolean)

        Public Sub New(trackIndex As Integer, Optional envIndex As Integer = 2)
            MyBase.New(trackIndex, envIndex)
        End Sub

        Protected Overrides Function GetValue() As Double
            Return If(_trackUIMute, 1.0, 0.0)
        End Function

        Protected Overrides Sub SetValue(value As Double)
            _trackUIMute = value <> 0.0
            utility.SetTrackUIMute(TrackIndex, _trackUIMute)
        End Sub

        ''' <summary>
        ''' Met à jour le cache depuis le moniteur d'enveloppes sans écrire dans Reaper.
        ''' Déclenche <see cref="On_TrackUIMuteChange"/> si l'état a changé.
        ''' </summary>
        Public Sub SetValueFromMonitor(normValue As Double)
            Dim mute = normValue <> 0.0
            If _trackUIMute <> mute Then
                _trackUIMute = mute
                If Suspend_events = False Then
                    RaiseOn_ValueChange(GetValue())
                    RaiseEvent On_TrackUIMuteChange(_trackUIMute)
                End If

            End If
        End Sub

        Public Overrides Sub Refresh()
            MyBase.Refresh()
            Dim new_mute = utility.GetTrackUIMute(TrackIndex)
            If _trackUIMute <> new_mute Then
                _trackUIMute = new_mute
                If Suspend_events = False Then
                    RaiseOn_ValueChange(GetValue())
                    RaiseEvent On_TrackUIMuteChange(_trackUIMute)
                End If

            End If
        End Sub

    End Class
    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER AUTO PAN — enveloppe de pan + pan POST-automation
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Spécialisation de <see cref="ReaperTrackEnvelope"/> pour l'enveloppe de pan.
    ''' Ajoute la propriété <see cref="TrackUIPan"/> (pan POST-automation de la piste)
    ''' et étend <see cref="Refresh"/> pour la charger en 1 appel pipe supplémentaire.
    ''' </summary>
    Public Class ReaperTrackPanEnvelope
        Inherits ReaperTrackEnvelope

        Private _trackUIPan As Double = 0
        Public Event On_TrackUIPanChange(_pan As Double)

        Public Sub New(trackIndex As Integer, Optional envIndex As Integer = 1)
            MyBase.New(trackIndex, envIndex)
        End Sub

        Protected Overrides Function GetValue() As Double
            Return _trackUIPan
        End Function

        Protected Overrides Sub SetValue(value As Double)
            _trackUIPan = value
            utility.SetTrackUIPan(TrackIndex, value)
        End Sub

        ''' <summary>
        ''' Met à jour le cache depuis le moniteur d'enveloppes sans écrire dans Reaper.
        ''' Déclenche <see cref="On_TrackUIPanChange"/> si la valeur a changé.
        ''' </summary>
        Public Sub SetValueFromMonitor(pan As Double)
            If _trackUIPan <> pan Then
                _trackUIPan = pan
                If Suspend_events = False Then
                    RaiseOn_ValueChange(pan)
                    RaiseEvent On_TrackUIPanChange(_trackUIPan)
                End If

            End If
        End Sub

        Public Overrides Sub Refresh()
            MyBase.Refresh()
            Dim vp = utility.GetTrackUIVolPan(TrackIndex)
            If vp.Vol = Double.MinValue Then Return
            If _trackUIPan <> vp.Pan Then
                _trackUIPan = vp.Pan
                If Suspend_events = False Then
                    RaiseOn_ValueChange(GetValue())
                    RaiseEvent On_TrackUIPanChange(_trackUIPan)
                End If

            End If
        End Sub

    End Class
    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER AUTO VOL — enveloppe de volume + volume POST-automation
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Spécialisation de <see cref="ReaperTrackEnvelope"/> pour l'enveloppe de volume.
    ''' Ajoute la propriété <see cref="TrackUIVol"/> (volume POST-automation de la piste)
    ''' et étend <see cref="Refresh"/> pour la charger en 1 appel pipe supplémentaire.
    ''' </summary>
    Public Class ReaperTrackVolEnvelope
        Inherits ReaperTrackEnvelope

        Private _trackUIVol As Double = 1
        Public Event On_TrackUIVolChange(_vol As Double)

        Public Sub New(trackIndex As Integer, Optional envIndex As Integer = 0)
            MyBase.New(trackIndex, envIndex)
        End Sub

        Protected Overrides Function GetValue() As Double
            Return _trackUIVol
        End Function

        Protected Overrides Sub SetValue(value As Double)
            _trackUIVol = value
            utility.SetTrackUIVolume(TrackIndex, value)
        End Sub

        ''' <summary>
        ''' Met à jour le cache depuis le moniteur d'enveloppes sans écrire dans Reaper.
        ''' Déclenche <see cref="On_TrackUIVolChange"/> si la valeur a changé.
        ''' </summary>
        Public Sub SetValueFromMonitor(vol As Double)
            If _trackUIVol <> vol Then
                _trackUIVol = vol
                If Suspend_events = False Then
                    RaiseOn_ValueChange(vol)
                    RaiseEvent On_TrackUIVolChange(_trackUIVol)
                End If

            End If
        End Sub

        Public Overrides Sub Refresh()
            MyBase.Refresh()
            Dim new_vol = utility.GetTrackUIVolume(TrackIndex)
            If new_vol = Double.MinValue Then Return
            If _trackUIVol <> new_vol Then
                _trackUIVol = new_vol
                If Suspend_events = False Then
                    RaiseOn_ValueChange(GetValue())
                    RaiseEvent On_TrackUIVolChange(_trackUIVol)
                End If

            End If
        End Sub

    End Class
    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER TRACK ENVELOPE — propriétés d'une enveloppe de piste
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Représente une enveloppe d'automation d'une piste Reaper.
    ''' Chaque propriété est lue via <see cref="utility.GetEnvelopePropVal"/>
    ''' et écrite via <see cref="utility.SetEnvelopePropVal"/> —
    ''' même pattern que <see cref="utility.GetEnvelopeExt"/>/<see cref="utility.SetEnvelopeExt"/>.
    ''' Appeler <see cref="Refresh"/> pour charger toutes les propriétés en une passe.
    ''' </summary>
    Public MustInherit Class ReaperTrackEnvelope

        Private ReadOnly _trackIndex As Integer
        Private ReadOnly _envIndex As Integer

        ' ── Cache ──────────────────────────────────────────────────────────────
        Private _name As String = String.Empty
        Private _guid As String = String.Empty
        Private _active As Boolean = True
        Private _visible As Boolean = True
        Private _armed As Boolean = False
        Private _showLane As Boolean = True
        'Private _laneHeight As Integer = 0
        'Private _defaultShape As Integer = 0
        'Private _minValue As Double = 0
        'Private _maxValue As Double = 1
        'Private _centerValue As Double = 0.5
        'Private _faderScaling As Boolean = False
        'Private _automationItemsOptions As Integer = -1
        Private _sendId As Integer = -1
        Private _Suspend_events As Boolean = False

        ' ── Données statiques : lues une seule fois ───────────────────────────
        Private _nameLoaded As Boolean = False
        Private _guidLoaded As Boolean = False
        Private _sendIdLoaded As Boolean = False

        ' ── Événements ────────────────────────────────────────────────────────
        Public Event On_ActiveChange(_active As Boolean)
        Public Event On_VisibleChange(_visible As Boolean)
        Public Event On_ArmedChange(_armed As Boolean)
        Public Event On_ValueChange(_value As Double)

        Public Sub New(trackIndex As Integer, envIndex As Integer)
            _trackIndex = trackIndex
            _envIndex = envIndex
        End Sub

        ''' <summary>
        ''' Renseigne le nom déjà lu par <see cref="ReaperTrackEnvelopes"/>
        ''' pour éviter une requête pipe supplémentaire dans <see cref="Refresh"/>.
        ''' </summary>
        Friend Sub SetKnownName(name As String)
            If String.IsNullOrEmpty(name) = False Then
                _name = name
                _nameLoaded = True
            End If
        End Sub

        ' ── Identité (lecture seule) ───────────────────────────────────────────
        Public Property Suspend_events As Boolean
            Get
                Return _Suspend_events
            End Get
            Set
                _Suspend_events = Value
            End Set
        End Property

        Public ReadOnly Property TrackIndex As Integer
            Get
                Return _trackIndex
            End Get
        End Property

        Public ReadOnly Property EnvIndex As Integer
            Get
                Return _envIndex
            End Get
        End Property

        Public ReadOnly Property Name As String
            Get
                Return _name
            End Get
        End Property

        Public ReadOnly Property Guid As String
            Get
                Return _guid
            End Get
        End Property

        ''' <summary>
        ''' I_SEND_IDX — Index 0-based de l'envoi auquel cette enveloppe est liée.
        ''' Retourne -1 si l'enveloppe n'est pas une enveloppe d'envoi
        ''' (volume, pan, mute de piste ou paramètre FX).
        ''' Lecture seule — assigné par Reaper.
        ''' Chargé par <see cref="Refresh"/>.
        ''' </summary>
        Public ReadOnly Property SendId As Integer
            Get
                Return _sendId
            End Get
        End Property

        ' ── Propriétés R/W via SetEnvelopePropVal ─────────────────────────────

        Public Property Active As Boolean
            Get
                Return _active
            End Get
            Set
                _active = Value
                SetB("ACTIVE", Value)
            End Set
        End Property

        Public Property Visible As Boolean
            Get
                Return _visible
            End Get
            Set
                _visible = Value
                SetB("VISIBLE", Value)
            End Set
        End Property

        Public Property Armed As Boolean
            Get
                Return _armed
            End Get
            Set
                _armed = Value
                SetB("ARM", Value)
            End Set
        End Property

        Public Property ShowLane As Boolean
            Get
                Return _showLane
            End Get
            Set
                _showLane = Value
                SetB("SHOWLANE", Value)
            End Set
        End Property



        ' ── Plage de valeurs (lecture seule) ──────────────────────────────────

        'Public ReadOnly Property MinValue As Double
        '    Get
        '        Return _minValue
        '    End Get
        'End Property

        'Public ReadOnly Property MaxValue As Double
        '    Get
        '        Return _maxValue
        '    End Get
        'End Property

        'Public ReadOnly Property CenterValue As Double
        '    Get
        '        Return _centerValue
        '    End Get
        'End Property

        'Public ReadOnly Property FaderScaling As Boolean
        '    Get
        '        Return _faderScaling
        '    End Get
        'End Property

        ' ── Valeur métier (MustOverride) ──────────────────────────────────────

        Protected MustOverride Function GetValue() As Double
        Protected MustOverride Sub SetValue(value As Double)

        Public Property Value As Double
            Get
                Return GetValue()
            End Get
            Set
                SetValue(Value)
            End Set
        End Property

        Protected Sub RaiseOn_ValueChange(value As Double)
            If Suspend_events = False Then
                RaiseEvent On_ValueChange(value)
            End If

        End Sub

        ' ── Chargement ─────────────────────────────────────────────────────────

        ''' <summary>
        ''' Charge les propriétés de l'enveloppe en 1 requête (<c>GET_ENVELOPE_ALL_INFO</c>).
        ''' Repli sur les requêtes unitaires si le plugin ne supporte pas la commande.
        ''' </summary>
        Public Overridable Sub Refresh()
            Dim info As utility.EnvelopeAllInfo
            If utility.TryGetEnvelopeAllInfo(_trackIndex, _envIndex, info) Then
                ApplyAllInfo(info)
                Return
            End If
            RefreshLegacy()
        End Sub

        Private Sub ApplyAllInfo(info As utility.EnvelopeAllInfo)
            If _nameLoaded = False AndAlso String.IsNullOrEmpty(info.Name) = False Then
                _name = info.Name
                _nameLoaded = True
            End If

            If _guidLoaded = False AndAlso String.IsNullOrEmpty(info.Guid) = False Then
                _guid = info.Guid
                _guidLoaded = True
            End If

            If info.Active <> Double.MinValue Then
                Dim b = info.Active <> 0
                If _active <> b Then
                    _active = b
                    If Suspend_events = False Then
                        RaiseEvent On_ActiveChange(_active)
                    End If
                End If
            End If

            If info.Visible <> Double.MinValue Then
                Dim b = info.Visible <> 0
                If _visible <> b Then
                    _visible = b
                    If Suspend_events = False Then
                        RaiseEvent On_VisibleChange(_visible)
                    End If
                End If
            End If

            If info.Armed <> Double.MinValue Then
                Dim b = info.Armed <> 0
                If _armed <> b Then
                    _armed = b
                    If Suspend_events = False Then
                        RaiseEvent On_ArmedChange(_armed)
                    End If
                End If
            End If

            If info.ShowLane <> Double.MinValue Then
                _showLane = info.ShowLane <> 0
            End If

            If _sendIdLoaded = False AndAlso info.SendIdx <> Double.MinValue Then
                _sendId = CInt(info.SendIdx - 1)
                _sendIdLoaded = True
            End If
        End Sub

        Private Sub RefreshLegacy()
            ' ── Chaînes (statiques : lues une seule fois) ──────────────────────
            If _nameLoaded = False Then
                _name = utility.GetEnvelopeName(_trackIndex, _envIndex)
                _nameLoaded = String.IsNullOrEmpty(_name) = False
            End If

            If _guidLoaded = False Then
                _guid = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "GUID:GUID")
                _guidLoaded = String.IsNullOrEmpty(_guid) = False
            End If

            ' ── Active ─
            Dim v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "ACTIVE")
            If v <> Double.MinValue Then
                Dim b = v <> 0
                If _active <> b Then
                    _active = b
                    If Suspend_events = False Then
                        RaiseEvent On_ActiveChange(_active)
                    End If

                End If
            End If
            ' ── Visible ────────────────────────────────────────────────────────
            v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "VISIBLE")
            If v <> Double.MinValue Then
                Dim b = v <> 0
                If _visible <> b Then
                    _visible = b
                    If Suspend_events = False Then
                        RaiseEvent On_VisibleChange(_visible)
                    End If

                End If
            End If
            ' ── Armed ──────────────────────────────────────────────────────────
            v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "ARM")
            If v <> Double.MinValue Then
                Dim b = v <> 0
                If _armed <> b Then
                    _armed = b
                    If Suspend_events = False Then
                        RaiseEvent On_ArmedChange(_armed)
                    End If

                End If
            End If
            ' ── InLane ─────────────────────────────────────────────────────────
            v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "SHOWLANE")
            If v <> Double.MinValue Then _showLane = v <> 0
            '' ── LaneHeight ─────────────────────────────────────────────────────
            'v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "LANEHEIGHT")
            'If v <> Double.MinValue Then _laneHeight = CInt(v)

            '' ── DefaultShape ───────────────────────────────────────────────────
            'v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "DEFSHAPE")
            'If v <> Double.MinValue Then _defaultShape = CInt(v)

            '' ── Plage de valeurs (lecture seule) ───────────────────────────────
            'v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "MINVALUE")
            'If v <> Double.MinValue Then _minValue = v

            'v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "MAXVALUE")
            'If v <> Double.MinValue Then _maxValue = v

            'v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "CENTERVALUE")
            'If v <> Double.MinValue Then _centerValue = v

            '' ── FaderScaling ───────────────────────────────────────────────────
            'v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "FADERSCALING")
            'If v <> Double.MinValue Then _faderScaling = v <> 0

            '' ── AutomationItemsOptions ─────────────────────────────────────────
            'v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "AUTO_ITEMS_OPTIONS")
            'If v <> Double.MinValue Then _automationItemsOptions = CInt(v)

            ' ── SendId (I_SEND_IDX) — lecture seule ────────────────────────────
            '1-based index of send in P_TRACK, or 0 if not a send
            If _sendIdLoaded = False Then
                v = utility.GetEnvelopePropVal2(_trackIndex, _envIndex, "I_SEND_IDX")
                If v <> Double.MinValue Then
                    _sendId = CInt(v - 1)
                    _sendIdLoaded = True
                End If
            End If
            'Console.WriteLine(_trackIndex & "|" & _envIndex & ":" & _name & "_" & _sendId)
        End Sub


        ' ── Helpers privés ─────────────────────────────────────────────────────

        Private Sub SetD(param As String, value As Double)
            utility.SetEnvelopePropVal(_trackIndex, _envIndex, param, value)
        End Sub

        Private Sub SetB(param As String, value As Boolean)
            SetD(param, If(value, 1.0, 0.0))
        End Sub

        Private Sub SetI(param As String, value As Integer)
            SetD(param, CDbl(value))
        End Sub

    End Class
#End Region
#Region "Track"

    ' ══════════════════════════════════════════════════════════════════════════
    '  TRACK GROUP TYPE — noms de groupes Reaper
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>Types de groupes de piste reconnus par GetSetTrackGroupMembershipEx.</summary>
    Public Enum TrackGroupType
        VolumeLead
        VolumeFollow
        VolumeVcaLead
        VolumeVcaFollow
        PanLead
        PanFollow
        PanVcaLead
        PanVcaFollow
        WidthLead
        WidthFollow
        MuteLead
        MuteFollow
        SoloLead
        SoloFollow
        RecArmLead
        RecArmFollow
        PolarityLead
        PolarityFollow
        AutoModeLead
        AutoModeFollow
        ReverseLead
        ReverseFollow
        SendVolLead
        SendVolFollow
        SendPanLead
        SendPanFollow
        SendMuteLead
        SendMuteFollow
    End Enum


    ''' <summary>
    ''' Représente une piste Reaper avec toutes ses propriétés typées.
    ''' Appeler <see cref="Refresh"/> pour charger l'état depuis Reaper en un seul appel pipe.
    ''' Les setters écrivent immédiatement via SET_TRACK_INFO.
    ''' </summary>
    Public Class ReaperTrack

        Private ReadOnly _index As Integer
        Private Shared ReadOnly Ic As CultureInfo = CultureInfo.InvariantCulture

        ' ── Enveloppes d'automation de la piste ───────────────────────────────
        ' Instanciées une seule fois — Refresh() est appelé à chaque Refresh de la piste.
        Private ReadOnly _envelopes As New ReaperTrackEnvelopes(_index)

        ' ── Cache ──────────────────────────────────────────────────────────────
        ' Doubles
        Private _vol As Double = 1
        Private _pan As Double = 0
        Private _width As Double = 0
        Private _dualPanL As Double = 0
        Private _dualPanR As Double = 0
        Private _panLaw As Double = -1
        Private _playOffset As Double = 0
        ' Booleans
        Private _mute As Boolean = False
        Private _phase As Boolean = False
        Private _recMonInEffect As Boolean = False  ' lecture seule
        Private _soloDefeat As Boolean = False
        Private _autoRecArm As Boolean = False
        Private _heightLock As Boolean = False
        Private _showInMixer As Boolean = True
        Private _showInTCP As Boolean = True
        Private _tcpPin As Boolean = False
        Private _mainSend As Boolean = True
        ' Entiers lecture seule
        Private _trackNumber As Integer = 0
        Private _wndH As Integer = 0
        Private _tcpH As Integer = 0
        Private _tcpY As Integer = 0
        Private _tcpScreenY As Integer = 0
        Private _mcpW As Integer = 0
        Private _mcpH As Integer = 0
        Private _mcpX As Integer = 0
        Private _mcpY As Integer = 0
        Private _mcpScreenX As Integer = 0
        Private _freezeCount As Integer = 0
        ' Entiers R/W
        Private _solo As Integer = 0
        Private _fxEnabled As Integer = -1
        Private _recArm As Integer = 0
        Private _recInput As Integer = -1
        Private _recMode As Integer = 0
        Private _recModeFlags As Integer = 0
        Private _recMon As Integer = 0
        Private _recMonItems As Integer = 0
        Private _vuMode As Integer = 0
        Private _autoMode As Integer = 0
        Private _nChannels As Integer = 2
        Private _selected As Integer = 0
        Private _folderDepth As Integer = 0
        Private _folderCompact As Integer = 0
        Private _midiHwOut As Integer = -1
        Private _midiInputChanMap As Integer = -1
        Private _midiCtlChan As Integer = -1
        Private _midiTrackSelFlag As Integer = 0
        Private _perfFlags As Integer = 0
        Private _customColor As Integer = 0
        Private _heightOverride As Integer = 0
        Private _spacer As Integer = 0
        Private _panMode As Integer = 0
        Private _panLawFlags As Integer = 0
        Private _playOffsetFlag As Integer = 0
        Private _freeMode As Integer = 0
        Private _numFixedLanes As Integer = 0
        ' Nom (via GET_TRACK_NAME séparé)
        Private _name As String = String.Empty


        ' ── Constructeur ───────────────────────────────────────────────────────

        Public Sub New(trackIndex As Integer)
            _index = trackIndex
            _envelopes = New ReaperTrackEnvelopes(_index)
            AddHandler _envelopes.On_CountChange,
                Sub(count As Integer)
                    RaiseEvent On_EnvelopeCountChange(count)
                End Sub
            Refresh()
        End Sub



        ''' <summary>Index 0-based de la piste dans le projet actif.</summary>
        Public ReadOnly Property TrackIndex As Integer
            Get
                Return _index
            End Get
        End Property
        ''' <summary>
        ''' Met à jour le cache de volume sans écrire dans Reaper.
        ''' Déclenche <see cref="On_VolChange"/> si la valeur a changé.
        ''' Appelé par <see cref="Monitors.ReaperTrackMonitor"/> via
        ''' <see cref="PageManagers.ReaperTracksManager"/>.
        ''' </summary>
        Public Sub SetVolFromMonitor(vol As Double)
            If _vol <> vol Then
                _vol = vol
                RaiseEvent On_VolChange(_vol)
            End If
        End Sub

        ''' <summary>
        ''' Met à jour le cache de pan sans écrire dans Reaper.
        ''' Déclenche <see cref="On_PanChange"/> si la valeur a changé.
        ''' </summary>
        Public Sub SetPanFromMonitor(pan As Double)
            If _pan <> pan Then
                _pan = pan
                RaiseEvent On_PanChange(_pan)
            End If
        End Sub

        ''' <summary>
        ''' Met à jour le cache de mute sans écrire dans Reaper.
        ''' Déclenche <see cref="On_MuteChange"/> si l'état a changé.
        ''' </summary>
        Public Sub SetMuteFromMonitor(mute As Boolean)
            If _mute <> mute Then
                _mute = mute
                RaiseEvent On_MuteChange(_mute)
            End If
        End Sub

        ''' <summary>
        ''' Met à jour le cache d'autoMode sans écrire dans Reaper.
        ''' Déclenche <see cref="On_AutoModeChange"/> si la valeur a changé.
        ''' </summary>
        Public Sub SetAutoModeFromMonitor(autoMode As Integer)
            If _autoMode <> autoMode Then
                _autoMode = autoMode
                RaiseEvent On_AutoModeChange(_autoMode)
            End If
        End Sub

        ''' <summary>
        ''' Met à jour le cache de solo sans écrire dans Reaper.
        ''' Déclenche <see cref="On_SoloChange"/> si la valeur a changé.
        ''' </summary>
        Public Sub SetSoloFromMonitor(solo As Integer)
            If _solo <> solo Then
                _solo = solo
                RaiseEvent On_SoloChange(_solo)
            End If
        End Sub

        ''' <summary>
        ''' Met à jour le cache de sélection sans écrire dans Reaper.
        ''' Déclenche <see cref="On_SelectedChange"/> si la valeur a changé.
        ''' </summary>
        Public Sub SetSelectedFromMonitor(selected As Integer)
            If _selected <> selected Then
                _selected = selected
                RaiseEvent On_SelectedChange(_selected)
            End If
        End Sub

        ''' <summary>
        ''' Met à jour le cache d'armement d'enregistrement sans écrire dans Reaper.
        ''' Déclenche <see cref="On_RecArmChange"/> si la valeur a changé.
        ''' </summary>
        Public Sub SetRecArmFromMonitor(recArm As Integer)
            If _recArm <> recArm Then
                _recArm = recArm
                RaiseEvent On_RecArmChange(_recArm)
            End If
        End Sub
        ''' <summary>
        ''' Met à jour le cache de solo defeat sans écrire dans Reaper.
        ''' Déclenche <see cref="On_SoloDefeatChange"/> si l'état a changé.
        ''' </summary>
        Public Sub SetSoloDefeatFromMonitor(soloDefeat As Boolean)
            If _soloDefeat <> soloDefeat Then
                _soloDefeat = soloDefeat
                RaiseEvent On_SoloDefeatChange(_soloDefeat)
            End If
        End Sub

        ' ── Événements ────────────────────────────────────────────────────────

        ''' <summary>
        ''' Bubblé depuis <see cref="ReaperTrackEnvelopes.On_CountChange"/> :
        ''' déclenché quand le nombre brut d'enveloppes Reaper change sur cette piste.
        ''' </summary>
        Public Event On_EnvelopeCountChange(count As Integer)
        'Events
        Public Event On_VolChange(_vol As Double)
        Public Event On_PanChange(_pan As Double)
        Public Event On_DualPanLChange(_dualPanL As Double)
        Public Event On_DualPanRChange(_dualPanR As Double)
        Public Event On_MuteChange(_mute As Boolean)
        Public Event On_SoloChange(_solo As Integer)
        Public Event On_FxEnabledChange(_fxEnabled As Integer)
        Public Event On_RecArmChange(_recArm As Integer)
        Public Event On_AutoModeChange(_autoMode As Integer)
        Public Event On_SelectedChange(_selected As Integer)
        Public Event On_RecInputChange(_recInput As Integer)
        Public Event On_RecModeChange(_recMode As Integer)
        Public Event On_NChannelsChange(_nChannels As Integer)
        Public Event On_SoloDefeatChange(_soloDefeat As Boolean)

        ' ── Chargement ─────────────────────────────────────────────────────────

        ''' <summary>
        ''' Charge tous les paramètres de la piste en un seul appel pipe.
        ''' Appeler avant d'accéder aux propriétés ou pour rafraîchir l'état.
        ''' <br/>
        ''' Format GET_TRACK_ALL_INFO (55 champs séparés par "|") :
        ''' <code>
        '''  [0]  D_VOL            [1]  D_PAN           [2]  D_WIDTH
        '''  [3]  D_DUALPANL       [4]  D_DUALPANR      [5]  D_PANLAW
        '''  [6]  D_PLAY_OFFSET    [7]  B_MUTE          [8]  B_PHASE            
        '''  [9]  B_RECMON_IN_EFFECT
        '''  [10] B_SOLO_DEFEAT       [11] B_AUTO_RECARM      [12] B_HEIGHTLOCK
        '''  [13] B_SHOWINMIXER       [14] B_SHOWINTCP        [15] B_TCPPIN
        '''  [16] B_MAINSEND          [17] IP_TRACKNUMBER
        '''  [18] I_SOLO           [19] I_FXEN          [20] I_RECARM
        '''  [21] I_RECINPUT       [22] I_RECMODE       [23] I_RECMODE_FLAGS
        '''  [24] I_RECMON         [25] I_RECMONITEMS   [26] I_VUMODE
        '''  [27] I_AUTOMODE       [28] I_NCHAN         [29] I_SELECTED
        '''  [30] I_WNDH           [31] I_TCPH          [32] I_TCPY
        '''  [33] I_TCPSCREENY     [34] I_MCPW          [35] I_MCPH
        '''  [36] I_MCPX           [37] I_MCPY          [38] I_MCPSCREENX
        '''  [39] I_FOLDERDEPTH    [40] I_FOLDERCOMPACT [41] I_MIDIHWOUT
        '''  [42] I_MIDI_INPUT_CHANMAP [43] I_MIDI_CTL_CHAN [44] I_MIDI_TRACKSEL_FLAG
        '''  [45] I_PERFFLAGS      [46] I_CUSTOMCOLOR   [47] I_HEIGHTOVERRIDE
        '''  [48] I_SPACER         [49] I_PANMODE       [50] I_PANLAW_FLAGS
        '''  [51] I_PLAY_OFFSET_FLAG [52] I_FREEZECOUNT [53] I_FREEMODE
        '''  [54] I_NUMFIXEDLANES
        ''' </code>
        ''' </summary>
        ' ── Chargement ─────────────────────────────────────────────────────────

        Public Sub Refresh(Optional pipe As ReaperPollPipe = Nothing)
            Dim nameRaw As String
            Dim raw As String
            Dim masterMuteSolo = -1
            If _index = -1 Then
                masterMuteSolo = GetMasterMuteSoloFlags()
            End If

            If pipe IsNot Nothing Then
                ' ── Appel via le pipe dédié du monitor ────────────────────────
                nameRaw = pipe.Request($"GET_TRACK_NAME|{_index}")
                raw = pipe.Request($"GET_TRACK_ALL_INFO|{_index}")

            Else
                ' ── Appel direct via le bridge (CallReaperBridge) ─────────────
                nameRaw = utility.CallReaperBridge($"GET_TRACK_NAME|{_index}")
                raw = utility.CallReaperBridge($"GET_TRACK_ALL_INFO|{_index}")
            End If
            ' ── Nom ───────────────────────────────────────────────────────────
            If Not String.IsNullOrEmpty(nameRaw) AndAlso Not nameRaw.StartsWith("ERROR", StringComparison.Ordinal) Then
                _name = nameRaw
            End If

            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Return
            End If

            Dim p = raw.Split("|"c)

            If p.Length < 55 Then Return

            Dim new_vol = ParseD(p(0))
            If _vol <> new_vol Then : _vol = new_vol : RaiseEvent On_VolChange(_vol) : End If

            Dim new_pan = ParseD(p(1))
            If _pan <> new_pan Then : _pan = new_pan : RaiseEvent On_PanChange(_pan) : End If

            _width = ParseD(p(2))

            Dim new_dualPanL = ParseD(p(3))
            If _dualPanL <> new_dualPanL Then
                _dualPanL = new_dualPanL
                RaiseEvent On_DualPanLChange(_dualPanL)
            End If

            Dim new_dualPanR = ParseD(p(4))
            If _dualPanR <> new_dualPanR Then
                _dualPanR = new_dualPanR
                RaiseEvent On_DualPanRChange(_dualPanR)
            End If

            _panLaw = ParseD(p(5))
            _playOffset = ParseD(p(6))

            Dim new_mute = p(7) <> "0"
            If _index = -1 Then
                new_mute = False
                If masterMuteSolo = 1 OrElse masterMuteSolo = 3 Then
                    new_mute = True
                End If
            End If
            If _mute <> new_mute Then : _mute = new_mute : RaiseEvent On_MuteChange(_mute) : End If

            _phase = p(8) <> "0"

            ' [9]  B_RECMON_IN_EFFECT (lecture seule)
            _recMonInEffect = p(9) <> "0"

            ' [10] B_SOLO_DEFEAT
            Dim new_soloDefeat = ParseD(p(10)) <> 0.0
            If _soloDefeat <> new_soloDefeat Then
                _soloDefeat = new_soloDefeat
                RaiseEvent On_SoloDefeatChange(_soloDefeat)
            End If

            ' [11] B_AUTO_RECARM
            _autoRecArm = p(11) <> "0"
            ' [12] B_HEIGHTLOCK
            _heightLock = p(12) <> "0"
            ' [13] B_SHOWINMIXER
            _showInMixer = p(13) <> "0"
            ' [14] B_SHOWINTCP
            _showInTCP = p(14) <> "0"
            ' [15] B_TCPPIN
            _tcpPin = p(15) <> "0"
            ' [16] B_MAINSEND
            _mainSend = p(16) <> "0"

            _trackNumber = ParseI(p(17))

            Dim new_solo = ParseI(p(18))
            If _index = -1 Then
                new_solo = 0
                If masterMuteSolo = 2 OrElse masterMuteSolo = 3 Then
                    new_solo = 2
                End If
            End If
            If _solo <> new_solo Then : _solo = new_solo : RaiseEvent On_SoloChange(_solo) : End If

            Dim new_fxEnabled = ParseI(p(19))
            If _fxEnabled <> new_fxEnabled Then
                _fxEnabled = new_fxEnabled
                RaiseEvent On_FxEnabledChange(_fxEnabled)
            End If

            Dim new_recArm = ParseI(p(20))
            If _recArm <> new_recArm Then
                _recArm = new_recArm
                RaiseEvent On_RecArmChange(_recArm)
            End If

            Dim new_recInput = ParseI(p(21))
            If _recInput <> new_recInput Then
                _recInput = new_recInput
                RaiseEvent On_RecInputChange(_recInput)
            End If

            Dim new_recMode = ParseI(p(22))
            If _recMode <> new_recMode Then
                _recMode = new_recMode
                RaiseEvent On_RecModeChange(_recMode)
            End If

            _recModeFlags = ParseI(p(23))
            _recMon = ParseI(p(24))
            _recMonItems = ParseI(p(25))
            _vuMode = ParseI(p(26))

            Dim new_autoMode = ParseI(p(27))
            If _autoMode <> new_autoMode Then
                _autoMode = new_autoMode
                RaiseEvent On_AutoModeChange(_autoMode)
            End If

            Dim new_nChannels = ParseI(p(28))
            If _nChannels <> new_nChannels Then
                _nChannels = new_nChannels
                RaiseEvent On_NChannelsChange(_nChannels)
            End If

            Dim new_selected = ParseI(p(29))
            If _selected <> new_selected Then
                _selected = new_selected
                RaiseEvent On_SelectedChange(_selected)
            End If

            _wndH = ParseI(p(30))
            _tcpH = ParseI(p(31))
            _tcpY = ParseI(p(32))
            _tcpScreenY = ParseI(p(33))
            _mcpW = ParseI(p(34))
            _mcpH = ParseI(p(35))
            _mcpX = ParseI(p(36))
            _mcpY = ParseI(p(37))
            _mcpScreenX = ParseI(p(38))
            _folderDepth = ParseI(p(39))
            _folderCompact = ParseI(p(40))
            _midiHwOut = ParseI(p(41))
            _midiInputChanMap = ParseI(p(42))
            _midiCtlChan = ParseI(p(43))
            _midiTrackSelFlag = ParseI(p(44))
            _perfFlags = ParseI(p(45))
            _customColor = ParseI(p(46))
            _heightOverride = ParseI(p(47))
            _spacer = ParseI(p(48))
            _panMode = ParseI(p(49))
            _panLawFlags = ParseI(p(50))
            _playOffsetFlag = ParseI(p(51))
            _freezeCount = ParseI(p(52))
            _freeMode = ParseI(p(53))
            _numFixedLanes = ParseI(p(54))

            ' ── Enveloppes d'automation ────────────────────────────────────────
            ' Synchronisées à chaque Refresh de la piste.
            ' ReaperTrackEnvelopes ne reconstruit sa liste que si le nombre brut
            ' retourné par Reaper a changé — pas de surcharge en régime statique.
            _envelopes.Refresh()
        End Sub

        ' ── Helpers privés ─────────────────────────────────────────────────────

        Private Shared Function ParseD(s As String) As Double
            Dim v As Double = 0
            Double.TryParse(s, NumberStyles.Any, Ic, v)
            Return v
        End Function

        Private Shared Function ParseI(s As String) As Integer
            Return CInt(ParseD(s))
        End Function

        Private Sub SetD(param As String, value As Double)
            utility.CallReaperBridge($"SET_TRACK_INFO|{_index}|{param}|{value.ToString("G", Ic)}")
        End Sub

        Private Sub SetB(param As String, value As Boolean)
            SetD(param, If(value, 1.0, 0.0))
        End Sub

        Private Sub SetI(param As String, value As Integer)
            SetD(param, CDbl(value))
        End Sub



        ' ══════════════════════════════════════════════════════════════════════
        '  PROPRIÉTÉS
        ' ══════════════════════════════════════════════════════════════════════


        ' ── Propriétés ────────────────────────────────────────────────────────

        ''' <summary>
        ''' Collection des enveloppes d'automation de la piste.
        ''' Synchronisée automatiquement lors de chaque appel à <see cref="Refresh"/>.
        ''' </summary>
        Public ReadOnly Property Envelopes As ReaperTrackEnvelopes
            Get
                Return _envelopes
            End Get
        End Property

        ' ── Nom ────────────────────────────────────────────────────────────────

        ''' <summary>Nom de la piste (chargé par Refresh, lecture seule via ce cache).</summary>
        Public ReadOnly Property Name As String
            Get
                Return _name
            End Get
        End Property

        ' ── Volume / Pan ───────────────────────────────────────────────────────

        ''' <summary>D_VOL — Volume trim (0=-inf  1=0dB  2=+6dB)</summary>
        Public Property Vol As Double
            Get
                Return _vol : End Get
            Set
                _vol = Value : SetD("D_VOL", Value) : End Set
        End Property

        ''' <summary>D_PAN — Pan trim (-1=L  0=Centre  1=R)</summary>
        Public Property Pan As Double
            Get
                Return _pan : End Get
            Set
                _pan = Value : SetD("D_PAN", Value) : End Set
        End Property

        ''' <summary>D_WIDTH — Largeur stéréo (-1..1)</summary>
        Public Property Width As Double
            Get
                Return _width : End Get
            Set
                _width = Value : SetD("D_WIDTH", Value) : End Set
        End Property

        ''' <summary>D_DUALPANL — Dual pan gauche (-1..1, si I_PANMODE=6)</summary>
        Public Property DualPanL As Double
            Get
                Return _dualPanL : End Get
            Set
                _dualPanL = Value : SetD("D_DUALPANL", Value) : End Set
        End Property

        ''' <summary>D_DUALPANR — Dual pan droit (-1..1, si I_PANMODE=6)</summary>
        Public Property DualPanR As Double
            Get
                Return _dualPanR : End Get
            Set
                _dualPanR = Value : SetD("D_DUALPANR", Value) : End Set
        End Property

        ''' <summary>D_PANLAW — Pan law (&lt;0=projet défaut  0.5=-6dB  1=0dB…)</summary>
        Public Property PanLaw As Double
            Get
                Return _panLaw : End Get
            Set
                _panLaw = Value : SetD("D_PANLAW", Value) : End Set
        End Property

        ''' <summary>I_PANMODE — 0=classic 3=balance 5=stéréo pan 6=dual pan</summary>
        Public Property PanMode As Integer
            Get
                Return _panMode : End Get
            Set
                _panMode = Value : SetI("I_PANMODE", Value) : End Set
        End Property

        ''' <summary>I_PANLAW_FLAGS — 0=sinus 1=hybrid 2=linéaire 3=hybrid2</summary>
        Public Property PanLawFlags As Integer
            Get
                Return _panLawFlags : End Get
            Set
                _panLawFlags = Value : SetI("I_PANLAW_FLAGS", Value) : End Set
        End Property

        ' ── État ───────────────────────────────────────────────────────────────

        ''' <summary>B_MUTE — Piste muette</summary>
        Public Property Mute As Boolean
            Get
                Return _mute : End Get
            Set
                _mute = Value : SetB("B_MUTE", Value) : End Set
        End Property

        ''' <summary>B_PHASE — Phase inversée</summary>
        Public Property Phase As Boolean
            Get
                Return _phase : End Get
            Set
                _phase = Value : SetB("B_PHASE", Value) : End Set
        End Property

        ''' <summary>I_SOLO — 0=off 1=solo 2=solo-in-place 5=safe 6=safe-in-place</summary>
        Public Property Solo As Integer
            Get
                Return _solo : End Get
            Set
                _solo = Value : SetI("I_SOLO", Value) : End Set
        End Property

        ''' <summary>B_SOLO_DEFEAT — Si vrai, joue même quand d'autres pistes sont solotées.</summary>
        Public Property SoloDefeat As Boolean
            Get
                Return _soloDefeat
            End Get
            Set
                _soloDefeat = Value
                SetB("B_SOLO_DEFEAT", Value)
            End Set
        End Property

        ''' <summary>I_SELECTED — Piste sélectionnée (0/1)</summary>
        Public Property Selected As Integer
            Get
                Return _selected : End Get
            Set
                _selected = Value : SetI("I_SELECTED", Value) : End Set
        End Property

        ''' <summary>I_FXEN — FX actifs (0=bypassé  !=0=actif)</summary>
        Public Property FxEnabled As Integer
            Get
                Return _fxEnabled : End Get
            Set
                _fxEnabled = Value : SetI("I_FXEN", Value) : End Set
        End Property

        ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
        Public Property AutoMode As Integer
            Get
                Return _autoMode : End Get
            Set
                _autoMode = Value : SetI("I_AUTOMODE", Value) : End Set
        End Property

        ''' <summary>I_NCHAN — Nombre de canaux (2-128, pair)</summary>
        Public Property NChannels As Integer
            Get
                Return _nChannels : End Get
            Set
                _nChannels = Value : SetI("I_NCHAN", Value) : End Set
        End Property

        ' ── Enregistrement ─────────────────────────────────────────────────────

        ''' <summary>I_RECARM — Armé pour l'enregistrement (0/1)</summary>
        Public Property RecArm As Integer
            Get
                Return _recArm : End Get
            Set
                _recArm = Value : SetI("I_RECARM", Value) : End Set
        End Property

        ''' <summary>B_AUTO_RECARM — Arm automatique à la sélection</summary>
        Public Property AutoRecArm As Boolean
            Get
                Return _autoRecArm : End Get
            Set
                _autoRecArm = Value : SetB("B_AUTO_RECARM", Value) : End Set
        End Property

        ''' <summary>I_RECINPUT — Entrée d'enregistrement (voir doc API pour flags)</summary>
        Public Property RecInput As Integer
            Get
                Return _recInput : End Get
            Set
                _recInput = Value : SetI("I_RECINPUT", Value) : End Set
        End Property

        ''' <summary>I_RECMODE — 0=input 1=out stéréo 2=none 4=MIDI out 7=MIDI OD…</summary>
        Public Property RecMode As Integer
            Get
                Return _recMode : End Get
            Set
                _recMode = Value : SetI("I_RECMODE", Value) : End Set
        End Property

        ''' <summary>I_RECMODE_FLAGS — &amp;3=mode sortie enregistrement</summary>
        Public Property RecModeFlags As Integer
            Get
                Return _recModeFlags : End Get
            Set
                _recModeFlags = Value : SetI("I_RECMODE_FLAGS", Value) : End Set
        End Property

        ''' <summary>I_RECMON — 0=off 1=normal 2=pas pendant lecture</summary>
        Public Property RecMon As Integer
            Get
                Return _recMon : End Get
            Set
                _recMon = Value : SetI("I_RECMON", Value) : End Set
        End Property

        ''' <summary>I_RECMONITEMS — Monitoring des items pendant enregistrement (0/1)</summary>
        Public Property RecMonItems As Integer
            Get
                Return _recMonItems : End Get
            Set
                _recMonItems = Value : SetI("I_RECMONITEMS", Value) : End Set
        End Property

        ''' <summary>B_RECMON_IN_EFFECT — Monitoring actif en lecture (lecture seule)</summary>
        Public ReadOnly Property RecMonInEffect As Boolean
            Get
                Return _recMonInEffect
            End Get
        End Property

        ' ── VU-mètre / Offset ──────────────────────────────────────────────────

        ''' <summary>I_VUMODE — Mode VU-mètre (&amp;1=désactivé &amp;30=type…)</summary>
        Public Property VuMode As Integer
            Get
                Return _vuMode : End Get
            Set
                _vuMode = Value : SetI("I_VUMODE", Value) : End Set
        End Property

        ''' <summary>D_PLAY_OFFSET — Décalage lecture (sec ou samples selon PlayOffsetFlag)</summary>
        Public Property PlayOffset As Double
            Get
                Return _playOffset : End Get
            Set
                _playOffset = Value : SetD("D_PLAY_OFFSET", Value) : End Set
        End Property

        ''' <summary>I_PLAY_OFFSET_FLAG — &amp;1=bypass &amp;2=en samples (sinon secondes)</summary>
        Public Property PlayOffsetFlag As Integer
            Get
                Return _playOffsetFlag : End Get
            Set
                _playOffsetFlag = Value : SetI("I_PLAY_OFFSET_FLAG", Value) : End Set
        End Property

        ' ── MIDI ───────────────────────────────────────────────────────────────

        ''' <summary>I_MIDIHWOUT — Sortie MIDI HW (&lt;0=désactivée)</summary>
        Public Property MidiHwOut As Integer
            Get
                Return _midiHwOut : End Get
            Set
                _midiHwOut = Value : SetI("I_MIDIHWOUT", Value) : End Set
        End Property

        ''' <summary>I_MIDI_INPUT_CHANMAP — -1=source 1-16=canal cible</summary>
        Public Property MidiInputChanMap As Integer
            Get
                Return _midiInputChanMap : End Get
            Set
                _midiInputChanMap = Value : SetI("I_MIDI_INPUT_CHANMAP", Value) : End Set
        End Property

        ''' <summary>I_MIDI_CTL_CHAN — -1=aucun 0-15=canal vol/pan 16=tous</summary>
        Public Property MidiCtlChan As Integer
            Get
                Return _midiCtlChan : End Get
            Set
                _midiCtlChan = Value : SetI("I_MIDI_CTL_CHAN", Value) : End Set
        End Property

        ''' <summary>I_MIDI_TRACKSEL_FLAG — Options liste MIDI editor</summary>
        Public Property MidiTrackSelFlag As Integer
            Get
                Return _midiTrackSelFlag : End Get
            Set
                _midiTrackSelFlag = Value : SetI("I_MIDI_TRACKSEL_FLAG", Value) : End Set
        End Property

        ' ── Dossier ────────────────────────────────────────────────────────────

        ''' <summary>I_FOLDERDEPTH — 0=normal 1=parent dossier -1=dernier dans dossier</summary>
        Public Property FolderDepth As Integer
            Get
                Return _folderDepth : End Get
            Set
                _folderDepth = Value : SetI("I_FOLDERDEPTH", Value) : End Set
        End Property

        ''' <summary>I_FOLDERCOMPACT — 0=normal 1=réduit 2=totalement réduit</summary>
        Public Property FolderCompact As Integer
            Get
                Return _folderCompact : End Get
            Set
                _folderCompact = Value : SetI("I_FOLDERCOMPACT", Value) : End Set
        End Property

        ' ── Visibilité / Interface ─────────────────────────────────────────────

        ''' <summary>B_SHOWINMIXER — Visible dans le mixer</summary>
        Public Property ShowInMixer As Boolean
            Get
                Return _showInMixer : End Get
            Set
                _showInMixer = Value : SetB("B_SHOWINMIXER", Value) : End Set
        End Property

        ''' <summary>B_SHOWINTCP — Visible dans l'arrangement</summary>
        Public Property ShowInTCP As Boolean
            Get
                Return _showInTCP : End Get
            Set
                _showInTCP = Value : SetB("B_SHOWINTCP", Value) : End Set
        End Property

        ''' <summary>B_TCPPIN — Épinglée en haut de l'arrangement</summary>
        Public Property TcpPin As Boolean
            Get
                Return _tcpPin : End Get
            Set
                _tcpPin = Value : SetB("B_TCPPIN", Value) : End Set
        End Property

        ''' <summary>B_MAINSEND — Envoie audio au parent</summary>
        Public Property MainSend As Boolean
            Get

                Return _mainSend : End Get
            Set
                _mainSend = Value : SetB("B_MAINSEND", Value) : End Set
        End Property

        ''' <summary>I_HEIGHTOVERRIDE — Hauteur TCP forcée en pixels (0=aucune)</summary>
        Public Property HeightOverride As Integer
            Get
                Return _heightOverride : End Get
            Set
                _heightOverride = Value : SetI("I_HEIGHTOVERRIDE", Value) : End Set
        End Property

        ''' <summary>B_HEIGHTLOCK — Hauteur TCP verrouillée (définir HeightOverride avant)</summary>
        Public Property HeightLock As Boolean
            Get
                Return _heightLock : End Get
            Set
                _heightLock = Value : SetB("B_HEIGHTLOCK", Value) : End Set
        End Property

        ''' <summary>I_SPACER — 1=espace TCP au-dessus de cette piste</summary>
        Public Property Spacer As Integer
            Get
                Return _spacer : End Get
            Set
                _spacer = Value : SetI("I_SPACER", Value) : End Set
        End Property

        ''' <summary>I_CUSTOMCOLOR — Couleur OS | 0x1000000 (0=couleur par défaut)</summary>
        Public Property CustomColor As Integer
            Get
                Return _customColor : End Get
            Set
                _customColor = Value : SetI("I_CUSTOMCOLOR", Value) : End Set
        End Property

        ''' <summary>I_PERFFLAGS — &amp;1=pas buffer média &amp;2=pas FX anticipatif</summary>
        Public Property PerfFlags As Integer
            Get
                Return _perfFlags : End Get
            Set
                _perfFlags = Value : SetI("I_PERFFLAGS", Value) : End Set
        End Property

        ' ── Lanes / Free mode ──────────────────────────────────────────────────

        ''' <summary>I_FREEMODE — 1=free item positioning 2=fixed lanes</summary>
        Public Property FreeMode As Integer
            Get
                Return _freeMode : End Get
            Set
                _freeMode = Value : SetI("I_FREEMODE", Value) : End Set
        End Property

        ''' <summary>I_NUMFIXEDLANES — Nombre de lanes fixes</summary>
        Public Property NumFixedLanes As Integer
            Get
                Return _numFixedLanes : End Get
            Set
                _numFixedLanes = Value : SetI("I_NUMFIXEDLANES", Value) : End Set
        End Property

        ' ── Lecture seule (position/taille UI, état système) ───────────────────

        ''' <summary>IP_TRACKNUMBER — Numéro 1-based (0=absent -1=master)</summary>
        Public ReadOnly Property TrackNumber As Integer
            Get
                Return _trackNumber
            End Get
        End Property

        ''' <summary>I_WNDH — Hauteur TCP px incl. enveloppes</summary>
        Public ReadOnly Property WndH As Integer
            Get
                Return _wndH
            End Get
        End Property

        ''' <summary>I_TCPH — Hauteur TCP px excl. enveloppes</summary>
        Public ReadOnly Property TcpH As Integer
            Get
                Return _tcpH
            End Get
        End Property

        ''' <summary>I_TCPY — Position Y TCP / haut de l'arrangement</summary>
        Public ReadOnly Property TcpY As Integer
            Get
                Return _tcpY
            End Get
        End Property

        ''' <summary>I_TCPSCREENY — Position Y TCP / écran</summary>
        Public ReadOnly Property TcpScreenY As Integer
            Get
                Return _tcpScreenY
            End Get
        End Property

        ''' <summary>I_MCPW — Largeur MCP en pixels</summary>
        Public ReadOnly Property McpW As Integer
            Get
                Return _mcpW
            End Get
        End Property

        ''' <summary>I_MCPH — Hauteur MCP en pixels</summary>
        Public ReadOnly Property McpH As Integer
            Get
                Return _mcpH
            End Get
        End Property

        ''' <summary>I_MCPX — Position X MCP / conteneur mixer</summary>
        Public ReadOnly Property McpX As Integer
            Get
                Return _mcpX
            End Get
        End Property

        ''' <summary>I_MCPY — Position Y MCP / conteneur mixer</summary>
        Public ReadOnly Property McpY As Integer
            Get
                Return _mcpY
            End Get
        End Property

        ''' <summary>I_MCPSCREENX — Position X MCP / écran</summary>
        Public ReadOnly Property McpScreenX As Integer
            Get
                Return _mcpScreenX
            End Get
        End Property

        ''' <summary>I_FREEZECOUNT — Nombre de freezes actifs</summary>
        Public ReadOnly Property FreezeCount As Integer
            Get
                Return _freezeCount
            End Get
        End Property

    End Class


#End Region
#Region "Send"

    ' ══════════════════════════════════════════════════════════════════════════
    '  CATÉGORIE D'ENVOI
    ' ══════════════════════════════════════════════════════════════════════════

    Public Enum SendCategory As Integer
        Sends = 0   ' Envois (outputs vers autres pistes)
        Receives = -1  ' Reçus  (inputs depuis autres pistes)
        Hardware = 1  ' Sorties matérielles
    End Enum

    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER SEND — un envoi individuel
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Représente un envoi (send/receive/HW) d'une piste Reaper.
    ''' Appeler <see cref="Refresh"/> pour charger l'état depuis Reaper.
    ''' Les setters écrivent immédiatement via SET_SEND_INFO.
    ''' </summary>
    Public Class ReaperSend

        Private ReadOnly _trackIndex As Integer
        Private ReadOnly _category As Integer
        Private ReadOnly _sendIndex As Integer
        Private Shared ReadOnly Ic As CultureInfo = CultureInfo.InvariantCulture

        ' ── Cache ──────────────────────────────────────────────────────────────
        Private _vol As Double = 1
        Private _pan As Double = 0
        Private _panLaw As Double = -1
        Private _mute As Boolean = False
        Private _phase As Boolean = False
        Private _mono As Boolean = False
        Private _sendMode As Integer = 0
        Private _srcChan As Integer = 0
        Private _dstChan As Integer = 0
        Private _midiFlags As Integer = 0
        Private _destTrackIndex As Integer = -1  ' IP_DSTTRKIDX — -1 = HW output
        Private _Name As String

        ' ── Événements ────────────────────────────────────────────────────────
        Public Event On_VolChange(_vol As Double)
        Public Event On_PanChange(_pan As Double)
        Public Event On_MuteChange(_mute As Boolean)

        Public Sub New(trackIndex As Integer, category As Integer, sendIndex As Integer)
            _trackIndex = trackIndex
            _category = category
            If _category = CInt(SendCategory.Sends) Then
                _sendIndex = sendIndex
            ElseIf _category = CInt(SendCategory.Hardware) Then
                _dstChan = sendIndex
            End If


        End Sub
        Public Property Name As String
            Get
                Return _Name
            End Get
            Set
                _Name = Value
            End Set
        End Property

        Public ReadOnly Property TrackIndex As Integer
            Get
                Return _trackIndex
            End Get
        End Property

        Public ReadOnly Property Category As Integer
            Get
                Return _category
            End Get
        End Property

        Public ReadOnly Property SendIndex As Integer
            Get
                Return _sendIndex
            End Get
        End Property

        ''' <summary>
        ''' Index 0-based de la piste destination de cet envoi.
        ''' Retourne -1 pour un envoi vers une sortie hardware.
        ''' Chargé par <see cref="Refresh"/>.
        ''' </summary>
        Public ReadOnly Property DestTrackIndex As Integer
            Get
                Return _destTrackIndex
            End Get
        End Property

        ''' <summary>
        ''' True si cet envoi est vers une sortie hardware (pas une piste).
        ''' </summary>
        Public ReadOnly Property IsHardwareOutput As Boolean
            Get
                Return _destTrackIndex = -1 AndAlso _category = CInt(SendCategory.Hardware)
            End Get
        End Property

        Public Sub SetVolFromMonitor(vol As Double)
            If _vol <> vol Then
                _vol = vol
                RaiseEvent On_VolChange(_vol)
            End If
        End Sub

        Public Sub SetPanFromMonitor(pan As Double)
            If _pan <> pan Then
                _pan = pan
                RaiseEvent On_PanChange(_pan)
            End If
        End Sub

        Public Sub SetMuteFromMonitor(mute As Boolean)
            If _mute <> mute Then
                _mute = mute
                RaiseEvent On_MuteChange(_mute)
            End If
        End Sub

        ''' <summary>
        ''' Charge tous les paramètres de l'envoi en un seul appel pipe.
        ''' Déclenche les événements uniquement si la valeur a changé.
        ''' </summary>
        Public Sub Refresh()
            Dim raw = utility.CallReaperBridge(
                $"GET_SEND_ALL_INFO|{_trackIndex}|{_category}|{_sendIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return

            Dim p = raw.Split("|"c)
            If p.Length < 11 Then Return

            Dim new_vol = ParseD(p(0))
            If _vol <> new_vol Then
                _vol = new_vol
                RaiseEvent On_VolChange(_vol)
            End If

            Dim new_pan = ParseD(p(1))
            If _pan <> new_pan Then
                _pan = new_pan
                RaiseEvent On_PanChange(_pan)
            End If

            _panLaw = ParseD(p(2))

            Dim new_mute = p(3) <> "0"
            If _mute <> new_mute Then
                _mute = new_mute
                RaiseEvent On_MuteChange(_mute)
            End If

            _phase = p(4) <> "0"
            _mono = p(5) <> "0"
            _sendMode = ParseI(p(6))
            _srcChan = ParseI(p(7))
            _dstChan = ParseI(p(8))
            _midiFlags = ParseI(p(9))
            _destTrackIndex = ParseI(p(10))
        End Sub

        Friend Sub UpdateFromParts(vol As Double, pan As Double, panLaw As Double,
                                    mute As Boolean, phase As Boolean, mono As Boolean,
                                    sendMode As Integer, srcChan As Integer,
                                    dstChan As Integer, midiFlags As Integer,
                                    Optional destTrackIndex As Integer = -1)
            If _vol <> vol Then : _vol = vol : RaiseEvent On_VolChange(_vol) : End If
            If _pan <> pan Then : _pan = pan : RaiseEvent On_PanChange(_pan) : End If
            _panLaw = panLaw
            If _mute <> mute Then : _mute = mute : RaiseEvent On_MuteChange(_mute) : End If
            _phase = phase
            _mono = mono
            _sendMode = sendMode
            _srcChan = srcChan
            _dstChan = dstChan
            _midiFlags = midiFlags
            _destTrackIndex = destTrackIndex
        End Sub

        Private Shared Function ParseD(s As String) As Double
            Dim v As Double = 0
            Double.TryParse(s, NumberStyles.Any, Ic, v)
            Return v
        End Function

        Private Shared Function ParseI(s As String) As Integer
            Return CInt(ParseD(s))
        End Function

        Private Sub SetD(param As String, value As Double)
            utility.CallReaperBridge(
                $"SET_SEND_INFO|{_trackIndex}|{_category}|{_sendIndex}|{param}|{value.ToString("G", Ic)}")
        End Sub

        Private Sub SetB(param As String, value As Boolean)
            SetD(param, If(value, 1.0, 0.0))
        End Sub

        Private Sub SetI(param As String, value As Integer)
            SetD(param, CDbl(value))
        End Sub

        Public Property Vol As Double
            Get
                Return _vol
            End Get
            Set
                _vol = Value
                'utility.SetTrackSendUIVol(TrackIndex, SendIndex, _vol)
                SetD("D_VOL", Value)
            End Set
        End Property

        Public Property Pan As Double
            Get
                Return _pan
            End Get
            Set
                _pan = Value
                utility.SetTrackSendUIPan(TrackIndex, SendIndex, _pan)
                'SetD("D_PAN", Value)
            End Set
        End Property

        Public Property PanLaw As Double
            Get
                Return _panLaw
            End Get
            Set
                _panLaw = Value
                SetD("D_PANLAW", Value)
            End Set
        End Property

        Public Property Mute As Boolean
            Get
                Return _mute
            End Get
            Set
                _mute = Value
                utility.SetTrackSendUIMute(TrackIndex, SendIndex, _mute)
                'SetB("B_MUTE", Value)
            End Set
        End Property

        Public Property Phase As Boolean
            Get
                Return _phase
            End Get
            Set
                _phase = Value
                SetB("B_PHASE", Value)
            End Set
        End Property

        Public Property Mono As Boolean
            Get
                Return _mono
            End Get
            Set
                _mono = Value
                SetB("B_MONO", Value)
            End Set
        End Property

        Public Property SendMode As Integer
            Get
                Return _sendMode
            End Get
            Set
                _sendMode = Value
                SetI("I_SENDMODE", Value)
            End Set
        End Property

        Public Property SrcChan As Integer
            Get
                Return _srcChan
            End Get
            Set
                _srcChan = Value
                SetI("I_SRCCHAN", Value)
            End Set
        End Property

        Public Property DstChan As Integer
            Get
                Return _dstChan
            End Get
            Set
                _dstChan = Value
                SetI("I_DSTCHAN", Value)
            End Set
        End Property

        Public Property MidiFlags As Integer
            Get
                Return _midiFlags
            End Get
            Set
                _midiFlags = Value
                SetI("I_MIDIFLAGS", Value)
            End Set
        End Property

    End Class



    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER TRACK SENDS — collection d'envois d'une piste
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Gère la collection d'envois d'une piste pour une catégorie donnée.
    ''' Appeler <see cref="Refresh"/> pour synchroniser la liste et les valeurs.
    ''' </summary>
    Public Class ReaperTrackSends

        Private ReadOnly _trackIndex As Integer
        Private ReadOnly _category As Integer
        Private ReadOnly _sends As New List(Of ReaperSend)

        ' ── Événements ────────────────────────────────────────────────────────
        ''' <summary>Déclenché quand le nombre d'envois change.</summary>
        Public Event On_CountChange(count As Integer)

        ' ── Constructeur ───────────────────────────────────────────────────────

        ''' <param name="trackIndex">Index 0-based de la piste.</param>
        ''' <param name="category"><see cref="SendCategory"/> : Sends=0  Receives=-1  Hardware=1</param>
        Public Sub New(trackIndex As Integer,
                       Optional category As SendCategory = SendCategory.Sends)
            _trackIndex = trackIndex
            _category = CInt(category)
            Refresh()
        End Sub

        ' ── Propriétés ─────────────────────────────────────────────────────────

        Public ReadOnly Property TrackIndex As Integer
            Get
                Return _trackIndex : End Get
        End Property

        ''' <summary>Catégorie de cet ensemble d'envois.</summary>
        Public ReadOnly Property Category As SendCategory
            Get
                Return CType(_category, SendCategory) : End Get
        End Property

        ''' <summary>Nombre d'envois (mis à jour par Refresh).</summary>
        Public ReadOnly Property Count As Integer
            Get
                Return _sends.Count : End Get
        End Property

        ''' <summary>Accès direct à un envoi par index.</summary>
        Default Public ReadOnly Property Item(sendIndex As Integer) As ReaperSend
            Get
                If sendIndex >= 0 Then
                    Return _sends(sendIndex)
                Else
                    Return Nothing
                End If
            End Get
        End Property

        ''' <summary>Liste en lecture seule des envois.</summary>
        Public ReadOnly Property Sends As IReadOnlyList(Of ReaperSend)
            Get
                Return _sends : End Get
        End Property

        ' ── Chargement ─────────────────────────────────────────────────────────

        ''' <summary>
        ''' Interroge Reaper pour le nombre d'envois puis rafraîchit chaque envoi.
        ''' Reconstruit la liste si le nombre a changé et déclenche On_CountChange.
        ''' </summary>
        Public Sub Refresh()
            Dim raw = utility.CallReaperBridge($"GET_NUM_SENDS|{_trackIndex}|{_category}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return

            Dim newCount As Integer = 0
            If Not Integer.TryParse(raw, newCount) Then Return

            ' ── Reconstruire la liste si le nombre a changé ────────────────────
            If newCount <> _sends.Count Then
                _sends.Clear()
                For i = 0 To newCount - 1
                    _sends.Add(New ReaperSend(_trackIndex, _category, i))
                Next
                RaiseEvent On_CountChange(newCount)
            End If

            ' ── Rafraîchir chaque envoi ────────────────────────────────────────
            For Each s In _sends
                s.Refresh()
            Next
        End Sub


    End Class

#End Region

#Region "Fx"

    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER FX PARAM — un paramètre individuel de FX
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Représente un paramètre d'un FX de piste Reaper.
    ''' Appeler <see cref="Refresh"/> pour charger valeur et nom depuis Reaper.
    ''' Le setter de <see cref="Value"/> écrit immédiatement via SET_FX_PARAM_NORMALIZED.
    ''' </summary>
    Public Class ReaperFXParam

        Private ReadOnly _trackIndex As Integer
        Private ReadOnly _fxIndex As Integer
        Private ReadOnly _paramIndex As Integer
        Private Shared ReadOnly Ic As CultureInfo = CultureInfo.InvariantCulture

        Public _name As String = String.Empty
        Public _formatted As String = String.Empty
        Private _normValue As Double = 0
        Private _rawValue As Double = 0
        Private _step As Double? = Nothing
        Private _smallStep As Double? = Nothing
        Private _largeStep As Double? = Nothing
        Private _isToggle As Boolean? = Nothing
        Private _minValue As Double? = Nothing
        Private _maxValue As Double? = Nothing
        Private _midValue As Double? = Nothing



        ''' <summary>Déclenché quand la valeur normalisée [0..1] change.</summary>
        Public Event On_NormValueChange(normValue As Double)
        ''' <summary>Déclenché quand la valeur brute native change.</summary>
        Public Event On_RawValueChange(rawValue As Double)
        Public Event SuspendMonitoring(_paramIndex As Integer, status As Boolean)

        Public Sub New(trackIndex As Integer, fxIndex As Integer, paramIndex As Integer)
            _trackIndex = trackIndex
            _fxIndex = fxIndex
            _paramIndex = paramIndex
            Refresh()
        End Sub

        Public Sub RaiseOn_NormValueChange(normValue As Double)
            RaiseEvent On_NormValueChange(normValue)
        End Sub

        Public Sub RaiseOn_RawValueChange(rawValue As Double)
            RaiseEvent On_RawValueChange(rawValue)
        End Sub



        Public ReadOnly Property TrackIndex As Integer
            Get
                Return _trackIndex
            End Get
        End Property

        Public ReadOnly Property FxIndex As Integer
            Get
                Return _fxIndex
            End Get
        End Property

        Public ReadOnly Property ParamIndex As Integer
            Get
                Return _paramIndex
            End Get
        End Property

        ''' <summary>Nom du paramètre (chargé par Refresh).</summary>
        Public ReadOnly Property Name As String
            Get
                Return _name
            End Get
        End Property


        ''' <summary>Pas minimal entre deux valeurs adjacentes.</summary>
        Public Property [Step] As Double?
            Get
                Return _step
            End Get
            Set(value As Double?)
                _step = value
            End Set
        End Property

        ''' <summary>Pas fin (déplacement précis).</summary>
        Public Property SmallStep As Double?
            Get
                Return _smallStep
            End Get
            Set(value As Double?)
                _smallStep = value
            End Set
        End Property

        ''' <summary>Pas large (déplacement rapide).</summary>
        Public Property LargeStep As Double?
            Get
                Return _largeStep
            End Get
            Set(value As Double?)
                _largeStep = value
            End Set
        End Property

        ''' <summary>True si le paramètre est de type toggle (on/off).</summary>
        Public ReadOnly Property IsToggle As Boolean?
            Get
                Return _isToggle
            End Get
        End Property

        ''' <summary>Valeur brute minimale du paramètre (échelle native du plugin).</summary>
        Public ReadOnly Property MinValue As Double?
            Get
                Return _minValue
            End Get
        End Property

        ''' <summary>Valeur brute maximale du paramètre (échelle native du plugin).</summary>
        Public ReadOnly Property MaxValue As Double?
            Get
                Return _maxValue
            End Get
        End Property

        ''' <summary>
        ''' Valeur brute médiane du paramètre retournée par <c>TrackFX_GetParamEx</c>.
        ''' Correspond généralement au point central affiché dans l'UI Reaper.
        ''' </summary>
        Public ReadOnly Property MidValue As Double?
            Get
                Return _midValue
            End Get
        End Property


        ''' <summary>Valeur formatée par Reaper ex: "-6.0 dB".</summary>
        Public ReadOnly Property Formatted As String
            Get
                Return _formatted
            End Get
        End Property

        ''' <summary>
        ''' Valeur normalisée [0..1] indépendante de <see cref="RawValue"/>.
        ''' Le setter écrit dans Reaper via <c>SET_FX_PARAM_NORMALIZED</c>
        ''' et déclenche <see cref="On_NormValueChange"/>.
        ''' </summary>
        Public Property NormValue As Double
            Get
                Return _normValue
            End Get
            Set
                If _normValue <> Value Then
                    _normValue = Value
                    'Reaper.BeginUndoBlock()
                    'RaiseEvent SuspendMonitoring(_paramIndex, True)
                    Reaper.SetFxParamNormalized(TrackIndex, FxIndex, ParamIndex, Value)
                    'Reaper.SetFxNamedConfigParm(TrackIndex, FxIndex, "param_automate", "1")
                    'Dim _FxParamValues = Reaper.GetFxParamEx(TrackIndex, FxIndex, ParamIndex)
                    'Dim RawValue = (Value * (_FxParamValues.maxValue - _FxParamValues.minValue)) + _FxParamValues.minValue
                    'Reaper.SetFxParamValue(TrackIndex, FxIndex, ParamIndex, RawValue)



                    'Reaper.EndUndoBlock("ParamFx Modification")

                    'Reaper.SaveProject()
                    'refresh format value:
                    Dim fmtRaw =
                        utility.CallReaperBridge(
                    $"GET_FX_PARAM_FORMATTED|{_trackIndex}|{_fxIndex}|{_paramIndex}")
                    If Not String.IsNullOrEmpty(fmtRaw) AndAlso Not fmtRaw.StartsWith("ERROR", StringComparison.Ordinal) Then
                        _formatted = fmtRaw
                    Else
                        _formatted = "err"
                    End If

                    'RaiseEvent SuspendMonitoring(_paramIndex, False)

                End If
            End Set
        End Property

        ''' <summary>
        ''' Valeur brute native du plugin (échelle réelle ex: dB, Hz…) indépendante de <see cref="NormValue"/>.
        ''' Le setter écrit dans Reaper via <c>SET_FX_PARAM</c>
        ''' et déclenche <see cref="On_RawValueChange"/>.
        ''' </summary>
        Public Property RawValue As Double
            Get
                Return _rawValue
            End Get
            Set
                If _rawValue <> Value Then
                    _rawValue = Value
                    RaiseEvent SuspendMonitoring(_paramIndex, True)
                    utility.CallReaperBridge(
                        $"SET_FX_PARAM|{_trackIndex}|{_fxIndex}|{_paramIndex}|{_rawValue.ToString("G", Ic)}")

                    RaiseEvent SuspendMonitoring(_paramIndex, False)

                End If
            End Set
        End Property

        ''' <summary>Convertit une valeur normalisée [0..1] en valeur brute native.</summary>
        Public Function NormalizedToRaw(normValue As Double) As Double
            Return _minValue + normValue * (_maxValue - _minValue)
        End Function

        ''' <summary>
        ''' Convertit une valeur brute native en valeur normalisée [0..1].
        ''' Retourne 0 si la plage est nulle.
        ''' </summary>
        Public Function RawToNormalized(rawValue As Double) As Double
            Dim range = _maxValue - _minValue
            If range = 0.0 Then
                Return 0.0
            End If
            Return (rawValue - _minValue) / range
        End Function

        Public Sub Refresh()
            Dim nameRaw = utility.CallReaperBridge(
                $"GET_FX_PARAM_NAME|{_trackIndex}|{_fxIndex}|{_paramIndex}")
            If Not String.IsNullOrEmpty(nameRaw) AndAlso Not nameRaw.StartsWith("ERROR", StringComparison.Ordinal) Then
                _name = nameRaw
                'Console.WriteLine("----------------------")
                'Console.WriteLine(_name)
            End If

            ' ── TrackFX_GetParamEx : valeur brute + min, max, mid ─────────────
            Dim exRaw = utility.CallReaperBridge(
                $"GET_FX_PARAM_EX|{_trackIndex}|{_fxIndex}|{_paramIndex}")
            If Not String.IsNullOrEmpty(exRaw) AndAlso Not exRaw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Dim p = exRaw.Split("|"c)
                If p.Length >= 4 Then
                    Dim new_raw As Double = 0
                    Double.TryParse(p(0), NumberStyles.Any, Ic, new_raw)
                    Dim dbl As Double = 0
                    _minValue = If(Double.TryParse(p(1), NumberStyles.Any, Ic, dbl), dbl, Nothing)
                    _maxValue = If(Double.TryParse(p(2), NumberStyles.Any, Ic, dbl), dbl, Nothing)
                    _midValue = If(Double.TryParse(p(3), NumberStyles.Any, Ic, dbl), dbl, Nothing)
                    Set_NewRawValue(new_raw)
                    'Console.WriteLine("MinMidMax:" & _minValue.ToString & "|" & _midValue.ToString & "|" & _maxValue.ToString)
                End If
            End If

            Dim valRaw = utility.CallReaperBridge(
                $"GET_FX_PARAM_NORMALIZED|{_trackIndex}|{_fxIndex}|{_paramIndex}")
            If Not String.IsNullOrEmpty(valRaw) AndAlso Not valRaw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Dim new_norm As Double = 0
                Double.TryParse(valRaw, NumberStyles.Any, Ic, new_norm)
                Set_NewNormValue(new_norm)
                'Console.WriteLine("NormValue:" & new_norm)
            End If

            Dim fmtRaw = utility.CallReaperBridge(
                $"GET_FX_PARAM_FORMATTED|{_trackIndex}|{_fxIndex}|{_paramIndex}")
            If Not String.IsNullOrEmpty(fmtRaw) AndAlso Not fmtRaw.StartsWith("ERROR", StringComparison.Ordinal) Then
                _formatted = fmtRaw
                'Console.WriteLine("FormatValue:" & _formatted)
            End If

            Dim stepRaw = utility.CallReaperBridge(
                $"GET_FX_PARAM_STEP_SIZES|{_trackIndex}|{_fxIndex}|{_paramIndex}")
            If Not String.IsNullOrEmpty(stepRaw) AndAlso Not stepRaw.StartsWith("ERROR", StringComparison.Ordinal) Then
                If stepRaw <> "0|0|0|0" Then

                    Dim p = stepRaw.Split("|"c)
                    If p.Length >= 4 Then
                        Dim dbl As Double = 0
                        _step = If(Double.TryParse(p(0), NumberStyles.Any, Ic, dbl), dbl, Nothing)
                        _smallStep = If(Double.TryParse(p(1), NumberStyles.Any, Ic, dbl), dbl, Nothing)
                        _largeStep = If(Double.TryParse(p(2), NumberStyles.Any, Ic, dbl), dbl, Nothing)
                        _isToggle = p(3) = "1"
                        'Console.WriteLine("steps:" & _step.ToString & "|" & _smallStep.ToString & "|" & _largeStep.ToString & "|" & _isToggle.ToString)

                    End If
                End If

            End If

            ' ── Détection binaire par sondage de 5 positions ──────────────────
            ' 0.0 / 0.25 / 0.5 / 0.75 / 1.0 → si ≤ 2 chaînes distinctes → binaire
            If IsToggle Is Nothing Then
                Dim probeValues() As Double = {0.0, 0.25, 0.5, 0.75, 1.0}
                Dim distinctFmt As New System.Collections.Generic.HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                For Each probe In probeValues
                    Dim probeRaw = utility.CallReaperBridge(
                        $"GET_FX_PARAM_FORMATTED_AT|{_trackIndex}|{_fxIndex}|{_paramIndex}|{probe.ToString("G", Ic)}")
                    If Not String.IsNullOrEmpty(probeRaw) AndAlso Not probeRaw.StartsWith("ERROR", StringComparison.Ordinal) Then
                        distinctFmt.Add(probeRaw.Trim())
                    End If
                Next
                _isToggle = If(distinctFmt.Count > 0 AndAlso distinctFmt.Count <= 2, True, False)
                'Console.WriteLine("isToggle:" & _isToggle.ToString)
            End If


        End Sub

        ''' <summary>Met à jour <see cref="NormValue"/> depuis le moniteur sans écrire dans Reaper.</summary>
        Public Sub SetNormValueFromMonitor(newNormValue As Double)
            Set_NewNormValue(newNormValue)
        End Sub

        ''' <summary>Met à jour <see cref="RawValue"/> depuis le moniteur sans écrire dans Reaper.</summary>
        Public Sub SetRawValueFromMonitor(newRawValue As Double)
            Set_NewRawValue(newRawValue)
        End Sub

        Private Sub Set_NewNormValue(new_norm As Double)
            If _normValue <> new_norm Then
                _normValue = new_norm
                RaiseEvent On_NormValueChange(_normValue)
            End If
        End Sub

        Private Sub Set_NewRawValue(new_raw As Double)
            If _rawValue <> new_raw Then
                _rawValue = new_raw
                RaiseEvent On_RawValueChange(_rawValue)
            End If
        End Sub

    End Class

    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER FX — un plugin FX avec tous ses paramètres
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Représente un FX d'une piste Reaper avec sa liste de paramètres.
    ''' Appeler <see cref="Refresh"/> pour charger l'état complet (2 appels pipe).
    ''' </summary>
    Public Class ReaperFX


        Private ReadOnly _trackIndex As Integer
        Private ReadOnly _fxIndex As Integer
        Private Shared ReadOnly Ic As CultureInfo = CultureInfo.InvariantCulture


        ' ── Cache ──────────────────────────────────────────────────────────────
        Private _name As String = String.Empty
        Private _enabled As Boolean = True
        Private _preset As String = String.Empty
        Private _params As New List(Of ReaperFXParam)
        Private _guid As String = String.Empty
        
    
        Private _IsHide As Boolean = False

        ' ── Événements ────────────────────────────────────────────────────────
        Public Event On_EnabledChange(_enabled As Boolean)
        Public Event On_PresetChange(_preset As String)

        Public Sub New(trackIndex As Integer, fxIndex As Integer)
            _trackIndex = trackIndex
            _fxIndex = fxIndex
        End Sub
        ''' <summary>
        ''' GUID unique du FX retourné par <c>TrackFX_GetFXGUID</c>.
        ''' Format : "{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}".
        ''' Chargé lors du premier <see cref="Refresh"/>.
        ''' </summary>
        Public ReadOnly Property Guid As String
            Get
                Return _guid
            End Get
        End Property
        Public ReadOnly Property TrackIndex As Integer
            Get
                Return _trackIndex : End Get
        End Property
        Public ReadOnly Property FxIndex As Integer
            Get
                Return _fxIndex : End Get
        End Property

        ''' <summary>Nom du FX ex: "VST: ReaEQ (Cockos)".</summary>
        Public ReadOnly Property Name As String
            Get
                Return _name : End Get
        End Property

        ''' <summary>FX actif (non bypassé) — setter écrit dans Reaper.</summary>
        Public Property Enabled As Boolean
            Get
                Return _enabled : End Get
            Set
                _enabled = Value
                utility.CallReaperBridge(
                    $"SET_FX_ENABLED|{_trackIndex}|{_fxIndex}|{If(Value, "1", "0")}")
            End Set
        End Property


        ''' <summary>
        ''' Ouvre ou ferme la fenêtre flottante du FX.
        ''' Le setter écrit immédiatement dans Reaper.
        ''' </summary>
        Public ReadOnly Property IsOpen As Boolean
            Get
                Return utility.TrackFX_IsOpen(_trackIndex, _fxIndex)
            End Get
        End Property
                

         ''' <summary>
        ''' Cache ou affiche la fenêtre flottante du FX.
        ''' Le setter écrit immédiatement dans Reaper.
        ''' </summary>
        Public Property IsHide As Boolean
            Get
                Return _IsHide
            End Get
            Set
                utility.TrackFX_Show(_trackIndex, _fxIndex,not Value)
                _IsHide = Value
            End Set
        End Property

        ''' <summary>Preset actuel — setter applique le preset par nom dans Reaper.</summary>
        Public Property Preset As String
            Get
                Return _preset : End Get
            Set
                _preset = Value
                utility.CallReaperBridge($"SET_FX_PRESET|{_trackIndex}|{_fxIndex}|{Value}")
            End Set
        End Property

        ''' <summary>Nombre de paramètres (après Refresh).</summary>
        Public ReadOnly Property ParamCount As Integer
            Get
                Return _params.Count : End Get
        End Property

        ''' <summary>Accès direct à un paramètre par index.</summary>
        Default Public ReadOnly Property Item(paramIndex As Integer) As ReaperFXParam
            Get
                Return _params(paramIndex) : End Get
        End Property

        ''' <summary>Liste en lecture seule des paramètres.</summary>
        Public ReadOnly Property Params As IReadOnlyList(Of ReaperFXParam)
            Get
                Return _params : End Get
        End Property

        ''' <summary>Navigue vers le preset suivant (+1) ou précédent (-1).</summary>
        Public Sub NavigatePreset(move As Integer)
            utility.CallReaperBridge($"NAVIGATE_FX_PRESET|{_trackIndex}|{_fxIndex}|{move}")
            ' ── Recharger le nom du preset après navigation ────────────────────
            Dim raw = utility.CallReaperBridge($"GET_FX_PRESET|{_trackIndex}|{_fxIndex}")
            If Not String.IsNullOrEmpty(raw) AndAlso Not raw.StartsWith("ERROR", StringComparison.Ordinal) Then
                Dim new_preset = raw
                If _preset <> new_preset Then
                    _preset = new_preset
                    RaiseEvent On_PresetChange(_preset)
                End If
            End If
        End Sub

        ''' <summary>
        ''' Charge nom, état actif, nombre de paramètres et preset en 1 appel pipe,
        ''' puis rafraîchit tous les paramètres en 1 appel pipe supplémentaire.
        ''' </summary>
        Public Sub Refresh()
            ' ── GET_FX_ALL_INFO : 1 appel ─────────────────────────────────────
            Dim infoRaw = utility.CallReaperBridge($"GET_FX_ALL_INFO|{_trackIndex}|{_fxIndex}")
            If String.IsNullOrEmpty(infoRaw) OrElse infoRaw.StartsWith("ERROR", StringComparison.Ordinal) Then Return

            Dim info = infoRaw.Split("|"c)
            If info.Length < 4 Then Return

            _name = info(0)

            ' ── GUID du FX ────────────────────────────────────────────────────
            If String.IsNullOrEmpty(_guid) Then
                Dim guidRaw = utility.CallReaperBridge($"GET_FX_GUID|{_trackIndex}|{_fxIndex}")
                If Not String.IsNullOrEmpty(guidRaw) AndAlso Not guidRaw.StartsWith("ERROR", StringComparison.Ordinal) Then
                    _guid = guidRaw
                End If
            End If

            Dim new_enabled = info(1) <> "0"
            If _enabled <> new_enabled Then
                _enabled = new_enabled
                RaiseEvent On_EnabledChange(_enabled)
            End If

            Dim paramCount = CInt(If(Integer.TryParse(info(2), 0), 0, 0))
            Integer.TryParse(info(2), paramCount)

            Dim new_preset = info(3)
            If _preset <> new_preset Then
                _preset = new_preset
                RaiseEvent On_PresetChange(_preset)
            End If

            ' ── Reconstruire la liste si le nombre de params a changé ──────────
            If paramCount <> _params.Count Then
                _params.Clear()
                For i = 0 To paramCount - 1
                    _params.Add(New ReaperFXParam(_trackIndex, _fxIndex, i))
                Next
            End If

            ' ── GET_FX_ALL_Raw PARAMS : 1 appel pour tous les noms + valeurs ───────
            If _params.Count = 0 Then Return
            For Each FxParam In _params
                FxParam.Refresh()
            Next

            'Dim paramsRaw = utility.CallReaperBridge($"GET_FX_ALL_PARAMS|{_trackIndex}|{_fxIndex}")
            'If String.IsNullOrEmpty(paramsRaw) OrElse paramsRaw.StartsWith("ERROR", StringComparison.Ordinal) Then Return

            'Dim p = paramsRaw.Split("|"c)
            'For i = 0 To _params.Count - 1
            '    Dim nameIdx = i * 2
            '    Dim valIdx = i * 2 + 1
            '    If valIdx >= p.Length Then Exit For


            '    _params(i)._name = p(nameIdx)   ' ← accès direct au champ pour éviter un appel pipe
            '    Dim new_val As Double = 0
            '    Double.TryParse(p(valIdx), NumberStyles.Any, Ic, new_val)
            '    If _params(i).RawValue <> new_val Then
            '        _params(i).RawValue = new_val
            '        _params(i).RaiseOn_RawValueChange(new_val)
            '    End If
            'Next
        End Sub

    End Class

    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER TRACK FX — collection de FX d'une piste
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Gère la liste des FX d'une piste Reaper.
    ''' Appeler <see cref="Refresh"/> pour synchroniser la liste et l'état de chaque FX.
    ''' </summary>
    Public Class ReaperTrackFX

        Private ReadOnly _trackIndex As Integer
        Private ReadOnly _fxList As New List(Of ReaperFX)


        ''' <summary>Déclenché quand le nombre de FX sur la piste change.</summary>
        Public Event On_CountChange(count As Integer)

        ''' <param name="trackIndex">Index 0-based de la piste (-1 pour la piste maître).</param>
        Public Sub New(trackIndex As Integer)
            _trackIndex = trackIndex
        End Sub

        Public ReadOnly Property TrackIndex As Integer
            Get
                Return _trackIndex : End Get
        End Property

        ''' <summary>Nombre de FX (après Refresh).</summary>
        Public ReadOnly Property Count As Integer
            Get
                Return _fxList.Count : End Get
        End Property

        ''' <summary>Accès direct à un FX par index.</summary>
        Default Public ReadOnly Property Item(fxIndex As Integer) As ReaperFX
            Get
                Return _fxList(fxIndex) : End Get
        End Property

        ''' <summary>Liste en lecture seule des FX.</summary>
        Public ReadOnly Property FxList As IReadOnlyList(Of ReaperFX)
            Get
                Return _fxList : End Get
        End Property

        ''' <summary>
        ''' Charge le nombre de FX, reconstruit la liste si nécessaire,
        ''' puis rafraîchit chaque FX (nom + paramètres).
        ''' </summary>
        Public Sub Refresh()
            Dim raw = utility.CallReaperBridge($"GET_FX_COUNT|{_trackIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return

            Dim newCount As Integer = 0
            If Not Integer.TryParse(raw, newCount) Then Return

            If newCount <> _fxList.Count Then
                _fxList.Clear()
                For i = 0 To newCount - 1
                    _fxList.Add(New ReaperFX(_trackIndex, i))
                Next
                RaiseEvent On_CountChange(newCount)
            End If

            For Each fx In _fxList
                fx.Refresh()
            Next
        End Sub
        ''' <summary>
        ''' Copie un FX de cette piste vers une autre piste.
        ''' destFxIndex : -1 = ajouter à la fin de la piste destination.
        ''' Retourne True si l'opération a réussi.
        ''' </summary>
        Public Function CopyFXToTrack(srcFxIndex As Integer,
                                      destTrackIndex As Integer,
                                      Optional destFxIndex As Integer = -1) As Boolean
            Dim raw = utility.CallReaperBridge(
                $"COPY_FX|{_trackIndex}|{srcFxIndex}|{destTrackIndex}|{destFxIndex}|0")
            Return raw = "OK"
        End Function

        ''' <summary>
        ''' Déplace un FX de cette piste vers une autre piste (supprime de la source).
        ''' destFxIndex : -1 = ajouter à la fin de la piste destination.
        ''' Retourne True si l'opération a réussi.
        ''' </summary>
        Public Function MoveFXToTrack(srcFxIndex As Integer,
                                      destTrackIndex As Integer,
                                      Optional destFxIndex As Integer = -1) As Boolean
            Dim raw = utility.CallReaperBridge(
                $"COPY_FX|{_trackIndex}|{srcFxIndex}|{destTrackIndex}|{destFxIndex}|1")
            If raw <> "OK" Then Return False
            ' ── Mettre à jour la liste locale après déplacement ────────────────
            If srcFxIndex >= 0 AndAlso srcFxIndex < _fxList.Count Then
                _fxList.RemoveAt(srcFxIndex)
                For i = srcFxIndex To _fxList.Count - 1
                    _fxList(i) = New ReaperFX(_trackIndex, i)
                Next
                RaiseEvent On_CountChange(_fxList.Count)
            End If
            Return True
        End Function
        ''' <summary>
        ''' Supprime le FX à l'index donné et met à jour la liste locale.
        ''' Retourne True si la suppression a réussi.
        ''' </summary>
        Public Function DeleteFX(fxIndex As Integer) As Boolean
            If fxIndex < 0 OrElse fxIndex >= _fxList.Count Then Return False
            Dim raw = utility.CallReaperBridge($"DELETE_FX|{_trackIndex}|{fxIndex}")
            If raw <> "OK" Then Return False
            _fxList.RemoveAt(fxIndex)
            ' ── Corriger les indices des FX suivants ───────────────────────────
            For i = fxIndex To _fxList.Count - 1
                _fxList(i) = New ReaperFX(_trackIndex, i)
            Next
            RaiseEvent On_CountChange(_fxList.Count)
            Return True
        End Function

    End Class

    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER INSTALLED FX — FX installé dans Reaper
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Représente un FX installé dans Reaper.
    ''' <br/>
    ''' <see cref="FullName"/> retourne l'identifiant complet à passer à
    ''' <c>TrackFX_AddByName</c> ex: "VST3: FabFilter Pro-Q 3 (FabFilter)".
    ''' </summary>
    Public Structure InstalledFXInfo

        ''' <summary>
        ''' Type du plugin extrait de l'identifiant : "VST" "VST3" "JS" "CLAP" "AU" "DX"…
        ''' Vide si l'ident ne contient pas de préfixe "Type: ".
        ''' </summary>
        Public ReadOnly FxType As String

        ''' <summary>
        ''' Nom affiché tel que retourné par Reaper ex: "FabFilter Pro-Q 3 (FabFilter)".
        ''' </summary>
        Public ReadOnly Name As String

        ''' <summary>
        ''' Identifiant complet retourné par <c>EnumInstalledFX</c>
        ''' ex: "VST3: FabFilter Pro-Q 3 (FabFilter)".
        ''' C'est la valeur à passer à <c>TrackFX_AddByName</c>.
        ''' </summary>
        Public ReadOnly Ident As String

        Public Sub New(name As String, ident As String)
            Me.Name = name
            Me.Ident = ident
            ' ── Extraire le type depuis l'ident : "VST3: ..." → "VST3" ──────
            Dim colonIdx = ident.IndexOf(": ", StringComparison.Ordinal)
            If colonIdx > 0 Then
                Me.FxType = ident.Substring(0, colonIdx)
            Else
                Me.FxType = String.Empty
            End If
        End Sub

        ''' <summary>
        ''' Identifiant complet compatible <c>TrackFX_AddByName</c>
        ''' ex: "VST3: FabFilter Pro-Q 3 (FabFilter)".
        ''' </summary>
        Public ReadOnly Property FullName As String
            Get
                Return Ident
            End Get
        End Property

    End Structure
#End Region
#Region "AudioIn/Out"
    ' ══════════════════════════════════════════════════════════════════════════
    '  AUDIO CHANNEL INFO — canal d'entrée ou de sortie audio
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>Représente un canal audio (entrée ou sortie) de l'interface audio.</summary>
    Public Structure AudioChannelInfo
        ''' <summary>Index 0-based du canal.</summary>
        Public ReadOnly Index As Integer
        ''' <summary>Nom du canal tel que retourné par Reaper.</summary>
        Public ReadOnly Name As String

        Public Sub New(index As Integer, name As String)
            Me.Index = index
            Me.Name = name
        End Sub
    End Structure

    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER INPUT CHANNEL — canal d'entrée audio avec encodage I_RECINPUT
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Représente une entrée audio disponible dans Reaper avec sa valeur
    ''' <c>I_RECINPUT</c> précalculée.
    ''' <br/>
    ''' Encodage <c>I_RECINPUT</c> :
    ''' <list type="table">
    ''' <item><term>-1</term><description>Aucune entrée</description></item>
    ''' <item><term>N (0-based)</term><description>Entrée mono canal N</description></item>
    ''' <item><term>1024 + N</term><description>Paire stéréo, canal gauche N (droite = N+1)</description></item>
    ''' </list>
    ''' </summary>
    Public Structure ReaperInputChannel

        ''' <summary>Valeur à écrire dans <c>I_RECINPUT</c> de la piste.</summary>
        Public ReadOnly RecInputValue As Integer

        ''' <summary>
        ''' Nom affiché sur la surface.
        ''' Exemples : "-- Aucune --"  "1: Mic L"  "1/2: Mic L/Mic R"
        ''' </summary>
        Public ReadOnly Name As String

        ''' <summary>True si c'est une paire stéréo.</summary>
        Public ReadOnly IsStereo As Boolean

        ''' <summary>
        ''' Index 0-based du canal gauche (ou seul canal pour mono).
        ''' -1 si <see cref="IsNone"/>.
        ''' </summary>
        Public ReadOnly LeftChannel As Integer

        Public Sub New(recInputValue As Integer,
                       name As String,
                       isStereo As Boolean,
                       leftChannel As Integer)
            Me.RecInputValue = recInputValue
            Me.Name = name
            Me.IsStereo = isStereo
            Me.LeftChannel = leftChannel
        End Sub

        ''' <summary>
        ''' Index 0-based du canal droit pour une entrée stéréo.
        ''' Retourne -1 pour mono ou "aucune entrée".
        ''' </summary>
        Public ReadOnly Property RightChannel As Integer
            Get
                If Not IsStereo OrElse LeftChannel < 0 Then Return -1
                Return LeftChannel + 1
            End Get
        End Property

        ''' <summary>True si cette entrée représente l'absence d'entrée.</summary>
        Public ReadOnly Property IsNone As Boolean
            Get
                Return RecInputValue = -1
            End Get
        End Property

        ''' <summary>Numéro 1-based du canal gauche (affichage utilisateur).</summary>
        Public ReadOnly Property LeftChannelDisplay As Integer
            Get
                If LeftChannel < 0 Then Return 0
                Return LeftChannel + 1
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return Name
        End Function

    End Structure

    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER OUTPUT CHANNEL — canal de sortie audio hardware
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Représente une sortie audio hardware disponible dans Reaper.
    ''' Utilisée pour configurer les envois matériels (<see cref="SendCategory.Hardware"/>).
    ''' <br/>
    ''' Encodage :
    ''' <list type="table">
    ''' <item><term><c>I_DSTCHAN</c></term><description>Index 0-based du canal hardware destination (gauche si stéréo).</description></item>
    ''' <item><term><c>I_SRCCHAN</c></term><description>Canal source de la piste OR-é avec <see cref="SrcChanFlag"/> (0=mono, 1024=stéréo).</description></item>
    ''' </list>
    ''' </summary>
    Public Structure ReaperOutputChannel

        ''' <summary>Valeur à écrire dans <c>I_DSTCHAN</c> du hardware send.</summary>
        Public ReadOnly DstChan As Integer

        ''' <summary>
        ''' Flag à OR-er dans <c>I_SRCCHAN</c> : 0 = mono, 1024 = stéréo.
        ''' </summary>
        Public ReadOnly SrcChanFlag As Integer

        ''' <summary>
        ''' Nom affiché sur la surface.
        ''' Exemples : "1: Output 1"  "1/2: Out L/Out R"
        ''' </summary>
        Public ReadOnly Name As String

        ''' <summary>True si c'est une paire stéréo.</summary>
        Public ReadOnly IsStereo As Boolean

        ''' <summary>Index 0-based du canal gauche (ou seul canal pour mono).</summary>
        Public ReadOnly LeftChannel As Integer

        Public Sub New(dstChan As Integer,
                       srcChanFlag As Integer,
                       name As String,
                       isStereo As Boolean,
                       leftChannel As Integer)
            Me.DstChan = dstChan
            Me.SrcChanFlag = srcChanFlag
            Me.Name = name
            Me.IsStereo = isStereo
            Me.LeftChannel = leftChannel
        End Sub

        ''' <summary>Index 0-based du canal droit. -1 si mono.</summary>
        Public ReadOnly Property RightChannel As Integer
            Get
                If Not IsStereo OrElse LeftChannel < 0 Then Return -1
                Return LeftChannel + 1
            End Get
        End Property

        ''' <summary>Numéro 1-based du canal gauche (affichage utilisateur).</summary>
        Public ReadOnly Property LeftChannelDisplay As Integer
            Get
                Return LeftChannel + 1
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return Name
        End Function

    End Structure


#End Region

#Region "Time"

    ' ══════════════════════════════════════════════════════════════════════════
    '  MODES DE FORMATAGE TEMPOREL
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>Format d'affichage d'une position temporelle (format_timepos2).</summary>
    Public Enum TimePositionMode As Integer
        ''' <summary>H:MM:SS.sss</summary>
        Time = 0
        ''' <summary>Mesure.Temps.Centièmes  ex: "3.2.40"</summary>
        MeasuresBeats = 1
        ''' <summary>MM:SS</summary>
        MinutesSeconds = 2
        ''' <summary>Samples</summary>
        Samples = 3
        ''' <summary>H:MM:SS:FF (SMPTE)</summary>
        SMPTEFrames = 4
        ''' <summary>Secondes uniquement</summary>
        Seconds = 5
    End Enum

    ' ══════════════════════════════════════════════════════════════════════════
    '  CURSOR INFO — état complet du curseur d'édition
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Regroupe la position du curseur d'édition et toutes ses conversions.
    ''' Chargé par <see cref="utility.GetCursorInfo"/> en un seul appel pipe.
    ''' </summary>
    Public Structure CursorInfo
        ''' <summary>Position en secondes.</summary>
        Public ReadOnly Time As Double
        ''' <summary>Position en Quarter Notes depuis le début du projet.</summary>
        Public ReadOnly QN As Double
        ''' <summary>Numéro de mesure 0-based.</summary>
        Public ReadOnly Measures As Integer
        ''' <summary>Beat entier dans la mesure (0-based).</summary>
        Public ReadOnly Cml As Integer
        ''' <summary>Beats complets depuis le début du projet.</summary>
        Public ReadOnly FullBeats As Double
        ''' <summary>Dénominateur de la signature en vigueur (4=noire 8=croche…).</summary>
        Public ReadOnly CDenom As Integer
        ''' <summary>Position fine dans le beat courant [0..1[.</summary>
        Public ReadOnly BeatPos As Double
        ''' <summary>Numérateur de la signature en vigueur (ex: 4 pour 4/4).</summary>
        Public ReadOnly TimeSigNum As Integer
        ''' <summary>Dénominateur de la signature en vigueur (ex: 4 pour 4/4).</summary>
        Public ReadOnly TimeSigDen As Integer
        ''' <summary>Tempo en BPM à la position courante.</summary>
        Public ReadOnly BPM As Double
        ''' <summary>Affichage M.B.H lisible pour la surface de contrôle (ex: "3.2.40").</summary>
        Public ReadOnly Formatted As String

        ''' <summary>Numéro de mesure 1-based (affiché dans Reaper).</summary>
        Public ReadOnly Property MeasureDisplay As Integer
            Get
                Return Measures + 1 : End Get
        End Property

        ''' <summary>Beat courant dans la mesure 1-based.</summary>
        Public ReadOnly Property BeatDisplay As Integer
            Get
                Return Cml + 1 : End Get
        End Property

        Public Sub New(time As Double, qn As Double,
                       measures As Integer, cml As Integer,
                       fullBeats As Double, cdenom As Integer, beatPos As Double,
                       timeSigNum As Integer, timeSigDen As Integer,
                       bpm As Double, formatted As String)
            Me.Time = time : Me.QN = qn
            Me.Measures = measures : Me.Cml = cml
            Me.FullBeats = fullBeats : Me.CDenom = cdenom : Me.BeatPos = beatPos
            Me.TimeSigNum = timeSigNum : Me.TimeSigDen = timeSigDen
            Me.BPM = bpm : Me.Formatted = formatted
        End Sub
    End Structure
    ' ══════════════════════════════════════════════════════════════════════════
    '  FLAGS DE BLOC D'ANNULATION
    ' ══════════════════════════════════════════════════════════════════════════

#End Region
#Region "Project"


    ''' <summary>Combinaison de flags pour <see cref="utility.EndUndoBlock"/>.</summary>
    <Flags>
    Public Enum UndoFlags As Integer
        Tracks = 1   ' modifications de pistes
        Items = 4   ' modifications d'items
        FX = 8   ' modifications de FX
        All = -1
    End Enum

    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER PROJECT INFO — état complet du projet actif
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>Regroupe les informations du projet actif chargées en un seul appel pipe.</summary>
    Public Structure ReaperProjectInfo
        ''' <summary>Nom du projet sans extension  ex: "MySession".</summary>
        Public ReadOnly Name As String
        ''' <summary>Chemin complet du fichier .rpp  (vide si jamais sauvegardé).</summary>
        Public ReadOnly FilePath As String
        ''' <summary>True si le projet a des modifications non sauvegardées.</summary>
        Public ReadOnly IsDirty As Boolean
        ''' <summary>Compteur de changements — s'incrémente à chaque modification.</summary>
        Public ReadOnly StateChangeCount As Integer
        ''' <summary>Nombre de pistes (sans la piste maître).</summary>
        Public ReadOnly TrackCount As Integer
        ''' <summary>Durée totale du projet en secondes.</summary>
        Public ReadOnly LengthSeconds As Double

        ''' <summary>Dossier du projet dérivé de <see cref="FilePath"/>.</summary>
        Public ReadOnly Property Folder As String
            Get
                If String.IsNullOrEmpty(FilePath) Then Return String.Empty
                Return IO.Path.GetDirectoryName(FilePath)
            End Get
        End Property

        ''' <summary>True si le projet n'a jamais été sauvegardé (pas de fichier .rpp).</summary>
        Public ReadOnly Property IsUnsaved As Boolean
            Get
                Return String.IsNullOrEmpty(FilePath) : End Get
        End Property

        Public Sub New(name As String, filePath As String, isDirty As Boolean,
                       stateChangeCount As Integer, trackCount As Integer,
                       lengthSeconds As Double)
            Me.Name = name
            Me.FilePath = filePath
            Me.IsDirty = isDirty
            Me.StateChangeCount = stateChangeCount
            Me.TrackCount = trackCount
            Me.LengthSeconds = lengthSeconds
        End Sub
    End Structure


    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER PROJECT — projet actif avec pistes et FX
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Représente le projet Reaper actif avec toutes ses pistes et leurs FX.
    ''' Appeler <see cref="Refresh"/> pour charger l'état complet depuis Reaper,
    ''' ou <see cref="RefreshInfo"/> / <see cref="RefreshTracks"/> / <see cref="RefreshAllFX"/>
    ''' pour un rafraîchissement ciblé.
    ''' </summary>
    Public Class ReaperProject
        Implements IDisposable

        Private Shared ReadOnly Ic As CultureInfo = CultureInfo.InvariantCulture

        ' ── Verrou ────────────────────────────────────────────────────────────
        Private ReadOnly _trackLock As New Object()

        ' ── Cache projet ──────────────────────────────────────────────────────
        Private _name As String = String.Empty
        Private _filePath As String = String.Empty
        Private _isDirty As Boolean = False
        Private _stateChangeCount As Integer = -1
        Private _lengthSeconds As Double = 0
        Private First_Time = True

        ' ── Monitor ───────────────────────────────────────────────────────────

        Private _disposed As Boolean = False

        ' ── Collections ───────────────────────────────────────────────────────
        Private ReadOnly _tracks As New List(Of ReaperTrack)
        Private ReadOnly _trackFXList As New List(Of ReaperTrackFX)
        Private ReadOnly _trackGuids As New List(Of String)
        Private _MasterTrackFx As ReaperTrackFX
        Private _MasterTrack As ReaperTrack

        ' ── Événements ────────────────────────────────────────────────────────

        ''' <summary>Déclenché quand le nombre de pistes change.</summary>
        Public Event On_TrackCountChange(count As Integer)
        ''' <summary>Déclenché quand l'état sauvegardé/non sauvegardé change.</summary>
        Public Event On_DirtyChange(isDirty As Boolean)
        ''' <summary>Déclenché à chaque modification du projet (compteur Reaper).</summary>
        Public Event On_StateChange(stateCount As Integer)
        ''' <summary>Déclenché quand le nom du projet change.</summary>
        Public Event On_NameChange(name As String)
        ''' <summary>Déclenché quand la durée totale du projet change.</summary>
        Public Event On_LengthChange(lengthSeconds As Double)
        ''' <summary>
        ''' Déclenché quand la liste des GUIDs de pistes change.
        ''' Couvre les cas de changement de nombre, d'insertion, de suppression
        ''' ou de remplacement d'une piste à un index donné.
        ''' </summary>
        Public Event On_TrackGuidsChanged(guids As IReadOnlyList(Of String))

        ' ── Constructeur ──────────────────────────────────────────────────────

        ''' <summary>
        ''' Initialise le projet, démarre le <see cref="Monitors.ReaperProjectMonitor"/>
        ''' et effectue un premier chargement complet.
        ''' </summary>
        Public Sub New()
            ' ── Chargement initial synchrone AVANT le démarrage du monitor ───
            ' Empêche OnTrackCountChanged de s'intercaler pendant RefreshTracks.
            RefreshTracks(GetCountTrack())
            RefreshInfo()


        End Sub

        ' ── Propriétés projet ─────────────────────────────────────────────────

        ''' <summary>Nom du projet sans extension  ex: "MySession".</summary>
        Public ReadOnly Property Name As String
            Get
                Return _name
            End Get
        End Property

        ''' <summary>Chemin complet du fichier .rpp  (vide si jamais sauvegardé).</summary>
        Public ReadOnly Property FilePath As String
            Get
                Return _filePath
            End Get
        End Property

        ''' <summary>Dossier du projet dérivé de <see cref="FilePath"/> (vide si jamais sauvegardé).</summary>
        Public ReadOnly Property Folder As String
            Get
                If String.IsNullOrEmpty(_filePath) Then Return String.Empty
                Return IO.Path.GetDirectoryName(_filePath)
            End Get
        End Property

        ''' <summary>True si le projet a des modifications non sauvegardées.</summary>
        Public ReadOnly Property IsDirty As Boolean
            Get
                Return _isDirty
            End Get
        End Property

        ''' <summary>Compteur de changements d'état (incrémenté par Reaper à chaque modification).</summary>
        Public ReadOnly Property StateChangeCount As Integer
            Get
                Return _stateChangeCount
            End Get
        End Property

        ''' <summary>Durée totale du projet en secondes.</summary>
        Public ReadOnly Property LengthSeconds As Double
            Get
                Return _lengthSeconds
            End Get
        End Property

        ''' <summary>True si le projet n'a jamais été sauvegardé (aucun fichier .rpp).</summary>
        Public ReadOnly Property IsUnsaved As Boolean
            Get
                Return String.IsNullOrEmpty(_filePath)
            End Get
        End Property

        ' ── Propriétés pistes ─────────────────────────────────────────────────

        ''' <summary>Nombre de pistes (sans la piste maître).</summary>
        Public ReadOnly Property TrackCount As Integer
            Get
                Return _tracks.Count
            End Get
        End Property

        ''' <summary>Accès direct à une piste par index 0-based.</summary>
        Default Public ReadOnly Property Item(trackIndex As Integer) As ReaperTrack
            Get
                Return _tracks(trackIndex)
            End Get
        End Property

        ''' <summary>Liste en lecture seule des pistes.</summary>
        Public ReadOnly Property Tracks As IReadOnlyList(Of ReaperTrack)
            Get
                Return _tracks
            End Get
        End Property

        ''' <summary>
        ''' GUIDs des pistes dans l'ordre de leur index 0-based, mis à jour par
        ''' <see cref="RefreshTracks"/>.
        ''' </summary>
        Public ReadOnly Property TrackGuids As IReadOnlyList(Of String)
            Get
                Return _trackGuids
            End Get
        End Property

        ''' <summary>
        ''' Liste en lecture seule des collections FX.
        ''' L'index correspond à l'index de la piste associée.
        ''' </summary>
        Public ReadOnly Property TrackFXList As IReadOnlyList(Of ReaperTrackFX)
            Get
                Return _trackFXList
            End Get
        End Property
        ''' <summary>
        ''' Renvoi la master track du projet
        ''' </summary>
        ''' <returns></returns>
        Public ReadOnly Property MasterTrack As ReaperTrack
            Get
                If _MasterTrack Is Nothing Then
                    _MasterTrack = New ReaperTrack(-1)

                End If
                Return _MasterTrack
            End Get
        End Property
        Public ReadOnly Property MasterTrackFx As ReaperTrackFX
            Get
                If _MasterTrackFx Is Nothing Then
                    _MasterTrackFx = New ReaperTrackFX(-1)
                End If
                Return _MasterTrackFx
            End Get
        End Property
        ' ── Méthodes d'accès ──────────────────────────────────────────────────

        ''' <summary>
        ''' Retourne le <see cref="ReaperTrackFX"/> associé à la piste à l'index donné.
        ''' Retourne Nothing si l'index est hors limite.
        ''' </summary>
        Public Function GetTrackFX(trackIndex As Integer) As ReaperTrackFX
            If trackIndex < 0 OrElse trackIndex >= _trackFXList.Count Then Return Nothing
            Return _trackFXList(trackIndex)
        End Function


        ''' <summary>
        ''' Retourne l'index 0-based de la piste dont le GUID correspond à
        ''' <paramref name="guid"/>, ou -1 si introuvable.
        ''' </summary>
        Public Function FindTrackByGuid(guid As String) As Integer
            For i = 0 To _trackGuids.Count - 1
                If _trackGuids(i) = guid Then Return i
            Next
            Return -1
        End Function

        ' ── Chargement ────────────────────────────────────────────────────────

        ''' <summary>
        ''' Handler interne câblé sur <see cref="Monitors.ReaperProjectMonitor.On_TrackCountChanged"/>.
        ''' </summary>
        Private Sub OnTrackCountChanged(count As Integer)
            RefreshTracks(count)
        End Sub

        ''' <summary>
        ''' Charge uniquement les infos du projet (nom, chemin, dirty, stateCount, durée).
        ''' </summary>
        Public Sub RefreshInfo()
            Dim raw = utility.CallReaperBridge("GET_PROJECT_INFO")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR", StringComparison.Ordinal) Then Return
            Dim p = raw.Split("|"c)
            If p.Length < 6 Then Return

            Dim new_name = p(0)
            If _name <> new_name Then
                _name = new_name
                RaiseEvent On_NameChange(_name)
            End If

            _filePath = p(1)

            Dim new_isDirty = p(2) = "1"
            If _isDirty <> new_isDirty Then
                _isDirty = new_isDirty
                RaiseEvent On_DirtyChange(_isDirty)
            End If

            Dim new_stateChangeCount As Integer = 0
            Integer.TryParse(p(3), new_stateChangeCount)
            If _stateChangeCount <> new_stateChangeCount Then
                _stateChangeCount = new_stateChangeCount
                RaiseEvent On_StateChange(_stateChangeCount)
            End If

            Dim new_lengthSeconds As Double = 0
            Double.TryParse(p(5), NumberStyles.Any, Ic, new_lengthSeconds)
            If _lengthSeconds <> new_lengthSeconds Then
                _lengthSeconds = new_lengthSeconds
                RaiseEvent On_LengthChange(_lengthSeconds)
            End If
        End Sub

        ''' <summary>
        ''' Interroge Reaper pour le nombre de pistes actuel.
        ''' Retourne 0 si le bridge est indisponible.
        ''' </summary>
        Private Function GetCountTrack() As Integer
            Dim countRaw = utility.CallReaperBridge("COUNT_TRACKS")
            If String.IsNullOrEmpty(countRaw) OrElse countRaw.StartsWith("ERROR", StringComparison.Ordinal) Then Return 0
            Dim n As Integer = 0
            Integer.TryParse(countRaw, n)
            Return n
        End Function

        ''' <summary>
        ''' Charge le nombre de pistes, reconstruit les listes si nécessaire,
        ''' met à jour les GUIDs puis rafraîchit chaque piste.
        ''' <list type="bullet">
        ''' <item>Déclenche <see cref="On_TrackCountChange"/> si le nombre change.</item>
        ''' <item>Déclenche <see cref="On_TrackGuidsChanged"/> si au moins un GUID diffère.</item>
        ''' </list>
        ''' </summary>
        Public Sub RefreshTracks(newCount As Integer)
            Dim snapshot As List(Of ReaperTrack)

            SyncLock _trackLock
                ' ── Reconstruction si le nombre de pistes a changé ────────────
                If newCount <> _tracks.Count Then
                    _tracks.Clear()
                    _trackFXList.Clear()
                    For i = 0 To newCount - 1
                        _tracks.Add(New ReaperTrack(i))
                        _trackFXList.Add(New ReaperTrackFX(i))
                    Next
                    RaiseEvent On_TrackCountChange(newCount)
                End If

                ' ── Mise à jour des GUIDs ─────────────────────────────────────
                Dim newGuids As New List(Of String)
                For i = 0 To newCount - 1
                    newGuids.Add(utility.GetTrackGUID(i))
                Next

                Dim guidsChanged = newGuids.Count <> _trackGuids.Count
                If Not guidsChanged Then
                    For i = 0 To newGuids.Count - 1
                        If newGuids(i) <> _trackGuids(i) Then
                            guidsChanged = True
                            Exit For
                        End If
                    Next
                End If

                If guidsChanged Then
                    _trackGuids.Clear()
                    _trackGuids.AddRange(newGuids)
                    If First_Time = True Then
                        First_Time = False
                    Else
                        RaiseEvent On_TrackGuidsChanged(_trackGuids)
                    End If



                End If

                ' ── Snapshot pour itérer hors lock ────────────────────────────
                snapshot = _tracks.ToList()
            End SyncLock

            ' ── Rafraîchissement des pistes HORS lock ─────────────────────────
            ' Évite le deadlock si Refresh() déclenche OnTrackCountChanged.
            For i = 0 To snapshot.Count - 1
                snapshot(i).Refresh()
            Next
        End Sub

        ''' <summary>Rafraîchit les FX de toutes les pistes.</summary>
        Public Sub RefreshAllFX()
            For Each fx In _trackFXList
                fx.Refresh()
            Next
        End Sub

        ''' <summary>Rafraîchit uniquement les FX de la piste à l'index donné.</summary>
        Public Sub RefreshTrackFX(trackIndex As Integer)
            If trackIndex < 0 OrElse trackIndex >= _trackFXList.Count Then Return
            _trackFXList(trackIndex).Refresh()
        End Sub

        ' ── Détection de changement ────────────────────────────────────────────

        Public Function HasChanged() As Boolean
            Dim current = utility.GetProjectStateChangeCount()
            If current = -1 OrElse current = _stateChangeCount Then Return False
            _stateChangeCount = current
            RaiseEvent On_StateChange(_stateChangeCount)
            Return True
        End Function

        ' ── Sauvegarde ────────────────────────────────────────────────────────

        Public Function Save() As Boolean
            Return utility.SaveProject()
        End Function

        Public Function SaveAs() As Boolean
            Return utility.SaveProjectAs()
        End Function

        ' ── Undo / Redo ───────────────────────────────────────────────────────

        Public Sub WithUndo(description As String, flags As UndoFlags, action As Action)
            utility.BeginUndoBlock()
            action()
            utility.EndUndoBlock(description, flags)
        End Sub

        Public Function Undo() As Boolean
            Return utility.Undo()
        End Function

        Public Function Redo() As Boolean
            Return utility.Redo()
        End Function

        Public Function CanUndo() As String
            Return utility.CanUndo()
        End Function

        Public Function CanRedo() As String
            Return utility.CanRedo()
        End Function




        ' ── IDisposable ───────────────────────────────────────────────────────

        ''' <summary>
        ''' Libère le <see cref="Monitors.ReaperProjectMonitor"/> :
        ''' désabonne le handler, arrête le thread de polling et ferme le pipe.
        ''' </summary>
        Protected Overridable Sub Dispose(disposing As Boolean)
            If Not _disposed Then
                If disposing Then

                End If
                _disposed = True
            End If
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Dispose(disposing:=True)
            GC.SuppressFinalize(Me)
        End Sub

    End Class


#End Region
#Region "Session"

    ' ══════════════════════════════════════════════════════════════════════════
    '  REAPER SESSION — surveillance de l'index du projet actif
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Surveille le projet Reaper actif et expose son index dans la liste
    ''' des projets ouverts.
    ''' <br/>
    ''' Délègue la détection à <see cref="Monitors.ReaperSessionMonitor"/>.
    ''' </summary>
    ''' <remarks>
    ''' Cycle de vie :
    ''' <code>
    '''   Dim session As New ReaperSession()
    '''   session.Start()
    '''   ...
    '''   session.Dispose()
    ''' </code>
    ''' </remarks>
    Public Class ReaperSession
        Implements IDisposable

        Private ReadOnly _monitor As New Monitors.ReaperSessionMonitor()
        Private ReadOnly _config As New ReaperSessionConfig()
        Private _LastClickedTrackIndex As Integer
        Private _disposed As Boolean = False
        Private _activeProjectIndex As Integer = -1
        Private _openProjects As IReadOnlyList(Of String) = Array.Empty(Of String)()

        Dim _ReaperMouseClickMonitor As Monitors.ReaperMouseClickMonitor

        ' ── Projet actif ──────────────────────────────────────────────────────
        Private _project As ReaperProject = Nothing

        ' ── Cache des projets ouverts : clé = chemin fichier .rpp ─────────────
        ' Les projets sont conservés entre les changements d'onglet.
        ' Un projet sans chemin (jamais sauvegardé) est stocké sous la clé "".
        Private ReadOnly _projectCache As New Dictionary(Of String, ReaperProject)()

        ' ── Événements ────────────────────────────────────────────────────────

        Public Event On_ActiveProjectChanged(projectIndex As Integer, filePath As String)
        Public Event On_OpenProjectsChanged(projects As IReadOnlyList(Of String))

        ''' <summary>
        ''' Déclenché quand le projet actif dans Reaper change (changement d'onglet).
        ''' <br/>
        ''' <paramref name="project"/> est l'instance <see cref="ReaperProject"/>
        ''' correspondante — réutilisée depuis le cache si elle existe déjà,
        ''' créée et synchronisée sinon.
        ''' </summary>
        Public Event On_ProjectChanged(project As ReaperProject)
        Public Event On_ReaperGUIClick(TrackIndex As Integer)

        Public Sub New()
            AddHandler _monitor.On_ActiveProjectChanged, AddressOf OnActiveProjectChanged
            AddHandler _monitor.On_OpenProjectsChanged, AddressOf OnOpenProjectsChanged

            _ReaperMouseClickMonitor = New Monitors.ReaperMouseClickMonitor()
            AddHandler _ReaperMouseClickMonitor.On_MouseClick,
                Sub(x As Integer, y As Integer)
                    _LastClickedTrackIndex = _ReaperMouseClickMonitor.LastClickedTrackIndex
                    RefreshTrackInfos()
                End Sub
            AddHandler ReaperPollHub.Instance.On_ProjectStateChange, AddressOf RefreshTrackInfos
        End Sub

        ' ── Propriétés ────────────────────────────────────────────────────────

        ''' <summary>Index 0-based de la dernière piste cliquée dans Reaper.</summary>
        Public ReadOnly Property LastClickedTrackIndex As Integer
            Get
                Return _LastClickedTrackIndex
            End Get
        End Property

        ''' <summary>
        ''' Projet Reaper actif.
        ''' Peut être Nothing si Reaper n'est pas encore joignable.
        ''' </summary>
        Public ReadOnly Property Project As ReaperProject
            Get
                Return _project
            End Get
        End Property

        ''' <summary>
        ''' Tous les projets actuellement en cache (ouverts au moins une fois).
        ''' Clé = chemin .rpp (vide si jamais sauvegardé).
        ''' </summary>
        Public ReadOnly Property Projects As IReadOnlyDictionary(Of String, ReaperProject)
            Get
                Return _projectCache
            End Get
        End Property

        Public ReadOnly Property ActiveProjectIndex As Integer
            Get
                Return _activeProjectIndex
            End Get
        End Property

        Public ReadOnly Property ActiveProjectFilePath As String
            Get
                Return _monitor.CurrentFilePath
            End Get
        End Property

        Public ReadOnly Property OpenProjects As IReadOnlyList(Of String)
            Get
                Return _openProjects
            End Get
        End Property

        Public ReadOnly Property Config As ReaperSessionConfig
            Get
                Return _config
            End Get
        End Property

        Public ReadOnly Property IsRunning As Boolean
            Get
                Return _monitor.IsRunning
            End Get
        End Property

        Public Property Interval As Integer
            Get
                Return _monitor.Interval
            End Get
            Set
                _monitor.Interval = Value
            End Set
        End Property

        ' ── Cycle de vie ──────────────────────────────────────────────────────

        Public Sub Start()
            If IsRunning Then Return
            _monitor.Start()
            _openProjects = utility.EnumAllProjects()
            _activeProjectIndex = FindProjectIndex(_monitor.CurrentFilePath)
            _config.RefreshPlugins()
            _ReaperMouseClickMonitor.Start()

            ' ── Activer le projet courant (depuis le cache ou en le créant) ───
            ActivateProject(_monitor.CurrentFilePath)
        End Sub

        Public Sub [Stop]()
            _monitor.Stop()
            _ReaperMouseClickMonitor.Stop()
        End Sub

        ' ── Gestion du cache de projets ───────────────────────────────────────

        ''' <summary>
        ''' Active le projet correspondant à <paramref name="filePath"/>.
        ''' S'il est déjà dans le cache, il est réutilisé et <see cref="ReaperProject.RefreshTracks"/>
        ''' est appelé pour le resynchroniser avec Reaper.
        ''' Sinon un nouveau <see cref="ReaperProject"/> est créé et mis en cache.
        ''' Déclenche <see cref="On_ProjectChanged"/> dans les deux cas.
        ''' </summary>
        Private Sub ActivateProject(filePath As String)
            Dim key = If(filePath, String.Empty)



            Dim proj As ReaperProject = Nothing
            If _projectCache.TryGetValue(key, proj) = False Then

                ' ── Nouveau projet : créer et mettre en cache ─────────────────
                proj = New ReaperProject()
                _projectCache(key) = proj
                Dim allFx As New List(Of ReaperTrackFX)
                allFx.AddRange(proj.TrackFXList)
                allFx.Add(proj.MasterTrackFx)
                _config.GetAllFxParams(allFx)
            End If

            _project = proj
            RaiseEvent On_ProjectChanged(_project)
        End Sub

        ''' <summary>
        ''' Retire du cache les projets dont le chemin n'est plus dans
        ''' <paramref name="openFilePaths"/> et les dispose.
        ''' Appelé quand la liste des projets ouverts change.
        ''' </summary>
        Private Sub PurgeClosedProjects(openFilePaths As IReadOnlyList(Of String))
            Dim toRemove As New List(Of String)()
            For Each key In _projectCache.Keys
                Dim stillOpen = False
                For Each path In openFilePaths
                    If path = key Then
                        stillOpen = True
                        Exit For
                    End If
                Next
                If Not stillOpen Then
                    toRemove.Add(key)
                End If
            Next
            For Each key In toRemove
                Dim proj As ReaperProject = Nothing
                If _projectCache.TryGetValue(key, proj) Then
                    ' Ne pas disposer le projet actif — il sera réactivé si besoin
                    If Not Object.ReferenceEquals(proj, _project) Then
                        proj.Dispose()
                    End If
                End If
                _projectCache.Remove(key)
            Next
        End Sub

        ' ── Handlers internes ─────────────────────────────────────────────────

        Private Sub OnActiveProjectChanged(newFilePath As String, previousFilePath As String)
            _activeProjectIndex = FindProjectIndex(newFilePath)
            RaiseEvent On_ActiveProjectChanged(_activeProjectIndex, newFilePath)
            ActivateProject(newFilePath)
        End Sub

        Private Sub OnOpenProjectsChanged(projects As IReadOnlyList(Of String))
            _openProjects = projects
            _activeProjectIndex = FindProjectIndex(_monitor.CurrentFilePath)
            PurgeClosedProjects(projects)
            RaiseEvent On_OpenProjectsChanged(projects)
        End Sub

        Private Function FindProjectIndex(filePath As String) As Integer
            If String.IsNullOrEmpty(filePath) Then Return -1
            For i = 0 To _openProjects.Count - 1
                If _openProjects(i) = filePath Then Return i
            Next
            Return -1
        End Function

        ' ── Maj de la track modifiée du projet automatiquement ────────────────

        Private Sub RefreshTrackInfos()
            If _project IsNot Nothing Then
                If _LastClickedTrackIndex > 0 AndAlso
                   _LastClickedTrackIndex <= _project.Tracks.Count - 1 Then
                    Dim ReaperTrk = _project.Tracks(_LastClickedTrackIndex)
                    ReaperTrk.Refresh()
                End If
            End If
        End Sub

        ' ── IDisposable ───────────────────────────────────────────────────────

        Public Sub Dispose() Implements IDisposable.Dispose
            If Not _disposed Then
                _monitor.Dispose()
                _ReaperMouseClickMonitor?.Dispose()
                ' ── Disposer tous les projets du cache ────────────────────────
                For Each proj In _projectCache.Values
                    proj.Dispose()
                Next
                _projectCache.Clear()
                _project = Nothing
                _disposed = True
            End If
        End Sub

    End Class


    ''' <summary>
    ''' États de solo Reaper pour <c>I_SOLO</c>.
    ''' Valeurs reconnues par <c>SET_TRACK_INFO</c>.
    ''' </summary>
    Public Enum SoloState As Integer
        ''' <summary>Piste non solotée.</summary>
        Off = 0
        ''' <summary>Solo standard (les autres pistes passent en mute).</summary>
        Solo = 1
        ''' <summary>Solo in place.</summary>
        SoloInPlace = 2
        ''' <summary>Safe solo — la piste n'est pas affectée par le solo d'autres pistes.</summary>
        SafeSolo = 5
        ''' <summary>Safe solo in place.</summary>
        SafeSoloInPlace = 6
    End Enum


    ' ══════════════════════════════════════════════════════════════════════════
    '  TRACK FX TYPE — classification d'un FX
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Classification d'un FX selon les listes EQ et DYN de la
    ''' <see cref="ReaperSessionConfig"/>.
    ''' </summary>
    Public Enum TrackFxType
        ''' <summary>FX générique (ni EQ ni DYN).</summary>
        Generic = 0
        ''' <summary>FX présent dans la liste EQ de la session.</summary>
        EQ = 1
        ''' <summary>FX présent dans la liste DYN de la session.</summary>
        Dyn = 2
    End Enum

    ' ══════════════════════════════════════════════════════════════════════════
    '  TRACK FX INFO — état d'un FX pour l'affichage surface
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Regroupe les informations d'un FX utiles pour l'affichage sur la surface
    ''' de contrôle.
    ''' </summary>
    Public Structure TrackFxInfo

        ''' <summary>Index 0-based du FX sur la piste.</summary>
        Public ReadOnly FxIndex As Integer

        ''' <summary>
        ''' Nom complet tel que retourné par Reaper.
        ''' Exemples : "VST3: FabFilter Pro-Q 3 (FabFilter)"  "VST: ReaEQ (Cockos)"
        ''' </summary>
        Public ReadOnly Name As String

        ''' <summary>True si le FX est actif (non bypassé).</summary>
        Public ReadOnly IsEnabled As Boolean

        ''' <summary>
        ''' Classification du FX d'après la <see cref="ReaperSessionConfig"/> :
        ''' <see cref="TrackFxType.EQ"/>, <see cref="TrackFxType.Dyn"/>
        ''' ou <see cref="TrackFxType.Generic"/>.
        ''' </summary>
        Public ReadOnly FxType As TrackFxType

        Public Sub New(fxIndex As Integer,
                       name As String,
                       isEnabled As Boolean,
                       fxType As TrackFxType)
            Me.FxIndex = fxIndex
            Me.Name = name
            Me.IsEnabled = isEnabled
            Me.FxType = fxType
        End Sub

        ''' <summary>
        ''' Nom court sans préfixe type, adapté à un affichage 4 caractères.
        ''' Exemple : "VST3: ReaEQ (Cockos)" → "ReaEQ (Cockos)"
        ''' </summary>
        Public ReadOnly Property ShortName As String
            Get
                Dim colonIdx = Name.IndexOf(":"c)
                If colonIdx < 0 Then Return Name.Trim()
                Return Name.Substring(colonIdx + 1).Trim()
            End Get
        End Property

        ''' <summary>True si le FX est valide (fxIndex ≥ 0).</summary>
        Public ReadOnly Property IsValid As Boolean
            Get
                Return FxIndex >= 0
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return Name
        End Function

    End Structure

    ' ── Structure de configuration d'un paramètre FX ─────────────────────────
    Public Structure FxParamConfig
        ''' <summary>Index 0-based du paramètre sur le FX (tel que retourné par Reaper).</summary>
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
    ' ══════════════════════════════════════════════════════════════════════════
    '  Session Config — configuration de session dans un fichier INI
    ' ══════════════════════════════════════════════════════════════════════════
    ''' <summary>
    ''' Lit et persiste la configuration de session dans un fichier INI placé
    ''' à côté de l'exécutable du driver.
    ''' <br/>
    ''' Utilise <see cref="ReaperConfigReader"/> pour la lecture et
    ''' <see cref="ReaperConfigWriter"/> pour l'écriture.
    ''' </summary>
    Public Class ReaperSessionConfig

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
        Private ReadOnly _pluginShortNames As New Dictionary(Of String, String)

        ' ── Configs des paramètres FX : clé = nom complet du plugin ──────────
        Private ReadOnly _fxParamConfigs As New Dictionary(Of String, List(Of FxParamConfig))

        ' ── Clé de section INI ────────────────────────────────────────────────
        Private Shared ReadOnly FxParamSectionPrefix As String = "FxParams:"
        ' ── Lignes brutes des sections FxParams déjà présentes dans le fichier INI ──
        ' Clé = fullName du plugin  Valeur = lignes originales (header + contenu)
        ' Permet de réécrire les sections existantes sans les modifier.
        Private ReadOnly _existingFxParamSectionLines As New Dictionary(Of String, List(Of String))

        Public Sub New()
            _iniPath = IO.Path.Combine(AppContext.BaseDirectory, "ReaperDriver.ini")
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
                Save()
            End Set
        End Property

        Public Property SelectedNetworkInterface As String
            Get
                Return _selectedInterface
            End Get
            Set
                _selectedInterface = Value
                Save()
            End Set
        End Property

        Public Property VSDebug As Boolean
            Get
                Return _vsDebug
            End Get
            Set
                _vsDebug = Value
                Save()
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
            Save()
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
            If IO.File.Exists(_iniPath) Then
                Dim rawLines As IReadOnlyList(Of String) = IO.File.ReadAllLines(_iniPath)
                LoadFxParams(rawLines)
            End If
        End Sub
        ' ── Mise à jour des plugins ───────────────────────────────────────────

        Public Sub RefreshPlugins()
            ' ── Snapshot des états existants avant reset ─────────────────────
            Dim existingVisible As New Dictionary(Of String, Boolean)(_pluginVisible)
            Dim existingShortNames As New Dictionary(Of String, String)(_pluginShortNames)

            _plugins.Clear()
            _pluginVisible.Clear()
            Dim installed = Reaper.utility.GetInstalledFXList
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
                Dim compressed = CompressFxName(fullName)
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
            Return ReaperFxList.CompressFxName(fullName)
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
        ' ────────────────────────────────────────────────────────────────────
        ' API FxParam
        ' ────────────────────────────────────────────────────────────────────



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
            EnsureParamExists(fxFullName, paramIndex)
            Dim cfg = _fxParamConfigs(fxFullName)(paramIndex)
            cfg.ShortName = shortName.PadRight(4).Substring(0, 4)
            _fxParamConfigs(fxFullName)(paramIndex) = cfg
            Save()
        End Sub

        ''' <summary>
        ''' Définit le nom long (8 car.) d'un paramètre et sauvegarde.
        ''' </summary>
        Public Sub SetFxParamLongName(fxFullName As String, paramIndex As Integer, longName As String)
            EnsureParamExists(fxFullName, paramIndex)
            Dim cfg = _fxParamConfigs(fxFullName)(paramIndex)
            cfg.LongName = longName.PadRight(8).Substring(0, 8)
            _fxParamConfigs(fxFullName)(paramIndex) = cfg
            Save()
        End Sub

        ''' <summary>
        ''' Définit la visibilité d'un paramètre et sauvegarde.
        ''' </summary>
        Public Sub SetFxParamVisible(fxFullName As String, paramIndex As Integer, visible As Boolean)
            EnsureParamExists(fxFullName, paramIndex)
            Dim cfg = _fxParamConfigs(fxFullName)(paramIndex)
            cfg.Visible = visible
            _fxParamConfigs(fxFullName)(paramIndex) = cfg
            Save()
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
        ''' Génère automatiquement une liste de configuration de paramètres pour un FX.
        ''' Chaque entrée contient l'index, le nom complet, un nom court (4 caractères),
        ''' un nom long (8 caractères) et la visibilité par défaut.
        ''' </summary>
        ''' <param name="fx">FX source à analyser.</param>
        ''' <returns>Liste des <see cref="FxParamConfig"/> générée à partir des paramètres du FX.</returns>
        Public Function GetGenericFxParamConfig(fx As ReaperFX) As List(Of FxParamConfig)
            Dim result As New List(Of FxParamConfig)

            If fx Is Nothing Then
                Return result
            End If

            If String.IsNullOrEmpty(fx.Name) Then
                Return result
            End If

            If fx.Params Is Nothing OrElse fx.Params.Count = 0 Then
                fx.Refresh()
            End If

            If fx.Params Is Nothing OrElse fx.Params.Count = 0 Then
                Return result
            End If

            Dim paramNames As New List(Of String)

            For Each param In fx.Params
                If param IsNot Nothing Then
                    paramNames.Add(param.Name)
                Else
                    paramNames.Add(String.Empty)
                End If
            Next

            If paramNames.Count = 0 Then
                Return result
            End If

            Dim pairs = ReaperFxParamList.BuildParamNamePairs(paramNames)

            For i As Integer = 0 To paramNames.Count - 1
                Dim cfg As New FxParamConfig

                cfg.ParamIndex = i
                cfg.FullName = paramNames(i)
                cfg.ShortName = pairs(i).Short4
                cfg.LongName = pairs(i).Long8
                cfg.Visible = True

                result.Add(cfg)
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
            Save()
        End Sub

        ''' <summary>Retire un plugin de la liste EQ et sauvegarde.</summary>
        Public Sub RemoveEqPlugin(fullName As String)
            If _eq.Remove(fullName) Then
                _removedEq.Add(fullName)
                Save()
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
            Save()
        End Sub

        ''' <summary>Retire un plugin de la liste DYN et sauvegarde.</summary>
        Public Sub RemoveDynPlugin(fullName As String)
            If _dyn.Remove(fullName) Then
                _removedDyn.Add(fullName)
                Save()
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
            If Not _fxParamConfigs.ContainsKey(fxFullName) Then
                Return Nothing
            End If
            For Each cfg In _fxParamConfigs(fxFullName)
                If cfg.ParamIndex = paramIndex Then
                    Return cfg
                End If
            Next
            Return Nothing
        End Function

        Private Sub EnsureParamExists(fxFullName As String, paramIndex As Integer)
            If Not _fxParamConfigs.ContainsKey(fxFullName) Then
                _fxParamConfigs(fxFullName) = New List(Of FxParamConfig)
            End If
            Dim configs = _fxParamConfigs(fxFullName)
            If GetFxParamConfig(fxFullName, paramIndex).HasValue Then Return
            configs.Add(New FxParamConfig With {
                .ParamIndex = paramIndex,
                .FullName = String.Empty,
                .ShortName = "Prm" & paramIndex.ToString().PadLeft(1),
                .LongName = ("Param" & paramIndex.ToString()).PadRight(8).Substring(0, 8),
                .Visible = True
            })
        End Sub
        Public Sub RefreshFxParams(fxFullName As String, paramNames As IReadOnlyList(Of String))
            Dim added = AddMissingFxParams(fxFullName, paramNames)
            If added Then Save()
        End Sub

        ''' <summary>
        ''' Ajoute uniquement les configs de paramètres absentes sans appeler Save.
        ''' Les configs existantes (modifiées par l'utilisateur) sont conservées.
        ''' Retourne True si au moins une nouvelle config a été ajoutée.
        ''' </summary>
        Private Function AddMissingFxParams(fxFullName As String,
                                             paramNames As IReadOnlyList(Of String)) As Boolean
            If Not _fxParamConfigs.ContainsKey(fxFullName) Then
                _fxParamConfigs(fxFullName) = New List(Of FxParamConfig)
            End If

            Dim configs = _fxParamConfigs(fxFullName)
            Dim pairs = ReaperFxParamList.BuildParamNamePairs(paramNames)
            Dim added As Boolean = False

            For i As Integer = 0 To paramNames.Count - 1
                If GetFxParamConfig(fxFullName, i).HasValue Then
                    Continue For
                End If
                Dim cfg As New FxParamConfig With {
                    .ParamIndex = i,
                    .FullName = paramNames(i),
                    .ShortName = pairs(i).Short4,
                    .LongName = pairs(i).Long8,
                    .Visible = True
                }
                configs.Add(cfg)
                added = True
            Next

            Return added
        End Function

        Public Sub GetAllFxParams(allTrackFx As IReadOnlyList(Of ReaperTrackFX))
            If allTrackFx Is Nothing OrElse allTrackFx.Count = 0 Then Return

            Dim needsSave As Boolean = False

            For Each trackFx In allTrackFx
                For Each fx In trackFx.FxList
                    If String.IsNullOrEmpty(fx.Name) Then
                        Continue For
                    End If

                    Dim paramNames As New List(Of String)
                    For Each param In fx.Params
                        paramNames.Add(param.Name)
                    Next

                    If paramNames.Count = 0 Then
                        Continue For
                    End If

                    ' ── Ajoute uniquement les params manquants (conserve les modifs user) ──
                    If AddMissingFxParams(fx.Name, paramNames) Then
                        needsSave = True
                    End If
                Next
            Next

            If needsSave Then Save()
        End Sub

        ' ── Load : lire les sections [FxParams:shortName] ─────────────────────

        Private Sub LoadFxParams(lines As IReadOnlyList(Of String))
            _existingFxParamSectionLines.Clear()

            Dim currentFullName As String = Nothing
            Dim currentConfigs As List(Of FxParamConfig) = Nothing
            Dim currentRawLines As List(Of String) = Nothing

            ' Préserve l'ordre d'apparition dans le fichier :
            ' IsText=True → séparateur text=  |  IsText=False → paramètre p{n}
            Dim orderedSlots As New List(Of (IsText As Boolean, TextValue As String, ParamN As Integer))
            Dim paramDict As New Dictionary(Of Integer, FxParamConfig)

            For Each line In lines
                Dim trimmed = line.Trim()

                If trimmed.StartsWith("[") AndAlso trimmed.EndsWith("]") Then

                    ' ── Flush la section FxParams précédente ──────────────────
                    If currentFullName IsNot Nothing AndAlso currentConfigs IsNot Nothing Then
                        currentConfigs.Clear()
                        For Each slot In orderedSlots
                            If slot.IsText Then
                                Dim textCfg As New FxParamConfig With {
                                    .ParamIndex = -1,
                                    .TextOnly = slot.TextValue
                                }
                                currentConfigs.Add(textCfg)
                            Else
                                Dim paramCfg As FxParamConfig
                                If paramDict.TryGetValue(slot.ParamN, paramCfg) Then
                                    currentConfigs.Add(paramCfg)
                                End If
                            End If
                        Next
                    End If
                    orderedSlots.Clear()
                    paramDict.Clear()

                    If trimmed.StartsWith("[" & FxParamSectionPrefix, StringComparison.OrdinalIgnoreCase) Then
                        Dim shortName = trimmed.Substring(FxParamSectionPrefix.Length + 1,
                                                          trimmed.Length - FxParamSectionPrefix.Length - 2)
                        currentFullName = GetPluginFullName(shortName.Trim())
                        If String.IsNullOrEmpty(currentFullName) Then
                            currentFullName = Nothing
                            currentConfigs = Nothing
                            currentRawLines = Nothing
                        Else
                            If Not _fxParamConfigs.ContainsKey(currentFullName) Then
                                _fxParamConfigs(currentFullName) = New List(Of FxParamConfig)
                            End If
                            currentConfigs = _fxParamConfigs(currentFullName)
                            currentRawLines = New List(Of String)
                            currentRawLines.Add(trimmed)
                            _existingFxParamSectionLines(currentFullName) = currentRawLines
                        End If
                    Else
                        currentFullName = Nothing
                        currentConfigs = Nothing
                        currentRawLines = Nothing
                    End If
                    Continue For
                End If

                If currentFullName Is Nothing OrElse currentConfigs Is Nothing Then
                    Continue For
                End If

                If currentRawLines IsNot Nothing Then
                    currentRawLines.Add(trimmed)
                End If

                Dim eqPos = trimmed.IndexOf("="c)
                If eqPos < 0 Then Continue For

                Dim key = trimmed.Substring(0, eqPos).Trim().ToLowerInvariant()
                Dim rawValue = trimmed.Substring(eqPos + 1)

                ' ── Ligne text="..." : extraire le contenu entre guillemets ──
                If key = "text" Then
                    Dim firstQuote = rawValue.IndexOf(""""c)
                    Dim lastQuote = rawValue.LastIndexOf(""""c)
                    Dim textContent As String
                    If firstQuote >= 0 AndAlso lastQuote > firstQuote Then
                        textContent = rawValue.Substring(firstQuote + 1, lastQuote - firstQuote - 1)
                    Else
                        textContent = rawValue
                    End If
                    orderedSlots.Add((True, textContent, -1))
                    Continue For
                End If

                Dim value = rawValue

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

            ' ── Flush la dernière section ──────────────────────────────────────
            If currentFullName IsNot Nothing AndAlso currentConfigs IsNot Nothing Then
                currentConfigs.Clear()
                For Each slot In orderedSlots
                    If slot.IsText Then
                        Dim textCfg As New FxParamConfig With {
                            .ParamIndex = -1,
                            .TextOnly = slot.TextValue
                        }
                        currentConfigs.Add(textCfg)
                    Else
                        Dim paramCfg As FxParamConfig = Nothing
                        If paramDict.TryGetValue(slot.ParamN, paramCfg) Then
                            currentConfigs.Add(paramCfg)
                        End If
                    End If
                Next
            End If
        End Sub

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
                lines.Add(String.Empty)

                ' ── Section déjà présente dans le fichier INI → reproduction fidèle ──
                ' Les modifications de l'utilisateur (ordre, noms, visibilité) sont
                ' conservées exactement telles quelles.
                If _existingFxParamSectionLines.ContainsKey(fullName) Then
                    lines.AddRange(_existingFxParamSectionLines(fullName))
                    Continue For
                End If

                ' ── Nouvelle section : génération automatique ─────────────────
                Dim shortName = GetPluginShortName(fullName)
                lines.Add("[" & FxParamSectionPrefix & shortName & "]")

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

            Return lines
        End Function
        ' ── Sauvegarde ────────────────────────────────────────────────────────

        Public Sub Save()

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

            Dim writer As New ReaperConfigWriter(_iniPath)

            writer.SetFileHeader("; ReaperDriverControl24 — Configuration de session" &
                                 Environment.NewLine &
                                 "; Ce fichier est mis à jour automatiquement.")

            writer.AddSection("Driver")
            writer.SetBoolean("Driver", "Autostart", _autostart,
                              "; Démarrage automatique du driver à l'ouverture de l'application")
            writer.SetString("Driver", "NetworkInterface", _selectedInterface,
                             "; Nom de l'interface réseau utilisée pour le driver Control24" &
                             Environment.NewLine &
                             "; Copier un nom depuis la section [Interfaces] ci-dessous")
            writer.SetBoolean("Driver", "VSDebug", _vsDebug,
                              "; Active le débogage Visual Studio au démarrage du driver (défaut : False)")
            writer.SetString("Driver", "NetworkInterface", _selectedInterface, "; Simulation d'une console Control24 NETWORKINTERFACE=TestWithoutC24")

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

            writer.Save()

            ' ── FxParams : sections [FxParams:shortName] ──────────────────
            Dim fxParamLines = BuildFxParamSections()
            If fxParamLines.Count > 0 Then
                IO.File.AppendAllLines(_iniPath, fxParamLines)
            End If
        End Sub


    End Class

#End Region
End Namespace
