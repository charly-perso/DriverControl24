Imports System.Globalization

Namespace Reaper
    Public Module utility
        ' Dans C24_Desk — test de connexion au bridge Reaper
        Public Function CallReaperBridge(request As String) As String
            Try
                Using pipe As New IO.Pipes.NamedPipeClientStream(
        ".", "c24_reaper_bridge",
        IO.Pipes.PipeDirection.InOut)

                    pipe.Connect(timeout:=200)
                    Dim writer As New IO.StreamWriter(pipe) With {.AutoFlush = True}
                    Dim reader As New IO.StreamReader(pipe)
                    writer.WriteLine(request)
                    Return reader.ReadLine()
                End Using
            Catch ex As Exception
                Console.WriteLine(ex.Message)
                Return String.Empty   ' Bridge non démarré (Reaper fermé / plugin absent)
            End Try
        End Function
        ''' <summary>
        ''' Retourne l'index 0-based de la dernière piste modifiée par l'utilisateur.
        ''' Retourne -1 si aucune piste n'a encore été modifiée.
        ''' </summary>
        Public Function GetLastTouchedTrackIndex() As Integer
            Dim raw = CallReaperBridge("GET_LAST_TOUCHED_TRACK")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return -1
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return (-1, -1, -1)
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return -1
            Dim count As Integer = -1
            Integer.TryParse(raw, count)
            Return count
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return -1
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return -1
            Dim v As Double = -1
            Double.TryParse(raw, Globalization.NumberStyles.Any,
                            Globalization.CultureInfo.InvariantCulture, v)
            Return v
        End Function

        ''' <summary>
        ''' Retourne les amplitudes linéaires L et R d'une piste en un seul appel pipe.
        ''' Evite deux appels séparés lors du rafraîchissement des VU-mètres.
        ''' </summary>
        Public Function Track_GetPeakStereo(trackIndex As Integer) As (L As Double, R As Double)
            Dim raw = CallReaperBridge($"GET_TRACK_PEAK_STEREO|{trackIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return (-1, -1)
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return Double.MinValue
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return Double.MinValue
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return 0UI
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return 0UI
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return -1
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return -1
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return String.Empty
            Return raw
        End Function

        ''' <summary>
        ''' Retourne le nom d'une sortie audio par index (0-based).
        ''' Retourne une chaîne vide si l'index est hors limite.
        ''' </summary>
        Public Function GetAudioOutputName(channelIndex As Integer) As String
            Dim raw = CallReaperBridge($"GET_AUDIO_OUTPUT_NAME|{channelIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return String.Empty
            Return raw
        End Function

        ''' <summary>
        ''' Retourne la liste de toutes les entrées audio en un seul appel pipe.
        ''' </summary>
        Public Function GetAllAudioInputs() As IReadOnlyList(Of AudioChannelInfo)
            Dim raw = CallReaperBridge("GET_ALL_AUDIO_INPUTS")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return -1
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return String.Empty
            Return raw
        End Function

        ''' <summary>
        ''' Convertit des secondes en Quarter Notes.
        ''' Retourne -1 si le bridge est indisponible.
        ''' </summary>
        Public Function TimePositionToQN(time As Double) As Double
            Dim raw = CallReaperBridge(
                $"TIMEPOS_TO_QN|{time.ToString("G", Globalization.CultureInfo.InvariantCulture)}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return -1
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return -1
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return (0, 0, 0)
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return -1
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return String.Empty
            Return raw
        End Function

        ''' <summary>
        ''' Retourne la description de l'action refaisable.
        ''' Retourne String.Empty si la pile de redo est vide.
        ''' </summary>
        Public Function CanRedo() As String
            Dim raw = CallReaperBridge("CAN_REDO")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return String.Empty
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return -1
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return String.Empty
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return -1
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then
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


    End Module


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
        End Sub

        ''' <summary>Index 0-based de la piste dans le projet actif.</summary>
        Public ReadOnly Property TrackIndex As Integer
            Get
                Return _index
            End Get
        End Property


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

        ' ── Chargement ─────────────────────────────────────────────────────────

        ''' <summary>
        ''' Charge tous les paramètres de la piste en un seul appel pipe.
        ''' Appeler avant d'accéder aux propriétés ou pour rafraîchir l'état.
        ''' </summary>
        Public Sub Refresh()
            ' Nom
            Dim nameRaw = utility.CallReaperBridge($"GET_TRACK_NAME|{_index}")
            If Not String.IsNullOrEmpty(nameRaw) AndAlso Not nameRaw.StartsWith("ERROR") Then
                _name = nameRaw
            End If

            ' Tous les paramètres numériques
            Dim raw = utility.CallReaperBridge($"GET_TRACK_ALL_INFO|{_index}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return

            Dim p = raw.Split("|"c)
            If p.Length < 55 Then Return

            ' Doubles (0-6)

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

            Dim new_mute = p(7) <> "0"
            If _mute <> new_mute Then
                _mute = new_mute
                RaiseEvent On_MuteChange(_mute)
            End If

            Dim new_solo = ParseI(p(18))
            If _solo <> new_solo Then
                _solo = new_solo
                RaiseEvent On_SoloChange(_solo)
            End If

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

            Dim new_autoMode = ParseI(p(27))
            If _autoMode <> new_autoMode Then
                _autoMode = new_autoMode
                RaiseEvent On_AutoModeChange(_autoMode)
            End If

            Dim new_selected = ParseI(p(29))
            If _selected <> new_selected Then
                _selected = new_selected
                RaiseEvent On_SelectedChange(_selected)
            End If

            ' Entiers lecture seule (30-38)
            _wndH = ParseI(p(30))
            _tcpH = ParseI(p(31))
            _tcpY = ParseI(p(32))
            _tcpScreenY = ParseI(p(33))
            _mcpW = ParseI(p(34))
            _mcpH = ParseI(p(35))
            _mcpX = ParseI(p(36))
            _mcpY = ParseI(p(37))
            _mcpScreenX = ParseI(p(38))
            ' Entiers R/W (39-54)
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

        ''' <summary>B_SOLO_DEFEAT — Si vrai, joue même quand d'autres pistes sont solotées</summary>
        Public Property SoloDefeat As Boolean
            Get
                Return _soloDefeat : End Get
            Set
                _soloDefeat = Value : SetB("B_SOLO_DEFEAT", Value) : End Set
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
        Private _sendMode As Integer = 0   ' 0=post-fader  1=pre-fx  3=pre-fader
        Private _srcChan As Integer = 0
        Private _dstChan As Integer = 0
        Private _midiFlags As Integer = 0

        ' ── Événements ────────────────────────────────────────────────────────
        Public Event On_VolChange(_vol As Double)
        Public Event On_PanChange(_pan As Double)
        Public Event On_MuteChange(_mute As Boolean)

        ' ── Constructeur ───────────────────────────────────────────────────────

        Public Sub New(trackIndex As Integer, category As Integer, sendIndex As Integer)
            _trackIndex = trackIndex
            _category = category
            _sendIndex = sendIndex
        End Sub

        Public ReadOnly Property TrackIndex As Integer
            Get
                Return _trackIndex : End Get
        End Property

        Public ReadOnly Property Category As Integer
            Get
                Return _category : End Get
        End Property

        Public ReadOnly Property SendIndex As Integer
            Get
                Return _sendIndex : End Get
        End Property

        ' ── Chargement ─────────────────────────────────────────────────────────

        ''' <summary>
        ''' Charge tous les paramètres de l'envoi en un seul appel pipe.
        ''' Déclenche les événements uniquement si la valeur a changé.
        ''' </summary>
        Public Sub Refresh()
            Dim raw = utility.CallReaperBridge(
                $"GET_SEND_ALL_INFO|{_trackIndex}|{_category}|{_sendIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return

            Dim p = raw.Split("|"c)
            If p.Length < 10 Then Return

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
            utility.CallReaperBridge(
                $"SET_SEND_INFO|{_trackIndex}|{_category}|{_sendIndex}|{param}|{value.ToString("G", Ic)}")
        End Sub

        Private Sub SetB(param As String, value As Boolean)
            SetD(param, If(value, 1.0, 0.0))
        End Sub

        Private Sub SetI(param As String, value As Integer)
            SetD(param, CDbl(value))
        End Sub

        ' ── Propriétés ─────────────────────────────────────────────────────────

        ''' <summary>D_VOL — Volume de l'envoi (0..4  1.0=0dB)</summary>
        Public Property Vol As Double
            Get
                Return _vol : End Get
            Set
                _vol = Value : SetD("D_VOL", Value) : End Set
        End Property

        ''' <summary>D_PAN — Pan de l'envoi (-1=L  0=C  1=R)</summary>
        Public Property Pan As Double
            Get
                Return _pan : End Get
            Set
                _pan = Value : SetD("D_PAN", Value) : End Set
        End Property

        ''' <summary>D_PANLAW — Pan law (&lt;0=projet défaut)</summary>
        Public Property PanLaw As Double
            Get
                Return _panLaw : End Get
            Set
                _panLaw = Value : SetD("D_PANLAW", Value) : End Set
        End Property

        ''' <summary>D_MUTE — Envoi muet</summary>
        Public Property Mute As Boolean
            Get
                Return _mute : End Get
            Set
                _mute = Value : SetB("D_MUTE", Value) : End Set
        End Property

        ''' <summary>B_PHASE — Phase inversée</summary>
        Public Property Phase As Boolean
            Get
                Return _phase : End Get
            Set
                _phase = Value : SetB("B_PHASE", Value) : End Set
        End Property

        ''' <summary>B_MONO — Mono</summary>
        Public Property Mono As Boolean
            Get
                Return _mono : End Get
            Set
                _mono = Value : SetB("B_MONO", Value) : End Set
        End Property

        ''' <summary>I_SENDMODE — 0=post-fader  1=pre-fx  3=pre-fader</summary>
        Public Property SendMode As Integer
            Get
                Return _sendMode : End Get
            Set
                _sendMode = Value : SetI("I_SENDMODE", Value) : End Set
        End Property

        ''' <summary>I_SRCCHAN — Canal source (bit10=stéréo  bit11=phase inversée)</summary>
        Public Property SrcChan As Integer
            Get
                Return _srcChan : End Get
            Set
                _srcChan = Value : SetI("I_SRCCHAN", Value) : End Set
        End Property

        ''' <summary>I_DSTCHAN — Canal destination</summary>
        Public Property DstChan As Integer
            Get
                Return _dstChan : End Get
            Set
                _dstChan = Value : SetI("I_DSTCHAN", Value) : End Set
        End Property

        ''' <summary>I_MIDIFLAGS — Flags MIDI</summary>
        Public Property MidiFlags As Integer
            Get
                Return _midiFlags : End Get
            Set
                _midiFlags = Value : SetI("I_MIDIFLAGS", Value) : End Set
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
                Return _sends(sendIndex) : End Get
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return

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

        ' ── Cache ──────────────────────────────────────────────────────────────
        Public _name As String = String.Empty
        Public _value As Double = 0       ' normalisé [0..1]
        Public _formatted As String = String.Empty

        ' ── Événement ─────────────────────────────────────────────────────────
        Public Event On_ValueChange(_value As Double)

        Public Sub New(trackIndex As Integer, fxIndex As Integer, paramIndex As Integer)
            _trackIndex = trackIndex
            _fxIndex = fxIndex
            _paramIndex = paramIndex
        End Sub
        Public Sub RaiseOn_ValueChange(_value As Double)
            RaiseEvent On_ValueChange(_value)
        End Sub
        Public ReadOnly Property TrackIndex As Integer
            Get
                Return _trackIndex : End Get
        End Property
        Public ReadOnly Property FxIndex As Integer
            Get
                Return _fxIndex : End Get
        End Property
        Public ReadOnly Property ParamIndex As Integer
            Get
                Return _paramIndex : End Get
        End Property

        ''' <summary>Nom du paramètre (chargé par Refresh).</summary>
        Public ReadOnly Property Name As String
            Get
                Return _name : End Get
        End Property

        ''' <summary>Valeur normalisée [0..1] — setter écrit dans Reaper.</summary>
        Public Property Value As Double
            Get
                Return _value : End Get
            Set
                _value = Value
                utility.CallReaperBridge(
                    $"SET_FX_PARAM_NORMALIZED|{_trackIndex}|{_fxIndex}|{_paramIndex}|{Value.ToString("G", Ic)}")
            End Set
        End Property

        ''' <summary>Valeur formatée par Reaper ex: "-6.0 dB" (chargée par Refresh).</summary>
        Public ReadOnly Property Formatted As String
            Get
                Return _formatted : End Get
        End Property

        ''' <summary>
        ''' Charge le nom, la valeur normalisée et la valeur formatée.
        ''' Déclenche On_ValueChange si la valeur a changé.
        ''' </summary>
        Public Sub Refresh()
            Dim nameRaw = utility.CallReaperBridge(
                $"GET_FX_PARAM_NAME|{_trackIndex}|{_fxIndex}|{_paramIndex}")
            If Not String.IsNullOrEmpty(nameRaw) AndAlso Not nameRaw.StartsWith("ERROR") Then
                _name = nameRaw
            End If

            Dim valRaw = utility.CallReaperBridge(
                $"GET_FX_PARAM_NORMALIZED|{_trackIndex}|{_fxIndex}|{_paramIndex}")
            If Not String.IsNullOrEmpty(valRaw) AndAlso Not valRaw.StartsWith("ERROR") Then
                Dim new_val As Double = 0
                Double.TryParse(valRaw, NumberStyles.Any, Ic, new_val)
                If _value <> new_val Then
                    _value = new_val
                    RaiseEvent On_ValueChange(_value)
                End If
            End If

            Dim fmtRaw = utility.CallReaperBridge(
                $"GET_FX_PARAM_FORMATTED|{_trackIndex}|{_fxIndex}|{_paramIndex}")
            If Not String.IsNullOrEmpty(fmtRaw) AndAlso Not fmtRaw.StartsWith("ERROR") Then
                _formatted = fmtRaw
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

        ' ── Événements ────────────────────────────────────────────────────────
        Public Event On_EnabledChange(_enabled As Boolean)
        Public Event On_PresetChange(_preset As String)

        Public Sub New(trackIndex As Integer, fxIndex As Integer)
            _trackIndex = trackIndex
            _fxIndex = fxIndex
        End Sub

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
        Public Property IsOpen As Boolean
            Get
                Dim raw = utility.CallReaperBridge($"GET_FX_OPEN|{_trackIndex}|{_fxIndex}")
                Return raw = "1"
            End Get
            Set
                utility.CallReaperBridge(
                    $"SET_FX_OPEN|{_trackIndex}|{_fxIndex}|{If(Value, "1", "0")}")
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
            If Not String.IsNullOrEmpty(raw) AndAlso Not raw.StartsWith("ERROR") Then
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
            If String.IsNullOrEmpty(infoRaw) OrElse infoRaw.StartsWith("ERROR") Then Return

            Dim info = infoRaw.Split("|"c)
            If info.Length < 4 Then Return

            _name = info(0)

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

            ' ── GET_FX_ALL_PARAMS : 1 appel pour tous les noms + valeurs ───────
            If _params.Count = 0 Then Return
            Dim paramsRaw = utility.CallReaperBridge($"GET_FX_ALL_PARAMS|{_trackIndex}|{_fxIndex}")
            If String.IsNullOrEmpty(paramsRaw) OrElse paramsRaw.StartsWith("ERROR") Then Return

            Dim p = paramsRaw.Split("|"c)
            For i = 0 To _params.Count - 1
                Dim nameIdx = i * 2
                Dim valIdx = i * 2 + 1
                If valIdx >= p.Length Then Exit For


                _params(i)._name = p(nameIdx)   ' ← accès direct au champ pour éviter un appel pipe
                Dim new_val As Double = 0
                Double.TryParse(p(valIdx), NumberStyles.Any, Ic, new_val)
                If _params(i)._value <> new_val Then
                    _params(i)._value = new_val
                    _params(i).RaiseOn_ValueChange(new_val)
                End If
            Next
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return

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

    ''' <summary>Représente un FX installé dans Reaper (type + nom).</summary>
    Public Structure InstalledFXInfo
        ''' <summary>Type du plugin : "VST"  "VST3"  "JS"  "CLAP"  "AU"  "DX"…</summary>
        Public ReadOnly FxType As String
        ''' <summary>Nom tel qu'affiché dans Reaper ex: "ReaEQ (Cockos)".</summary>
        Public ReadOnly Name As String

        Public Sub New(fxType As String, name As String)
            Me.FxType = fxType
            Me.Name = name
        End Sub

        ''' <summary>Nom complet préfixé par le type, compatible avec TrackFX_AddByName.</summary>
        Public ReadOnly Property FullName As String
            Get
                Return $"{FxType}: {Name}" : End Get
        End Property
    End Structure


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

    ''' <summary>Combinaison de flags pour <see cref="utility.EndUndoBlock"/>.</summary>
    <Flags>
    Public Enum UndoFlags As Integer
        Tracks = 1   ' modifications de pistes
        Items = 4   ' modifications d'items
        FX = 8   ' modifications de FX
        All = Tracks Or Items Or FX
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





End Namespace
