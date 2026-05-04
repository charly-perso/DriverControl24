Imports System.Globalization
Imports System.Threading
Imports DriverConfig
Imports DriverReaperControl24.Reaper.Monitors

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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return Double.MinValue
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then
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
        ''' Écrit un paramètre FX en valeur normalisée [0..1].
        ''' Forme recommandée depuis une surface de contrôle : indépendante de la
        ''' plage native de chaque plugin.
        ''' Retourne True si Reaper a accepté la valeur.
        ''' </summary>
        Public Function SetFxParamNormalized(trackIndex As Integer,
                                              fxIndex As Integer,
                                              paramIndex As Integer,
                                              normValue As Double) As Boolean
            Dim raw = CallReaperBridge(
                $"SET_FX_PARAM_NORM|{trackIndex}|{fxIndex}|{paramIndex}|{normValue.ToString("G", CultureInfo.InvariantCulture)}")
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return Double.MinValue
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return Double.MinValue
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then
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
        ''' Écrit le volume d'une piste via <c>SetTrackUIVolume</c> (surface-safe).
        ''' Déclenche l'enregistrement d'automation si la piste est en Touch/Write/Latch.
        ''' Retourne True si Reaper a accepté la valeur.
        ''' </summary>
        ''' <param name="vol">Volume 0..4  (1.0 = 0 dB)</param>
        ''' <param name="isend">0=drag en cours  1=fin de drag/commit (défaut)  -1=écriture directe sans undo</param>
        Public Function SetTrackUIVolume(trackIndex As Integer,
                                         vol As Double,
                                         Optional isend As Integer = 1) As Boolean
            Dim raw = CallReaperBridge(
                $"SET_TRACK_UI_VOL|{trackIndex}|{vol.ToString("G", CultureInfo.InvariantCulture)}|{isend}")
            Return raw = "OK"
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return False
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then
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
        ''' Écrit le pan d'une piste via <c>SetTrackUIPan</c> (UI-safe).
        ''' Déclenche l'enregistrement d'automation si la piste est en Touch/Write/Latch.
        ''' Retourne True si Reaper a accepté la valeur.
        ''' </summary>
        ''' <param name="pan">Pan -1=Gauche  0=Centre  1=Droite.</param>
        ''' <param name="isend">0=drag en cours  1=fin de drag/commit (défaut)  -1=écriture directe sans undo.</param>
        Public Function SetTrackUIPan(trackIndex As Integer,
                                      pan As Double,
                                      Optional isend As Integer = 1) As Boolean
            Dim raw = CallReaperBridge(
                $"SET_TRACK_UI_PAN|{trackIndex}|{pan.ToString("G", CultureInfo.InvariantCulture)}|{isend}")
            Return raw = "OK"
        End Function

        ''' <summary>
        ''' Retourne l'état mute POST-automation d'une piste via <c>GetTrackUIMute</c>.
        ''' Retourne False si le bridge est indisponible.
        ''' </summary>
        Public Function GetTrackUIMute(trackIndex As Integer) As Boolean
            Dim raw = CallReaperBridge($"GET_TRACK_UI_MUTE|{trackIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return False
            Return raw = "1"
        End Function

        ''' <summary>
        ''' Écrit l'état mute d'une piste via <c>SetTrackUIMute</c> (UI-safe).
        ''' Déclenche l'enregistrement d'automation si la piste est en Touch/Write/Latch.
        ''' Retourne True si Reaper a accepté la valeur.
        ''' </summary>
        ''' <param name="mute">True=muet  False=actif.</param>
        ''' <param name="isend">0=drag en cours  1=fin de drag/commit (défaut)  -1=écriture directe sans undo.</param>
        Public Function SetTrackUIMute(trackIndex As Integer,
                                       mute As Boolean,
                                       Optional isend As Integer = 1) As Boolean
            Dim raw = CallReaperBridge(
                $"SET_TRACK_UI_MUTE|{trackIndex}|{If(mute, "1", "0")}|{isend}")
            Return raw = "OK"
        End Function

        ''' <summary>
        ''' Bascule l'état mute d'une piste (toggle UI-safe).
        ''' Retourne le nouvel état mute, ou False si le bridge est indisponible.
        ''' </summary>
        ''' <param name="isend">0=drag en cours  1=fin de drag/commit (défaut)  -1=sans undo.</param>
        Public Function ToggleTrackUIMute(trackIndex As Integer,
                                          Optional isend As Integer = 1) As Boolean
            Dim current = GetTrackUIMute(trackIndex)
            Dim newMute = Not current
            SetTrackUIMute(trackIndex, newMute, isend)
            Return newMute
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return -1
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return String.Empty
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return String.Empty
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return String.Empty
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
        ''' Lit une propriété numérique d'une enveloppe via <c>GetSetEnvelopeInfo_Value</c>.
        ''' Paramètres : "ACTIVE" "VISIBLE" "ARMED" "IN_LANE" "LANEHEIGHT" "DEFSHAPE"
        '''              "MINVALUE" "MAXVALUE" "CENTERVALUE" "FADERSCALING" "AUTO_ITEMS_OPTIONS".
        ''' Retourne <see cref="Double.MinValue"/> si le bridge est indisponible.
        ''' </summary>
        Public Function GetEnvelopePropVal(trackIndex As Integer,
                                            envIndex As Integer,
                                            paramName As String) As Double
            Dim raw = CallReaperBridge($"GET_ENVELOPE_PROP_VAL|{trackIndex}|{envIndex}|{paramName}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return Double.MinValue
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return Double.MinValue
            Dim v As Double = Double.MinValue
            Double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, v)
            Return v
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
                $"SET_ENVELOPE_PROP|{trackIndex}|{envIndex}|{paramName}|{value.ToString("G", CultureInfo.InvariantCulture)}")
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return -1
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return String.Empty
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return -1
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return String.Empty
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

    End Module

    Namespace Monitors
        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER SEND MONITOR — surveillance vol/pan/mute d'un envoi sur N pistes
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Surveille les changements de vol, pan et mute de l'envoi
        ''' <see cref="SendIndex"/> pour une collection de pistes.
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

            ' ── Identité de l'envoi (fixe) ────────────────────────────────────
            Private ReadOnly _sendIndex As Integer
            Private ReadOnly _category As Integer

            ' ── Flag de changement d'état ──────────────────────────────────────
            Private _stateChanged As Boolean = True


            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 50

            ' ── Pistes surveillées ────────────────────────────────────────────
            ' Clé  = trackIndex  |  Bool = True si suspendu
            ' Suspension → cache NON mis à jour, événements muets.
            ' Reprise   → cache effacé → resync au prochain cycle.
            Private ReadOnly _lock As New Object()
            Private ReadOnly _tracks As New Dictionary(Of Integer, Boolean)
            Private ReadOnly _volCache As New Dictionary(Of Integer, Double)
            Private ReadOnly _panCache As New Dictionary(Of Integer, Double)
            Private ReadOnly _muteCache As New Dictionary(Of Integer, Boolean)

            ' ── Événements ────────────────────────────────────────────────────

            ''' <summary>Déclenché quand la connexion au bridge démarre.</summary>
            Public Event On_Connected()
            ''' <summary>Déclenché quand le thread de monitoring s'arrête.</summary>
            Public Event On_Disconnected()
            ''' <summary>Déclenché quand le volume de l'envoi change sur une piste.</summary>
            Public Event On_VolChange(trackIndex As Integer, vol As Double)
            ''' <summary>Déclenché quand le pan de l'envoi change sur une piste.</summary>
            Public Event On_PanChange(trackIndex As Integer, pan As Double)
            ''' <summary>Déclenché quand l'état mute de l'envoi change sur une piste.</summary>
            Public Event On_MuteChange(trackIndex As Integer, mute As Boolean)
            ''' <summary>Déclenché quand l'état de lecture Reaper change.</summary>
            Public Event On_PlayStateChange(playState As Integer)

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Initialise le moniteur pour un envoi donné.
            ''' </summary>
            ''' <param name="sendIndex">Index 0-based de l'envoi à surveiller.</param>
            ''' <param name="category"><see cref="SendCategory"/> (défaut : Sends).</param>
            Public Sub New(sendIndex As Integer,
                           Optional category As SendCategory = SendCategory.Sends)
                _sendIndex = sendIndex
                _category = CInt(category)
            End Sub

            ' ── Propriétés ────────────────────────────────────────────────────

            ''' <summary>Index 0-based de l'envoi surveillé.</summary>
            Public ReadOnly Property SendIndex As Integer
                Get
                    Return _sendIndex
                End Get
            End Property

            ''' <summary>Catégorie de l'envoi surveillé.</summary>
            Public ReadOnly Property Category As SendCategory
                Get
                    Return CType(_category, SendCategory)
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

            ' ── Gestion de la collection ──────────────────────────────────────

            ''' <summary>
            ''' Ajoute une piste à la surveillance.
            ''' Sans effet si la piste est déjà enregistrée.
            ''' </summary>
            Public Sub WatchTrack(trackIndex As Integer)
                SyncLock _lock
                    If Not _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = False
                    End If
                End SyncLock
            End Sub

            ''' <summary>Ajoute une collection de pistes à la surveillance.</summary>
            Public Sub WatchTracks(tracks As IEnumerable(Of ReaperTrack))
                For Each trk In tracks
                    WatchTrack(trk.TrackIndex)
                Next
            End Sub

            ''' <summary>Retire une piste et efface ses caches.</summary>
            Public Sub UnwatchTrack(trackIndex As Integer)
                SyncLock _lock
                    _tracks.Remove(trackIndex)
                    _volCache.Remove(trackIndex)
                    _panCache.Remove(trackIndex)
                    _muteCache.Remove(trackIndex)
                End SyncLock
            End Sub

            ''' <summary>Retire toutes les pistes et efface les caches.</summary>
            Public Sub ClearWatched()
                SyncLock _lock
                    _tracks.Clear()
                    _volCache.Clear()
                    _panCache.Clear()
                    _muteCache.Clear()
                End SyncLock
            End Sub

            ' ── Suspension par piste ──────────────────────────────────────────

            ''' <summary>
            ''' Suspend la détection pour une piste.
            ''' Le cache n'est plus mis à jour ; à la reprise il est effacé.
            ''' </summary>
            Public Sub SuspendTrack(trackIndex As Integer)
                SyncLock _lock
                    If _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = True
                    End If
                End SyncLock
            End Sub

            ''' <summary>
            ''' Réactive la détection pour une piste suspendue.
            ''' Le cache est effacé → resync au prochain cycle.
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

            ' ── Cycle de vie ──────────────────────────────────────────────────

            ''' <summary>Démarre le thread de monitoring. Sans effet si déjà démarré.</summary>
            Public Sub Start()
                If IsRunning Then Return
                ' ── S'abonner au hub avant de démarrer ────────────────────────
                AddHandler ReaperPollHub.Instance.On_ProjectStateChange,
                    AddressOf OnProjectStateChange
                Threading.Volatile.Write(_isRunning, True)
                _thread = New Thread(AddressOf MonitorLoop) With {
                    .Name = $"SendMonitor_S{_sendIndex}_Cat{_category}",
                    .IsBackground = True,
                    .Priority = ThreadPriority.Normal
                }
                _thread.Start()
            End Sub

            ''' <summary>Arrête le thread de monitoring et ferme la connexion pipe.</summary>
            Public Sub [Stop]()
                RemoveHandler ReaperPollHub.Instance.On_ProjectStateChange,
                    AddressOf OnProjectStateChange
                Threading.Volatile.Write(_isRunning, False)
            End Sub

            ' ── Boucle de monitoring ──────────────────────────────────────────

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

            ''' <summary>
            ''' Interroge vol+pan (1 requête) et mute (1 requête) par piste surveillée.
            ''' <list type="bullet">
            ''' <item>Suspendue → requêtes faites, caches NON mis à jour, événements muets.</item>
            ''' <item>Active    → caches mis à jour, événements déclenchés si changement.</item>
            ''' </list>
            ''' </summary>
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

                    ' ── Vol + Pan (1 requête) ─────────────────────────────────
                    Dim rawVP = ReaperPollHub.Instance.Request($"GET_SEND_VOL_PAN|{tIdx}|{_category}|{_sendIndex}")
                    If Not String.IsNullOrEmpty(rawVP) AndAlso Not rawVP.StartsWith("ERROR") Then
                        Dim p = rawVP.Split("|"c)
                        If p.Length >= 2 Then
                            Dim newVol As Double = 1, newPan As Double = 0
                            Double.TryParse(p(0), NumberStyles.Any, ic, newVol)
                            Double.TryParse(p(1), NumberStyles.Any, ic, newPan)

                            If Not entry.isSuspended Then
                                Dim volChg As Boolean, panChg As Boolean
                                SyncLock _lock
                                    Dim pv As Double = Double.MinValue
                                    volChg = Not _volCache.TryGetValue(tIdx, pv) OrElse pv <> newVol
                                    If volChg Then _volCache(tIdx) = newVol
                                    panChg = Not _panCache.TryGetValue(tIdx, pv) OrElse pv <> newPan
                                    If panChg Then _panCache(tIdx) = newPan
                                End SyncLock
                                ' ── Déclencher HORS lock ──────────────────────
                                If volChg Then RaiseEvent On_VolChange(tIdx, newVol)
                                If panChg Then RaiseEvent On_PanChange(tIdx, newPan)
                            End If
                        End If
                    End If

                    ' ── Mute (1 requête séparée) ──────────────────────────────
                    Dim rawMute = ReaperPollHub.Instance.Request($"GET_SEND_MUTE|{tIdx}|{_category}|{_sendIndex}")
                    If Not String.IsNullOrEmpty(rawMute) AndAlso Not rawMute.StartsWith("ERROR") Then
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

            ' ── IDisposable ───────────────────────────────────────────────────

            ''' <summary>Arrête le monitoring et libère toutes les ressources.</summary>
            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()

                    _disposed = True
                End If
            End Sub

        End Class
        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER PROJECT MONITOR — surveillance du nombre de pistes
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Surveille le nombre de pistes du projet Reaper actif et déclenche
        ''' <see cref="On_TrackCountChanged"/> dès qu'il varie.
        ''' <br/>
        ''' Utilise le <see cref="ReaperPollPipe"/> pour ne vérifier le compteur
        ''' que lorsque le <c>ProjectStateChangeCount</c> a évolué —
        ''' 0 requête superflue entre deux modifications.
        ''' </summary>
        Public Class ReaperProjectMonitor
            Implements IDisposable

            ' ── Flag de changement d'état projet ──────────────────────────────────
            ' True au démarrage → force un premier poll (sync initiale).
            Private _stateChanged As Boolean = True
            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 50

            ' ── Cache ─────────────────────────────────────────────────────────────
            Private ReadOnly _lock As New Object()
            Private _lastCount As Integer = -1

            ' ── Événements ────────────────────────────────────────────────────────

            ''' <summary>Déclenché quand la connexion au bridge démarre.</summary>
            Public Event On_Connected()
            ''' <summary>Déclenché quand le thread de monitoring s'arrête.</summary>
            Public Event On_Disconnected()

            ''' <summary>
            ''' Déclenché quand le nombre de pistes du projet actif change.
            ''' <paramref name="count"/> = nouveau nombre de pistes (sans la piste maître).
            ''' </summary>
            Public Event On_TrackCountChanged(count As Integer)

            ' ── Propriétés ────────────────────────────────────────────────────────

            ''' <summary>
            ''' Nombre de pistes au dernier cycle de polling.
            ''' Retourne -1 si le monitoring n'a pas encore effectué de premier poll.
            ''' </summary>
            Public ReadOnly Property TrackCount As Integer
                Get
                    SyncLock _lock
                        Return _lastCount
                    End SyncLock
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

            ' ── Cycle de vie ──────────────────────────────────────────────────────

            ''' <summary>Démarre le thread de monitoring. Sans effet si déjà démarré.</summary>
            Public Sub Start()
                If IsRunning Then Return
                ' ── S'abonner au hub avant de démarrer ────────────────────────
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

            ''' <summary>Arrête le thread de monitoring et ferme la connexion pipe.</summary>
            Public Sub [Stop]()
                RemoveHandler ReaperPollHub.Instance.On_ProjectStateChange,
                    AddressOf OnProjectStateChange
                Threading.Volatile.Write(_isRunning, False)
            End Sub

            ' ── Boucle de monitoring ──────────────────────────────────────────────

            Private Sub MonitorLoop()
                RaiseEvent On_Connected()

                While Threading.Volatile.Read(_isRunning)
                    If Threading.Volatile.Read(_stateChanged) Then
                        Threading.Volatile.Write(_stateChanged, False)
                        PollTrackCount()
                    End If
                    Thread.Sleep(_interval)
                End While

                RaiseEvent On_Disconnected()
            End Sub

            Private Sub OnProjectStateChange(stateCount As Integer)
                Threading.Volatile.Write(_stateChanged, True)
            End Sub

            ' ── Détection du nombre de pistes ─────────────────────────────────────

            ''' <summary>
            ''' Interroge Reaper via <c>COUNT_TRACKS</c> sur le pipe persistant.
            ''' Déclenche <see cref="On_TrackCountChanged"/> uniquement si le
            ''' nombre a changé depuis le dernier appel.
            ''' </summary>
            Private Sub PollTrackCount()
                Dim raw = ReaperPollHub.Instance.Request("COUNT_TRACKS")
                If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return

                Dim newCount As Integer = -1
                If Not Integer.TryParse(raw, newCount) Then Return

                Dim prevCount As Integer
                SyncLock _lock
                    If newCount = _lastCount Then Return
                    prevCount = _lastCount
                    _lastCount = newCount
                End SyncLock

                ' ── Déclencher HORS lock ──────────────────────────────────────────
                RaiseEvent On_TrackCountChanged(newCount)
            End Sub

            ' ── IDisposable ───────────────────────────────────────────────────────

            ''' <summary>Arrête le monitoring et libère toutes les ressources.</summary>
            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
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
        Public Class ReaperSessionMonitor
            Implements IDisposable

            ' ── Thread ────────────────────────────────────────────────────────────
            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 500

            ' ── Cache ─────────────────────────────────────────────────────────────
            Private _lastFilePath As String = String.Empty
            Private ReadOnly _lastOpenProjects As New List(Of String)

            ' ── Événements ────────────────────────────────────────────────────────

            ''' <summary>
            ''' Déclenché quand le projet actif change (changement d'onglet dans Reaper).
            ''' </summary>
            ''' <param name="newFilePath">Chemin du nouveau projet actif (vide si jamais sauvegardé).</param>
            ''' <param name="previousFilePath">Chemin du projet précédent.</param>
            Public Event On_ActiveProjectChanged(newFilePath As String, previousFilePath As String)

            ''' <summary>
            ''' Déclenché quand la liste des projets ouverts change
            ''' (nouveau projet ouvert ou projet fermé).
            ''' </summary>
            Public Event On_OpenProjectsChanged(projects As IReadOnlyList(Of String))

            ' ── Propriétés ────────────────────────────────────────────────────────

            ''' <summary>Intervalle de polling en ms (défaut : 500 ms).</summary>
            Public Property Interval As Integer
                Get
                    Return _interval
                End Get
                Set
                    _interval = Value
                End Set
            End Property

            ''' <summary>True si le thread de surveillance est actif.</summary>
            Public ReadOnly Property IsRunning As Boolean
                Get
                    Return Threading.Volatile.Read(_isRunning)
                End Get
            End Property

            ''' <summary>
            ''' Chemin du projet actif au dernier cycle de polling.
            ''' Vide si le projet n'a jamais été sauvegardé.
            ''' </summary>
            Public ReadOnly Property CurrentFilePath As String
                Get
                    Return _lastFilePath
                End Get
            End Property

            ' ── Cycle de vie ──────────────────────────────────────────────────────

            ''' <summary>
            ''' Démarre le thread de surveillance.
            ''' Initialise le cache à partir de l'état courant de Reaper avant de démarrer —
            ''' appeler après un premier <see cref="ReaperProject.Refresh"/> pour garantir
            ''' que Reaper est joignable.
            ''' Sans effet si déjà démarré.
            ''' </summary>
            Public Sub Start()
                If IsRunning Then Return

                ' ── Snapshot initial pour ne pas déclencher d'événements au 1er cycle ─
                _lastFilePath = utility.GetProjectFilePath()
                _lastOpenProjects.Clear()
                _lastOpenProjects.AddRange(utility.EnumAllProjects())

                Threading.Volatile.Write(_isRunning, True)
                _thread = New Thread(AddressOf MonitorLoop) With {
                .Name = "ReaperProjectMonitor",
                .IsBackground = True,
                .Priority = ThreadPriority.BelowNormal
            }
                _thread.Start()
            End Sub

            ''' <summary>Arrête le thread de surveillance.</summary>
            Public Sub [Stop]()
                Threading.Volatile.Write(_isRunning, False)
            End Sub

            ' ── Boucle de monitoring ──────────────────────────────────────────────

            Private Sub MonitorLoop()
                While Threading.Volatile.Read(_isRunning)
                    Thread.Sleep(_interval)
                    PollProject()
                End While
            End Sub

            ''' <summary>
            ''' Vérifie le projet actif et la liste des projets ouverts.
            ''' Déclenche les événements uniquement si un changement est détecté.
            ''' </summary>
            Private Sub PollProject()
                ' ── Projet actif ──────────────────────────────────────────────────
                Dim currentPath = utility.GetProjectFilePath()
                If currentPath <> _lastFilePath Then
                    Dim previous = _lastFilePath
                    _lastFilePath = currentPath
                    RaiseEvent On_ActiveProjectChanged(currentPath, previous)
                End If

                ' ── Projets ouverts ───────────────────────────────────────────────
                Dim openProjects = utility.EnumAllProjects()
                Dim changed = openProjects.Count <> _lastOpenProjects.Count
                If Not changed Then
                    For i = 0 To openProjects.Count - 1
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

            ' ── IDisposable ───────────────────────────────────────────────────────

            ''' <summary>Arrête le monitoring et libère les ressources.</summary>
            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
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

            ''' <summary>Arrête le thread de monitoring et ferme la connexion pipe.</summary>
            Public Sub [Stop]()
                RemoveHandler ReaperPollHub.Instance.On_ProjectStateChange,
                    AddressOf OnProjectStateChange
                Threading.Volatile.Write(_isRunning, False)
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
                    Dim raw = ReaperPollHub.Instance.Request($"GET_FX_PARAM_NORM|{_trackIndex}|{_fxIndex}|{entry.paramIndex}")
                    If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Continue For

                    Dim newVal As Double = Double.MinValue
                    If Not Double.TryParse(raw, Globalization.NumberStyles.Any, ic, newVal) Then Continue For

                    Dim changed As Boolean
                    Dim prevVal As Double = Double.MinValue

                    SyncLock _lock
                        changed = Not _cache.TryGetValue(entry.paramIndex, prevVal) OrElse prevVal <> newVal
                        If changed Then _cache(entry.paramIndex) = newVal
                    End SyncLock

                    ' ── Événement uniquement si changé ET non suspendu ────────────
                    If changed AndAlso Not entry.isSuspended Then
                        RaiseEvent On_ParamChange(entry.paramIndex, newVal)
                    End If
                Next
            End Sub

            ' ── IDisposable ───────────────────────────────────────────────────────

            ''' <summary>Arrête le monitoring et libère toutes les ressources.</summary>
            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
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
        Public Class ReaperFxEnvelopeParamMonitor
            Implements IDisposable

            Private ReadOnly _trackIndex As Integer
            Private ReadOnly _fxIndex As Integer
            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 50
            Private _isPlaying As Boolean = False          ' ← AJOUT : état lecture

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
                ' ── S'abonner au hub pour les événements de transport ──────────
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
            End Sub

            ' ── Handlers hub ──────────────────────────────────────────────────

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
                    ' ── PollParams uniquement si Reaper est en lecture ─────────
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
                    ' ── Requête via le hub partagé ────────────────────────────
                    Dim raw = ReaperPollHub.Instance.Request(
                        $"GET_FX_PARAM_NORM|{_trackIndex}|{_fxIndex}|{entry.paramIndex}")
                    If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Continue For

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

                    If changed Then RaiseEvent On_ValueChange(entry.paramIndex, newVal)
                Next
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
                    ' Pas de _pipe.Dispose() — le hub est partagé
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

            Private _stateChanged As Boolean = True
            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 50

            ' ── Collection de pistes surveillées ──────────────────────────────
            ' Clé = trackIndex
            ' Valeur = (Track, IsSuspended)
            ' Pas de caches internes : ReaperTrack.Refresh() gère tout.
            Private ReadOnly _lock As New Object()
            Private ReadOnly _tracks As New Dictionary(Of Integer, (Track As ReaperTrack, IsSuspended As Boolean))
            ' Lambdas de RemoveHandler stockées pour un désabonnement propre.
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
                ' ── S'abonner au hub avant de démarrer ────────────────────────
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
            End Sub

            Private Sub MonitorLoop()
                RaiseEvent On_Connected()
                While Threading.Volatile.Read(_isRunning)
                    If Threading.Volatile.Read(_stateChanged) Then
                        Threading.Volatile.Write(_stateChanged, False)
                        PollTracks()
                    End If
                    Thread.Sleep(_interval)
                End While
                RaiseEvent On_Disconnected()
            End Sub

            Private Sub OnProjectStateChange(stateCount As Integer)
                Threading.Volatile.Write(_stateChanged, True)
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
                    entry.Track.Refresh()
                Next
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
                    ClearWatched()
                    ' Pas de _pipe.Dispose() — le hub est partagé
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
        ''' <see cref="ReaperFxEnvelopeParamMonitor"/> : pendant la suspension,
        ''' les caches ne sont pas mis à jour. À la reprise, les caches sont effacés
        ''' → resync immédiat avec les valeurs courantes de Reaper.
        ''' </summary>

        Public Class ReaperTrackEnvelopeMonitor
            Implements IDisposable

            ' ── Plus de _pipe individuel — on utilise ReaperPollHub ───────────
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

            Public Sub WatchTracks(firstIndex As Integer, count As Integer)
                SyncLock _lock
                    For i = firstIndex To firstIndex + count - 1
                        If Not _tracks.ContainsKey(i) Then
                            _tracks(i) = False
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

                    ' ── Vol + Pan (1 requête via le hub) ──────────────────────
                    Dim rawVP = ReaperPollHub.Instance.Request(
                        $"GET_TRACK_UI_VOL_PAN|{tIdx}")
                    If Not String.IsNullOrEmpty(rawVP) AndAlso
                       Not rawVP.StartsWith("ERROR") Then
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
                                ' ── Déclencher HORS lock ──────────────────────
                                If volChg Then RaiseEvent On_VolChange(tIdx, newVol)
                                If panChg Then RaiseEvent On_PanChange(tIdx, newPan)
                            End If
                        End If
                    End If

                    ' ── Mute (1 requête séparée via le hub) ───────────────────
                    Dim rawMute = ReaperPollHub.Instance.Request(
                        $"GET_TRACK_UI_MUTE|{tIdx}")
                    If Not String.IsNullOrEmpty(rawMute) AndAlso
                       Not rawMute.StartsWith("ERROR") Then
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
                    ' Pas de _pipe.Dispose() — le hub est partagé
                    _disposed = True
                End If
            End Sub

        End Class

        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER TRANSPORT MONITOR — surveillance de l'état de transport
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Surveille en continu l'état de transport Reaper :
        ''' play/pause/stop/rec (via <c>POLL_STATE</c>, immédiat) et
        ''' loop, quick-punch, sélection temporelle (via <c>ProjectStateChange</c>, gated).
        ''' <br/>
        ''' Utilise un <see cref="ReaperPollPipe"/> persistant partagé pour les
        ''' deux familles de requêtes.
        ''' </summary>

        Public Class ReaperTransportMonitor
            Implements IDisposable

            ' ── Plus de _pipe individuel — on utilise ReaperPollHub ───────────

            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 50
            Private _stateChanged As Boolean = True
            Private _suspended As Boolean = False

            Private ReadOnly _lock As New Object()
            Private _isLooping As Boolean = False
            Private _loopIn As Double = 0
            Private _loopOut As Double = 0
            Private _isQuickPunch As Boolean = False
            Private _timeSelIn As Double = 0
            Private _timeSelOut As Double = 0

            Public Event On_Connected()
            Public Event On_Disconnected()
            Public Event On_PlayStateChange(playState As Integer)
            Public Event On_PlayPositionChange(playPos As Double)
            Public Event On_LoopChange(isLooping As Boolean, loopIn As Double, loopOut As Double)
            Public Event On_QuickPunchChange(isQuickPunch As Boolean)
            Public Event On_TimeSelectionChange(selIn As Double, selOut As Double)

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

            ' ── Suspension ────────────────────────────────────────────────────

            ''' <summary>
            ''' Suspend tous les événements du moniteur.
            ''' Appeler avant d'envoyer une commande transport pour éviter
            ''' que le changement produit par la commande ne reboucle vers la surface.
            ''' </summary>
            Public Sub SuspendMonitor()
                Threading.Volatile.Write(_suspended, True)
            End Sub

            ''' <summary>
            ''' Réactive les événements et force un re-poll immédiat au prochain cycle.
            ''' </summary>
            Public Sub ResumeMonitor()
                Threading.Volatile.Write(_stateChanged, True)
                Threading.Volatile.Write(_suspended, False)
            End Sub

            ' ── Cycle de vie ──────────────────────────────────────────────────

            Public Sub Start()
                If IsRunning Then Return
                AddHandler ReaperPollHub.Instance.On_ProjectStateChange,
                    AddressOf OnProjectStateChange
                AddHandler ReaperPollHub.Instance.On_PlayStateChange,
                    AddressOf OnHubPlayStateChange
                AddHandler ReaperPollHub.Instance.On_PlayPositionChange,
                    AddressOf OnHubPlayPositionChange
                Threading.Volatile.Write(_isRunning, True)
                _thread = New Thread(AddressOf MonitorLoop) With {
                    .Name = "TransportMonitor",
                    .IsBackground = True,
                    .Priority = ThreadPriority.Normal
                }
                _thread.Start()
            End Sub

            Public Sub [Stop]()
                RemoveHandler ReaperPollHub.Instance.On_ProjectStateChange,
                    AddressOf OnProjectStateChange
                RemoveHandler ReaperPollHub.Instance.On_PlayStateChange,
                    AddressOf OnHubPlayStateChange
                RemoveHandler ReaperPollHub.Instance.On_PlayPositionChange,
                    AddressOf OnHubPlayPositionChange
                Threading.Volatile.Write(_isRunning, False)
            End Sub

            ' ── Handlers hub ──────────────────────────────────────────────────

            Private Sub OnHubPlayStateChange(playState As Integer)
                If Not Threading.Volatile.Read(_suspended) Then
                    RaiseEvent On_PlayStateChange(playState)
                End If
            End Sub

            Private Sub OnHubPlayPositionChange(playPos As Double)
                If Not Threading.Volatile.Read(_suspended) Then
                    RaiseEvent On_PlayPositionChange(playPos)
                End If
            End Sub

            Private Sub OnProjectStateChange(stateCount As Integer)
                Threading.Volatile.Write(_stateChanged, True)
            End Sub

            ' ── Boucle ────────────────────────────────────────────────────────

            Private Sub MonitorLoop()
                RaiseEvent On_Connected()

                While Threading.Volatile.Read(_isRunning)
                    ' ── PollTransportState uniquement sur ProjectStateChange ───
                    If Threading.Volatile.Read(_stateChanged) Then
                        Threading.Volatile.Write(_stateChanged, False)
                        PollTransportState()
                    End If
                    Thread.Sleep(_interval)
                End While

                RaiseEvent On_Disconnected()
            End Sub

            Private Sub PollTransportState()
                If Threading.Volatile.Read(_suspended) Then Return

                ' ── Requête via le hub partagé ────────────────────────────────
                Dim raw = ReaperPollHub.Instance.Request("GET_TRANSPORT_STATE")
                If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return

                Dim p = raw.Split("|"c)
                If p.Length < 6 Then Return

                Dim ic = CultureInfo.InvariantCulture

                Dim newLooping = p(0) = "1"
                Dim newLoopIn As Double = 0
                Dim newLoopOut As Double = 0
                Double.TryParse(p(1), Globalization.NumberStyles.Any, ic, newLoopIn)
                Double.TryParse(p(2), Globalization.NumberStyles.Any, ic, newLoopOut)

                Dim newQuickPunch = p(3) = "1"

                Dim newTimeSelIn As Double = 0
                Dim newTimeSelOut As Double = 0
                Double.TryParse(p(4), Globalization.NumberStyles.Any, ic, newTimeSelIn)
                Double.TryParse(p(5), Globalization.NumberStyles.Any, ic, newTimeSelOut)

                Dim loopChg As Boolean
                Dim qpChg As Boolean
                Dim timeSelChg As Boolean

                SyncLock _lock
                    loopChg = _isLooping <> newLooping OrElse
                              _loopIn <> newLoopIn OrElse
                              _loopOut <> newLoopOut
                    If loopChg Then
                        _isLooping = newLooping
                        _loopIn = newLoopIn
                        _loopOut = newLoopOut
                    End If

                    qpChg = _isQuickPunch <> newQuickPunch
                    If qpChg Then _isQuickPunch = newQuickPunch

                    timeSelChg = _timeSelIn <> newTimeSelIn OrElse _timeSelOut <> newTimeSelOut
                    If timeSelChg Then
                        _timeSelIn = newTimeSelIn
                        _timeSelOut = newTimeSelOut
                    End If
                End SyncLock

                If loopChg Then RaiseEvent On_LoopChange(newLooping, newLoopIn, newLoopOut)
                If qpChg Then RaiseEvent On_QuickPunchChange(newQuickPunch)
                If timeSelChg Then RaiseEvent On_TimeSelectionChange(newTimeSelIn, newTimeSelOut)
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
                    ' Pas de _pipe.Dispose() — le hub est partagé
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
            Private _writer As IO.StreamWriter
            Private _reader As IO.StreamReader
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
            ''' 0=stop  1=play  2=pause  4=rec  5=rec+play
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
            Public Function Connect() As Boolean
                Try
                    Disconnect()
                    _pipe = New IO.Pipes.NamedPipeClientStream(
                        ".", "c24_reaper_poll", IO.Pipes.PipeDirection.InOut)
                    _pipe.Connect(timeout:=500)
                    _writer = New IO.StreamWriter(_pipe) With {.AutoFlush = True}
                    _reader = New IO.StreamReader(_pipe)
                    Return True
                Catch
                    Disconnect()
                    Return False
                End Try
            End Function

            ''' <summary>
            ''' Ferme la connexion persistante et réinitialise le cache.
            ''' </summary>
            Public Sub Disconnect()
                Try
                    _writer = Nothing
                    _reader = Nothing
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
            Public Sub Poll()
                If Not IsConnected Then
                    If Not Connect() Then Return
                End If

                Dim raw As String
                Try
                    _writer.WriteLine("POLL_STATE")
                    raw = _reader.ReadLine()
                Catch
                    Disconnect()
                    Return
                End Try

                If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return
                Dim p = raw.Split("|"c)
                If p.Length < 3 Then Return

                ' ── Compteur d'état projet ────────────────────────────────────────
                Dim new_stateCount As Integer = 0
                Integer.TryParse(p(0), new_stateCount)
                If _lastStateCount <> new_stateCount Then
                    _lastStateCount = new_stateCount
                    RaiseEvent On_ProjectStateChange(_lastStateCount)
                End If

                ' ── État de lecture ───────────────────────────────────────────────
                Dim new_playState As Integer = 0
                Integer.TryParse(p(1), new_playState)
                If _lastPlayState <> new_playState Then
                    _lastPlayState = new_playState
                    RaiseEvent On_PlayStateChange(_lastPlayState)
                End If

                ' ── Position de lecture ───────────────────────────────────────────
                Dim new_playPos As Double = 0
                Double.TryParse(p(2), NumberStyles.Any, Ic, new_playPos)
                If _lastPlayPos <> new_playPos Then
                    _lastPlayPos = new_playPos
                    RaiseEvent On_PlayPositionChange(_lastPlayPos)
                End If
            End Sub
            Public Sub Log_Poll()
                If Not IsConnected Then
                    Dim ok = Connect()
                    Console.WriteLine($"[PollPipe] Connect → {ok}")
                    If Not ok Then Return
                End If

                Dim raw As String
                Try
                    _writer.WriteLine("POLL_STATE")
                    raw = _reader.ReadLine()
                Catch ex As Exception
                    Console.WriteLine($"[PollPipe] Erreur lecture : {ex.Message}")
                    Disconnect()
                    Return
                End Try

                Console.WriteLine($"[PollPipe] Réponse brute : '{raw}'")

                If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return
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
                    _writer.WriteLine(query)
                    Return If(_reader.ReadLine(), String.Empty)
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

            ' ── Singleton ─────────────────────────────────────────────────────────
            Private Shared ReadOnly _instance As New ReaperPollHub()

            ''' <summary>Instance unique partagée par tous les moniteurs.</summary>
            Public Shared ReadOnly Property Instance As ReaperPollHub
                Get
                    Return _instance
                End Get
            End Property

            ' ── Pipe + thread ─────────────────────────────────────────────────────
            Private ReadOnly _pipe As New ReaperPollPipe()
            Private _thread As Thread
            Private _isRunning As Boolean = False
            Private _disposed As Boolean = False
            Private _interval As Integer = 50
            Private ReadOnly _lock As New Object()

            ' ── Événements distribués à tous les abonnés ──────────────────────────

            ''' <summary>Déclenché quand le <c>ProjectStateChangeCount</c> Reaper change.</summary>
            Public Event On_ProjectStateChange(stateCount As Integer)

            ''' <summary>Déclenché quand l'état de lecture Reaper change.</summary>
            Public Event On_PlayStateChange(playState As Integer)

            ''' <summary>Déclenché quand la position de lecture change (secondes).</summary>
            Public Event On_PlayPositionChange(playPos As Double)

            ' ── Constructeur privé (singleton) ────────────────────────────────────

            Private Sub New()
            End Sub

            ' ── Propriétés ────────────────────────────────────────────────────────

            ''' <summary>Intervalle de polling en ms (défaut : 50 ms).</summary>
            Public Property Interval As Integer
                Get
                    Return _interval
                End Get
                Set
                    _interval = Value
                End Set
            End Property

            ''' <summary>True si le thread de polling est actif.</summary>
            Public ReadOnly Property IsRunning As Boolean
                Get
                    Return Threading.Volatile.Read(_isRunning)
                End Get
            End Property

            ''' <summary>True si la connexion pipe vers le bridge est active.</summary>
            Public ReadOnly Property IsConnected As Boolean
                Get
                    Return _pipe.IsConnected
                End Get
            End Property

            ' ── Cycle de vie ──────────────────────────────────────────────────────

            ''' <summary>
            ''' Démarre le thread de polling central. Sans effet si déjà démarré.
            ''' Appeler une seule fois au démarrage de l'application.
            ''' </summary>
            Public Sub Start()
                SyncLock _lock
                    If IsRunning Then Return
                    Threading.Volatile.Write(_isRunning, True)
                    _thread = New Thread(AddressOf PollLoop) With {
                        .Name = "ReaperPollHub",
                        .IsBackground = True,
                        .Priority = ThreadPriority.AboveNormal
                    }
                    _thread.Start()
                End SyncLock
            End Sub

            ''' <summary>Arrête le thread de polling et ferme la connexion pipe.</summary>
            Public Sub [Stop]()
                Threading.Volatile.Write(_isRunning, False)
                _pipe.Disconnect()
            End Sub

            ' ── Boucle centrale ───────────────────────────────────────────────────

            Private Sub PollLoop()
                ' Câbler les événements du pipe vers les événements du hub
                AddHandler _pipe.On_ProjectStateChange,
                    Sub(sc As Integer) RaiseEvent On_ProjectStateChange(sc)
                AddHandler _pipe.On_PlayStateChange,
                    Sub(ps As Integer) RaiseEvent On_PlayStateChange(ps)
                AddHandler _pipe.On_PlayPositionChange,
                    Sub(pp As Double) RaiseEvent On_PlayPositionChange(pp)

                While Threading.Volatile.Read(_isRunning)
                    _pipe.Poll()
                    Thread.Sleep(_interval)
                End While
            End Sub

            ''' <summary>
            ''' Envoie une requête arbitraire sur la connexion persistante du hub.
            ''' Reconnecte automatiquement si la connexion est perdue.
            ''' </summary>
            Public Function Request(query As String) As String
                Return _pipe.Request(query)
            End Function

            ' ── IDisposable ───────────────────────────────────────────────────────

            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _disposed Then
                    [Stop]()
                    _pipe.Dispose()
                    _disposed = True
                End If
            End Sub

        End Class


    End Namespace

    Namespace PageManagers

        ''' <summary>
        ''' Gère une collection de paramètres FX paginés et surveille leurs valeurs
        ''' via <see cref="Monitors.ReaperFxParamMonitor"/> (gated) et
        ''' <see cref="Monitors.ReaperFxEnvelopeParamMonitor"/> (continu, automation).
        ''' </summary>
        Public Class ReaperTrackFxParamsManager
            Implements IDisposable

            ' ── Identité FX (fixe à la construction) ──────────────────────────
            Private ReadOnly _trackIndex As Integer
            Private ReadOnly _fxIndex As Integer
            Private ReadOnly _MaxParamNbPerPage As Integer

            ' ── Moniteurs ─────────────────────────────────────────────────────
            Private _FxMonitor As Monitors.ReaperFxParamMonitor
            Private _EnvMonitor As Monitors.ReaperFxEnvelopeParamMonitor

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
                    _EnvMonitor = New Monitors.ReaperFxEnvelopeParamMonitor(_trackIndex, _fxIndex)
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
            ''' Suspend ou réactive l'<see cref="Monitors.ReaperFxEnvelopeParamMonitor"/>
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
                _ReaperTrackFxParams(pageIdx).SetValueFromMonitor(normValue)
            End Sub

            Private Sub On_FXParamChange(paramIndex As Integer, normValue As Double)
                Dim pageIdx = GetPageParmIndex(paramIndex)
                If pageIdx < 0 OrElse pageIdx >= _ReaperTrackFxParams?.Count Then Return
                _ReaperTrackFxParams(pageIdx).SetValueFromMonitor(normValue)
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

            Private ReadOnly _project As ReaperProject
            Private _TrackMonitor As Monitors.ReaperTrackMonitor
            Private _EnvMonitor As Monitors.ReaperTrackEnvelopeMonitor
            Private _FxManager As ReaperTrackFxManager         ' ← AJOUT
            Private ReadOnly _maxTracksNbPerPage As Integer
            Private ReadOnly _all_Tracks As New List(Of List(Of ReaperTrack))
            Private ReadOnly _all_VolEnvelopes As New List(Of List(Of ReaperTrackVolEnvelope))
            Private ReadOnly _all_PanEnvelopes As New List(Of List(Of ReaperTrackPanEnvelope))
            Private ReadOnly _all_MuteEnvelopes As New List(Of List(Of ReaperTrackMuteEnvelope))
            Private _Tracks As List(Of ReaperTrack)
            Private _VolEnvelopes As List(Of ReaperTrackVolEnvelope)
            Private _PanEnvelopes As List(Of ReaperTrackPanEnvelope)
            Private _MuteEnvelopes As List(Of ReaperTrackMuteEnvelope)
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

            Public ReadOnly Property Tracks As List(Of ReaperTrack)
                Get
                    Return _Tracks
                End Get
            End Property

            Public ReadOnly Property VolEnvelopes As List(Of ReaperTrackVolEnvelope)
                Get
                    Return _VolEnvelopes
                End Get
            End Property

            Public ReadOnly Property PanEnvelopes As List(Of ReaperTrackPanEnvelope)
                Get
                    Return _PanEnvelopes
                End Get
            End Property

            Public ReadOnly Property MuteEnvelopes As List(Of ReaperTrackMuteEnvelope)
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
                Dim fxCollection = _project.GetTrackFX(trackIndex)
                If fxCollection Is Nothing Then Return True
                For i = 0 To fxCollection.Count - 1
                    If _sessionConfig.IsEQPlugin(fxCollection(i).Name) Then
                        Return False
                    End If
                Next
                Return True
            End Function

            Public Function IsTrackDynFxEmpty(trackIndex As Integer) As Boolean
                If _sessionConfig Is Nothing Then Return True
                Dim fxCollection = _project.GetTrackFX(trackIndex)
                If fxCollection Is Nothing Then Return True
                For i = 0 To fxCollection.Count - 1
                    If _sessionConfig.IsDynPlugin(fxCollection(i).Name) Then
                        Return False
                    End If
                Next
                Return True
            End Function

            Public Function IsTrackGenericFxEmpty(trackIndex As Integer) As Boolean
                If _sessionConfig Is Nothing Then Return True
                Dim fxCollection = _project.GetTrackFX(trackIndex)
                If fxCollection Is Nothing Then Return True
                For i = 0 To fxCollection.Count - 1
                    Dim fxName = fxCollection(i).Name
                    If Not _sessionConfig.IsEQPlugin(fxName) AndAlso
                       Not _sessionConfig.IsDynPlugin(fxName) Then
                        Return False
                    End If
                Next
                Return True
            End Function

            ' ── Constructeur ──────────────────────────────────────────────────

            Public Sub New(proj As ReaperProject,
                           Optional maxTracksNb As Integer = 24,
                           Optional sessionConfig As ReaperSessionConfig = Nothing)
                _project = proj
                _maxTracksNbPerPage = maxTracksNb
                _sessionConfig = sessionConfig

                AddHandler _project.On_TrackGuidsChanged, AddressOf OnTrackGuidsChanged

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
                RaiseEvent On_TracksRebuilt()
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

                For i = 0 To _all_Tracks.Count - 1
                    _all_VolEnvelopes.Add(Nothing)
                    _all_PanEnvelopes.Add(Nothing)
                    _all_MuteEnvelopes.Add(Nothing)
                Next
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
                SuspendTrack(pageTrackIndex)
                Tracks(pageTrackIndex).Mute = status
                ResumeTrack(pageTrackIndex)
            End Sub

            Public Sub SetVolume(pageTrackIndex As Integer, vol As Double)
                SuspendTrack(pageTrackIndex)
                Tracks(pageTrackIndex).Vol = vol
                ResumeTrack(pageTrackIndex)
            End Sub

            Public Sub SetPan(pageTrackIndex As Integer, pan As Double)
                SuspendTrack(pageTrackIndex)
                Tracks(pageTrackIndex).Pan = pan
                ResumeTrack(pageTrackIndex)
            End Sub

            Public Sub SetSelect(pageTrackIndex As Integer, selected As Integer)
                SuspendTrack(pageTrackIndex)
                Tracks(pageTrackIndex).Selected = selected
                ResumeTrack(pageTrackIndex)
            End Sub

            Public Sub ToggleSelect(pageTrackIndex As Integer)
                Dim current = Tracks(pageTrackIndex).Selected
                SetSelect(pageTrackIndex, If(current = 0, 1, 0))
            End Sub

            Public Sub SetAutoMode(pageTrackIndex As Integer, autoMode As Integer)
                SuspendTrack(pageTrackIndex)
                Tracks(pageTrackIndex).AutoMode = autoMode
                ResumeTrack(pageTrackIndex)
            End Sub

            Public Sub CycleAutoMode(pageTrackIndex As Integer)
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
                SuspendTrack(pageTrackIndex)
                Tracks(pageTrackIndex).RecArm = recArm
                ResumeTrack(pageTrackIndex)
            End Sub

            Public Sub ToggleRecArm(pageTrackIndex As Integer)
                Dim current = Tracks(pageTrackIndex).RecArm
                SetRecArm(pageTrackIndex, If(current = 0, 1, 0))
            End Sub

            Public Sub Set_SoloStatus(pageTrackIndex As Integer,
                                              soloState As SoloState)
                SuspendTrack(pageTrackIndex)
                Tracks(pageTrackIndex).Solo = CInt(soloState)
                ResumeTrack(pageTrackIndex)
            End Sub

            Public Sub ToggleSolo(pageTrackIndex As Integer)
                SuspendTrack(pageTrackIndex)
                Dim current = Tracks(pageTrackIndex).Solo
                Dim next_state = If(current = 0, SoloState.Solo, SoloState.Off)
                Set_SoloStatus(pageTrackIndex, next_state)
                ResumeTrack(pageTrackIndex)
            End Sub

            ' ── Navigation ────────────────────────────────────────────────────

            Public Sub Set_Next_Page()
                _Actual_PageNb = Math.Min(_all_Tracks.Count - 1, _Actual_PageNb + 1)
                Set_Tracks(_Actual_PageNb)
            End Sub

            Public Sub Set_Prev_Page()
                _Actual_PageNb = Math.Max(0, _Actual_PageNb - 1)
                Set_Tracks(_Actual_PageNb)
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

                    _EnvMonitor?.Dispose()
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

                    If _all_VolEnvelopes(pageNb) Is Nothing Then
                        Dim volPage As New List(Of ReaperTrackVolEnvelope)
                        Dim panPage As New List(Of ReaperTrackPanEnvelope)
                        Dim mutePage As New List(Of ReaperTrackMuteEnvelope)
                        For Each trk In _Tracks
                            volPage.Add(New ReaperTrackVolEnvelope(trk.TrackIndex))
                            panPage.Add(New ReaperTrackPanEnvelope(trk.TrackIndex))
                            mutePage.Add(New ReaperTrackMuteEnvelope(trk.TrackIndex))
                        Next
                        _all_VolEnvelopes(pageNb) = volPage
                        _all_PanEnvelopes(pageNb) = panPage
                        _all_MuteEnvelopes(pageNb) = mutePage
                    End If

                    _VolEnvelopes = _all_VolEnvelopes(pageNb)
                    _PanEnvelopes = _all_PanEnvelopes(pageNb)
                    _MuteEnvelopes = _all_MuteEnvelopes(pageNb)

                    _TrackMonitor.WatchTracks(_Tracks)



                    For Each trk In _Tracks
                        _EnvMonitor.WatchTrack(trk.TrackIndex)
                    Next

                    _TrackMonitor.Start()
                                                               _EnvMonitor.Start()

                Finally
                    _pageLock.ExitWriteLock()
                End Try
            End Sub

            ' ── Handlers EnvMonitor ───────────────────────────────────────────

            Private Sub On_EnvVolChange(trackIndex As Integer, vol As Double)
                Dim pageIdx As Integer = -1
                If Not _trackPageMap.TryGetValue(trackIndex, pageIdx) Then Return
                _VolEnvelopes(pageIdx).SetValueFromMonitor(vol)
            End Sub

            Private Sub On_EnvPanChange(trackIndex As Integer, pan As Double)
                Dim pageIdx As Integer = -1
                If Not _trackPageMap.TryGetValue(trackIndex, pageIdx) Then Return
                _PanEnvelopes(pageIdx).SetValueFromMonitor(pan)
            End Sub

            Private Sub On_EnvMuteChange(trackIndex As Integer, mute As Boolean)
                Dim pageIdx As Integer = -1
                If Not _trackPageMap.TryGetValue(trackIndex, pageIdx) Then Return
                _MuteEnvelopes(pageIdx).SetValueFromMonitor(If(mute, 1.0, 0.0))
            End Sub

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
                        RemoveHandler _project.On_TrackGuidsChanged,
                                      AddressOf OnTrackGuidsChanged
                        _TrackMonitor?.Dispose()
                        _EnvMonitor?.Dispose()
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

        End Class

        ' ══════════════════════════════════════════════════════════════════════════
        '  REAPER TRACK SEND MANAGER — envoi N paginé sur une liste de pistes
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Gère de manière paginée l'envoi <see cref="SendIndex"/> pour une liste
        ''' fixe de <see cref="ReaperTrack"/>.
        ''' <br/>
        ''' Pour chaque page, un <see cref="Monitors.ReaperSendMonitor"/> surveille
        ''' vol/pan/mute et propage les changements vers les <see cref="ReaperSend"/>
        ''' via <c>SetXxxFromMonitor</c>, déclenchant leurs propres événements.
        ''' <br/>
        ''' La liste de pistes source est fixe à la construction — utiliser
        ''' <see cref="ReaperTracksManager"/> si elle doit réagir aux changements
        ''' structurels du projet.
        ''' </summary>
        Public Class ReaperTrackSendManager
            Implements IDisposable

            ' ── Identité de l'envoi (fixe) ────────────────────────────────────
            Private ReadOnly _sendIndex As Integer
            Private ReadOnly _category As Integer
            Private ReadOnly _maxTracksNbPerPage As Integer

            ' ── Liste source de pistes (fixe à la construction) ───────────────
            Private ReadOnly _sourceTracks As IReadOnlyList(Of ReaperTrack)

            ' ── Moniteur ──────────────────────────────────────────────────────
            Private _SendMonitor As Monitors.ReaperSendMonitor

            ' ── Pages ─────────────────────────────────────────────────────────
            ' Chaque page = liste de ReaperSend (un par piste, tous au même sendIndex)
            Private ReadOnly _all_Sends As New List(Of List(Of ReaperSend))
            Private _Sends As List(Of ReaperSend)
            Private _Actual_PageNb As Integer = 0
            Private ReadOnly _trackPageMap As New Dictionary(Of Integer, Integer)

            ' ── Verrou ────────────────────────────────────────────────────────
            Private ReadOnly _pageLock As New ReaderWriterLockSlim()
            Private disposedValue As Boolean

            ' ── Événements ────────────────────────────────────────────────────

            ''' <summary>
            ''' Déclenché après chaque changement de page.
            ''' Abonnez-vous pour recâbler la surface (ré-affecter les vpots…).
            ''' </summary>
            Public Event On_SendsRebuilt()

            ' ── Propriétés ────────────────────────────────────────────────────

            ''' <summary>Index 0-based de l'envoi surveillé sur toutes les pistes.</summary>
            Public ReadOnly Property SendIndex As Integer
                Get
                    Return _sendIndex
                End Get
            End Property

            ''' <summary>Catégorie des envois (Sends/Receives/Hardware).</summary>
            Public ReadOnly Property Category As SendCategory
                Get
                    Return CType(_category, SendCategory)
                End Get
            End Property

            ''' <summary>Envois de la page courante (un par piste visible).</summary>
            Public ReadOnly Property Sends As List(Of ReaperSend)
                Get
                    Return _Sends
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
                    Return _all_Sends.Count
                End Get
            End Property

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Initialise le manager, construit les pages et démarre la surveillance.
            ''' </summary>
            ''' <param name="tracks">Liste fixe de pistes à paginer.</param>
            ''' <param name="sendIndex">Index 0-based de l'envoi à surveiller.</param>
            ''' <param name="category"><see cref="SendCategory"/> (défaut : Sends).</param>
            ''' <param name="maxTracksNbPerPage">Pistes max par page (défaut : 24).</param>
            Public Sub New(tracks As IReadOnlyList(Of ReaperTrack),
                           sendIndex As Integer,
                           Optional category As SendCategory = SendCategory.Sends,
                           Optional maxTracksNbPerPage As Integer = 24)
                _sourceTracks = tracks
                _sendIndex = sendIndex
                _category = CInt(category)
                _maxTracksNbPerPage = maxTracksNbPerPage

                ' ── Construire toutes les pages dès le départ ─────────────────
                ' Pas de lazy-creation ici : les ReaperSend sont légers (pas de pipe).
                RebuildAllPages()
                If _all_Sends.Count > 0 Then Set_Sends(0)
            End Sub

            ' ── Construction des pages ────────────────────────────────────────

            ''' <summary>
            ''' Découpe la liste source en pages de <c>maxTracksNbPerPage</c> pistes
            ''' et crée un <see cref="ReaperSend"/> par piste.
            ''' Appeler uniquement depuis le constructeur (liste source fixe).
            ''' </summary>
            Private Sub RebuildAllPages()
                _all_Sends.Clear()
                Dim page As New List(Of ReaperSend)
                For Each trk In _sourceTracks
                    If page.Count = _maxTracksNbPerPage Then
                        _all_Sends.Add(page)
                        page = New List(Of ReaperSend)
                    End If
                    page.Add(New ReaperSend(trk.TrackIndex, _category, _sendIndex))
                Next
                If page.Count > 0 Then _all_Sends.Add(page)
            End Sub

            ' ── Changement de page ────────────────────────────────────────────

            ''' <summary>
            ''' Dispose et recrée le moniteur pour la page donnée,
            ''' câble les handlers et démarre la surveillance.
            ''' Protégé par <see cref="_pageLock"/> (WriteLock).
            ''' </summary>
            Private Sub Set_Sends(pageNb As Integer)
                _pageLock.EnterWriteLock()
                Try
                    ' ── Recréer le moniteur ───────────────────────────────────
                    _SendMonitor?.Dispose()
                    _SendMonitor = New Monitors.ReaperSendMonitor(
                                       _sendIndex, CType(_category, SendCategory))
                    AddHandler _SendMonitor.On_VolChange, AddressOf On_MonitorVolChange
                    AddHandler _SendMonitor.On_PanChange, AddressOf On_MonitorPanChange
                    AddHandler _SendMonitor.On_MuteChange, AddressOf On_MonitorMuteChange

                    ' ── Pointer sur la page ───────────────────────────────────
                    _Sends = _all_Sends(pageNb)
                    _trackPageMap.Clear()
                    For i = 0 To _Sends.Count - 1
                        _trackPageMap(_Sends(i).TrackIndex) = i
                        _SendMonitor.WatchTrack(_Sends(i).TrackIndex)
                    Next

                    _SendMonitor.Start()

                Finally
                    _pageLock.ExitWriteLock()
                End Try
            End Sub

            ' ── Navigation ────────────────────────────────────────────────────

            ''' <summary>Passe à la page suivante si possible.</summary>
            Public Sub Set_Next_Page()
                _Actual_PageNb = Math.Min(_all_Sends.Count - 1, _Actual_PageNb + 1)
                Set_Sends(_Actual_PageNb)
                RaiseEvent On_SendsRebuilt()
            End Sub

            ''' <summary>Revient à la page précédente si possible.</summary>
            Public Sub Set_Prev_Page()
                _Actual_PageNb = Math.Max(0, _Actual_PageNb - 1)
                Set_Sends(_Actual_PageNb)
                RaiseEvent On_SendsRebuilt()
            End Sub

            ' ── Suspension par piste ──────────────────────────────────────────

            ''' <summary>Suspend le moniteur pour la piste de la page donnée.</summary>
            Public Sub SuspendTrack(pageTrackIndex As Integer)
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                If tIdx < 0 Then Return
                _SendMonitor?.SuspendTrack(tIdx)
            End Sub

            ''' <summary>Réactive le moniteur pour la piste de la page donnée.</summary>
            Public Sub ResumeTrack(pageTrackIndex As Integer)
                Dim tIdx = GetAbsoluteTrackIndex(pageTrackIndex)
                If tIdx < 0 Then Return
                _SendMonitor?.ResumeTrack(tIdx)
            End Sub

            ' ── Écriture depuis la surface ────────────────────────────────────

            ''' <summary>
            ''' Écrit le volume de l'envoi pour la piste de la page donnée.
            ''' Suspend le moniteur pendant l'écriture (anti-feedback).
            ''' </summary>
            ''' <param name="vol">Volume 0..4  (1.0 = 0 dB).</param>
            Public Sub Set_Vol(pageTrackIndex As Integer, vol As Double)
                _pageLock.EnterReadLock()
                Try
                    If pageTrackIndex < 0 OrElse pageTrackIndex >= _Sends?.Count Then Return
                    SuspendTrack(pageTrackIndex)
                    utility.SetTrackSendUIVol(_Sends(pageTrackIndex).TrackIndex, _sendIndex, vol)
                    ResumeTrack(pageTrackIndex)
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Sub

            ''' <summary>
            ''' Écrit le pan de l'envoi pour la piste de la page donnée.
            ''' Suspend le moniteur pendant l'écriture (anti-feedback).
            ''' </summary>
            ''' <param name="pan">Pan -1=Gauche  0=Centre  1=Droite.</param>
            Public Sub Set_Pan(pageTrackIndex As Integer, pan As Double)
                _pageLock.EnterReadLock()
                Try
                    If pageTrackIndex < 0 OrElse pageTrackIndex >= _Sends?.Count Then Return
                    SuspendTrack(pageTrackIndex)
                    utility.SetTrackSendUIPan(_Sends(pageTrackIndex).TrackIndex,
                                              _sendIndex, pan,
                                              CType(_category, SendCategory))
                    ResumeTrack(pageTrackIndex)
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Sub

            ''' <summary>
            ''' Impose l'état mute de l'envoi pour la piste de la page donnée.
            ''' Suspend le moniteur pendant l'écriture (anti-feedback).
            ''' </summary>
            Public Sub Set_Mute(pageTrackIndex As Integer, mute As Boolean)
                _pageLock.EnterReadLock()
                Try
                    If pageTrackIndex < 0 OrElse pageTrackIndex >= _Sends?.Count Then Return
                    SuspendTrack(pageTrackIndex)
                    utility.SetTrackSendUIMute(_Sends(pageTrackIndex).TrackIndex,
                                               _sendIndex, mute)
                    ResumeTrack(pageTrackIndex)
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Sub

            ''' <summary>
            ''' Bascule le mute de l'envoi pour la piste de la page donnée.
            ''' </summary>
            Public Sub ToggleMute(pageTrackIndex As Integer)
                _pageLock.EnterReadLock()
                Try
                    If pageTrackIndex < 0 OrElse pageTrackIndex >= _Sends?.Count Then Return
                    Dim current = _Sends(pageTrackIndex).Mute
                    Set_Mute(pageTrackIndex, Not current)
                Finally
                    _pageLock.ExitReadLock()
                End Try
            End Sub

            ' ── Lecture d'état ────────────────────────────────────────────────

            ''' <summary>Retourne la liste des volumes de la page courante.</summary>
            Public Function Get_VolStatus() As List(Of Double)
                Dim result As New List(Of Double)
                For Each s In _Sends
                    result.Add(s.Vol)
                Next
                Return result
            End Function

            ''' <summary>Retourne la liste des pans de la page courante.</summary>
            Public Function Get_PanStatus() As List(Of Double)
                Dim result As New List(Of Double)
                For Each s In _Sends
                    result.Add(s.Pan)
                Next
                Return result
            End Function

            ''' <summary>Retourne la liste des états mute de la page courante.</summary>
            Public Function Get_MuteStatus() As List(Of Boolean)
                Dim result As New List(Of Boolean)
                For Each s In _Sends
                    result.Add(s.Mute)
                Next
                Return result
            End Function

            ' ── Handlers internes ─────────────────────────────────────────────

            ''' <summary>
            ''' Reçoit les changements de vol depuis <see cref="Monitors.ReaperSendMonitor"/>
            ''' et les route vers le <see cref="ReaperSend"/> correspondant via
            ''' <c>SetVolFromMonitor</c>, qui déclenchera son propre événement
            ''' <c>On_VolChange</c> si la valeur diffère.
            ''' </summary>
            Private Sub On_MonitorVolChange(trackIndex As Integer, vol As Double)
                Dim pageIdx As Integer = -1
                If Not _trackPageMap.TryGetValue(trackIndex, pageIdx) Then Return
                _Sends(pageIdx).SetVolFromMonitor(vol)
            End Sub

            Private Sub On_MonitorPanChange(trackIndex As Integer, pan As Double)
                Dim pageIdx As Integer = -1
                If Not _trackPageMap.TryGetValue(trackIndex, pageIdx) Then Return
                _Sends(pageIdx).SetPanFromMonitor(pan)
            End Sub

            Private Sub On_MonitorMuteChange(trackIndex As Integer, mute As Boolean)
                Dim pageIdx As Integer = -1
                If Not _trackPageMap.TryGetValue(trackIndex, pageIdx) Then Return
                _Sends(pageIdx).SetMuteFromMonitor(mute)
            End Sub

            ' ── Utilitaires ───────────────────────────────────────────────────

            Private Function GetAbsoluteTrackIndex(pageTrackIndex As Integer) As Integer
                If _Sends Is Nothing OrElse
                   pageTrackIndex < 0 OrElse
                   pageTrackIndex >= _Sends.Count Then Return -1
                Return _Sends(pageTrackIndex).TrackIndex
            End Function

            ' ── IDisposable ───────────────────────────────────────────────────

            Protected Overridable Sub Dispose(disposing As Boolean)
                If Not disposedValue Then
                    If disposing Then
                        _SendMonitor?.Dispose()
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
                If Not String.IsNullOrEmpty(raw) AndAlso Not raw.StartsWith("ERROR") Then
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
                If tIdx < 0 Then Return 0
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
                If tIdx < 0 Then Return -1
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
                If idx < 0 OrElse idx >= _allOutputs.Count Then
                    Return New ReaperOutputChannel(-1, 0, String.Empty, False, -1)
                End If
                Return _allOutputs(idx)
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
                If tIdx < 0 Then Return Nothing
                Dim sends As List(Of ReaperSend) = Nothing
                If Not _trackHWSends.TryGetValue(tIdx, sends) Then Return Nothing
                If sendIndex < 0 OrElse sendIndex >= sends.Count Then Return Nothing
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
                    If tIdx < 0 Then Return False

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
                    If tIdx < 0 Then Return False
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
                    If tIdx < 0 Then Return
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
        '  REAPER PEAK LEVEL MANAGER — niveaux peak stéréo à la demande, pipe persistant
        ' ══════════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Gère la lecture des niveaux peak stéréo d'une collection de pistes Reaper.
        ''' <br/>
        ''' Le rafraîchissement est <b>à la demande</b> via <see cref="Refresh"/> —
        ''' typiquement appelé toutes les 50 ms depuis un timer ou un thread externe.
        ''' <br/>
        ''' La connexion pipe est <b>persistante</b> entre les appels, réutilisée via
        ''' <see cref="ReaperPollPipe.Request"/> : aucune surcharge
        ''' d'ouverture/fermeture à chaque requête (~0.1 ms économisé par piste).
        ''' <br/>
        ''' Convertir les valeurs linéaires en dB :
        ''' <see cref="utility.SLIDER2DB"/>(peak) ou via <see cref="GetPeakDB"/>.
        ''' </summary>
        Public Class ReaperPeakLevelManager
            Implements IDisposable

            ' ── Pipe persistant — reconnexion automatique si perdu ────────────
            Private ReadOnly _pipe As New ReaperPollPipe()

            ' ── Pistes surveillées ────────────────────────────────────────────
            ' Clé = trackIndex 0-based  |  Bool = True si suspendu
            Private ReadOnly _lock As New Object()
            Private ReadOnly _tracks As New Dictionary(Of Integer, Boolean)

            ' ── Caches peak courant (linéaire 0..1+  |  1.0 = 0 dBFS) ────────
            Private ReadOnly _peakL As New Dictionary(Of Integer, Double)
            Private ReadOnly _peakR As New Dictionary(Of Integer, Double)

            ' ── Peak hold — valeur max depuis le dernier reset ────────────────
            Private ReadOnly _holdL As New Dictionary(Of Integer, Double)
            Private ReadOnly _holdR As New Dictionary(Of Integer, Double)

            Private _disposed As Boolean = False

            ' ── Événements ────────────────────────────────────────────────────

            ''' <summary>
            ''' Déclenché quand les niveaux peak d'une piste changent.
            ''' Valeurs linéaires [0..1+] — 1.0 = 0 dBFS.
            ''' Convertir avec <see cref="utility.SLIDER2DB"/>.
            ''' </summary>
            Public Event On_PeakChange(trackIndex As Integer,
                                        left As Double,
                                        right As Double)

            ''' <summary>
            ''' Déclenché quand le peak-hold d'une piste augmente (nouvelle valeur max).
            ''' Valeurs linéaires [0..1+].
            ''' </summary>
            Public Event On_PeakHoldChange(trackIndex As Integer,
                                            leftHold As Double,
                                            rightHold As Double)

            ''' <summary>
            ''' Déclenché quand un canal dépasse 0 dBFS (valeur linéaire ≥ 1.0 = clip).
            ''' </summary>
            Public Event On_Clip(trackIndex As Integer,
                                  leftClip As Boolean,
                                  rightClip As Boolean)

            ' ── Propriétés ────────────────────────────────────────────────────

            ''' <summary>Nombre de pistes surveillées.</summary>
            Public ReadOnly Property TrackCount As Integer
                Get
                    SyncLock _lock
                        Return _tracks.Count
                    End SyncLock
                End Get
            End Property

            ''' <summary>True si la connexion pipe vers le bridge est active.</summary>
            Public ReadOnly Property IsConnected As Boolean
                Get
                    Return _pipe.IsConnected
                End Get
            End Property

            ' ── Gestion de la collection ──────────────────────────────────────

            ''' <summary>
            ''' Ajoute une piste à la surveillance et initialise ses caches à 0.
            ''' Sans effet si la piste est déjà enregistrée.
            ''' </summary>
            ''' <param name="trackIndex">Index 0-based de la piste.</param>
            Public Sub WatchTrack(trackIndex As Integer)
                SyncLock _lock
                    If _tracks.ContainsKey(trackIndex) Then Return
                    _tracks(trackIndex) = False
                    _peakL(trackIndex) = 0
                    _peakR(trackIndex) = 0
                    _holdL(trackIndex) = 0
                    _holdR(trackIndex) = 0
                End SyncLock
            End Sub

            ''' <summary>Ajoute une collection de pistes à la surveillance.</summary>
            Public Sub WatchTracks(tracks As IEnumerable(Of ReaperTrack))
                For Each trk In tracks
                    WatchTrack(trk.TrackIndex)
                Next
            End Sub

            ''' <summary>Retire une piste et efface ses caches.</summary>
            ''' <param name="trackIndex">Index 0-based de la piste.</param>
            Public Sub UnwatchTrack(trackIndex As Integer)
                SyncLock _lock
                    _tracks.Remove(trackIndex)
                    _peakL.Remove(trackIndex)
                    _peakR.Remove(trackIndex)
                    _holdL.Remove(trackIndex)
                    _holdR.Remove(trackIndex)
                End SyncLock
            End Sub

            ''' <summary>Retire toutes les pistes et efface les caches.</summary>
            Public Sub ClearWatched()
                SyncLock _lock
                    _tracks.Clear()
                    _peakL.Clear()
                    _peakR.Clear()
                    _holdL.Clear()
                    _holdR.Clear()
                End SyncLock
            End Sub

            ' ── Suspension par piste ──────────────────────────────────────────

            ''' <summary>
            ''' Suspend la mise à jour des peaks pour une piste.
            ''' Les requêtes pipe sont toujours émises mais le cache n'est pas mis
            ''' à jour et les événements sont muets.
            ''' </summary>
            Public Sub SuspendTrack(trackIndex As Integer)
                SyncLock _lock
                    If _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = True
                    End If
                End SyncLock
            End Sub

            ''' <summary>Réactive la mise à jour des peaks pour une piste suspendue.</summary>
            Public Sub ResumeTrack(trackIndex As Integer)
                SyncLock _lock
                    If _tracks.ContainsKey(trackIndex) Then
                        _tracks(trackIndex) = False
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

            ' ── Rafraîchissement à la demande ─────────────────────────────────

            ''' <summary>
            ''' Interroge Reaper pour les niveaux peak stéréo de toutes les pistes
            ''' surveillées via la connexion pipe persistante.
            ''' <br/>
            ''' <b>À appeler depuis un timer externe</b> toutes les ~50 ms.
            ''' <br/>
            ''' Chaque piste = 1 requête <c>GET_TRACK_PEAK_STEREO</c> sur le pipe
            ''' persistant (L+R en une seule réponse).
            ''' La connexion est réutilisée entre les appels et se reconnecte
            ''' automatiquement en cas de déconnexion.
            ''' <br/>
            ''' Déclenche :
            ''' <list type="bullet">
            ''' <item><see cref="On_PeakChange"/> si L ou R ont changé.</item>
            ''' <item><see cref="On_PeakHoldChange"/> si un nouveau max est atteint.</item>
            ''' <item><see cref="On_Clip"/> si L ou R ≥ 1.0 (0 dBFS).</item>
            ''' </list>
            ''' </summary>
            Public Sub Refresh()
                ' ── Snapshot hors lock pour ne pas bloquer pendant les requêtes ─
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

                    ' ── L + R en 1 requête sur le pipe persistant ─────────────
                    Dim raw = _pipe.Request($"GET_TRACK_PEAK_STEREO|{tIdx}")
                    If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then
                        Continue For
                    End If

                    Dim parts = raw.Split("|"c)
                    If parts.Length < 2 Then Continue For

                    Dim newL As Double = 0, newR As Double = 0
                    Double.TryParse(parts(0), NumberStyles.Any, ic, newL)
                    Double.TryParse(parts(1), NumberStyles.Any, ic, newR)

                    ' ── Piste suspendue → lecture faite, cache ignoré ─────────
                    If entry.isSuspended Then Continue For

                    ' ── Mise à jour des caches (sous lock) ───────────────────
                    Dim peakChg As Boolean
                    Dim holdChg As Boolean
                    Dim newHoldL As Double = 0, newHoldR As Double = 0

                    SyncLock _lock
                        Dim prevL As Double = 0, prevR As Double = 0
                        _peakL.TryGetValue(tIdx, prevL)
                        _peakR.TryGetValue(tIdx, prevR)
                        peakChg = prevL <> newL OrElse prevR <> newR
                        If peakChg Then
                            _peakL(tIdx) = newL
                            _peakR(tIdx) = newR
                        End If

                        ' ── Peak hold : mémoriser le max observé ──────────────
                        Dim curHoldL As Double = 0, curHoldR As Double = 0
                        _holdL.TryGetValue(tIdx, curHoldL)
                        _holdR.TryGetValue(tIdx, curHoldR)
                        newHoldL = Math.Max(curHoldL, newL)
                        newHoldR = Math.Max(curHoldR, newR)
                        holdChg = newHoldL <> curHoldL OrElse newHoldR <> curHoldR
                        If holdChg Then
                            _holdL(tIdx) = newHoldL
                            _holdR(tIdx) = newHoldR
                        End If
                    End SyncLock

                    ' ── Déclencher HORS lock ──────────────────────────────────
                    If peakChg Then
                        RaiseEvent On_PeakChange(tIdx, newL, newR)
                    End If
                    If holdChg Then
                        RaiseEvent On_PeakHoldChange(tIdx, newHoldL, newHoldR)
                    End If
                    If newL >= 1.0 OrElse newR >= 1.0 Then
                        RaiseEvent On_Clip(tIdx, newL >= 1.0, newR >= 1.0)
                    End If
                Next
            End Sub

            ' ── Lecture d'état ────────────────────────────────────────────────

            ''' <summary>
            ''' Retourne les derniers niveaux peak stéréo d'une piste.
            ''' Valeurs linéaires [0..1+] — 1.0 = 0 dBFS.
            ''' Retourne (0, 0) si la piste n'est pas surveillée.
            ''' </summary>
            Public Function GetPeak(trackIndex As Integer) As (L As Double, R As Double)
                SyncLock _lock
                    Dim l As Double = 0, r As Double = 0
                    _peakL.TryGetValue(trackIndex, l)
                    _peakR.TryGetValue(trackIndex, r)
                    Return (l, r)
                End SyncLock
            End Function

            ''' <summary>
            ''' Retourne les niveaux peak en dB d'une piste.
            ''' Retourne (-150, -150) si la piste n'est pas surveillée.
            ''' </summary>
            Public Function GetPeakDB(trackIndex As Integer) As (L As Double, R As Double)
                Dim peak = GetPeak(trackIndex)
                Return (utility.SLIDER2DB(peak.L), utility.SLIDER2DB(peak.R))
            End Function

            ''' <summary>
            ''' Retourne les valeurs peak-hold stéréo d'une piste.
            ''' Valeurs linéaires [0..1+].
            ''' Retourne (0, 0) si la piste n'est pas surveillée.
            ''' </summary>
            Public Function GetPeakHold(trackIndex As Integer) As (L As Double, R As Double)
                SyncLock _lock
                    Dim l As Double = 0, r As Double = 0
                    _holdL.TryGetValue(trackIndex, l)
                    _holdR.TryGetValue(trackIndex, r)
                    Return (l, r)
                End SyncLock
            End Function

            ''' <summary>
            ''' Retourne les valeurs peak-hold en dB d'une piste.
            ''' Retourne (-150, -150) si la piste n'est pas surveillée.
            ''' </summary>
            Public Function GetPeakHoldDB(trackIndex As Integer) As (L As Double, R As Double)
                Dim hold = GetPeakHold(trackIndex)
                Return (utility.SLIDER2DB(hold.L), utility.SLIDER2DB(hold.R))
            End Function

            ''' <summary>
            ''' Retourne les peaks de toutes les pistes surveillées pour un
            ''' rafraîchissement groupé de la surface (ex: mise à jour de N VU-mètres).
            ''' </summary>
            Public Function GetAllPeaks() As Dictionary(Of Integer, (L As Double, R As Double))
                Dim result As New Dictionary(Of Integer, (Double, Double))
                SyncLock _lock
                    For Each tIdx In _tracks.Keys
                        Dim l As Double = 0, r As Double = 0
                        _peakL.TryGetValue(tIdx, l)
                        _peakR.TryGetValue(tIdx, r)
                        result(tIdx) = (l, r)
                    Next
                End SyncLock
                Return result
            End Function

            ' ── Réinitialisation peak hold ────────────────────────────────────

            ''' <summary>
            ''' Réinitialise le peak-hold de toutes les pistes surveillées.
            ''' </summary>
            Public Sub ResetAllPeakHold()
                SyncLock _lock
                    For Each tIdx In _tracks.Keys
                        _holdL(tIdx) = 0
                        _holdR(tIdx) = 0
                    Next
                End SyncLock
            End Sub

            ''' <summary>
            ''' Réinitialise le peak-hold d'une seule piste.
            ''' </summary>
            Public Sub ResetPeakHold(trackIndex As Integer)
                SyncLock _lock
                    If _holdL.ContainsKey(trackIndex) Then
                        _holdL(trackIndex) = 0
                        _holdR(trackIndex) = 0
                    End If
                End SyncLock
            End Sub

            ' ── IDisposable ───────────────────────────────────────────────────

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


        Namespace PageManagers

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
                        If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return False
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
                        If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return False
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
                    If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return 0
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
                    If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return 0
                    Dim count As Integer = 0
                    Integer.TryParse(raw, count)
                    Return count
                End Function

            End Class

        End Namespace

    End Namespace

    Namespace Managers
        Namespace Managers

            ' ══════════════════════════════════════════════════════════════════════════
            '  REAPER TRANSPORT MANAGER — pilotage complet du transport Reaper
            ' ══════════════════════════════════════════════════════════════════════════

            ''' <summary>
            ''' Gère et pilote l'état de transport Reaper :
            ''' <list type="bullet">
            ''' <item>Surveillance en temps réel via <see cref="Monitors.ReaperTransportMonitor"/>.</item>
            ''' <item>Commandes : Play / Stop / Pause / Record / Loop / Quick Punch /
            '''       Go to Start / Go to End / Rewind / Fast Forward.</item>
            ''' <item>Gestion des points de boucle et de la sélection temporelle.</item>
            ''' </list>
            ''' Appeler <see cref="Start"/> pour démarrer la surveillance.
            ''' </summary>
            Public Class ReaperTransportManager
                Implements IDisposable

                Private Shared ReadOnly Ic As CultureInfo = CultureInfo.InvariantCulture

                ' ── Moniteur interne ──────────────────────────────────────────────
                Private ReadOnly _monitor As New Monitors.ReaperTransportMonitor()
                Private _disposed As Boolean = False

                ' ── Cache état transport ──────────────────────────────────────────
                Private _playState As Integer = 0
                Private _playPos As Double = 0
                Private _isLooping As Boolean = False
                Private _loopIn As Double = 0
                Private _loopOut As Double = 0
                Private _isQuickPunch As Boolean = False
                Private _timeSelIn As Double = 0
                Private _timeSelOut As Double = 0

                ' ── Événements ────────────────────────────────────────────────────

                ''' <summary>
                ''' Déclenché quand l'état de transport change.
                ''' 0=stop  1=play  2=pause  4=rec  5=rec+play
                ''' </summary>
                Public Event On_PlayStateChange(playState As Integer)

                ''' <summary>Déclenché quand la position de lecture change (secondes).</summary>
                Public Event On_PositionChange(pos As Double)

                ''' <summary>
                ''' Déclenché quand l'état ou les points de boucle changent.
                ''' <paramref name="isLooping"/> indique si la boucle est active.
                ''' </summary>
                Public Event On_LoopChange(isLooping As Boolean,
                                            loopIn As Double,
                                            loopOut As Double)

                ''' <summary>Déclenché quand l'état Quick Punch change.</summary>
                Public Event On_QuickPunchChange(isQuickPunch As Boolean)

                ''' <summary>Déclenché quand la sélection temporelle (In/Out) change.</summary>
                Public Event On_TimeSelectionChange(selIn As Double, selOut As Double)

                ' ── Constructeur ──────────────────────────────────────────────────

                ''' <summary>
                ''' Initialise le manager et câble le moniteur interne.
                ''' Appeler <see cref="Start"/> pour démarrer la surveillance.
                ''' </summary>
                Public Sub New()
                    AddHandler _monitor.On_PlayStateChange, AddressOf OnPlayStateChange
                    AddHandler _monitor.On_PlayPositionChange, AddressOf OnPlayPositionChange
                    AddHandler _monitor.On_LoopChange, AddressOf OnLoopChange
                    AddHandler _monitor.On_QuickPunchChange, AddressOf OnQuickPunchChange
                    AddHandler _monitor.On_TimeSelectionChange, AddressOf OnTimeSelectionChange
                End Sub

                ' ── Propriétés d'état ─────────────────────────────────────────────

                ''' <summary>
                ''' État de transport brut Reaper.
                ''' 0=stop  1=play  2=pause  4=rec  5=rec+play
                ''' </summary>
                Public ReadOnly Property PlayState As Integer
                    Get
                        Return _playState
                    End Get
                End Property

                ''' <summary>Position de lecture courante en secondes.</summary>
                Public ReadOnly Property PlayPosition As Double
                    Get
                        Return _playPos
                    End Get
                End Property

                ''' <summary>True si Reaper est en lecture (bit 0 de <see cref="PlayState"/>).</summary>
                Public ReadOnly Property IsPlaying As Boolean
                    Get
                        Return (_playState And 1) <> 0
                    End Get
                End Property

                ''' <summary>True si Reaper est en pause (bit 1 de <see cref="PlayState"/>).</summary>
                Public ReadOnly Property IsPaused As Boolean
                    Get
                        Return (_playState And 2) <> 0
                    End Get
                End Property

                ''' <summary>True si Reaper est en enregistrement (bit 2 de <see cref="PlayState"/>).</summary>
                Public ReadOnly Property IsRecording As Boolean
                    Get
                        Return (_playState And 4) <> 0
                    End Get
                End Property

                ''' <summary>True si la boucle est active.</summary>
                Public ReadOnly Property IsLooping As Boolean
                    Get
                        Return _isLooping
                    End Get
                End Property

                ''' <summary>True si Quick Punch est activé.</summary>
                Public ReadOnly Property IsQuickPunch As Boolean
                    Get
                        Return _isQuickPunch
                    End Get
                End Property

                ''' <summary>Point de début de boucle en secondes.</summary>
                Public ReadOnly Property LoopIn As Double
                    Get
                        Return _loopIn
                    End Get
                End Property

                ''' <summary>Point de fin de boucle en secondes.</summary>
                Public ReadOnly Property LoopOut As Double
                    Get
                        Return _loopOut
                    End Get
                End Property

                ''' <summary>Point de début de sélection temporelle en secondes.</summary>
                Public ReadOnly Property TimeSelIn As Double
                    Get
                        Return _timeSelIn
                    End Get
                End Property

                ''' <summary>Point de fin de sélection temporelle en secondes.</summary>
                Public ReadOnly Property TimeSelOut As Double
                    Get
                        Return _timeSelOut
                    End Get
                End Property

                ''' <summary>Intervalle de polling du moniteur en ms (défaut : 50 ms).</summary>
                Public Property Interval As Integer
                    Get
                        Return _monitor.Interval
                    End Get
                    Set
                        _monitor.Interval = Value
                    End Set
                End Property

                ' ── Cycle de vie du moniteur ──────────────────────────────────────

                ''' <summary>Démarre la surveillance du transport. Sans effet si déjà démarré.</summary>
                Public Sub Start()
                    _monitor.Start()
                End Sub

                ''' <summary>Arrête la surveillance du transport.</summary>
                Public Sub [Stop]()
                    _monitor.[Stop]()
                End Sub

                ' ── Suspension du moniteur ────────────────────────────────────────

                ''' <summary>
                ''' Suspend les événements du moniteur de transport.
                ''' Appeler avant d'envoyer une commande pour éviter le feedback.
                ''' </summary>
                Public Sub SuspendMonitor()
                    _monitor.SuspendMonitor()
                End Sub

                ''' <summary>
                ''' Réactive les événements du moniteur et force un re-poll immédiat
                ''' pour capturer l'état résultant de la commande.
                ''' </summary>
                Public Sub ResumeMonitor()
                    _monitor.ResumeMonitor()
                End Sub

                ' ── Commandes de transport ────────────────────────────────────────

                ''' <summary>Lance la lecture.</summary>
                Public Sub Play()
                    SuspendMonitor()
                    utility.CallReaperBridge("TRANSPORT_PLAY")
                    ResumeMonitor()
                End Sub

                ''' <summary>Arrête la lecture.</summary>
                Public Sub StopPlayback()
                    SuspendMonitor()
                    utility.CallReaperBridge("TRANSPORT_STOP")
                    ResumeMonitor()
                End Sub

                ''' <summary>Bascule la pause.</summary>
                Public Sub Pause()
                    SuspendMonitor()
                    utility.CallReaperBridge("TRANSPORT_PAUSE")
                    ResumeMonitor()
                End Sub

                ''' <summary>Lance l'enregistrement.</summary>
                Public Sub Record()
                    SuspendMonitor()
                    utility.CallReaperBridge("TRANSPORT_REC")
                    ResumeMonitor()
                End Sub

                ''' <summary>Bascule l'état de boucle (Loop On/Off).</summary>
                Public Sub ToggleLoop()
                    SuspendMonitor()
                    utility.CallReaperBridge("TRANSPORT_TOGGLE_LOOP")
                    ResumeMonitor()
                End Sub

                ''' <summary>Bascule le mode Quick Punch.</summary>
                Public Sub ToggleQuickPunch()
                    SuspendMonitor()
                    utility.CallReaperBridge("TRANSPORT_TOGGLE_QUICK_PUNCH")
                    ResumeMonitor()
                End Sub

                ''' <summary>Déplace le curseur au début du projet.</summary>
                Public Sub GoToStart()
                    SuspendMonitor()
                    utility.SetCursorPosition(0.0, moveView:=True, seekPlay:=IsPlaying)
                    ResumeMonitor()
                End Sub

                ''' <summary>Déplace le curseur à la fin du projet.</summary>
                Public Sub GoToEnd()
                    Dim length = utility.GetProjectLength()
                    If length < 0 Then Return
                    SuspendMonitor()
                    utility.SetCursorPosition(length, moveView:=True, seekPlay:=IsPlaying)
                    ResumeMonitor()
                End Sub

                ''' <summary>Recule le curseur de <paramref name="stepSeconds"/> secondes.</summary>
                Public Sub Rewind(Optional stepSeconds As Double = 5.0)
                    Dim newPos = Math.Max(0.0, _playPos - stepSeconds)
                    SuspendMonitor()
                    utility.SetCursorPosition(newPos, moveView:=True, seekPlay:=True)
                    ResumeMonitor()
                End Sub

                ''' <summary>Avance le curseur de <paramref name="stepSeconds"/> secondes.</summary>
                Public Sub FastForward(Optional stepSeconds As Double = 5.0)
                    Dim newPos = _playPos + stepSeconds
                    SuspendMonitor()
                    utility.SetCursorPosition(newPos, moveView:=True, seekPlay:=True)
                    ResumeMonitor()
                End Sub

                ''' <summary>Déplace le point de début de boucle.</summary>
                Public Sub SetLoopIn(time As Double)
                    SuspendMonitor()
                    utility.CallReaperBridge(
                        $"TRANSPORT_SET_LOOP_IN|{time.ToString("G", Ic)}")
                    ResumeMonitor()
                End Sub

                ''' <summary>Déplace le point de fin de boucle.</summary>
                Public Sub SetLoopOut(time As Double)
                    SuspendMonitor()
                    utility.CallReaperBridge(
                        $"TRANSPORT_SET_LOOP_OUT|{time.ToString("G", Ic)}")
                    ResumeMonitor()
                End Sub

                ''' <summary>Définit les deux points de boucle en un seul appel.</summary>
                Public Sub SetLoopPoints(inTime As Double, outTime As Double)
                    SuspendMonitor()
                    utility.CallReaperBridge(
                        $"TRANSPORT_SET_LOOP_POINTS|{inTime.ToString("G", Ic)}|{outTime.ToString("G", Ic)}")
                    ResumeMonitor()
                End Sub

                ''' <summary>Déplace le point de début de la sélection temporelle.</summary>
                Public Sub SetTimeSelIn(time As Double)
                    SuspendMonitor()
                    utility.CallReaperBridge(
                        $"TRANSPORT_SET_TIME_SEL_IN|{time.ToString("G", Ic)}")
                    ResumeMonitor()
                End Sub

                ''' <summary>Déplace le point de fin de la sélection temporelle.</summary>
                Public Sub SetTimeSelOut(time As Double)
                    SuspendMonitor()
                    utility.CallReaperBridge(
                        $"TRANSPORT_SET_TIME_SEL_OUT|{time.ToString("G", Ic)}")
                    ResumeMonitor()
                End Sub

                ''' <summary>Définit les deux points de sélection temporelle en un seul appel.</summary>
                Public Sub SetTimeSelection(inTime As Double, outTime As Double)
                    SuspendMonitor()
                    utility.CallReaperBridge(
                        $"TRANSPORT_SET_TIME_SEL|{inTime.ToString("G", Ic)}|{outTime.ToString("G", Ic)}")
                    ResumeMonitor()
                End Sub

                ' ── Handlers internes ─────────────────────────────────────────────

                Private Sub OnPlayStateChange(playState As Integer)
                    _playState = playState
                    RaiseEvent On_PlayStateChange(_playState)
                End Sub

                Private Sub OnPlayPositionChange(playPos As Double)
                    _playPos = playPos
                    RaiseEvent On_PositionChange(_playPos)
                End Sub

                Private Sub OnLoopChange(isLooping As Boolean,
                                          loopIn As Double,
                                          loopOut As Double)
                    _isLooping = isLooping
                    _loopIn = loopIn
                    _loopOut = loopOut
                    RaiseEvent On_LoopChange(isLooping, loopIn, loopOut)
                End Sub

                Private Sub OnQuickPunchChange(isQuickPunch As Boolean)
                    _isQuickPunch = isQuickPunch
                    RaiseEvent On_QuickPunchChange(isQuickPunch)
                End Sub

                Private Sub OnTimeSelectionChange(selIn As Double, selOut As Double)
                    _timeSelIn = selIn
                    _timeSelOut = selOut
                    RaiseEvent On_TimeSelectionChange(selIn, selOut)
                End Sub

                ' ── IDisposable ───────────────────────────────────────────────────

                Protected Overridable Sub Dispose(disposing As Boolean)
                    If Not _disposed Then
                        If disposing Then
                            _monitor.Dispose()
                        End If
                        _disposed = True
                    End If
                End Sub

                ''' <summary>Arrête le monitoring et libère toutes les ressources.</summary>
                Public Sub Dispose() Implements IDisposable.Dispose
                    Dispose(disposing:=True)
                    GC.SuppressFinalize(Me)
                End Sub

            End Class

            ' ══════════════════════════════════════════════════════════════════════════
            '  JOG WHEEL MODE — modes de la jogwheel
            ' ══════════════════════════════════════════════════════════════════════════

            ''' <summary>Modes de fonctionnement de la jogwheel de la surface de contrôle.</summary>
            Public Enum JogWheelMode
                ''' <summary>Déplace le curseur d'édition avant/arrière sur la timeline.</summary>
                Cursor = 0
                ''' <summary>Déplace le point In de la sélection temporelle.</summary>
                SelectionIn = 1
                ''' <summary>Déplace le point Out de la sélection temporelle.</summary>
                SelectionOut = 2
                ''' <summary>Zoom horizontal de la timeline (in/out).</summary>
                ZoomHorizontal = 3
                ''' <summary>Zoom vertical de la piste sélectionnée (in/out).</summary>
                ZoomVertical = 4
            End Enum

            Namespace Managers

                ' ══════════════════════════════════════════════════════════════════════════
                '  REAPER JOG WHEEL MANAGER — interface jogwheel multi-modes
                ' ══════════════════════════════════════════════════════════════════════════

                ''' <summary>
                ''' Interface entre la jogwheel d'une surface de contrôle et Reaper.
                ''' Gère plusieurs modes :
                ''' <list type="table">
                ''' <item><term><see cref="JogWheelMode.Cursor"/></term>
                '''       <description>Déplace le curseur d'édition avant/arrière.</description></item>
                ''' <item><term><see cref="JogWheelMode.SelectionIn"/></term>
                '''       <description>Déplace le point In de la sélection temporelle.</description></item>
                ''' <item><term><see cref="JogWheelMode.SelectionOut"/></term>
                '''       <description>Déplace le point Out de la sélection temporelle.</description></item>
                ''' <item><term><see cref="JogWheelMode.ZoomHorizontal"/></term>
                '''       <description>Zoom horizontal de la timeline (vue arrangement).</description></item>
                ''' <item><term><see cref="JogWheelMode.ZoomVertical"/></term>
                '''       <description>Zoom vertical de la piste sélectionnée.</description></item>
                ''' </list>
                ''' Appeler <see cref="Jog"/> à chaque tick détecté de la jogwheel.
                ''' Un tick positif correspond à une rotation dans le sens horaire
                ''' (forward / zoom in / point +) ; un tick négatif à une rotation
                ''' anti-horaire (backward / zoom out / point -).
                ''' <br/>
                ''' Fournir un <see cref="ReaperTransportManager"/> via le constructeur ou
                ''' <see cref="TransportManager"/> pour lire <c>IsPlaying</c> et la sélection
                ''' temporelle depuis le cache (évite une requête pipe).
                ''' </summary>
                Public Class ReaperJogWheelManager

                    Private Shared ReadOnly Ic As CultureInfo = CultureInfo.InvariantCulture

                    ' ── Mode courant ──────────────────────────────────────────────────
                    Private _mode As JogWheelMode = JogWheelMode.Cursor

                    ' ── Référence optionnelle au transport manager ────────────────────
                    Private _transportManager As ReaperTransportManager

                    ' ── Pas de déplacement par tick ───────────────────────────────────
                    Private _cursorStepSeconds As Double = 0.1
                    Private _selectionStepSeconds As Double = 0.1

                    ' ── Mode fin (Ctrl/Shift sur la surface) ──────────────────────────
                    Private _fineMode As Boolean = False
                    Private _fineDivider As Double = 10.0

                    ' ── Événements ────────────────────────────────────────────────────

                    ''' <summary>Déclenché quand le mode change.</summary>
                    Public Event On_ModeChanged(mode As JogWheelMode)

                    ''' <summary>
                    ''' Déclenché après chaque déplacement du curseur d'édition.
                    ''' <paramref name="newPosition"/> = nouvelle position en secondes.
                    ''' </summary>
                    Public Event On_CursorMoved(newPosition As Double)

                    ''' <summary>
                    ''' Déclenché après chaque modification de la sélection temporelle.
                    ''' </summary>
                    Public Event On_SelectionChanged(selIn As Double, selOut As Double)

                    ''' <summary>
                    ''' Déclenché après chaque action de zoom.
                    ''' <paramref name="direction"/> : +1 = zoom in   -1 = zoom out.
                    ''' </summary>
                    Public Event On_Zoomed(mode As JogWheelMode, direction As Integer)

                    ' ── Constructeur ──────────────────────────────────────────────────

                    ''' <summary>
                    ''' Initialise le manager.
                    ''' </summary>
                    ''' <param name="transportManager">
                    ''' Optionnel — permet de lire <c>IsPlaying</c>, <c>TimeSelIn</c> et
                    ''' <c>TimeSelOut</c> depuis le cache. Peut être défini plus tard via
                    ''' <see cref="TransportManager"/>.
                    ''' </param>
                    Public Sub New(Optional transportManager As ReaperTransportManager = Nothing)
                        _transportManager = transportManager
                    End Sub

                    ' ── Propriétés ────────────────────────────────────────────────────

                    ''' <summary>Mode de fonctionnement courant.</summary>
                    Public Property Mode As JogWheelMode
                        Get
                            Return _mode
                        End Get
                        Set
                            If _mode = Value Then Return
                            _mode = Value
                            RaiseEvent On_ModeChanged(_mode)
                        End Set
                    End Property

                    ''' <summary>
                    ''' Référence au <see cref="ReaperTransportManager"/>.
                    ''' Peut être définie après construction.
                    ''' </summary>
                    Public Property TransportManager As ReaperTransportManager
                        Get
                            Return _transportManager
                        End Get
                        Set
                            _transportManager = Value
                        End Set
                    End Property

                    ''' <summary>
                    ''' Pas de déplacement du curseur par tick en mode
                    ''' <see cref="JogWheelMode.Cursor"/> (secondes, défaut : 0.1 s).
                    ''' </summary>
                    Public Property CursorStepSeconds As Double
                        Get
                            Return _cursorStepSeconds
                        End Get
                        Set
                            _cursorStepSeconds = Math.Max(0.001, Value)
                        End Set
                    End Property

                    ''' <summary>
                    ''' Pas de déplacement des points de sélection par tick en modes
                    ''' <see cref="JogWheelMode.SelectionIn"/> et
                    ''' <see cref="JogWheelMode.SelectionOut"/> (secondes, défaut : 0.1 s).
                    ''' </summary>
                    Public Property SelectionStepSeconds As Double
                        Get
                            Return _selectionStepSeconds
                        End Get
                        Set
                            _selectionStepSeconds = Math.Max(0.001, Value)
                        End Set
                    End Property

                    ''' <summary>
                    ''' Quand True, le pas effectif est divisé par <see cref="FineDivider"/>.
                    ''' Activer via un modificateur (Ctrl/Shift) sur la surface.
                    ''' </summary>
                    Public Property FineMode As Boolean
                        Get
                            Return _fineMode
                        End Get
                        Set
                            _fineMode = Value
                        End Set
                    End Property

                    ''' <summary>
                    ''' Facteur de division du pas en mode fin (défaut : 10 → pas ÷ 10).
                    ''' Minimum : 2.
                    ''' </summary>
                    Public Property FineDivider As Double
                        Get
                            Return _fineDivider
                        End Get
                        Set
                            _fineDivider = Math.Max(2.0, Value)
                        End Set
                    End Property

                    ' ── Point d'entrée principal ──────────────────────────────────────

                    ''' <summary>
                    ''' À appeler à chaque tick détecté de la jogwheel.
                    ''' <list type="table">
                    ''' <item><term><paramref name="ticks"/> &gt; 0</term>
                    '''       <description>Sens horaire : forward / zoom in / point +</description></item>
                    ''' <item><term><paramref name="ticks"/> &lt; 0</term>
                    '''       <description>Sens anti-horaire : backward / zoom out / point -</description></item>
                    ''' </list>
                    ''' </summary>
                    ''' <param name="ticks">
                    ''' Nombre de crans (positif = horaire).
                    ''' Une valeur de ±1 correspond à un cran standard de la roue.
                    ''' </param>
                    Public Sub Jog(ticks As Integer)
                        If ticks = 0 Then Return
                        Select Case _mode
                            Case JogWheelMode.Cursor
                                JogCursor(ticks)
                            Case JogWheelMode.SelectionIn
                                JogSelectionIn(ticks)
                            Case JogWheelMode.SelectionOut
                                JogSelectionOut(ticks)
                            Case JogWheelMode.ZoomHorizontal
                                JogZoomHorizontal(ticks)
                            Case JogWheelMode.ZoomVertical
                                JogZoomVertical(ticks)
                        End Select
                    End Sub

                    ' ── Raccourcis de mode ────────────────────────────────────────────

                    ''' <summary>Passe en mode <see cref="JogWheelMode.Cursor"/>.</summary>
                    Public Sub SetCursorMode()
                        Mode = JogWheelMode.Cursor
                    End Sub

                    ''' <summary>Passe en mode <see cref="JogWheelMode.SelectionIn"/>.</summary>
                    Public Sub SetSelectionInMode()
                        Mode = JogWheelMode.SelectionIn
                    End Sub

                    ''' <summary>Passe en mode <see cref="JogWheelMode.SelectionOut"/>.</summary>
                    Public Sub SetSelectionOutMode()
                        Mode = JogWheelMode.SelectionOut
                    End Sub

                    ''' <summary>Passe en mode <see cref="JogWheelMode.ZoomHorizontal"/>.</summary>
                    Public Sub SetZoomHorizontalMode()
                        Mode = JogWheelMode.ZoomHorizontal
                    End Sub

                    ''' <summary>Passe en mode <see cref="JogWheelMode.ZoomVertical"/>.</summary>
                    Public Sub SetZoomVerticalMode()
                        Mode = JogWheelMode.ZoomVertical
                    End Sub

                    ''' <summary>
                    ''' Cycle vers le mode suivant dans l'ordre :
                    ''' Cursor → SelectionIn → SelectionOut → ZoomHorizontal → ZoomVertical → Cursor…
                    ''' Utile pour un bouton "Mode" dédié sur la surface.
                    ''' </summary>
                    Public Sub CycleMode()
                        Select Case _mode
                            Case JogWheelMode.Cursor
                                Mode = JogWheelMode.SelectionIn
                            Case JogWheelMode.SelectionIn
                                Mode = JogWheelMode.SelectionOut
                            Case JogWheelMode.SelectionOut
                                Mode = JogWheelMode.ZoomHorizontal
                            Case JogWheelMode.ZoomHorizontal
                                Mode = JogWheelMode.ZoomVertical
                            Case Else
                                Mode = JogWheelMode.Cursor
                        End Select
                    End Sub

                    ' ── Mode Cursor ───────────────────────────────────────────────────

                    ''' <summary>
                    ''' Déplace le curseur d'édition de <c>ticks × <see cref="CursorStepSeconds"/></c>.
                    ''' Si Reaper est en lecture (<see cref="ReadIsPlaying"/>), cherche la
                    ''' nouvelle position sans s'arrêter (seek).
                    ''' </summary>
                    Private Sub JogCursor(ticks As Integer)
                        Dim currentPos = utility.GetCursorPosition()
                        If currentPos < 0 Then currentPos = 0

                        Dim _step = EffectiveStep(_cursorStepSeconds)
                        Dim newPos = Math.Max(0.0, currentPos + ticks * _step)

                        utility.SetCursorPosition(newPos,
                                                  moveView:=True,
                                                  seekPlay:=ReadIsPlaying())
                        RaiseEvent On_CursorMoved(newPos)
                    End Sub

                    ' ── Mode SelectionIn ──────────────────────────────────────────────

                    ''' <summary>
                    ''' Déplace le point In de la sélection temporelle.
                    ''' Clampé à [0, selOut].
                    ''' </summary>
                    Private Sub JogSelectionIn(ticks As Integer)
                        Dim sel = ReadSelectionState()

                        Dim _step = EffectiveStep(_selectionStepSeconds)
                        Dim newIn = Math.Max(0.0, sel.selIn + ticks * _step)
                        If newIn > sel.selOut Then newIn = sel.selOut

                        utility.CallReaperBridge(
                            $"TRANSPORT_SET_TIME_SEL_IN|{newIn.ToString("G", Ic)}")
                        RaiseEvent On_SelectionChanged(newIn, sel.selOut)
                    End Sub

                    ' ── Mode SelectionOut ─────────────────────────────────────────────

                    ''' <summary>
                    ''' Déplace le point Out de la sélection temporelle.
                    ''' Clampé à [selIn, +∞[.
                    ''' </summary>
                    Private Sub JogSelectionOut(ticks As Integer)
                        Dim sel = ReadSelectionState()

                        Dim _step = EffectiveStep(_selectionStepSeconds)
                        Dim newOut = Math.Max(sel.selIn, sel.selOut + ticks * _step)

                        utility.CallReaperBridge(
                            $"TRANSPORT_SET_TIME_SEL_OUT|{newOut.ToString("G", Ic)}")
                        RaiseEvent On_SelectionChanged(sel.selIn, newOut)
                    End Sub

                    ' ── Mode ZoomHorizontal ───────────────────────────────────────────

                    ''' <summary>
                    ''' Zoom horizontal de la timeline.
                    ''' Chaque tick émet une commande <c>ZOOM_H_IN</c> ou <c>ZOOM_H_OUT</c>
                    ''' vers le bridge.
                    ''' </summary>
                    Private Sub JogZoomHorizontal(ticks As Integer)
                        Dim dir = Math.Sign(ticks)
                        Dim count = Math.Abs(ticks)
                        For i = 0 To count - 1
                            If dir > 0 Then
                                utility.CallReaperBridge("ZOOM_H_IN")
                            Else
                                utility.CallReaperBridge("ZOOM_H_OUT")
                            End If
                        Next
                        RaiseEvent On_Zoomed(JogWheelMode.ZoomHorizontal, dir)
                    End Sub

                    ' ── Mode ZoomVertical ─────────────────────────────────────────────

                    ''' <summary>
                    ''' Zoom vertical sur la piste sélectionnée.
                    ''' Chaque tick émet une commande <c>ZOOM_TRACK_IN</c> ou
                    ''' <c>ZOOM_TRACK_OUT</c> vers le bridge.
                    ''' </summary>
                    Private Sub JogZoomVertical(ticks As Integer)
                        Dim dir = Math.Sign(ticks)
                        Dim count = Math.Abs(ticks)
                        For i = 0 To count - 1
                            If dir > 0 Then
                                utility.CallReaperBridge("ZOOM_TRACK_IN")
                            Else
                                utility.CallReaperBridge("ZOOM_TRACK_OUT")
                            End If
                        Next
                        RaiseEvent On_Zoomed(JogWheelMode.ZoomVertical, dir)
                    End Sub

                    ' ── Helpers internes ──────────────────────────────────────────────

                    ''' <summary>
                    ''' Calcule le pas effectif :
                    ''' en mode normal → <paramref name="baseStep"/> ;
                    ''' en mode fin → <paramref name="baseStep"/> ÷ <see cref="FineDivider"/>.
                    ''' </summary>
                    Private Function EffectiveStep(baseStep As Double) As Double
                        If _fineMode Then
                            Return baseStep / _fineDivider
                        End If
                        Return baseStep
                    End Function

                    ''' <summary>
                    ''' Lit la sélection temporelle courante.
                    ''' Utilise le cache du <see cref="TransportManager"/> si disponible,
                    ''' sinon effectue une requête pipe directe <c>GET_TIME_SELECTION</c>.
                    ''' Retourne (0, 0) si indisponible.
                    ''' </summary>
                    Private Function ReadSelectionState() As (selIn As Double, selOut As Double)
                        If _transportManager IsNot Nothing Then
                            Return (_transportManager.TimeSelIn, _transportManager.TimeSelOut)
                        End If

                        Dim raw = utility.CallReaperBridge("GET_TIME_SELECTION")
                        If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then
                            Return (0, 0)
                        End If

                        Dim p = raw.Split("|"c)
                        If p.Length < 2 Then Return (0, 0)

                        Dim selIn As Double = 0, selOut As Double = 0
                        Double.TryParse(p(0), Globalization.NumberStyles.Any, Ic, selIn)
                        Double.TryParse(p(1), Globalization.NumberStyles.Any, Ic, selOut)
                        Return (selIn, selOut)
                    End Function

                    ''' <summary>
                    ''' Retourne True si Reaper est en lecture.
                    ''' Utilise le <see cref="TransportManager"/> si disponible,
                    ''' sinon retourne False (pas de requête pipe supplémentaire).
                    ''' </summary>
                    Private Function ReadIsPlaying() As Boolean
                        If _transportManager IsNot Nothing Then
                            Return _transportManager.IsPlaying
                        End If
                        Return False
                    End Function

                End Class

            End Namespace

        End Namespace
    End Namespace

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

                ' ── 1. Cartographier les enveloppes FX existantes ─────────────
                ' Doit précéder CreateEnvelope — utilisé dans le Select Case Else.
                BuildFxParamMap()

                ' ── 2. Reconstruire la liste ──────────────────────────────────
                _envelopes.Clear()
                For i = 0 To newCount - 1
                    Dim envName = utility.GetEnvelopeName(_trackIndex, i)
                    Dim new_env = CreateEnvelope(i, envName)
                    If new_env IsNot Nothing Then
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
        Private Sub BuildFxParamMap()
            _fxParamMap.Clear()
            Dim fxCount = GetFxCount()
            For fxIdx = 0 To fxCount - 1
                Dim paramCount = GetFxParamCount(fxIdx)
                For paramIdx = 0 To paramCount - 1
                    Dim envIdx = utility.GetFXEnvelopeIndex(
                                     _trackIndex, fxIdx, paramIdx, create:=False)
                    If envIdx >= 0 Then
                        _fxParamMap(envIdx) = (fxIdx, paramIdx)
                    End If
                Next
            Next
        End Sub

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

        ' ── Helpers pipe ───────────────────────────────────────────────────────

        ''' <summary>
        ''' Retourne le nombre de FX sur la piste via <c>GET_FX_COUNT</c>.
        ''' Retourne 0 si le bridge est indisponible.
        ''' </summary>
        Private Function GetFxCount() As Integer
            Dim raw = utility.CallReaperBridge($"GET_FX_COUNT|{_trackIndex}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return 0
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return 0
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
                RaiseOn_ValueChange(new_val)
                RaiseEvent On_NormalizedValueChange(_normalizedValue, _paramIndex)
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
            utility.SetTrackSendUIVol(TrackIndex, SendId, value)
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
                RaiseOn_ValueChange(vp.Vol)
                RaiseEvent On_SendUIVolChange(_sendUIVol)
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
                RaiseOn_ValueChange(vp.Pan)
                RaiseEvent On_SendUIPanChange(_sendUIPan)
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
                RaiseOn_ValueChange(GetValue())
                RaiseEvent On_SendUIMuteChange(_sendUIMute)
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
                RaiseOn_ValueChange(GetValue())
                RaiseEvent On_TrackUIMuteChange(_trackUIMute)
            End If
        End Sub

        Public Overrides Sub Refresh()
            MyBase.Refresh()
            Dim new_mute = utility.GetTrackUIMute(TrackIndex)
            If _trackUIMute <> new_mute Then
                _trackUIMute = new_mute
                RaiseOn_ValueChange(GetValue())
                RaiseEvent On_TrackUIMuteChange(_trackUIMute)
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
                RaiseOn_ValueChange(pan)
                RaiseEvent On_TrackUIPanChange(_trackUIPan)
            End If
        End Sub

        Public Overrides Sub Refresh()
            MyBase.Refresh()
            Dim vp = utility.GetTrackUIVolPan(TrackIndex)
            If vp.Vol = Double.MinValue Then Return
            If _trackUIPan <> vp.Pan Then
                _trackUIPan = vp.Pan
                RaiseOn_ValueChange(GetValue())
                RaiseEvent On_TrackUIPanChange(_trackUIPan)
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
                RaiseOn_ValueChange(vol)
                RaiseEvent On_TrackUIVolChange(_trackUIVol)
            End If
        End Sub

        Public Overrides Sub Refresh()
            MyBase.Refresh()
            Dim new_vol = utility.GetTrackUIVolume(TrackIndex)
            If new_vol = Double.MinValue Then Return
            If _trackUIVol <> new_vol Then
                _trackUIVol = new_vol
                RaiseOn_ValueChange(GetValue())
                RaiseEvent On_TrackUIVolChange(_trackUIVol)
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
        Private _inLane As Boolean = True
        Private _laneHeight As Integer = 0
        Private _defaultShape As Integer = 0
        Private _minValue As Double = 0
        Private _maxValue As Double = 1
        Private _centerValue As Double = 0.5
        Private _faderScaling As Boolean = False
        Private _automationItemsOptions As Integer = -1
        Private _sendId As Integer = -1

        ' ── Événements ────────────────────────────────────────────────────────
        Public Event On_ActiveChange(_active As Boolean)
        Public Event On_VisibleChange(_visible As Boolean)
        Public Event On_ArmedChange(_armed As Boolean)
        Public Event On_ValueChange(_value As Double)

        Public Sub New(trackIndex As Integer, envIndex As Integer)
            _trackIndex = trackIndex
            _envIndex = envIndex
        End Sub

        ' ── Identité (lecture seule) ───────────────────────────────────────────

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
                SetB("ARMED", Value)
            End Set
        End Property

        Public Property InLane As Boolean
            Get
                Return _inLane
            End Get
            Set
                _inLane = Value
                SetB("IN_LANE", Value)
            End Set
        End Property

        Public Property LaneHeight As Integer
            Get
                Return _laneHeight
            End Get
            Set
                _laneHeight = Value
                SetI("LANEHEIGHT", Value)
            End Set
        End Property

        Public Property DefaultShape As Integer
            Get
                Return _defaultShape
            End Get
            Set
                _defaultShape = Value
                SetI("DEFSHAPE", Value)
            End Set
        End Property

        Public Property AutomationItemsOptions As Integer
            Get
                Return _automationItemsOptions
            End Get
            Set
                _automationItemsOptions = Value
                SetI("AUTO_ITEMS_OPTIONS", Value)
            End Set
        End Property

        ' ── Plage de valeurs (lecture seule) ──────────────────────────────────

        Public ReadOnly Property MinValue As Double
            Get
                Return _minValue
            End Get
        End Property

        Public ReadOnly Property MaxValue As Double
            Get
                Return _maxValue
            End Get
        End Property

        Public ReadOnly Property CenterValue As Double
            Get
                Return _centerValue
            End Get
        End Property

        Public ReadOnly Property FaderScaling As Boolean
            Get
                Return _faderScaling
            End Get
        End Property

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
            RaiseEvent On_ValueChange(value)
        End Sub

        ' ── Chargement ─────────────────────────────────────────────────────────

        Public Overridable Sub Refresh()
            ' ── Chaînes ────────────────────────────────────────────────────────
            _name = utility.GetEnvelopeName(_trackIndex, _envIndex)
            _guid = utility.GetEnvelopeGuid(_trackIndex, _envIndex)

            ' ── Active ─────────────────────────────────────────────────────────
            Dim v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "ACTIVE")
            If v <> Double.MinValue Then
                Dim b = v <> 0
                If _active <> b Then : _active = b : RaiseEvent On_ActiveChange(_active) : End If
            End If

            ' ── Visible ────────────────────────────────────────────────────────
            v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "VISIBLE")
            If v <> Double.MinValue Then
                Dim b = v <> 0
                If _visible <> b Then : _visible = b : RaiseEvent On_VisibleChange(_visible) : End If
            End If

            ' ── Armed ──────────────────────────────────────────────────────────
            v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "ARMED")
            If v <> Double.MinValue Then
                Dim b = v <> 0
                If _armed <> b Then : _armed = b : RaiseEvent On_ArmedChange(_armed) : End If
            End If

            ' ── InLane ─────────────────────────────────────────────────────────
            v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "IN_LANE")
            If v <> Double.MinValue Then _inLane = v <> 0

            ' ── LaneHeight ─────────────────────────────────────────────────────
            v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "LANEHEIGHT")
            If v <> Double.MinValue Then _laneHeight = CInt(v)

            ' ── DefaultShape ───────────────────────────────────────────────────
            v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "DEFSHAPE")
            If v <> Double.MinValue Then _defaultShape = CInt(v)

            ' ── Plage de valeurs (lecture seule) ───────────────────────────────
            v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "MINVALUE")
            If v <> Double.MinValue Then _minValue = v

            v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "MAXVALUE")
            If v <> Double.MinValue Then _maxValue = v

            v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "CENTERVALUE")
            If v <> Double.MinValue Then _centerValue = v

            ' ── FaderScaling ───────────────────────────────────────────────────
            v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "FADERSCALING")
            If v <> Double.MinValue Then _faderScaling = v <> 0

            ' ── AutomationItemsOptions ─────────────────────────────────────────
            v = utility.GetEnvelopePropVal(_trackIndex, _envIndex, "AUTO_ITEMS_OPTIONS")
            If v <> Double.MinValue Then _automationItemsOptions = CInt(v)

            ' ── SendId (I_SEND_IDX) — lecture seule ────────────────────────────
            ' -1 = enveloppe de piste ou de paramètre FX (pas un envoi).
            ' ≥ 0 = index 0-based de l'envoi auquel cette enveloppe est liée.
            v = utility.GetEnvelopePropVal2(_trackIndex, _envIndex, "I_SEND_IDX")
            If v <> Double.MinValue Then _sendId = CInt(v)
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
        '''  [9]  B_SOLO_DEFEAT    [10] B_AUTO_RECARM   [11] B_HEIGHTLOCK
        '''  [12] B_SHOWINMIXER    [13] B_SHOWINTCP      [14] B_TCPPIN
        '''  [15] B_MAINSEND       [16] B_RECMON_IN_EFFECT [17] IP_TRACKNUMBER
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

        Public Sub Refresh()
            ' ── Nom ───────────────────────────────────────────────────────────
            Dim nameRaw = utility.CallReaperBridge($"GET_TRACK_NAME|{_index}")
            If Not String.IsNullOrEmpty(nameRaw) AndAlso Not nameRaw.StartsWith("ERROR") Then
                _name = nameRaw
            End If

            ' ── Tous les paramètres numériques (1 seul appel pipe) ─────────────
            Dim raw = utility.CallReaperBridge($"GET_TRACK_ALL_INFO|{_index}")
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return

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
            If _mute <> new_mute Then : _mute = new_mute : RaiseEvent On_MuteChange(_mute) : End If

            _phase = p(8) <> "0"
            _soloDefeat = p(9) <> "0"
            _autoRecArm = p(10) <> "0"
            _heightLock = p(11) <> "0"
            _showInMixer = p(12) <> "0"
            _showInTCP = p(13) <> "0"
            _tcpPin = p(14) <> "0"
            _mainSend = p(15) <> "0"
            _recMonInEffect = p(16) <> "0"

            _trackNumber = ParseI(p(17))

            Dim new_solo = ParseI(p(18))
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
        ''' <summary>
        ''' Met à jour le cache de volume depuis le moniteur sans écrire dans Reaper.
        ''' Déclenche <see cref="On_VolChange"/> si la valeur a changé.
        ''' </summary>
        Public Sub SetVolFromMonitor(vol As Double)
            If _vol <> vol Then
                _vol = vol
                RaiseEvent On_VolChange(_vol)
            End If
        End Sub

        ''' <summary>
        ''' Met à jour le cache de pan depuis le moniteur sans écrire dans Reaper.
        ''' Déclenche <see cref="On_PanChange"/> si la valeur a changé.
        ''' </summary>
        Public Sub SetPanFromMonitor(pan As Double)
            If _pan <> pan Then
                _pan = pan
                RaiseEvent On_PanChange(_pan)
            End If
        End Sub

        ''' <summary>
        ''' Met à jour le cache de mute depuis le moniteur sans écrire dans Reaper.
        ''' Déclenche <see cref="On_MuteChange"/> si l'état a changé.
        ''' </summary>
        Public Sub SetMuteFromMonitor(mute As Boolean)
            If _mute <> mute Then
                _mute = mute
                RaiseEvent On_MuteChange(_mute)
            End If
        End Sub
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
        ''' <summary>
        ''' Met à jour les champs depuis les données d'un appel batch.
        ''' Déclenche uniquement les événements dont la valeur a changé.
        ''' Appelé par <see cref="ReaperTracksSend.Refresh"/>.
        ''' </summary>
        Friend Sub UpdateFromParts(vol As Double, pan As Double, panLaw As Double,
                                    mute As Boolean, phase As Boolean, mono As Boolean,
                                    sendMode As Integer, srcChan As Integer,
                                    dstChan As Integer, midiFlags As Integer)
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
    '  REAPER TRACKS SEND — envoi N°X pour une collection de pistes en 1 appel
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Rafraîchit en un seul appel pipe l'envoi à l'index <see cref="SendIndex"/>
    ''' pour une collection de pistes.
    ''' Chaque <see cref="ReaperSend"/> est mis à jour et ses événements
    ''' <c>On_XxxChange</c> sont déclenchés si la valeur a changé.
    ''' </summary>
    Public Class ReaperTracksSend

        Private ReadOnly _sendIndex As Integer
        Private ReadOnly _category As Integer
        Private ReadOnly _sends As New List(Of ReaperSend)

        ''' <summary>Déclenché après chaque <see cref="Refresh"/> réussi.</summary>
        Public Event On_Refreshed()

        ''' <param name="sendIndex">Index 0-based de l'envoi sur chaque piste.</param>
        ''' <param name="trackIndexes">Indexes 0-based des pistes à surveiller.</param>
        ''' <param name="category"><see cref="SendCategory"/> (défaut : Sends).</param>
        Public Sub New(sendIndex As Integer,
                       trackIndexes As IEnumerable(Of Integer),
                       Optional category As SendCategory = SendCategory.Sends)
            _sendIndex = sendIndex
            _category = CInt(category)
            For Each ti In trackIndexes
                _sends.Add(New ReaperSend(ti, _category, sendIndex))
            Next
        End Sub

        ''' <summary>Index de l'envoi commun à toutes les pistes.</summary>
        Public ReadOnly Property SendIndex As Integer
            Get
                Return _sendIndex : End Get
        End Property

        ''' <summary>Catégorie des envois.</summary>
        Public ReadOnly Property Category As SendCategory
            Get
                Return CType(_category, SendCategory) : End Get
        End Property

        ''' <summary>Nombre de pistes surveillées.</summary>
        Public ReadOnly Property Count As Integer
            Get
                Return _sends.Count : End Get
        End Property

        ''' <summary>Accès à l'envoi d'une piste par sa position dans la collection.</summary>
        Default Public ReadOnly Property Item(index As Integer) As ReaperSend
            Get
                Return _sends(index) : End Get
        End Property

        ''' <summary>Liste en lecture seule des envois.</summary>
        Public ReadOnly Property Sends As IReadOnlyList(Of ReaperSend)
            Get
                Return _sends : End Get
        End Property

        ''' <summary>
        ''' Charge tous les envois en un seul appel pipe via <c>GET_TRACKS_SENDS</c>.
        ''' Déclenche les événements <c>On_XxxChange</c> sur chaque <see cref="ReaperSend"/>
        ''' uniquement si la valeur a changé.
        ''' </summary>
        Public Sub Refresh()
            If _sends.Count = 0 Then Return

            ' ── Construire la commande batch ──────────────────────────────────
            ' GET_TRACKS_SENDS|sendIndex|category|trackIdx0|trackIdx1|...
            Dim parts As New List(Of String) From {
                "GET_TRACKS_SENDS",
                _sendIndex.ToString(CultureInfo.InvariantCulture),
                _category.ToString(CultureInfo.InvariantCulture)
            }
            For Each s In _sends
                parts.Add(s.TrackIndex.ToString(CultureInfo.InvariantCulture))
            Next

            Dim raw = utility.CallReaperBridge(String.Join("|", parts))
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return

            ' ── Parser la réponse : 10 champs par piste ───────────────────────
            ' vol|pan|panLaw|mute|phase|mono|sendMode|srcChan|dstChan|midiFlags
            Dim p = raw.Split("|"c)
            Const FieldsPerSend As Integer = 10
            Dim ic = CultureInfo.InvariantCulture

            For i = 0 To _sends.Count - 1
                Dim offset = i * FieldsPerSend
                If offset + FieldsPerSend > p.Length Then Exit For

                Dim vol As Double = 0, pan As Double = 0, panLaw As Double = -1
                Double.TryParse(p(offset), NumberStyles.Any, ic, vol)
                Double.TryParse(p(offset + 1), NumberStyles.Any, ic, pan)
                Double.TryParse(p(offset + 2), NumberStyles.Any, ic, panLaw)

                Dim sendMode As Integer = 0, srcChan As Integer = 0
                Dim dstChan As Integer = 0, midiFlags As Integer = 0
                Integer.TryParse(p(offset + 6), sendMode)
                Integer.TryParse(p(offset + 7), srcChan)
                Integer.TryParse(p(offset + 8), dstChan)
                Integer.TryParse(p(offset + 9), midiFlags)

                _sends(i).UpdateFromParts(
                    vol, pan, panLaw,
                    p(offset + 3) <> "0",
                    p(offset + 4) <> "0",
                    p(offset + 5) <> "0",
                    sendMode, srcChan, dstChan, midiFlags)
            Next

            RaiseEvent On_Refreshed()
        End Sub

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
        Public Event SuspendMonitoring(_paramIndex As Integer, status As Boolean)

        Public Sub New(trackIndex As Integer, fxIndex As Integer, paramIndex As Integer)
            _trackIndex = trackIndex
            _fxIndex = fxIndex
            _paramIndex = paramIndex
            Refresh()
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
                If _value <> Value Then
                    _value = Value
                    RaiseEvent SuspendMonitoring(_paramIndex, True)
                    utility.CallReaperBridge(
                    $"SET_FX_PARAM_NORMALIZED|{_trackIndex}|{_fxIndex}|{_paramIndex}|{Value.ToString("G", Ic)}")
                    RaiseEvent SuspendMonitoring(_paramIndex, False)
                    RaiseEvent On_ValueChange(_value)
                End If
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
                Set_NewValue(new_val)

            End If

            Dim fmtRaw = utility.CallReaperBridge(
                $"GET_FX_PARAM_FORMATTED|{_trackIndex}|{_fxIndex}|{_paramIndex}")
            If Not String.IsNullOrEmpty(fmtRaw) AndAlso Not fmtRaw.StartsWith("ERROR") Then
                _formatted = fmtRaw
            End If
        End Sub

        Public Sub SetValueFromMonitor(newValue As Double)
            Set_NewValue(newValue)
        End Sub

        Private Sub Set_NewValue(new_val As Double)
            If _value <> new_val Then
                _value = new_val
                RaiseEvent On_ValueChange(_value)
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

        ' ── Monitor ───────────────────────────────────────────────────────────
        Private ReadOnly _projectMonitor As New Monitors.ReaperProjectMonitor()
        Private _disposed As Boolean = False

        ' ── Collections ───────────────────────────────────────────────────────
        Private ReadOnly _tracks As New List(Of ReaperTrack)
        Private ReadOnly _trackFXList As New List(Of ReaperTrackFX)
        Private ReadOnly _trackGuids As New List(Of String)

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

            ' ── Câbler puis démarrer le monitor APRÈS le chargement initial ──
            AddHandler _projectMonitor.On_TrackCountChanged, AddressOf OnTrackCountChanged
            _projectMonitor.Start()
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
            If String.IsNullOrEmpty(raw) OrElse raw.StartsWith("ERROR") Then Return
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
            If String.IsNullOrEmpty(countRaw) OrElse countRaw.StartsWith("ERROR") Then Return 0
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
                    RaiseEvent On_TrackGuidsChanged(_trackGuids)
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
                    ' ── Désabonner avant Dispose pour éviter tout appel tardif ─
                    RemoveHandler _projectMonitor.On_TrackCountChanged,
                                  AddressOf OnTrackCountChanged
                    _projectMonitor.Dispose()
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
        Private _disposed As Boolean = False
        Private _activeProjectIndex As Integer = -1
        Private _openProjects As IReadOnlyList(Of String) = Array.Empty(Of String)()

        ' ── Projet géré en interne ────────────────────────────────────────────
        Private _project As ReaperProject = Nothing

        ' ── Événements ────────────────────────────────────────────────────────

        Public Event On_ActiveProjectChanged(projectIndex As Integer, filePath As String)
        Public Event On_OpenProjectsChanged(projects As IReadOnlyList(Of String))

        ''' <summary>
        ''' Déclenché quand le projet actif dans Reaper change (changement d'onglet).
        ''' <br/>
        ''' L'ancien <see cref="ReaperProject"/> est déjà disposé avant le déclenchement.
        ''' <paramref name="project"/> est un nouveau <see cref="ReaperProject"/> déjà
        ''' initialisé et synchronisé avec Reaper — câbler la surface dessus directement.
        ''' </summary>
        Public Event On_ProjectChanged(project As ReaperProject)

        Public Sub New()
            AddHandler _monitor.On_ActiveProjectChanged, AddressOf OnActiveProjectChanged
            AddHandler _monitor.On_OpenProjectsChanged, AddressOf OnOpenProjectsChanged
        End Sub

        ' ── Propriétés ────────────────────────────────────────────────────────

        ''' <summary>
        ''' Projet Reaper actif, créé et disposé automatiquement par la session.
        ''' Peut être Nothing si Reaper n'est pas encore joignable.
        ''' </summary>
        Public ReadOnly Property Project As ReaperProject
            Get
                Return _project
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

            ' ── Créer le premier ReaperProject ────────────────────────────────
            SwapProject()
        End Sub

        Public Sub [Stop]()
            _monitor.Stop()
        End Sub

        ' ── Gestion du cycle de vie du projet ─────────────────────────────────

        ''' <summary>
        ''' Dispose l'ancien projet et en crée un nouveau synchronisé avec Reaper.
        ''' Déclenche <see cref="On_ProjectChanged"/> avec la nouvelle instance.
        ''' </summary>
        Private Sub SwapProject()
            _project?.Dispose()
            _project = New ReaperProject()
            RaiseEvent On_ProjectChanged(_project)
        End Sub

        ' ── Handlers internes ─────────────────────────────────────────────────

        Private Sub OnActiveProjectChanged(newFilePath As String, previousFilePath As String)
            _activeProjectIndex = FindProjectIndex(newFilePath)
            RaiseEvent On_ActiveProjectChanged(_activeProjectIndex, newFilePath)

            ' ── Remplacer le projet automatiquement ───────────────────────────
            SwapProject()
        End Sub

        Private Sub OnOpenProjectsChanged(projects As IReadOnlyList(Of String))
            _openProjects = projects
            _activeProjectIndex = FindProjectIndex(_monitor.CurrentFilePath)
            RaiseEvent On_OpenProjectsChanged(projects)
        End Sub

        Private Function FindProjectIndex(filePath As String) As Integer
            If String.IsNullOrEmpty(filePath) Then Return -1
            For i = 0 To _openProjects.Count - 1
                If _openProjects(i) = filePath Then Return i
            Next
            Return -1
        End Function

        ' ── IDisposable ───────────────────────────────────────────────────────

        Public Sub Dispose() Implements IDisposable.Dispose
            If Not _disposed Then
                _monitor.Dispose()
                _project?.Dispose()   ' ← disposer le projet courant
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

        Private _autostart As Boolean = False
        Private _selectedInterface As String = String.Empty
        Private _vsDebug As Boolean = False
        Private ReadOnly _allNetworkInterfaces As New List(Of String)
        Private ReadOnly _availableInterfaces As New List(Of String)

        Public Event On_PluginsRefreshed(count As Integer)
        Public Event On_InterfacesRefreshed(count As Integer)

        Public Sub New()
            _iniPath = IO.Path.Combine(AppContext.BaseDirectory, "ReaperDriver.ini")

            RefreshNetworkInterfaces()

            Dim fileExistedBeforeLoad = IO.File.Exists(_iniPath)
            Load()

            If Not fileExistedBeforeLoad Then
                Save()
            End If
        End Sub

        ' ── Propriétés ────────────────────────────────────────────────────────

        ''' <summary>Chemin complet du fichier INI.</summary>
        Public ReadOnly Property IniPath As String
            Get
                Return _iniPath
            End Get
        End Property

        ''' <summary>Plugins installés dans Reaper (section [Plugins]).</summary>
        Public ReadOnly Property Plugins As IReadOnlyList(Of String)
            Get
                Return _plugins
            End Get
        End Property

        ''' <summary>Plugins EQ définis par l'utilisateur (section [EQ]).</summary>
        Public ReadOnly Property EQPlugins As IReadOnlyList(Of String)
            Get
                Return _eq
            End Get
        End Property

        ''' <summary>Plugins de dynamique définis par l'utilisateur (section [DYN]).</summary>
        Public ReadOnly Property DynPlugins As IReadOnlyList(Of String)
            Get
                Return _dyn
            End Get
        End Property

        ''' <summary>
        ''' Si True, le driver démarre automatiquement à l'ouverture de l'application.
        ''' Persisté dans <c>[Driver] Autostart=</c> du fichier INI.
        ''' </summary>
        Public Property Autostart As Boolean
            Get
                Return _autostart
            End Get
            Set
                _autostart = Value
                Save()
            End Set
        End Property

        ''' <summary>
        ''' Nom de l'interface réseau sélectionnée pour le driver Control24.
        ''' Persisté dans <c>[Driver] NetworkInterface=</c> du fichier INI.
        ''' </summary>
        Public Property SelectedNetworkInterface As String
            Get
                Return _selectedInterface
            End Get
            Set
                _selectedInterface = Value
                Save()
            End Set
        End Property

        ''' <summary>
        ''' Si True, le driver attend qu'un débogueur Visual Studio s'attache
        ''' au processus au démarrage. Défaut : False.
        ''' Persisté dans <c>[Driver] VSDebug=</c> du fichier INI.
        ''' </summary>
        Public Property VSDebug As Boolean
            Get
                Return _vsDebug
            End Get
            Set
                _vsDebug = Value
                Save()
            End Set
        End Property

        ''' <summary>
        ''' Liste en lecture seule de toutes les interfaces réseau présentes
        ''' sur l'ordinateur, quel que soit leur type ou leur état.
        ''' </summary>
        Public ReadOnly Property AllNetworkInterfaces As IReadOnlyList(Of String)
            Get
                Return _allNetworkInterfaces
            End Get
        End Property

        ''' <summary>
        ''' Sous-ensemble filtré Ethernet / Wi-Fi à l'état Up ou Unknown.
        ''' </summary>
        Public ReadOnly Property AvailableInterfaces As IReadOnlyList(Of String)
            Get
                Return _availableInterfaces
            End Get
        End Property

        ''' <summary>
        ''' True si <see cref="SelectedNetworkInterface"/> correspond à une interface
        ''' présente dans <see cref="AllNetworkInterfaces"/>.
        ''' </summary>
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

        ' ── Interfaces réseau ─────────────────────────────────────────────────

        ''' <summary>
        ''' Interroge le système pour toutes les interfaces réseau et met à jour
        ''' <see cref="AllNetworkInterfaces"/> et <see cref="AvailableInterfaces"/>.
        ''' </summary>
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
        ''' Lit le fichier INI via <see cref="ReaperConfigReader"/> et peuple
        ''' les champs internes sans déclencher de <see cref="Save"/> intermédiaire.
        ''' </summary>
        Private Sub Load()
            Dim reader As New ReaperConfigReader(_iniPath)
            reader.Load()

            If Not reader.IsLoaded Then Return

            _autostart = reader.GetBoolean("Driver", "Autostart", False)
            _selectedInterface = reader.GetString("Driver", "NetworkInterface", String.Empty)
            _vsDebug = reader.GetBoolean("Driver", "VSDebug", False)

            _plugins.Clear()
            _plugins.AddRange(reader.GetList("Plugins", "Plugin"))

            _eq.Clear()
            _eq.AddRange(reader.GetList("EQ", "EQ"))

            _dyn.Clear()
            _dyn.AddRange(reader.GetList("DYN", "DYN"))
        End Sub

        ' ── Mise à jour des plugins ───────────────────────────────────────────

        ''' <summary>
        ''' Interroge Reaper pour la liste des plugins installés et sauvegarde le fichier INI.
        ''' </summary>
        Public Sub RefreshPlugins()
            _plugins.Clear()
            Dim installed = Reaper.utility.GetInstalledFXList
            For Each fx In installed
                _plugins.Add(fx.Name)
            Next
            Save()
            RaiseEvent On_PluginsRefreshed(_plugins.Count)
        End Sub

        ' ── Sauvegarde ────────────────────────────────────────────────────────

        ''' <summary>
        ''' Écrit les cinq sections dans le fichier INI via <see cref="ReaperConfigWriter"/>.
        ''' </summary>
        Public Sub Save()
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

            writer.AddSection("Interfaces",
                              "; Liste de toutes les interfaces réseau détectées sur cet ordinateur." &
                              Environment.NewLine &
                              "; Section générée automatiquement — ne pas modifier." &
                              Environment.NewLine &
                              "; Copier un nom dans [Driver] NetworkInterface= pour l'utiliser.")
            writer.SetList("Interfaces", "Interface", _allNetworkInterfaces)

            writer.AddSection("Plugins", "; Mis à jour automatiquement au démarrage — ne pas modifier")
            writer.SetList("Plugins", "Plugin", _plugins)

            writer.AddSection("EQ", "; Noms exacts des plugins EQ (copier depuis [Plugins])")
            writer.SetList("EQ", "EQ", _eq)

            writer.AddSection("DYN", "; Noms exacts des plugins de dynamique (copier depuis [Plugins])")
            writer.SetList("DYN", "DYN", _dyn)

            writer.Save()
        End Sub

    End Class


End Namespace
