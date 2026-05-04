Imports System.Diagnostics
Imports System.Runtime.InteropServices
Imports DriverConfig


Public Class C24Config
    Public Property InterfaceName As String = String.Empty
    Public Property AutoStart As Boolean = True

End Class

Public Class ReaperBridge


    Private Shared WithEvents _driverProcess As Process = Nothing
    Private Shared _dllDirectory As String = String.Empty

    ' ✅ Pointeurs statiques — doivent rester en vie tant que Reaper tourne
    Private Shared _actionIdPtr As IntPtr = IntPtr.Zero
    Private Shared _actionNamePtr As IntPtr = IntPtr.Zero
    Private Shared _actionStructPtr As IntPtr = IntPtr.Zero

    ' ✅ ID mis en cache après enregistrement
    Private Shared _commandId As Integer = 0

    Private Shared ReadOnly ConfigPath As String =
            IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ReaControl24", "control24_config.json")

    ' ── Pointeur pré-alloué pour noSortIn=True (InsertEnvelopePoint en mode batch) ──
    ' bool* → True : indique à Reaper de ne pas trier après chaque insertion.
    ' Alloué une seule fois au démarrage — valide pour toute la durée du processus.
    ' Passer IntPtr.Zero à la place = tri automatique après chaque insertion.
    Private Shared ReadOnly _noSortPtr As IntPtr = InitNoSortPtr()

    Private Shared Function InitNoSortPtr() As IntPtr
        Dim p = Marshal.AllocHGlobal(1)
        Marshal.WriteByte(p, 1)   ' true
        Return p
    End Function

    Public Shared Function Initialize(rec As IntPtr, dllDirectory As String) As Integer
        If rec = IntPtr.Zero Then Return 0

        _dllDirectory = dllDirectory


        Dim autoStart As Boolean = False
        Dim VSDebug As Boolean = False

        'load config from ini file
        Dim iniFilePath As String = IO.Path.Combine(dllDirectory, "ReaperDriver.ini")
        If IO.File.Exists(iniFilePath) Then
            Dim _ReaperConfigReader As New ReaperConfigReader(iniFilePath)
            _ReaperConfigReader.Load()
            autoStart = _ReaperConfigReader.GetBoolean("Driver", "Autostart", False)
            VSDebug = _ReaperConfigReader.GetBoolean("Driver", "VSDebug", False)
        End If



        If autoStart Then

            Task.Run(Function() StartPipeServerAsync())

            Task.Run(Function() StartPollPipeServerAsync())

            If VSDebug = False Then
                LaunchDriver()
            End If

        End If

        Return 1
    End Function



    Public Shared Function OnReaperCommand(cmd As Integer, flag As Integer) As Boolean
        'If _commandId <> 0 AndAlso cmd = _commandId Then
        '    ReaperAPI.ShowConsoleMsg($"[Control24] Config action triggered{Environment.NewLine}")
        '    ShowConfigDialog()
        '    Return True
        'End If
        Return False
    End Function

    ' ── Console Reaper ────────────────────────────────────────────────────────

    ''' <summary>
    ''' Affiche un message dans la console Reaper (View → ReaScript console).
    ''' Ajoute automatiquement un retour à la ligne en fin de message.
    ''' Sans effet si <see cref="ReaperAPI.ShowConsoleMsg"/> n'est pas initialisé.
    ''' </summary>
    ''' <param name="message">Texte à afficher.</param>
    Public Shared Sub Log(message As String)
        If ReaperAPI.ShowConsoleMsg Is Nothing Then Return
        ReaperAPI.ShowConsoleMsg(message & Environment.NewLine)
    End Sub

    ''' <summary>
    ''' Affiche un message préfixé par <c>[Control24]</c> dans la console Reaper.
    ''' </summary>
    ''' <param name="message">Texte à afficher.</param>
    Public Shared Sub LogInfo(message As String)
        Log($"[Control24] {message}")
    End Sub

    ''' <summary>
    ''' Affiche un message d'erreur préfixé par <c>[Control24] ERROR:</c>
    ''' dans la console Reaper.
    ''' </summary>
    ''' <param name="message">Description de l'erreur.</param>
    Public Shared Sub LogError(message As String)
        Log($"[Control24] ERROR: {message}")
    End Sub

    Private Shared Sub LaunchDriver()
        Dim driverPath = IO.Path.Combine(_dllDirectory, "DriverReaperControl24.exe")
        If Not IO.File.Exists(driverPath) Then Return

        _driverProcess = New Process() With {
            .StartInfo = New ProcessStartInfo(driverPath) With {
                .CreateNoWindow = False,
                .UseShellExecute = False
            },
            .EnableRaisingEvents = True
        }

        ' ── Handler de fin de processus ───────────────────────────────────────
        ' Déclenché quand le driver s'arrête de lui-même (crash ou arrêt propre).
        AddHandler _driverProcess.Exited, Sub(s, e)
                                              LogInfo("Driver process exited.")
                                          End Sub

        _driverProcess.Start()
    End Sub

    ''' <summary>
    ''' Arrête le driver en cours d'exécution.
    ''' Appelé par <see cref="Shutdown"/> avant le déchargement du plugin.
    ''' </summary>
    Private Shared Sub StopDriver()
        If _driverProcess Is Nothing Then Return
        If _driverProcess.HasExited Then Return
        _driverProcess.Kill()
    End Sub

    ' ── Cycle de vie du plugin ────────────────────────────────────────────────

    ''' <summary>
    ''' Appelé par <c>ReaperEntry.ReaperPluginEntry</c> quand <c>rec = IntPtr.Zero</c>,
    ''' c'est-à-dire juste avant que Reaper décharge le plugin (fermeture ou rechargement).
    ''' Toute logique de nettoyage doit être placée ici.
    ''' </summary>
    Public Shared Sub Shutdown()
        LogInfo("Shutting down...")
        StopDriver()
        ' ← Ajouter ici tout autre nettoyage pré-déchargement
    End Sub




    Private Shared Async Function StartPipeServerAsync() As Task
        While True
            Dim hasError = False
            Try
                Using pipe As New IO.Pipes.NamedPipeServerStream(
                        "c24_reaper_bridge", IO.Pipes.PipeDirection.InOut, 1,
                        IO.Pipes.PipeTransmissionMode.Byte)   ' ✅ Byte = cross-platform (Message = Windows only)
                    Await pipe.WaitForConnectionAsync()
                    Dim reader As New IO.StreamReader(pipe)
                    Dim writer As New IO.StreamWriter(pipe) With {.AutoFlush = True}
                    Dim request = Await reader.ReadLineAsync()
                    If request IsNot Nothing Then
                        Await writer.WriteLineAsync(HandleRequest(request))
                    End If
                End Using
            Catch
                hasError = True
            End Try
            If hasError Then Await Task.Delay(100)
        End While
    End Function
    ''' <summary>
    ''' Démarre le serveur pipe persistant pour le polling de changements.
    ''' Contrairement au pipe on-demand, une seule connexion est maintenue ouverte
    ''' et traite N requêtes séquentielles sans reconnexion.
    ''' Nom du pipe : c24_reaper_poll
    ''' </summary>
    Private Shared Async Function StartPollPipeServerAsync() As Task
        While True
            Dim hasError = False
            Try
                Using pipe As New IO.Pipes.NamedPipeServerStream(
                        "c24_reaper_poll",
                        IO.Pipes.PipeDirection.InOut, 1,
                        IO.Pipes.PipeTransmissionMode.Byte)
                    Await pipe.WaitForConnectionAsync()
                    Dim reader As New IO.StreamReader(pipe)
                    Dim writer As New IO.StreamWriter(pipe) With {.AutoFlush = True}

                    ' ── Boucle multi-requêtes sur la même connexion ────────────
                    Dim request = Await reader.ReadLineAsync()
                    While request IsNot Nothing AndAlso pipe.IsConnected
                        Await writer.WriteLineAsync(HandleRequest(request))
                        request = Await reader.ReadLineAsync()
                    End While
                End Using
            Catch
                hasError = True
            End Try
            If hasError Then Await Task.Delay(50)
        End While
    End Function
    Private Shared Function HandleRequest(request As String) As String
        Try
            Dim parts = request.Split("|"c)
            Select Case parts(0)

                 ' ── SHOW_CONSOLE_MSG|message ──────────────────────────────────
                ' Affiche un message dans la console Reaper (View → ReaScript console).
                ' Le message peut contenir des "|" — tout ce qui suit le premier "|"
                ' est reconstitué comme texte complet.
                ' Réponse : "OK" ou "ERROR:API unavailable"
                Case "SHOW_CONSOLE_MSG"
                    If parts.Length < 2 Then Return "ERROR:Usage SHOW_CONSOLE_MSG|message"
                    If ReaperAPI.ShowConsoleMsg Is Nothing Then Return "ERROR:API unavailable"
                    Dim msg = String.Join("|", parts, 1, parts.Length - 1)
                    ReaperAPI.ShowConsoleMsg(msg & Environment.NewLine)
                    Return "OK"

                                    ' ── SHOW_MESSAGE_BOX|type|title|message ───────────────────────
                ' Affiche une boîte de dialogue modale dans Reaper.
                ' type    : 0=OK  1=OK/Cancel  2=Abort/Retry/Ignore
                '           3=Yes/No/Cancel  4=Yes/No  5=Retry/Cancel
                ' title   : titre de la boîte (peut contenir des espaces)
                ' message : texte affiché (peut contenir des "|" — reconstitué)
                ' Réponse : code bouton cliqué (entier)
                '   1=OK  2=Cancel  3=Abort  4=Retry  5=Ignore  6=Yes  7=No
                Case "SHOW_MESSAGE_BOX"
                    If parts.Length < 4 Then Return "ERROR:Usage SHOW_MESSAGE_BOX|type|title|message"
                    If ReaperAPI.ShowMessageBox Is Nothing Then Return "ERROR:API unavailable"
                    Dim msgType As Integer = 0
                    Integer.TryParse(parts(1), msgType)
                    Dim title = parts(2)
                    Dim msg = String.Join("|", parts, 3, parts.Length - 3)
                    Return ReaperAPI.ShowMessageBox(msg, title, msgType).ToString()

                Case "PING"
                    Return "PONG"

                Case "GET_TRACK_COUNT"
                    If ReaperAPI.GetNumTracks Is Nothing Then Return "ERROR:API unavailable"
                    Return ReaperAPI.GetNumTracks().ToString()



                ' ── GET_TRACK_INFO|trackIndex|paramName ───────────────────────
                Case "GET_TRACK_INFO"
                    If parts.Length < 3 Then Return "ERROR:Usage GET_TRACK_INFO|index|param"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    If ReaperAPI.GetMediaTrackInfo_Value Is Nothing Then Return "ERROR:API unavailable"
                    Return ReaperAPI.GetMediaTrackInfo_Value(tr, parts(2)).ToString("G", Globalization.CultureInfo.InvariantCulture)

                ' ── CSurf_ON_VOL|trackIndex|volume[|relative] ────────────────
                ' volume  : [0..4] absolu (1.0 = 0 dB) — ou delta si relative=1
                ' relative: 0=absolu (défaut)  1=relatif
                ' Réponse : volume réellement appliqué par Reaper
                Case "CSurf_ON_VOL"
                    If parts.Length < 3 Then Return "ERROR:Usage CSurf_ON_VOL|trackIndex|volume[|relative]"
                    If ReaperAPI.CSurf_OnVolumeChange Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim vol = Double.Parse(parts(2), Globalization.CultureInfo.InvariantCulture)
                    Dim relative = parts.Length >= 4 AndAlso parts(3) = "1"
                    Dim applied = ReaperAPI.CSurf_OnVolumeChange(tr, vol, relative)
                    Return applied.ToString("G", Globalization.CultureInfo.InvariantCulture)

                ' ── SET_TRACK_INFO|trackIndex|paramName|value ─────────────────
                Case "SET_TRACK_INFO"
                    If parts.Length < 4 Then Return "ERROR:Usage SET_TRACK_INFO|index|param|value"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    If ReaperAPI.SetMediaTrackInfo_Value Is Nothing Then Return "ERROR:API unavailable"
                    Dim newVal = Double.Parse(parts(3), Globalization.CultureInfo.InvariantCulture)
                    Return If(ReaperAPI.SetMediaTrackInfo_Value(tr, parts(2), newVal), "OK", "ERROR:Set failed")

                ' ── GET_TRACK_NAME|trackIndex ─────────────────────────────────
                Case "GET_TRACK_NAME"
                    If parts.Length < 2 Then Return "ERROR:Usage GET_TRACK_NAME|index"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    If ReaperAPI.GetTrackName Is Nothing Then Return "ERROR:API unavailable"
                    Dim buf = Marshal.AllocHGlobal(256)
                    Try
                        ReaperAPI.GetTrackName(tr, buf, 256)
                        Dim trk_name As String = String.Empty
                        trk_name = Marshal.PtrToStringAnsi(buf)
                        Return trk_name

                    Finally
                        Marshal.FreeHGlobal(buf)
                    End Try
                     ' ── GET_LAST_TOUCHED_TRACK ────────────────────────────────────
                ' Réponse : index 0-based de la dernière piste modifiée, -1 si aucune
                Case "GET_LAST_TOUCHED_TRACK"
                    If ReaperAPI.GetLastTouchedTrack Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = ReaperAPI.GetLastTouchedTrack()
                    If tr = IntPtr.Zero Then Return "-1"
                    If ReaperAPI.GetMediaTrackInfo_Value Is Nothing Then Return "ERROR:API unavailable"
                    Dim trackNum = CInt(ReaperAPI.GetMediaTrackInfo_Value(tr, "IP_TRACKNUMBER"))
                    Return (trackNum - 1).ToString()   ' ✅ 0-based

                ' ── GET_PLAY_STATE ────────────────────────────────────────────
                Case "GET_PLAY_STATE"
                    If ReaperAPI.GetPlayState Is Nothing Then Return "ERROR:API unavailable"
                    Return ReaperAPI.GetPlayState().ToString()

                ' ── GET_PLAY_POSITION ─────────────────────────────────────────
                Case "GET_PLAY_POSITION"
                    If ReaperAPI.GetPlayPosition Is Nothing Then Return "ERROR:API unavailable"
                    Return ReaperAPI.GetPlayPosition().ToString("G", Globalization.CultureInfo.InvariantCulture)

                ' ── GET_TEMPO ─────────────────────────────────────────────────
                Case "GET_TEMPO"
                    If ReaperAPI.Master_GetTempo Is Nothing Then Return "ERROR:API unavailable"
                    Return ReaperAPI.Master_GetTempo().ToString("G", Globalization.CultureInfo.InvariantCulture)

                ' ── GET_TRACK_PEAK|trackIndex|channel ─────────────────────────
                ' channel : 0=Gauche  1=Droite  2+=autres canaux
                ' Réponse : amplitude linéaire [0.0 .. n] en InvariantCulture
                '           Convertir en dB : 20*log10(value)  ou  SLIDER2DB si disponible
                Case "GET_TRACK_PEAK"
                    If parts.Length < 3 Then Return "ERROR:Usage GET_TRACK_PEAK|index|channel"
                    If ReaperAPI.Track_GetPeakInfo Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim peak = ReaperAPI.Track_GetPeakInfo(tr, Integer.Parse(parts(2)))
                    Return peak.ToString("G", Globalization.CultureInfo.InvariantCulture)

                ' ── GET_TRACK_PEAK_STEREO|trackIndex ─────────────────────────
                ' Réponse : "peakL|peakR" — commodité pour éviter 2 appels pipe
                Case "GET_TRACK_PEAK_STEREO"
                    If parts.Length < 2 Then Return "ERROR:Usage GET_TRACK_PEAK_STEREO|index"
                    If ReaperAPI.Track_GetPeakInfo Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim peakL = ReaperAPI.Track_GetPeakInfo(tr, 0)
                    Dim peakR = ReaperAPI.Track_GetPeakInfo(tr, 1)
                    Return $"{peakL.ToString("G", Globalization.CultureInfo.InvariantCulture)}|{peakR.ToString("G", Globalization.CultureInfo.InvariantCulture)}"

                ' ── GET_TRACK_PEAK_HOLD|trackIndex|channel ────────────────────
                ' Réponse : valeur peak-hold en dB
                Case "GET_TRACK_PEAK_HOLD"
                    If parts.Length < 3 Then Return "ERROR:Usage GET_TRACK_PEAK_HOLD|index|channel"
                    If ReaperAPI.Track_GetPeakHoldDB Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim holdDb = ReaperAPI.Track_GetPeakHoldDB(tr, Integer.Parse(parts(2)), clear:=False)
                    Return holdDb.ToString("G", Globalization.CultureInfo.InvariantCulture)

                ' ── RESET_TRACK_PEAK_HOLD|trackIndex|channel ─────────────────
                ' Lit et réinitialise simultanément le peak-hold
                Case "RESET_TRACK_PEAK_HOLD"
                    If parts.Length < 3 Then Return "ERROR:Usage RESET_TRACK_PEAK_HOLD|index|channel"
                    If ReaperAPI.Track_GetPeakHoldDB Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim holdDb = ReaperAPI.Track_GetPeakHoldDB(tr, Integer.Parse(parts(2)), clear:=True)
                    Return holdDb.ToString("G", Globalization.CultureInfo.InvariantCulture)

                ' ── CSurf_ON_SEND_VOL|trackIndex|sendIndex|volume[|relative] ──────
                ' volume  : [0..4] absolu (1.0 = 0 dB) — ou delta si relative=1
                ' relative: 0=absolu (défaut)  1=relatif
                ' Réponse : volume réellement appliqué par Reaper
                Case "CSurf_ON_SEND_VOL"
                    If parts.Length < 4 Then Return "ERROR:Usage CSurf_ON_SEND_VOL|trackIndex|sendIndex|volume[|relative]"
                    If ReaperAPI.CSurf_OnSendVolumeChange Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim sendIdx = Integer.Parse(parts(2))
                    Dim vol = Double.Parse(parts(3), Globalization.CultureInfo.InvariantCulture)
                    Dim relative = parts.Length >= 5 AndAlso parts(4) = "1"
                    Dim applied = ReaperAPI.CSurf_OnSendVolumeChange(tr, sendIdx, vol, relative)
                    Return applied.ToString("G", Globalization.CultureInfo.InvariantCulture)

                               ' ── GET_TRACK_ALL_INFO|trackIndex ─────────────────────────────
                ' Retourne tous les paramètres numériques GetMediaTrackInfo_Value.
                ' Format fixe séparé par "|" — index stables, InvariantCulture.
                '
                ' Idx  Param                  Type    Description
                ' ───  ─────────────────────  ──────  ───────────────────────────────────────────
                '  0   D_VOL                  double  Volume trim (0=-inf  1=0dB  2=+6dB)
                '  1   D_PAN                  double  Pan trim (-1=L  0=C  1=R)
                '  2   D_WIDTH                double  Largeur stéréo (-1..1)
                '  3   D_DUALPANL             double  Dual pan L (-1..1, si I_PANMODE=6)
                '  4   D_DUALPANR             double  Dual pan R (-1..1, si I_PANMODE=6)
                '  5   D_PANLAW               double  Pan law (<0=projet défaut)
                '  6   D_PLAY_OFFSET          double  Décalage lecture (sec ou samples selon flag)
                '  7   B_MUTE                 bool    Muet
                '  8   B_PHASE                bool    Phase inversée
                '  9   B_RECMON_IN_EFFECT     bool    Monitoring actif (lecture seule)
                ' 10   B_SOLO_DEFEAT          bool    Solo defeat
                ' 11   B_AUTO_RECARM          bool    Rec arm auto à la sélection
                ' 12   B_HEIGHTLOCK           bool    Hauteur TCP verrouillée
                ' 13   B_SHOWINMIXER          bool    Visible dans le mixer
                ' 14   B_SHOWINTCP            bool    Visible dans l'arrangement
                ' 15   B_TCPPIN               bool    Épinglée en haut de l'arrangement
                ' 16   B_MAINSEND             bool    Envoie audio au parent
                ' 17   IP_TRACKNUMBER         int     Numéro 1-based (0=absent -1=master, lecture seule)
                ' 18   I_SOLO                 int     0=off 1=solo 2=solo in place 5=safe 6=safe in place
                ' 19   I_FXEN                 int     0=FX bypassé  !=0=FX actif
                ' 20   I_RECARM               int     0/1
                ' 21   I_RECINPUT             int     Entrée d'enregistrement (voir doc)
                ' 22   I_RECMODE              int     0=input 1=out stéréo 2=none 4=MIDI out…
                ' 23   I_RECMODE_FLAGS        int     &3=mode sortie enregistrement
                ' 24   I_RECMON               int     0=off 1=normal 2=pas pendant lecture
                ' 25   I_RECMONITEMS          int     0=off 1=on
                ' 26   I_VUMODE               int     Mode VU-mètre (&1=désactivé &30=type…)
                ' 27   I_AUTOMODE             int     0=trim/off 1=read 2=touch 3=write 4=latch
                ' 28   I_NCHAN                int     Canaux de piste (2-128, pair)
                ' 29   I_SELECTED             int     0/1
                ' 30   I_WNDH                 int     Hauteur TCP incl. enveloppes (lecture seule)
                ' 31   I_TCPH                 int     Hauteur TCP excl. enveloppes (lecture seule)
                ' 32   I_TCPY                 int     Position Y TCP / haut arrangement (lecture seule)
                ' 33   I_TCPSCREENY           int     Position Y TCP / écran (lecture seule)
                ' 34   I_MCPW                 int     Largeur MCP px (lecture seule)
                ' 35   I_MCPH                 int     Hauteur MCP px (lecture seule)
                ' 36   I_MCPX                 int     Position X MCP / conteneur (lecture seule)
                ' 37   I_MCPY                 int     Position Y MCP / conteneur (lecture seule)
                ' 38   I_MCPSCREENX           int     Position X MCP / écran (lecture seule)
                ' 39   I_FOLDERDEPTH          int     0=normal 1=parent dossier -1=dernier dans dossier
                ' 40   I_FOLDERCOMPACT        int     0=normal 1=réduit 2=totalement réduit
                ' 41   I_MIDIHWOUT            int     Sortie MIDI HW (<0=désactivée)
                ' 42   I_MIDI_INPUT_CHANMAP   int     -1=source 1-16=canal cible
                ' 43   I_MIDI_CTL_CHAN        int     -1=aucun lien 0-15=canal vol/pan 16=tous
                ' 44   I_MIDI_TRACKSEL_FLAG   int     Options liste MIDI editor
                ' 45   I_PERFFLAGS            int     &1=pas buffer média &2=pas FX anticipatif
                ' 46   I_CUSTOMCOLOR          int     Couleur OS | 0x1000000 (0=défaut)
                ' 47   I_HEIGHTOVERRIDE       int     Hauteur TCP forcée (0=none)
                ' 48   I_SPACER               int     1=espace TCP au-dessus
                ' 49   I_PANMODE              int     0=classic 3=balance 5=stereo pan 6=dual pan
                ' 50   I_PANLAW_FLAGS         int     0=sinus 1=hybrid 2=linéaire 3=hybrid2
                ' 51   I_PLAY_OFFSET_FLAG     int     &1=bypass &2=en samples (sinon secondes)
                ' 52   I_FREEZECOUNT          int     État freeze (lecture seule)
                ' 53   I_FREEMODE             int     1=free item pos 2=fixed lanes
                ' 54   I_NUMFIXEDLANES        int     Nombre de lanes fixes
                Case "GET_TRACK_ALL_INFO"
                    If parts.Length < 2 Then Return "ERROR:Usage GET_TRACK_ALL_INFO|index"
                    If ReaperAPI.GetMediaTrackInfo_Value Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim g = Function(p As String) _
                                ReaperAPI.GetMediaTrackInfo_Value(tr, p).ToString("G", ic)
                    Return String.Join("|",
                        g("D_VOL"), g("D_PAN"), g("D_WIDTH"),
                        g("D_DUALPANL"), g("D_DUALPANR"), g("D_PANLAW"), g("D_PLAY_OFFSET"),
                        g("B_MUTE"), g("B_PHASE"), g("B_RECMON_IN_EFFECT"),
                        g("B_SOLO_DEFEAT"), g("B_AUTO_RECARM"), g("B_HEIGHTLOCK"),
                        g("B_SHOWINMIXER"), g("B_SHOWINTCP"), g("B_TCPPIN"), g("B_MAINSEND"),
                        g("IP_TRACKNUMBER"),
                        g("I_SOLO"), g("I_FXEN"), g("I_RECARM"), g("I_RECINPUT"),
                        g("I_RECMODE"), g("I_RECMODE_FLAGS"), g("I_RECMON"), g("I_RECMONITEMS"),
                        g("I_VUMODE"), g("I_AUTOMODE"), g("I_NCHAN"), g("I_SELECTED"),
                        g("I_WNDH"), g("I_TCPH"), g("I_TCPY"), g("I_TCPSCREENY"),
                        g("I_MCPW"), g("I_MCPH"), g("I_MCPX"), g("I_MCPY"), g("I_MCPSCREENX"),
                        g("I_FOLDERDEPTH"), g("I_FOLDERCOMPACT"),
                        g("I_MIDIHWOUT"), g("I_MIDI_INPUT_CHANMAP"), g("I_MIDI_CTL_CHAN"),
                        g("I_MIDI_TRACKSEL_FLAG"), g("I_PERFFLAGS"),
                        g("I_CUSTOMCOLOR"), g("I_HEIGHTOVERRIDE"), g("I_SPACER"),
                        g("I_PANMODE"), g("I_PANLAW_FLAGS"), g("I_PLAY_OFFSET_FLAG"),
                        g("I_FREEZECOUNT"), g("I_FREEMODE"), g("I_NUMFIXEDLANES"))
                                    ' ── GET_NUM_SENDS|trackIndex|category ────────────────────────
                ' category : 0=envois  -1=reçus  1=sorties HW
                ' Réponse  : nombre d'envois (entier)
                Case "GET_NUM_SENDS"
                    If parts.Length < 3 Then Return "ERROR:Usage GET_NUM_SENDS|trackIndex|category"
                    If ReaperAPI.GetTrackNumSends Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Return ReaperAPI.GetTrackNumSends(tr, Integer.Parse(parts(2))).ToString()

                ' ── GET_SEND_INFO|trackIndex|category|sendIndex|paramName ─────
                ' category  : 0=envois  -1=reçus  1=sorties HW
                ' paramName : D_VOL D_PAN D_PANLAW D_MUTE B_PHASE B_MONO
                '             I_SENDMODE I_SRCCHAN I_DSTCHAN I_MIDIFLAGS
                ' Réponse   : valeur double en InvariantCulture
                Case "GET_SEND_INFO"
                    If parts.Length < 5 Then Return "ERROR:Usage GET_SEND_INFO|trackIndex|category|sendIndex|paramName"
                    If ReaperAPI.GetTrackSendInfo_Value Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim value = ReaperAPI.GetTrackSendInfo_Value(
                        tr,
                        Integer.Parse(parts(2)),
                        Integer.Parse(parts(3)),
                        parts(4))
                    Return value.ToString("G", Globalization.CultureInfo.InvariantCulture)

                ' ── SET_SEND_INFO|trackIndex|category|sendIndex|paramName|value
                ' Réponse : OK ou ERROR:...
                Case "SET_SEND_INFO"
                    If parts.Length < 6 Then Return "ERROR:Usage SET_SEND_INFO|trackIndex|category|sendIndex|paramName|value"
                    If ReaperAPI.SetTrackSendInfo_Value Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim newVal = Double.Parse(parts(5), Globalization.CultureInfo.InvariantCulture)
                    Return If(ReaperAPI.SetTrackSendInfo_Value(
                        tr,
                        Integer.Parse(parts(2)),
                        Integer.Parse(parts(3)),
                        parts(4),
                        newVal), "OK", "ERROR:Set failed")

                ' ── GET_SEND_ALL_INFO|trackIndex|category|sendIndex ───────────
                ' Retourne tous les paramètres d'un envoi en un seul appel pipe.
                ' Format fixe séparé par "|" (InvariantCulture) :
                '
                ' Idx  Param        Description
                ' ───  ───────────  ──────────────────────────────────────────
                '  0   D_VOL        Volume (0..4  1.0=0dB)
                '  1   D_PAN        Pan (-1=L  0=C  1=R)
                '  2   D_PANLAW     Pan law (<0=projet défaut)
                '  3   D_MUTE       Muet (0/1)
                '  4   B_PHASE      Phase inversée (0/1)
                '  5   B_MONO       Mono (0/1)
                '  6   I_SENDMODE   0=post-fader  1=pre-fx  3=pre-fader
                '  7   I_SRCCHAN    Canal source (bit10=stéréo  bit11=phase inv.)
                '  8   I_DSTCHAN    Canal destination
                '  9   I_MIDIFLAGS  Flags MIDI
                Case "GET_SEND_ALL_INFO"
                    If parts.Length < 4 Then Return "ERROR:Usage GET_SEND_ALL_INFO|trackIndex|category|sendIndex"
                    If ReaperAPI.GetTrackSendInfo_Value Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim cat = Integer.Parse(parts(2))
                    Dim si = Integer.Parse(parts(3))
                    Dim g = Function(p As String) _
                                ReaperAPI.GetTrackSendInfo_Value(tr, cat, si, p).ToString("G", ic)
                    Return String.Join("|",
                        g("D_VOL"), g("D_PAN"), g("D_PANLAW"), g("D_MUTE"),
                        g("B_PHASE"), g("B_MONO"),
                        g("I_SENDMODE"), g("I_SRCCHAN"), g("I_DSTCHAN"), g("I_MIDIFLAGS"))
                                    ' ── GET_FX_COUNT|trackIndex ───────────────────────────────────
                Case "GET_FX_COUNT"
                    If parts.Length < 2 Then Return "ERROR:Usage GET_FX_COUNT|trackIndex"
                    If ReaperAPI.TrackFX_GetCount Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Return ReaperAPI.TrackFX_GetCount(tr).ToString()

                ' ── GET_FX_ALL_INFO|trackIndex|fxIndex ────────────────────────
                ' Réponse : "name|enabled|paramCount|preset"
                Case "GET_FX_ALL_INFO"
                    If parts.Length < 3 Then Return "ERROR:Usage GET_FX_ALL_INFO|trackIndex|fxIndex"
                    If ReaperAPI.TrackFX_GetFXName Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim fx = Integer.Parse(parts(2))
                    Dim nameBuf = Marshal.AllocHGlobal(512)
                    Dim presBuf = Marshal.AllocHGlobal(512)
                    Try
                        ReaperAPI.TrackFX_GetFXName(tr, fx, nameBuf, 512)
                        Dim fxName = If(Marshal.PtrToStringAnsi(nameBuf), String.Empty)
                        Dim enabled = If(ReaperAPI.TrackFX_GetEnabled IsNot Nothing AndAlso
                                         ReaperAPI.TrackFX_GetEnabled(tr, fx), "1", "0")
                        Dim paramCount = If(ReaperAPI.TrackFX_GetNumParams IsNot Nothing,
                                            ReaperAPI.TrackFX_GetNumParams(tr, fx), 0)
                        ReaperAPI.TrackFX_GetPreset?(tr, fx, presBuf, 512)
                        Dim preset = If(Marshal.PtrToStringAnsi(presBuf), String.Empty)
                        Return $"{fxName}|{enabled}|{paramCount}|{preset}"
                    Finally
                        Marshal.FreeHGlobal(nameBuf)
                        Marshal.FreeHGlobal(presBuf)
                    End Try

                ' ── SET_FX_ENABLED|trackIndex|fxIndex|value ───────────────────
                Case "SET_FX_ENABLED"
                    If parts.Length < 4 Then Return "ERROR:Usage SET_FX_ENABLED|trackIndex|fxIndex|value"
                    If ReaperAPI.TrackFX_SetEnabled Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    ReaperAPI.TrackFX_SetEnabled(tr, Integer.Parse(parts(2)), parts(3) <> "0")
                    Return "OK"

                ' ── GET_FX_PARAM_NAME|trackIndex|fxIndex|paramIndex ───────────
                Case "GET_FX_PARAM_NAME"
                    If parts.Length < 4 Then Return "ERROR:Usage GET_FX_PARAM_NAME|trackIndex|fxIndex|paramIndex"
                    If ReaperAPI.TrackFX_GetParamName Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim buf = Marshal.AllocHGlobal(256)
                    Try
                        ReaperAPI.TrackFX_GetParamName(tr, Integer.Parse(parts(2)), Integer.Parse(parts(3)), buf, 256)
                        Return If(Marshal.PtrToStringAnsi(buf), String.Empty)
                    Finally
                        Marshal.FreeHGlobal(buf)
                    End Try

                ' ── GET_FX_PARAM_NORMALIZED|trackIndex|fxIndex|paramIndex ─────
                ' Réponse : valeur normalisée [0..1]
                Case "GET_FX_PARAM_NORMALIZED"
                    If parts.Length < 4 Then Return "ERROR:Usage GET_FX_PARAM_NORMALIZED|trackIndex|fxIndex|paramIndex"
                    If ReaperAPI.TrackFX_GetParamNormalized Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Return ReaperAPI.TrackFX_GetParamNormalized(
                        tr, Integer.Parse(parts(2)), Integer.Parse(parts(3))
                    ).ToString("G", Globalization.CultureInfo.InvariantCulture)

                ' ── SET_FX_PARAM_NORMALIZED|trackIndex|fxIndex|paramIndex|value
                Case "SET_FX_PARAM_NORMALIZED"
                    If parts.Length < 5 Then Return "ERROR:Usage SET_FX_PARAM_NORMALIZED|trackIndex|fxIndex|paramIndex|value"
                    If ReaperAPI.TrackFX_SetParamNormalized Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim v = Double.Parse(parts(4), Globalization.CultureInfo.InvariantCulture)
                    Return If(ReaperAPI.TrackFX_SetParamNormalized(tr, Integer.Parse(parts(2)), Integer.Parse(parts(3)), v),
                              "OK", "ERROR:Set failed")

                ' ── GET_FX_PARAM_FORMATTED|trackIndex|fxIndex|paramIndex ──────
                ' Réponse : valeur formatée ex: "-6.0 dB"
                Case "GET_FX_PARAM_FORMATTED"
                    If parts.Length < 4 Then Return "ERROR:Usage GET_FX_PARAM_FORMATTED|trackIndex|fxIndex|paramIndex"
                    If ReaperAPI.TrackFX_GetFormattedParamValue Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim buf = Marshal.AllocHGlobal(256)
                    Try
                        ReaperAPI.TrackFX_GetFormattedParamValue(tr, Integer.Parse(parts(2)), Integer.Parse(parts(3)), buf, 256)
                        Return If(Marshal.PtrToStringAnsi(buf), String.Empty)
                    Finally
                        Marshal.FreeHGlobal(buf)
                    End Try

                ' ── GET_FX_PRESET|trackIndex|fxIndex ─────────────────────────
                Case "GET_FX_PRESET"
                    If parts.Length < 3 Then Return "ERROR:Usage GET_FX_PRESET|trackIndex|fxIndex"
                    If ReaperAPI.TrackFX_GetPreset Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim buf = Marshal.AllocHGlobal(512)
                    Try
                        ReaperAPI.TrackFX_GetPreset(tr, Integer.Parse(parts(2)), buf, 512)
                        Return If(Marshal.PtrToStringAnsi(buf), String.Empty)
                    Finally
                        Marshal.FreeHGlobal(buf)
                    End Try

                ' ── SET_FX_PRESET|trackIndex|fxIndex|presetName ───────────────
                Case "SET_FX_PRESET"
                    If parts.Length < 4 Then Return "ERROR:Usage SET_FX_PRESET|trackIndex|fxIndex|presetName"
                    If ReaperAPI.TrackFX_SetPreset Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim presetName = String.Join("|", parts, 3, parts.Length - 3)
                    Return If(ReaperAPI.TrackFX_SetPreset(tr, Integer.Parse(parts(2)), presetName),
                              "OK", "ERROR:Set failed")

                ' ── NAVIGATE_FX_PRESET|trackIndex|fxIndex|move ───────────────
                ' move : +1=suivant  -1=précédent  0=actuel
                Case "NAVIGATE_FX_PRESET"
                    If parts.Length < 4 Then Return "ERROR:Usage NAVIGATE_FX_PRESET|trackIndex|fxIndex|move"
                    If ReaperAPI.TrackFX_NavigatePresets Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Return If(ReaperAPI.TrackFX_NavigatePresets(tr, Integer.Parse(parts(2)), Integer.Parse(parts(3))),
                              "OK", "ERROR:Navigate failed")

                ' ── GET_FX_ALL_PARAMS|trackIndex|fxIndex ─────────────────────
                ' Retourne tous les paramètres en un seul appel pipe.
                ' Format : "name0|val0|name1|val1|...|nameN|valN"
                ' val = valeur normalisée [0..1] en InvariantCulture
                Case "GET_FX_ALL_PARAMS"
                    If parts.Length < 3 Then Return "ERROR:Usage GET_FX_ALL_PARAMS|trackIndex|fxIndex"
                    If ReaperAPI.TrackFX_GetNumParams Is Nothing OrElse
                       ReaperAPI.TrackFX_GetParamNormalized Is Nothing OrElse
                       ReaperAPI.TrackFX_GetParamName Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim fx = Integer.Parse(parts(2))
                    Dim count = ReaperAPI.TrackFX_GetNumParams(tr, fx)
                    Dim nameBuf = Marshal.AllocHGlobal(256)
                    Try
                        Dim tokens(count * 2 - 1) As String
                        For i = 0 To count - 1
                            ReaperAPI.TrackFX_GetParamName(tr, fx, i, nameBuf, 256)
                            tokens(i * 2) = If(Marshal.PtrToStringAnsi(nameBuf), String.Empty)
                            tokens(i * 2 + 1) = ReaperAPI.TrackFX_GetParamNormalized(tr, fx, i).ToString("G", ic)
                        Next
                        Return String.Join("|", tokens)
                    Finally
                        Marshal.FreeHGlobal(nameBuf)
                    End Try

                                    ' ── GET_LAST_TOUCHED_FX ───────────────────────────────────────
                ' Réponse : "trackIndex|fxIndex|paramIndex"
                '   trackIndex : 0-based (-1 = master)
                '   fxIndex    : 0-based
                '   paramIndex : 0-based
                ' Réponse "-1|-1|-1" si aucun FX n'a encore été touché.
                Case "GET_LAST_TOUCHED_FX"
                    If ReaperAPI.GetLastTouchedFX Is Nothing Then Return "ERROR:API unavailable"
                    Dim trackNum As Integer = 0
                    Dim fxNum As Integer = 0
                    Dim paramNum As Integer = 0
                    If Not ReaperAPI.GetLastTouchedFX(trackNum, fxNum, paramNum) Then
                        Return "-1|-1|-1"
                    End If
                    ' ✅ Reaper : tracknumber 0=master 1=première piste → on-based → driver 0-based
                    Dim trackIdx = If(trackNum = 0, -1, trackNum - 1)
                    Return $"{trackIdx}|{fxNum}|{paramNum}"

                                    ' ── COUNT_TRACKS ──────────────────────────────────────────────
                ' Utilise CountTracks (accepte un projet) vs GetNumTracks (projet actif).
                ' Réponse : nombre de pistes du projet actif (entier)
                Case "COUNT_TRACKS"
                    If ReaperAPI.CountTracks Is Nothing Then Return "ERROR:API unavailable"
                    Return ReaperAPI.CountTracks(IntPtr.Zero).ToString()

                                    ' ── CREATE_TRACK_SEND|srcTrackIndex|destTrackIndex ────────────
                ' Crée un envoi depuis srcTrack vers destTrack.
                ' destTrackIndex = -2 → sortie matérielle (desttrIn = NULL)
                ' destTrackIndex = -1 → piste maître
                ' Réponse : index 0-based du nouvel envoi, ou "ERROR:..."
                Case "CREATE_TRACK_SEND"
                    If parts.Length < 3 Then Return "ERROR:Usage CREATE_TRACK_SEND|srcIndex|destIndex"
                    If ReaperAPI.CreateTrackSend Is Nothing Then Return "ERROR:API unavailable"
                    Dim srcTr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If srcTr = IntPtr.Zero Then Return "ERROR:Source track not found"
                    ' ✅ -2 = hardware output (desttrIn = NULL)
                    Dim destIdx = Integer.Parse(parts(2))
                    Dim destTr = If(destIdx = -2, IntPtr.Zero, GetTrackByIndex(destIdx))
                    If destTr = IntPtr.Zero AndAlso destIdx <> -2 Then Return "ERROR:Destination track not found"
                    Dim sendIdx = ReaperAPI.CreateTrackSend(srcTr, destTr)
                    Return If(sendIdx >= 0, sendIdx.ToString(), "ERROR:CreateTrackSend failed")

                ' ── REMOVE_TRACK_SEND|trackIndex|category|sendIndex ───────────
                ' category : 0=envois  -1=reçus  1=sorties HW
                ' Réponse  : "OK" ou "ERROR:..."
                Case "REMOVE_TRACK_SEND"
                    If parts.Length < 4 Then Return "ERROR:Usage REMOVE_TRACK_SEND|trackIndex|category|sendIndex"
                    If ReaperAPI.RemoveTrackSend Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Return If(ReaperAPI.RemoveTrackSend(tr, Integer.Parse(parts(2)), Integer.Parse(parts(3))),
                              "OK", "ERROR:RemoveTrackSend failed")
                                    ' ── ANY_TRACK_SOLO ────────────────────────────────────────────
                ' Réponse : "1" si une piste est en solo, "0" sinon
                Case "ANY_TRACK_SOLO"
                    If ReaperAPI.AnyTrackSolo Is Nothing Then Return "ERROR:API unavailable"
                    Return If(ReaperAPI.AnyTrackSolo(IntPtr.Zero), "1", "0")

                                    ' ── GET_FX_OPEN|trackIndex|fxIndex ────────────────────────────
                ' Réponse : "1" si la fenêtre est ouverte, "0" sinon
                Case "GET_FX_OPEN"
                    If parts.Length < 3 Then Return "ERROR:Usage GET_FX_OPEN|trackIndex|fxIndex"
                    If ReaperAPI.TrackFX_GetOpen Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Return If(ReaperAPI.TrackFX_GetOpen(tr, Integer.Parse(parts(2))), "1", "0")

                ' ── SET_FX_OPEN|trackIndex|fxIndex|open ───────────────────────
                ' open : "1"=ouvrir  "0"=fermer
                ' Réponse : "OK"
                Case "SET_FX_OPEN"
                    If parts.Length < 4 Then Return "ERROR:Usage SET_FX_OPEN|trackIndex|fxIndex|open"
                    If ReaperAPI.TrackFX_SetOpen Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    ReaperAPI.TrackFX_SetOpen(tr, Integer.Parse(parts(2)), parts(3) <> "0")
                    Return "OK"

                              ' ── GET_INSTALLED_FX_LIST ─────────────────────────────────────
                ' Énumère tous les FX installés en un seul appel pipe.
                ' Format réponse : "name0|ident0|name1|ident1|...|nameN|identN"
                '   name  : nom affiché ex: "FabFilter Pro-Q 3 (FabFilter)"
                '   ident : identifiant complet ex: "VST3: FabFilter Pro-Q 3 (FabFilter)"
                ' Retourne une chaîne vide s'il n'y a aucun FX installé.
                Case "GET_INSTALLED_FX_LIST"
                    If ReaperAPI.EnumInstalledFX Is Nothing Then Return "ERROR:API unavailable"
                    Dim tokens As New List(Of String)
                    Dim i As Integer = 0
                    Dim namePtr As IntPtr = IntPtr.Zero
                    Dim identPtr As IntPtr = IntPtr.Zero
                    While ReaperAPI.EnumInstalledFX(i, namePtr, identPtr)
                        tokens.Add(If(Marshal.PtrToStringAnsi(namePtr), String.Empty))
                        tokens.Add(If(Marshal.PtrToStringAnsi(identPtr), String.Empty))
                        i += 1
                    End While
                    Return String.Join("|", tokens)

                ' ── GET_INSTALLED_FX_LIST_BYTYPE|type ────────────────────────
                ' Filtre sur le préfixe de l'ident (VST, VST3, JS, CLAP…).
                ' Format réponse : "name0|ident0|name1|ident1|..."
                Case "GET_INSTALLED_FX_LIST_BYTYPE"
                    If parts.Length < 2 Then Return "ERROR:Usage GET_INSTALLED_FX_LIST_BYTYPE|type"
                    If ReaperAPI.EnumInstalledFX Is Nothing Then Return "ERROR:API unavailable"
                    Dim filter = parts(1).ToUpperInvariant()
                    Dim tokens As New List(Of String)
                    Dim i As Integer = 0
                    Dim namePtr As IntPtr = IntPtr.Zero
                    Dim identPtr As IntPtr = IntPtr.Zero
                    While ReaperAPI.EnumInstalledFX(i, namePtr, identPtr)
                        Dim fxIdent = If(Marshal.PtrToStringAnsi(identPtr), String.Empty)
                        ' ── L'ident commence par "VST3: " / "VST: " / "JS: "… ──
                        Dim colonIdx = fxIdent.IndexOf(": ", StringComparison.Ordinal)
                        If colonIdx > 0 Then
                            Dim fxType = fxIdent.Substring(0, colonIdx).ToUpperInvariant()
                            If fxType = filter Then
                                tokens.Add(If(Marshal.PtrToStringAnsi(namePtr), String.Empty))
                                tokens.Add(fxIdent)
                            End If
                        End If
                        i += 1
                    End While
                    Return String.Join("|", tokens)



                                    ' ── GET_TRACK_GROUP|trackIndex|groupName ──────────────────────
                ' Retourne le masque d'appartenance (entier décimal, bits 0-31 = groupes 1-32)
                Case "GET_TRACK_GROUP"
                    If parts.Length < 3 Then Return "ERROR:Usage GET_TRACK_GROUP|trackIndex|groupName"
                    If ReaperAPI.GetSetTrackGroupMembershipEx Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim mask = ReaperAPI.GetSetTrackGroupMembershipEx(tr, parts(2), 0UI, 0UI)
                    Return CType(mask, Long).ToString()

                ' ── SET_TRACK_GROUP|trackIndex|groupName|setMask|setVal ───────
                ' setMask : masque des bits à modifier  (ex: 1=groupe1 3=groupes1et2)
                ' setVal  : valeur des bits (1=rejoindre 0=quitter)
                ' Retourne le nouveau masque après modification
                Case "SET_TRACK_GROUP"
                    If parts.Length < 5 Then Return "ERROR:Usage SET_TRACK_GROUP|trackIndex|groupName|setMask|setVal"
                    If ReaperAPI.GetSetTrackGroupMembershipEx Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim setMask As UInteger = 0
                    Dim setVal As UInteger = 0
                    UInteger.TryParse(parts(3), setMask)
                    UInteger.TryParse(parts(4), setVal)
                    Dim newMask = ReaperAPI.GetSetTrackGroupMembershipEx(tr, parts(2), setMask, setVal)
                    Return CType(newMask, Long).ToString()

                                    ' ── GET_NUM_AUDIO_INPUTS ──────────────────────────────────────
                Case "GET_NUM_AUDIO_INPUTS"
                    If ReaperAPI.GetNumAudioInputs Is Nothing Then Return "ERROR:API unavailable"
                    Return ReaperAPI.GetNumAudioInputs().ToString()

                ' ── GET_NUM_AUDIO_OUTPUTS ─────────────────────────────────────
                Case "GET_NUM_AUDIO_OUTPUTS"
                    If ReaperAPI.GetNumAudioOutputs Is Nothing Then Return "ERROR:API unavailable"
                    Return ReaperAPI.GetNumAudioOutputs().ToString()

                ' ── GET_AUDIO_INPUT_NAME|channelIndex ─────────────────────────
                Case "GET_AUDIO_INPUT_NAME"
                    If parts.Length < 2 Then Return "ERROR:Usage GET_AUDIO_INPUT_NAME|channelIndex"
                    If ReaperAPI.GetInputChannelName Is Nothing Then Return "ERROR:API unavailable"
                    Dim ptr = ReaperAPI.GetInputChannelName(Integer.Parse(parts(1)))
                    If ptr = IntPtr.Zero Then Return String.Empty
                    Dim name = Marshal.PtrToStringAnsi(ptr)
                    Return If(name IsNot Nothing, name, String.Empty)

                ' ── GET_AUDIO_OUTPUT_NAME|channelIndex ────────────────────────
                Case "GET_AUDIO_OUTPUT_NAME"
                    If parts.Length < 2 Then Return "ERROR:Usage GET_AUDIO_OUTPUT_NAME|channelIndex"
                    If ReaperAPI.GetOutputChannelName Is Nothing Then Return "ERROR:API unavailable"
                    Dim ptr = ReaperAPI.GetOutputChannelName(Integer.Parse(parts(1)))
                    If ptr = IntPtr.Zero Then Return String.Empty
                    Dim name = Marshal.PtrToStringAnsi(ptr)
                    Return If(name IsNot Nothing, name, String.Empty)

                ' ── GET_ALL_AUDIO_INPUTS ──────────────────────────────────────
                ' Retourne tous les noms en un seul appel : "name0|name1|...|nameN"
                Case "GET_ALL_AUDIO_INPUTS"
                    If ReaperAPI.GetNumAudioInputs Is Nothing OrElse
                       ReaperAPI.GetInputChannelName Is Nothing Then Return "ERROR:API unavailable"
                    Dim count = ReaperAPI.GetNumAudioInputs()
                    Dim tokens(count - 1) As String
                    For i = 0 To count - 1
                        Dim ptr = ReaperAPI.GetInputChannelName(i)
                        Dim n = If(ptr <> IntPtr.Zero, Marshal.PtrToStringAnsi(ptr), String.Empty)
                        tokens(i) = If(n IsNot Nothing, n, String.Empty)
                    Next
                    Return String.Join("|", tokens)

                ' ── GET_ALL_AUDIO_OUTPUTS ─────────────────────────────────────
                ' Retourne tous les noms en un seul appel : "name0|name1|...|nameN"
                Case "GET_ALL_AUDIO_OUTPUTS"
                    If ReaperAPI.GetNumAudioOutputs Is Nothing OrElse
                       ReaperAPI.GetOutputChannelName Is Nothing Then Return "ERROR:API unavailable"
                    Dim count = ReaperAPI.GetNumAudioOutputs()
                    Dim tokens(count - 1) As String
                    For i = 0 To count - 1
                        Dim ptr = ReaperAPI.GetOutputChannelName(i)
                        Dim n = If(ptr <> IntPtr.Zero, Marshal.PtrToStringAnsi(ptr), String.Empty)
                        tokens(i) = If(n IsNot Nothing, n, String.Empty)
                    Next
                    Return String.Join("|", tokens)

                     ' ── COPY_FX|srcTrackIndex|srcFxIndex|destTrackIndex|destFxIndex|move
                ' Copie ou déplace un FX vers une autre piste.
                ' destFxIndex : -1 = ajouter à la fin
                ' move        : 1=déplacer (source supprimée)  0=copier
                ' Réponse : "OK" ou "ERROR:..."
                Case "COPY_FX"
                    If parts.Length < 6 Then Return "ERROR:Usage COPY_FX|srcTrack|srcFx|destTrack|destFx|move"
                    If ReaperAPI.TrackFX_CopyToTrack Is Nothing Then Return "ERROR:API unavailable"
                    Dim srcTr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If srcTr = IntPtr.Zero Then Return "ERROR:Source track not found"
                    Dim destTr = GetTrackByIndex(Integer.Parse(parts(3)))
                    If destTr = IntPtr.Zero Then Return "ERROR:Destination track not found"
                    ReaperAPI.TrackFX_CopyToTrack(
                        srcTr, Integer.Parse(parts(2)),
                        destTr, Integer.Parse(parts(4)),
                        parts(5) = "1")
                    Return "OK"

                                         ' ── GET_CURSOR_POSITION ───────────────────────────────────────
                ' Réponse : position du curseur d'édition en secondes (InvariantCulture)
                Case "GET_CURSOR_POSITION"
                    If ReaperAPI.GetCursorPosition Is Nothing Then Return "ERROR:API unavailable"
                    Return ReaperAPI.GetCursorPosition().ToString("G", Globalization.CultureInfo.InvariantCulture)

                ' ── SET_CURSOR_POSITION|time[|moveview|seekplay] ──────────────
                ' moveview : 1=déplacer la vue  0=non (défaut)
                ' seekplay : 1=chercher en lecture  0=non (défaut)
                ' Réponse  : "OK"
                Case "SET_CURSOR_POSITION"
                    If parts.Length < 2 Then Return "ERROR:Usage SET_CURSOR_POSITION|time[|moveview|seekplay]"
                    If ReaperAPI.SetEditCurPos Is Nothing Then Return "ERROR:API unavailable"
                    Dim t = Double.Parse(parts(1), Globalization.CultureInfo.InvariantCulture)
                    Dim mv = parts.Length >= 3 AndAlso parts(2) = "1"
                    Dim sp = parts.Length >= 4 AndAlso parts(3) = "1"
                    ReaperAPI.SetEditCurPos(t, mv, sp)
                    Return "OK"

                ' ── FORMAT_TIMEPOS|time|mode ──────────────────────────────────
                ' mode : 0=H:MM:SS.sss  1=M.B.H  2=min:sec  3=samples  4=SMPTE  5=secondes
                ' Réponse : chaîne formatée
                Case "FORMAT_TIMEPOS"
                    If parts.Length < 3 Then Return "ERROR:Usage FORMAT_TIMEPOS|time|mode"
                    If ReaperAPI.format_timepos2 Is Nothing Then Return "ERROR:API unavailable"
                    Dim t = Double.Parse(parts(1), Globalization.CultureInfo.InvariantCulture)
                    Dim mode = Integer.Parse(parts(2))
                    Dim buf = Marshal.AllocHGlobal(128)
                    Try
                        ReaperAPI.format_timepos2(t, buf, 128, mode)
                        Dim s = Marshal.PtrToStringAnsi(buf)
                        Return If(s IsNot Nothing, s, String.Empty)
                    Finally
                        Marshal.FreeHGlobal(buf)
                    End Try

                ' ── TIMEPOS_TO_QN|time ────────────────────────────────────────
                ' Réponse : position en Quarter Notes (InvariantCulture)
                Case "TIMEPOS_TO_QN"
                    If parts.Length < 2 Then Return "ERROR:Usage TIMEPOS_TO_QN|time"
                    If ReaperAPI.TimeMap_timeToQN Is Nothing Then Return "ERROR:API unavailable"
                    Dim t = Double.Parse(parts(1), Globalization.CultureInfo.InvariantCulture)
                    Return ReaperAPI.TimeMap_timeToQN(t).ToString("G", Globalization.CultureInfo.InvariantCulture)

                ' ── QN_TO_TIMEPOS|qn ──────────────────────────────────────────
                ' Réponse : secondes correspondant à la position en QN
                Case "QN_TO_TIMEPOS"
                    If parts.Length < 2 Then Return "ERROR:Usage QN_TO_TIMEPOS|qn"
                    If ReaperAPI.TimeMap_QNToTime Is Nothing Then Return "ERROR:API unavailable"
                    Dim qn = Double.Parse(parts(1), Globalization.CultureInfo.InvariantCulture)
                    Return ReaperAPI.TimeMap_QNToTime(qn).ToString("G", Globalization.CultureInfo.InvariantCulture)

                ' ── TIMEPOS_TO_BEATS|time ─────────────────────────────────────
                ' Réponse : "measures|cml|fullbeats|cdenom|beatInMeasure"
                '   measures     : numéro de mesure 0-based
                '   cml          : position entière en temps dans la mesure (0-based)
                '   fullbeats    : position absolue en temps (depuis début projet)
                '   cdenom       : dénominateur de la signature (4=noire 8=croche…)
                '   beatInMeasure: beat courant dans la mesure (1-based, formaté 0.00)
                Case "TIMEPOS_TO_BEATS"
                    If parts.Length < 2 Then Return "ERROR:Usage TIMEPOS_TO_BEATS|time"
                    If ReaperAPI.TimeMap2_timeToBeats Is Nothing Then Return "ERROR:API unavailable"
                    Dim t = Double.Parse(parts(1), Globalization.CultureInfo.InvariantCulture)
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim measures As Integer = 0, cml As Integer = 0, cdenom As Integer = 0
                    Dim fullbeats As Double = 0
                    Dim beatInMeasure = ReaperAPI.TimeMap2_timeToBeats(
                        IntPtr.Zero, t, measures, cml, fullbeats, cdenom)
                    Return String.Join("|",
                        measures.ToString(),
                        cml.ToString(),
                        fullbeats.ToString("G", ic),
                        cdenom.ToString(),
                        beatInMeasure.ToString("G", ic))

                ' ── GET_TIMESIG_AT_TIME|time ──────────────────────────────────
                ' Réponse : "num|denom|bpm"  ex: "4|4|120"
                Case "GET_TIMESIG_AT_TIME"
                    If parts.Length < 2 Then Return "ERROR:Usage GET_TIMESIG_AT_TIME|time"
                    If ReaperAPI.TimeMap_GetTimeSigAtTime Is Nothing Then Return "ERROR:API unavailable"
                    Dim t = Double.Parse(parts(1), Globalization.CultureInfo.InvariantCulture)
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim tsNum As Integer = 0, tsDen As Integer = 0
                    Dim bpm As Double = 0
                    ReaperAPI.TimeMap_GetTimeSigAtTime(IntPtr.Zero, t, tsNum, tsDen, bpm)
                    Return $"{tsNum}|{tsDen}|{bpm.ToString("G", ic)}"

                ' ── GET_CURSOR_INFO ───────────────────────────────────────────
                ' Retourne toutes les conversions en un seul appel pipe.
                ' Réponse : "time|qn|measures|cml|fullbeats|cdenom|beatPos|tsNum|tsDen|bpm|formatted"
                ' formatted : M.B.H (mode=1) lisible par la surface
                Case "GET_CURSOR_INFO"
                    If ReaperAPI.GetCursorPosition Is Nothing Then Return "ERROR:API unavailable"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim t = ReaperAPI.GetCursorPosition()
                    Dim qn As Double = If(ReaperAPI.TimeMap_timeToQN IsNot Nothing,
                                          ReaperAPI.TimeMap_timeToQN(t), 0)
                    Dim measures As Integer = 0, cml As Integer = 0, cdenom As Integer = 0
                    Dim fullbeats As Double = 0
                    Dim beatPos As Double = 0
                    If ReaperAPI.TimeMap2_timeToBeats IsNot Nothing Then
                        beatPos = ReaperAPI.TimeMap2_timeToBeats(
                            IntPtr.Zero, t, measures, cml, fullbeats, cdenom)
                    End If
                    Dim tsNum As Integer = 0, tsDen As Integer = 0, bpm As Double = 0
                    If ReaperAPI.TimeMap_GetTimeSigAtTime IsNot Nothing Then
                        ReaperAPI.TimeMap_GetTimeSigAtTime(IntPtr.Zero, t, tsNum, tsDen, bpm)
                    End If
                    Dim formatted As String = String.Empty
                    If ReaperAPI.format_timepos2 IsNot Nothing Then
                        Dim buf = Marshal.AllocHGlobal(128)
                        Try
                            ReaperAPI.format_timepos2(t, buf, 128, 1)
                            Dim s = Marshal.PtrToStringAnsi(buf)
                            formatted = If(s IsNot Nothing, s, String.Empty)
                        Finally
                            Marshal.FreeHGlobal(buf)
                        End Try
                    End If
                    Return String.Join("|",
                        t.ToString("G", ic), qn.ToString("G", ic),
                        measures.ToString(), cml.ToString(),
                        fullbeats.ToString("G", ic), cdenom.ToString(),
                        beatPos.ToString("G", ic),
                        tsNum.ToString(), tsDen.ToString(), bpm.ToString("G", ic),
                        formatted)

                                    ' ── DELETE_FX|trackIndex|fxIndex ─────────────────────────────
                ' Supprime un FX de la chaîne de la piste.
                ' Réponse : "OK" ou "ERROR:..."
                Case "DELETE_FX"
                    If parts.Length < 3 Then Return "ERROR:Usage DELETE_FX|trackIndex|fxIndex"
                    If ReaperAPI.TrackFX_Delete Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Return If(ReaperAPI.TrackFX_Delete(tr, Integer.Parse(parts(2))),
                              "OK", "ERROR:TrackFX_Delete failed")

                ' ── ADD_FX_BY_NAME|trackIndex|fxName[|recFX] ─────────────────
                ' fxName  : nom complet ex: "VST: ReaEQ (Cockos)" ou "JS: volume"
                ' recFX   : 1=ajouter dans la chaîne d'enregistrement  0=chaîne normale (défaut)
                ' Réponse : index 0-based du FX ajouté, ou "ERROR:..."
                Case "ADD_FX_BY_NAME"
                    If parts.Length < 3 Then Return "ERROR:Usage ADD_FX_BY_NAME|trackIndex|fxName[|recFX]"
                    If ReaperAPI.TrackFX_AddByName Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim fxName = String.Join("|", parts, 2, parts.Length - (If(parts.Length >= 4, 3, 2)))
                    Dim recFX = parts.Length >= 4 AndAlso parts(parts.Length - 1) = "1"
                    Dim fxIdx = ReaperAPI.TrackFX_AddByName(tr, fxName, recFX, -1)
                    Return If(fxIdx >= 0, fxIdx.ToString(), "ERROR:AddByName failed")

                                    ' ── UNDO ──────────────────────────────────────────────────────
                ' Exécute un Undo (Ctrl+Z) sur le projet actif.
                ' Réponse : "OK" ou "ERROR:..."
                Case "UNDO"
                    If ReaperAPI.Undo_DoUndo2 Is Nothing Then Return "ERROR:API unavailable"
                    ReaperAPI.Undo_DoUndo2(IntPtr.Zero)
                    Return "OK"

                ' ── REDO ──────────────────────────────────────────────────────
                ' Exécute un Redo (Ctrl+Y) sur le projet actif.
                ' Réponse : "OK" ou "ERROR:..."
                Case "REDO"
                    If ReaperAPI.Undo_DoRedo2 Is Nothing Then Return "ERROR:API unavailable"
                    ReaperAPI.Undo_DoRedo2(IntPtr.Zero)
                    Return "OK"

                ' ── CAN_UNDO ──────────────────────────────────────────────────
                ' Réponse : description de l'action annulable, ou "" si pile vide
                Case "CAN_UNDO"
                    If ReaperAPI.Undo_CanUndo2 Is Nothing Then Return "ERROR:API unavailable"
                    Dim ptr = ReaperAPI.Undo_CanUndo2(IntPtr.Zero)
                    If ptr = IntPtr.Zero Then Return String.Empty
                    Dim desc = Marshal.PtrToStringAnsi(ptr)
                    Return If(desc IsNot Nothing, desc, String.Empty)

                ' ── CAN_REDO ──────────────────────────────────────────────────
                ' Réponse : description de l'action refaisable, ou "" si pile vide
                Case "CAN_REDO"
                    If ReaperAPI.Undo_CanRedo2 Is Nothing Then Return "ERROR:API unavailable"
                    Dim ptr = ReaperAPI.Undo_CanRedo2(IntPtr.Zero)
                    If ptr = IntPtr.Zero Then Return String.Empty
                    Dim desc = Marshal.PtrToStringAnsi(ptr)
                    Return If(desc IsNot Nothing, desc, String.Empty)

                ' ── BEGIN_UNDO_BLOCK ──────────────────────────────────────────
                ' Démarre un bloc d'annulation.
                ' Appeler avant de modifier des pistes/items.
                Case "BEGIN_UNDO_BLOCK"
                    If ReaperAPI.Undo_BeginBlock Is Nothing Then Return "ERROR:API unavailable"
                    ReaperAPI.Undo_BeginBlock()
                    Return "OK"

                ' ── END_UNDO_BLOCK|description|flags ─────────────────────────
                ' Ferme un bloc d'annulation.
                ' flags : 1=pistes  4=items  8=FX  (combinables par OR)
                ' Réponse : "OK"
                Case "END_UNDO_BLOCK"
                    If parts.Length < 3 Then Return "ERROR:Usage END_UNDO_BLOCK|description|flags"
                    If ReaperAPI.Undo_EndBlock Is Nothing Then Return "ERROR:API unavailable"
                    Dim desc = String.Join("|", parts, 1, parts.Length - 2)
                    Dim flags As Integer = 0
                    Integer.TryParse(parts(parts.Length - 1), flags)
                    ReaperAPI.Undo_EndBlock(desc, flags)
                    Return "OK"


                                    ' ── GET_PROJECT_STATE_COUNT ───────────────────────────────────
                ' Retourne un entier qui s'incrémente à chaque modification du projet.
                ' Comparer avec la valeur précédente pour détecter un changement.
                Case "GET_PROJECT_STATE_COUNT"
                    If ReaperAPI.GetProjectStateChangeCount Is Nothing Then Return "ERROR:API unavailable"
                    Return ReaperAPI.GetProjectStateChangeCount(IntPtr.Zero).ToString()

                ' ── IS_PROJECT_DIRTY ──────────────────────────────────────────
                ' Réponse : "1" = modifications non sauvegardées  "0" = projet propre
                Case "IS_PROJECT_DIRTY"
                    If ReaperAPI.IsProjectDirty Is Nothing Then Return "ERROR:API unavailable"
                    Return If(ReaperAPI.IsProjectDirty(IntPtr.Zero) <> 0, "1", "0")

                ' ── SAVE_PROJECT[|saveas] ─────────────────────────────────────
                ' saveas = 1 → ouvre la boîte "Enregistrer sous"
                ' Réponse : "OK"
                Case "SAVE_PROJECT"
                    If ReaperAPI.Main_SaveProject Is Nothing Then Return "ERROR:API unavailable"
                    Dim saveAs = parts.Length >= 2 AndAlso parts(1) = "1"
                    ReaperAPI.Main_SaveProject(IntPtr.Zero, saveAs)
                    Return "OK"

                ' ── GET_PROJECT_FILE_PATH ─────────────────────────────────────
                ' Réponse : chemin complet du fichier .rpp  ex: "C:\Projects\MyProject.rpp"
                ' Réponse vide si le projet n'a jamais été sauvegardé.
                Case "GET_PROJECT_FILE_PATH"
                    If ReaperAPI.EnumProjects Is Nothing Then Return "ERROR:API unavailable"
                    Dim buf = Marshal.AllocHGlobal(4096)
                    Try
                        ReaperAPI.EnumProjects(-1, buf, 4096)
                        Dim path = Marshal.PtrToStringAnsi(buf)
                        Return If(path IsNot Nothing, path, String.Empty)
                    Finally
                        Marshal.FreeHGlobal(buf)
                    End Try

                ' ── GET_PROJECT_FOLDER ────────────────────────────────────────
                ' Réponse : dossier de travail du projet (chemin sans nom de fichier)
                Case "GET_PROJECT_FOLDER"
                    If ReaperAPI.GetProjectPath Is Nothing Then Return "ERROR:API unavailable"
                    Dim buf = Marshal.AllocHGlobal(4096)
                    Try
                        ReaperAPI.GetProjectPath(buf, 4096)
                        Dim folder = Marshal.PtrToStringAnsi(buf)
                        Return If(folder IsNot Nothing, folder, String.Empty)
                    Finally
                        Marshal.FreeHGlobal(buf)
                    End Try

                ' ── GET_PROJECT_LENGTH ────────────────────────────────────────
                ' Réponse : durée totale du projet en secondes (InvariantCulture)
                Case "GET_PROJECT_LENGTH"
                    If ReaperAPI.GetProjectLength Is Nothing Then Return "ERROR:API unavailable"
                    Return ReaperAPI.GetProjectLength(IntPtr.Zero).ToString("G", Globalization.CultureInfo.InvariantCulture)

                ' ── GET_PROJECT_INFO ──────────────────────────────────────────
                ' Retourne toutes les infos projet en un seul appel pipe.
                ' Format : "name|filePath|isDirty|stateCount|trackCount|lengthSecs"
                ' name     : nom sans extension  (ex: "MyProject")
                ' filePath : chemin complet .rpp (vide si jamais sauvegardé)
                ' isDirty  : 1=non sauvegardé   0=propre
                ' stateCount  : compteur de changements d'état
                ' trackCount  : nombre de pistes (sans maître)
                ' lengthSecs  : durée en secondes
                Case "GET_PROJECT_INFO"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    ' ── chemin du fichier ────
                    Dim filePath = String.Empty
                    Dim projName = String.Empty
                    If ReaperAPI.EnumProjects IsNot Nothing Then
                        Dim fbuf = Marshal.AllocHGlobal(4096)
                        Try
                            ReaperAPI.EnumProjects(-1, fbuf, 4096)
                            Dim s = Marshal.PtrToStringAnsi(fbuf)
                            filePath = If(s IsNot Nothing, s, String.Empty)
                            If filePath <> String.Empty Then
                                projName = IO.Path.GetFileNameWithoutExtension(filePath)
                            End If
                        Finally
                            Marshal.FreeHGlobal(fbuf)
                        End Try
                    End If
                    ' ── dirty ────────────────
                    Dim isDirty = If(ReaperAPI.IsProjectDirty IsNot Nothing AndAlso
                                     ReaperAPI.IsProjectDirty(IntPtr.Zero) <> 0, "1", "0")
                    ' ── stateCount ───────────
                    Dim stateCount = If(ReaperAPI.GetProjectStateChangeCount IsNot Nothing,
                                        ReaperAPI.GetProjectStateChangeCount(IntPtr.Zero).ToString(), "0")
                    ' ── trackCount ───────────
                    Dim trackCount = If(ReaperAPI.CountTracks IsNot Nothing,
                                        ReaperAPI.CountTracks(IntPtr.Zero).ToString(), "0")
                    ' ── length ───────────────
                    Dim length = If(ReaperAPI.GetProjectLength IsNot Nothing,
                                    ReaperAPI.GetProjectLength(IntPtr.Zero).ToString("G", ic), "0")
                    Return String.Join("|", projName, filePath, isDirty, stateCount, trackCount, length)

                ' ── ENUM_PROJECTS ─────────────────────────────────────────────
                ' Réponse : "filePath0|filePath1|...|filePathN"
                ' Chaque token = chemin complet d'un projet ouvert dans Reaper.
                Case "ENUM_PROJECTS"
                    If ReaperAPI.EnumProjects Is Nothing Then Return "ERROR:API unavailable"
                    Dim buf = Marshal.AllocHGlobal(4096)
                    Try
                        Dim paths As New List(Of String)
                        Dim idx As Integer = 0
                        While ReaperAPI.EnumProjects(idx, buf, 4096) <> IntPtr.Zero
                            Dim p2 = Marshal.PtrToStringAnsi(buf)
                            paths.Add(If(p2 IsNot Nothing, p2, String.Empty))
                            idx += 1
                        End While
                        Return String.Join("|", paths)
                    Finally
                        Marshal.FreeHGlobal(buf)
                    End Try

                ' ── SET_ACTIVE_PROJECT|index ──────────────────────────────────
                ' Active le projet à l'index donné (0-based parmi les projets ouverts).
                ' Réponse : "OK" ou "ERROR:Project not found"
                Case "SET_ACTIVE_PROJECT"
                    If parts.Length < 2 Then Return "ERROR:Usage SET_ACTIVE_PROJECT|index"
                    If ReaperAPI.EnumProjects Is Nothing OrElse
                       ReaperAPI.SelectProjectInstance Is Nothing Then Return "ERROR:API unavailable"
                    Dim proj = ReaperAPI.EnumProjects(Integer.Parse(parts(1)), IntPtr.Zero, 0)
                    If proj = IntPtr.Zero Then Return "ERROR:Project not found"
                    ReaperAPI.SelectProjectInstance(proj)
                    Return "OK"

                                    ' ── POLL_STATE ────────────────────────────────────────────────
                ' Commande ultra-légère pour le polling de changement d'état.
                ' 3 valeurs en 1 appel — utilisée sur le pipe persistant.
                ' Réponse : "stateCount|playState|playPos"
                '   stateCount : compteur Reaper — change à chaque modification
                '   playState  : 0=stop 1=play 2=pause 4=rec 5=rec+play
                '   playPos    : position lecture en secondes (InvariantCulture)
                Case "POLL_STATE"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim sc = If(ReaperAPI.GetProjectStateChangeCount IsNot Nothing,
                                ReaperAPI.GetProjectStateChangeCount(IntPtr.Zero).ToString(), "-1")
                    Dim ps = If(ReaperAPI.GetPlayState IsNot Nothing,
                                ReaperAPI.GetPlayState().ToString(), "0")
                    Dim pp = If(ReaperAPI.GetPlayPosition IsNot Nothing,
                                ReaperAPI.GetPlayPosition().ToString("G", ic), "0")
                    Return $"{sc}|{ps}|{pp}"

                      ' ── EVAL_ENVELOPE|trackIndex|envIndex|time ────────────────────
                ' Évalue l'enveloppe à la position temporelle donnée (secondes).
                ' time = position en secondes (InvariantCulture)
                ' Réponse : "value|dVdS"  (valeur + dérivée première)
                '   value  : valeur interpolée de l'enveloppe à <time>
                '   dVdS   : dérivée (vitesse de changement par sample)
                Case "EVAL_ENVELOPE"
                    If parts.Length < 4 Then Return "ERROR:Usage EVAL_ENVELOPE|trackIndex|envIndex|time"
                    If ReaperAPI.GetTrackEnvelope Is Nothing Then Return "ERROR:API unavailable"
                    If ReaperAPI.Envelope_Evaluate Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim envIdx As Integer = 0
                    If Not Integer.TryParse(parts(2), envIdx) Then Return "ERROR:Invalid envIndex"
                    Dim env = ReaperAPI.GetTrackEnvelope(tr, envIdx)
                    If env = IntPtr.Zero Then Return "ERROR:Envelope not found"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim time As Double = 0
                    Double.TryParse(parts(3), Globalization.NumberStyles.Any, ic, time)
                    Dim val As Double = 0, dv As Double = 0, ddv As Double = 0, dddv As Double = 0
                    ReaperAPI.Envelope_Evaluate(env, time, 0, 0, val, dv, ddv, dddv)
                    Return $"{val.ToString("G", ic)}|{dv.ToString("G", ic)}"

                     ' ── INSERT_ENVELOPE_POINT|trackIndex|envIndex|time|value|shape|tension ──
    ' time    : position en secondes (InvariantCulture)
    ' value   : valeur normalisée [0..1] ou valeur native selon l'enveloppe
    ' shape   : 0=linéaire  1=carré  2=slow start/end
    '           3=fast start  4=fast end  5=bézier
    ' tension : [-1..1] — pertinent uniquement pour shape=5 bézier
    ' Réponse : "OK" ou "ERROR:..."
    '
    ' ⚠️  noSortIn est bool* (pointer optionnel) — on passe _noSortPtr (=True)
    '     pour éviter un re-tri à chaque point en mode batch.
    '     Appeler SORT_ENVELOPE_POINTS après le batch.
                Case "INSERT_ENVELOPE_POINT"
                    If parts.Length < 7 Then Return "ERROR:Usage INSERT_ENVELOPE_POINT|trackIndex|envIndex|time|value|shape|tension"
                    If ReaperAPI.GetTrackEnvelope Is Nothing OrElse
                       ReaperAPI.InsertEnvelopePoint Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim envIdx As Integer = 0
                    If Not Integer.TryParse(parts(2), envIdx) Then Return "ERROR:Invalid envIndex"
                    Dim env = ReaperAPI.GetTrackEnvelope(tr, envIdx)
                    If env = IntPtr.Zero Then Return "ERROR:Envelope not found"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim time As Double = 0, value As Double = 0, tension As Double = 0
                    Dim shape As Integer = 0
                    Double.TryParse(parts(3), Globalization.NumberStyles.Any, ic, time)
                    Double.TryParse(parts(4), Globalization.NumberStyles.Any, ic, value)
                    Integer.TryParse(parts(5), shape)
                    Double.TryParse(parts(6), Globalization.NumberStyles.Any, ic, tension)
                    ' ✅ _noSortPtr = pointeur vers True (bool*) → pas de tri après chaque insertion
                    '    Passer IntPtr.Zero ici trierait automatiquement (plus lent en batch).
                    Dim ok = ReaperAPI.InsertEnvelopePoint(env, time, value, shape, tension, False, _noSortPtr)
                    Return If(ok, "OK", "ERROR:InsertEnvelopePoint failed")

                Case "INSERT_FX_ENVELOPE_POINT_NOW"
                    If parts.Length < 7 Then Return "ERROR:Usage INSERT_FX_ENVELOPE_POINT_NOW|trackIndex|fxIndex|paramIndex|value|shape|tension"
                    If ReaperAPI.GetPlayPosition Is Nothing OrElse
                       ReaperAPI.GetFXEnvelope Is Nothing OrElse
                       ReaperAPI.InsertEnvelopePoint Is Nothing Then Return "ERROR:API unavailable"
                    ' ── Position atomique ─────────────────────────────────────────────
                    Dim pos = ReaperAPI.GetPlayPosition()
                    If pos <= 0 Then Return "STOPPED"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim fxIdx As Integer = 0, paramIdx As Integer = 0
                    Integer.TryParse(parts(2), fxIdx)
                    Integer.TryParse(parts(3), paramIdx)
                    ' ── Enveloppe ────────────────────────────────────────────────────
                    Dim existingEnv = ReaperAPI.GetFXEnvelope(tr, fxIdx, paramIdx, False)
                    Dim isNew = (existingEnv = IntPtr.Zero)
                    Dim env = If(isNew,
                                 ReaperAPI.GetFXEnvelope(tr, fxIdx, paramIdx, True),
                                 existingEnv)
                    If env = IntPtr.Zero Then Return "ERROR:Envelope not found"
                    ' ── ARMED=0 ───────────────────────────────────────────────────────
                    If ReaperAPI.GetSetEnvelopeInfo_Value IsNot Nothing Then
                        ReaperAPI.GetSetEnvelopeInfo_Value(env, "ARMED", 0.0, True)
                    End If
                    ' ── Nettoyage initial (première création uniquement) ──────────────
                    If isNew AndAlso ReaperAPI.DeleteEnvelopePointRange IsNot Nothing Then
                        ReaperAPI.DeleteEnvelopePointRange(env, pos, pos + 0.5)
                        ReaperAPI.Envelope_SortPoints?(env)
                    End If
                    ' ── Mode WRITE : effacer les points devant le playhead ────────────
                    ' Supprime tous les points à partir de la position courante jusqu'à
                    ' la fin du projet. Sans cela, les anciens points d'une passe
                    ' précédente restent présents "légèrement devant" et sont relus par
                    ' le moteur d'automation en interférant avec le nouvel enregistrement.
                    If ReaperAPI.DeleteEnvelopePointRange IsNot Nothing Then
                        ReaperAPI.DeleteEnvelopePointRange(env, pos, pos + 0.5)
                    End If
                    ' ── Insertion ────────────────────────────────────────────────────
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim value As Double = 0, tension As Double = 0
                    Dim shape As Integer = 0
                    Double.TryParse(parts(4), Globalization.NumberStyles.Any, ic, value)
                    Integer.TryParse(parts(5), shape)
                    Double.TryParse(parts(6), Globalization.NumberStyles.Any, ic, tension)
                    Dim ok = ReaperAPI.InsertEnvelopePoint(env, pos, value, shape, tension, False, IntPtr.Zero)
                    Return If(ok, "OK", "ERROR:InsertEnvelopePoint failed")

                ' ── SORT_ENVELOPE_POINTS|trackIndex|envIndex ──────────────────
                ' Trie les points d'enveloppe par position temporelle croissante.
                ' Obligatoire après INSERT_ENVELOPE_POINT (noSort=True) avant tout refresh UI.
                ' Réponse : "OK" ou "ERROR:..."
                Case "SORT_ENVELOPE_POINTS"
                    If parts.Length < 3 Then Return "ERROR:Usage SORT_ENVELOPE_POINTS|trackIndex|envIndex"
                    If ReaperAPI.GetTrackEnvelope Is Nothing OrElse
                       ReaperAPI.Envelope_SortPoints Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim envIdx As Integer = 0
                    If Not Integer.TryParse(parts(2), envIdx) Then Return "ERROR:Invalid envIndex"
                    Dim env = ReaperAPI.GetTrackEnvelope(tr, envIdx)
                    If env = IntPtr.Zero Then Return "ERROR:Envelope not found"
                    Dim ok = ReaperAPI.Envelope_SortPoints(env)
                    Return If(ok, "OK", "ERROR:SortPoints failed")

                ' ── UPDATE_ARRANGE ────────────────────────────────────────────
                ' Rafraîchit la vue d'arrangement Reaper après modification d'enveloppes.
                ' Appeler après SORT_ENVELOPE_POINTS pour que les changements soient visibles.
                ' Réponse : "OK"
                Case "UPDATE_ARRANGE"
                    ReaperAPI.UpdateArrange?.Invoke()
                    Return "OK"

                ' ── DELETE_FX_ENVELOPE|trackIndex|fxIndex|paramIndex ─────────
                ' Supprime l'enveloppe d'automation d'un paramètre FX :
                ' efface tous les points, puis ACTIVE=0 + VISIBLE=0.
                ' Reaper ne fournit pas d'API DeleteEnvelope publique —
                ' vider + désactiver est l'équivalent fonctionnel.
                ' Réponse : "OK"  "NOTFOUND"  ou "ERROR:..."
                Case "DELETE_FX_ENVELOPE"
                    If parts.Length < 4 Then Return "ERROR:Usage DELETE_FX_ENVELOPE|trackIndex|fxIndex|paramIndex"
                    If ReaperAPI.GetFXEnvelope Is Nothing OrElse
                       ReaperAPI.GetSetEnvelopeInfo_Value Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim fxIdx As Integer = 0, paramIdx As Integer = 0
                    If Not Integer.TryParse(parts(2), fxIdx) Then Return "ERROR:Invalid fxIndex"
                    If Not Integer.TryParse(parts(3), paramIdx) Then Return "ERROR:Invalid paramIndex"

                    Dim env = ReaperAPI.GetFXEnvelope(tr, fxIdx, paramIdx, False)
                    If env = IntPtr.Zero Then Return "NOTFOUND"

                    ' ── Effacer tous les points ───────────────────────────────
                    If ReaperAPI.DeleteEnvelopePointRange IsNot Nothing Then
                        ReaperAPI.DeleteEnvelopePointRange(env, -1.0, 1.0E+15)
                        ReaperAPI.Envelope_SortPoints?(env)
                    End If

                    ' ── Désactiver + masquer ──────────────────────────────────
                    ReaperAPI.GetSetEnvelopeInfo_Value(env, "ACTIVE", 0.0, True)
                    ReaperAPI.GetSetEnvelopeInfo_Value(env, "VISIBLE", 0.0, True)

                    ReaperAPI.UpdateArrange?.Invoke()
                    Return "OK"

                                    ' ── SET_FX_PARAM|trackIndex|fxIndex|paramIndex|value ─────────
                ' Écrit un paramètre FX en valeur brute (plage native du plugin).
                ' Préférer SET_FX_PARAM_NORM depuis une surface de contrôle.
                ' Réponse : "OK" ou "ERROR:..."
                Case "SET_FX_PARAM"
                    If parts.Length < 5 Then Return "ERROR:Usage SET_FX_PARAM|trackIndex|fxIndex|paramIndex|value"
                    If ReaperAPI.TrackFX_SetParam Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim fxIdx As Integer = 0, pmIdx As Integer = 0
                    Integer.TryParse(parts(2), fxIdx)
                    Integer.TryParse(parts(3), pmIdx)
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim val As Double = 0
                    Double.TryParse(parts(4), Globalization.NumberStyles.Any, ic, val)
                    Return If(ReaperAPI.TrackFX_SetParam(tr, fxIdx, pmIdx, val), "OK", "ERROR:Set failed")

                ' ── SET_FX_PARAM_NORM|trackIndex|fxIndex|paramIndex|normValue ─
                ' Écrit un paramètre FX en valeur normalisée [0..1].
                ' Forme recommandée depuis une surface de contrôle car indépendante
                ' de la plage native de chaque plugin.
                ' Réponse : "OK" ou "ERROR:..."
                Case "SET_FX_PARAM_NORM"
                    If parts.Length < 5 Then Return "ERROR:Usage SET_FX_PARAM_NORM|trackIndex|fxIndex|paramIndex|normValue"
                    If ReaperAPI.TrackFX_SetParamNormalized Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim fxIdx As Integer = 0, pmIdx As Integer = 0
                    Integer.TryParse(parts(2), fxIdx)
                    Integer.TryParse(parts(3), pmIdx)
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim normVal As Double = 0
                    Double.TryParse(parts(4), Globalization.NumberStyles.Any, ic, normVal)
                    Return If(ReaperAPI.TrackFX_SetParamNormalized(tr, fxIdx, pmIdx, normVal), "OK", "ERROR:Set failed")
                                    ' ── GET_FX_PARAM_NORM|trackIndex|fxIndex|paramIndex ───────────
                ' Retourne la valeur normalisée [0..1] du paramètre FX.
                ' Réponse : valeur en InvariantCulture
                Case "GET_FX_PARAM_NORM"
                    If parts.Length < 4 Then Return "ERROR:Usage GET_FX_PARAM_NORM|trackIndex|fxIndex|paramIndex"
                    If ReaperAPI.TrackFX_GetParamNormalized Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim fxIdx As Integer = 0, pmIdx As Integer = 0
                    Integer.TryParse(parts(2), fxIdx)
                    Integer.TryParse(parts(3), pmIdx)
                    Dim normVal = ReaperAPI.TrackFX_GetParamNormalized(tr, fxIdx, pmIdx)
                    Return normVal.ToString("G", Globalization.CultureInfo.InvariantCulture)


                                    ' ── GET_TRACK_UI_VOL|trackIndex ───────────────────────────────
                ' Retourne le volume POST-automation (valeur affichée dans l'UI Reaper).
                ' Différent de GET_TRACK_INFO|D_VOL qui retourne le trim/fader manuel.
                ' Réponse : volume en InvariantCulture  0..4  (1.0 = 0 dB)
                Case "GET_TRACK_UI_VOL"
                    If parts.Length < 2 Then Return "ERROR:Usage GET_TRACK_UI_VOL|trackIndex"
                    If ReaperAPI.GetTrackUIVolPan Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim vol As Double = 0, pan As Double = 0
                    ReaperAPI.GetTrackUIVolPan(tr, vol, pan)
                    Return vol.ToString("G", Globalization.CultureInfo.InvariantCulture)

                ' ── GET_TRACK_UI_VOL_PAN|trackIndex ──────────────────────────
                ' Retourne volume ET pan POST-automation en un seul appel pipe.
                ' Réponse : "vol|pan"
                Case "GET_TRACK_UI_VOL_PAN"
                    If parts.Length < 2 Then Return "ERROR:Usage GET_TRACK_UI_VOL_PAN|trackIndex"
                    If ReaperAPI.GetTrackUIVolPan Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim vol As Double = 0, pan As Double = 0
                    ReaperAPI.GetTrackUIVolPan(tr, vol, pan)
                    Return $"{vol.ToString("G", ic)}|{pan.ToString("G", ic)}"

                ' ── SET_TRACK_UI_VOL|trackIndex|vol[|relative] ───────────────
                ' Écrit le volume via CSurf_OnVolumeChange (surface-safe).
                ' Déclenche l'enregistrement d'automation si Touch/Write/Latch.
                ' vol      : 0..4  (1.0 = 0 dB)
                ' relative : 0=absolu (défaut)  1=delta
                ' Réponse  : volume réellement appliqué par Reaper
                Case "SET_TRACK_UI_VOL"
                    If parts.Length < 3 Then Return "ERROR:Usage SET_TRACK_UI_VOL|trackIndex|vol[|relative]"
                    If ReaperAPI.CSurf_OnVolumeChange Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim vol As Double = 0
                    Double.TryParse(parts(2), Globalization.NumberStyles.Any, ic, vol)
                    Dim relative = parts.Length >= 4 AndAlso parts(3) = "1"
                    Dim applied = ReaperAPI.CSurf_OnVolumeChange(tr, vol, relative)
                    Return applied.ToString("G", ic)
                     ' ── GET_SEND_MUTE|trackIndex|category|sendIndex ──────────────
                ' Retourne l'état mute POST-automation d'un envoi (category=0)
                ' ou valeur brute via GetTrackSendInfo_Value (category ≠ 0).
                ' category : 0=envois  -1=reçus  1=HW
                ' Réponse  : "1"=muet  "0"=actif
                Case "GET_SEND_MUTE"
                    If parts.Length < 4 Then Return "ERROR:Usage GET_SEND_MUTE|trackIndex|category|sendIndex"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim cat As Integer = 0, idx As Integer = 0
                    Integer.TryParse(parts(2), cat)
                    Integer.TryParse(parts(3), idx)
                    ' ── category=0 : GetTrackSendUIMute → valeur post-automation ─
                    ' ── category≠0 : GetTrackSendInfo_Value → valeur brute ────────
                    If cat = 0 AndAlso ReaperAPI.GetTrackSendUIMute IsNot Nothing Then
                        Dim mute As Boolean = False
                        ReaperAPI.GetTrackSendUIMute(tr, idx, mute)
                        Return If(mute, "1", "0")
                    End If
                    If ReaperAPI.GetTrackSendInfo_Value Is Nothing Then Return "ERROR:API unavailable"
                    Return If(ReaperAPI.GetTrackSendInfo_Value(tr, cat, idx, "D_MUTE") <> 0, "1", "0")





                                    ' ── GET_SEND_VOL_PAN|trackIndex|category|sendIndex ───────────
                ' Retourne volume et pan POST-automation d'un envoi (category=0)
                ' ou trim brut (category=-1/1 via GetTrackSendInfo_Value).
                ' Réponse : "vol|pan"  (InvariantCulture)
                Case "GET_SEND_VOL_PAN"
                    If parts.Length < 4 Then Return "ERROR:Usage GET_SEND_VOL_PAN|trackIndex|category|sendIndex"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim cat As Integer = 0, sendIdx As Integer = 0
                    Integer.TryParse(parts(2), cat)
                    Integer.TryParse(parts(3), sendIdx)
                    Dim vol As Double = 0, pan As Double = 0
                    ' ── category=0 : GetTrackSendUIVolPan → valeurs post-automation ──
                    ' ── category≠0 : GetTrackSendInfo_Value  → trim brut ─────────────
                    If cat = 0 AndAlso ReaperAPI.GetTrackSendUIVolPan IsNot Nothing Then
                        ReaperAPI.GetTrackSendUIVolPan(tr, sendIdx, vol, pan)
                    ElseIf ReaperAPI.GetTrackSendInfo_Value IsNot Nothing Then
                        vol = ReaperAPI.GetTrackSendInfo_Value(tr, cat, sendIdx, "D_VOL")
                        pan = ReaperAPI.GetTrackSendInfo_Value(tr, cat, sendIdx, "D_PAN")
                    Else
                        Return "ERROR:API unavailable"
                    End If
                    Return $"{vol.ToString("G", ic)}|{pan.ToString("G", ic)}"

                               ' ── SET_SEND_VOL|trackIndex|sendIndex|vol[|isend] ────────────
                ' Écrit le volume d'un envoi via SetTrackSendUIVol (UI-safe).
                ' vol   : 0..4  (1.0 = 0 dB)
                ' isend : 0=drag en cours  1=fin de drag (défaut)  -1=sans undo
                ' Réponse : "OK" ou "ERROR:..."
                Case "SET_SEND_VOL"
                    If parts.Length < 4 Then Return "ERROR:Usage SET_SEND_VOL|trackIndex|sendIndex|vol[|isend]"
                    If ReaperAPI.SetTrackSendUIVol Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim sendIdx As Integer = 0
                    Integer.TryParse(parts(2), sendIdx)
                    Dim vol As Double = 0
                    Double.TryParse(parts(3), Globalization.NumberStyles.Any,
                                    Globalization.CultureInfo.InvariantCulture, vol)
                    Dim isend As Integer = 1
                    If parts.Length >= 5 Then Integer.TryParse(parts(4), isend)
                    Return If(ReaperAPI.SetTrackSendUIVol(tr, sendIdx, vol, isend), "OK", "ERROR:Set failed")



                                    ' ── TOGGLE_SEND_MUTE|trackIndex|sendIndex[|isend] ────────────
                ' Bascule l'état mute d'un envoi via ToggleTrackSendUIMute (atomique, UI-safe).
                ' Déclenche l'enregistrement d'automation si Touch/Write/Latch.
                ' isend : 0=drag en cours  1=fin de drag/commit (défaut)  -1=sans undo
                ' Réponse : "OK" ou "ERROR:..."
                Case "TOGGLE_SEND_MUTE"
                    If parts.Length < 3 Then Return "ERROR:Usage TOGGLE_SEND_MUTE|trackIndex|sendIndex[|isend]"
                    If ReaperAPI.ToggleTrackSendUIMute Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim sendIdx As Integer = 0
                    Integer.TryParse(parts(2), sendIdx)
                    Dim isend As Integer = 1
                    If parts.Length >= 4 Then Integer.TryParse(parts(3), isend)
                    Return If(ReaperAPI.ToggleTrackSendUIMute(tr, sendIdx, isend), "OK", "ERROR:Set failed")

                ' ── SET_SEND_PAN|trackIndex|category|sendIndex|pan ────────────
                ' pan : -1=Gauche  0=Centre  1=Droit
                ' Réponse : "OK" ou "ERROR:..."
                Case "SET_SEND_PAN"
                    If parts.Length < 5 Then Return "ERROR:Usage SET_SEND_PAN|trackIndex|category|sendIndex|pan"
                    If ReaperAPI.SetTrackSendInfo_Value Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim cat As Integer = 0, sendIdx As Integer = 0
                    Integer.TryParse(parts(2), cat)
                    Integer.TryParse(parts(3), sendIdx)
                    Dim pan As Double = 0
                    Double.TryParse(parts(4), Globalization.NumberStyles.Any, ic, pan)
                    Return If(ReaperAPI.SetTrackSendInfo_Value(tr, cat, sendIdx, "D_PAN", pan), "OK", "ERROR:Set failed")

                ' ── SET_SEND_VOL_PAN|trackIndex|category|sendIndex|vol|pan ────
                ' Écrit volume ET pan en deux appels Reaper côté bridge (1 seul appel pipe).
                ' vol : 0..4  (1.0 = 0 dB)   pan : -1..1
                ' Réponse : "OK" ou "ERROR:..."
                Case "SET_SEND_VOL_PAN"
                    If parts.Length < 6 Then Return "ERROR:Usage SET_SEND_VOL_PAN|trackIndex|category|sendIndex|vol|pan"
                    If ReaperAPI.SetTrackSendInfo_Value Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim cat As Integer = 0, sendIdx As Integer = 0
                    Integer.TryParse(parts(2), cat)
                    Integer.TryParse(parts(3), sendIdx)
                    Dim vol As Double = 0, pan As Double = 0
                    Double.TryParse(parts(4), Globalization.NumberStyles.Any, ic, vol)
                    Double.TryParse(parts(5), Globalization.NumberStyles.Any, ic, pan)
                    Dim volOk As Boolean
                    If cat = 0 AndAlso ReaperAPI.CSurf_OnSendVolumeChange IsNot Nothing Then
                        ReaperAPI.CSurf_OnSendVolumeChange(tr, sendIdx, vol, False)
                        volOk = True
                    Else
                        volOk = ReaperAPI.SetTrackSendInfo_Value(tr, cat, sendIdx, "D_VOL", vol)
                    End If
                    Dim panOk = ReaperAPI.SetTrackSendInfo_Value(tr, cat, sendIdx, "D_PAN", pan)
                    Return If(volOk AndAlso panOk, "OK", "ERROR:Partial set failed")


                                    ' ── GET_TRACK_UI_PAN|trackIndex ───────────────────────────────
                ' Retourne le pan POST-automation (valeur affichée dans l'UI Reaper).
                ' Réutilise GetTrackUIVolPan — pas de fonction API dédiée pour le pan seul.
                ' Réponse : pan en InvariantCulture  -1..1  (-1=L  0=C  1=R)
                Case "GET_TRACK_UI_PAN"
                    If parts.Length < 2 Then Return "ERROR:Usage GET_TRACK_UI_PAN|trackIndex"
                    If ReaperAPI.GetTrackUIVolPan Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim vol As Double = 0, pan As Double = 0
                    ReaperAPI.GetTrackUIVolPan(tr, vol, pan)
                    Return pan.ToString("G", Globalization.CultureInfo.InvariantCulture)

                ' ── SET_TRACK_UI_PAN|trackIndex|pan[|isend] ──────────────────
                ' Écrit le pan via SetTrackUIPan (UI-safe).
                ' Déclenche l'enregistrement d'automation si Touch/Write/Latch.
                ' pan   : -1..1  (-1=Gauche  0=Centre  1=Droite)
                ' isend : 0=drag en cours  1=fin de drag/commit (défaut)  -1=sans undo
                ' Réponse : "OK" ou "ERROR:..."
                Case "SET_TRACK_UI_PAN"
                    If parts.Length < 3 Then Return "ERROR:Usage SET_TRACK_UI_PAN|trackIndex|pan[|isend]"
                    If ReaperAPI.SetTrackUIPan Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim pan As Double = 0
                    Double.TryParse(parts(2), Globalization.NumberStyles.Any,
                                    Globalization.CultureInfo.InvariantCulture, pan)
                    Dim isend As Integer = 1
                    If parts.Length >= 4 Then Integer.TryParse(parts(3), isend)
                    Return If(ReaperAPI.SetTrackUIPan(tr, pan, isend), "OK", "ERROR:Set failed")

                                    ' ── GET_TRACK_UI_MUTE|trackIndex ──────────────────────────────
                ' Retourne l'état mute POST-automation via GetTrackUIMute.
                ' Réponse : "1"=muet  "0"=actif
                Case "GET_TRACK_UI_MUTE"
                    If parts.Length < 2 Then Return "ERROR:Usage GET_TRACK_UI_MUTE|trackIndex"
                    If ReaperAPI.GetTrackUIMute Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim mute As Boolean = False
                    ReaperAPI.GetTrackUIMute(tr, mute)
                    Return If(mute, "1", "0")

                    ' ── SET_TRACK_UI_MUTE|trackIndex|mute[|isend] ────────────────
                ' Écrit l'état mute via SetTrackUIMute (UI-safe).
                ' Déclenche l'enregistrement d'automation si Touch/Write/Latch.
                ' mute  : "1"=muet  "0"=actif
                ' isend : 0=drag en cours  1=fin de drag/commit (défaut)  -1=sans undo
                ' Réponse : "OK" ou "ERROR:..."
                Case "SET_TRACK_UI_MUTE"
                    If parts.Length < 3 Then Return "ERROR:Usage SET_TRACK_UI_MUTE|trackIndex|mute[|isend]"
                    If ReaperAPI.SetTrackUIMute Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim mute = parts(2) = "1"
                    Dim isend As Integer = 1
                    If parts.Length >= 4 Then Integer.TryParse(parts(3), isend)
                    Return If(ReaperAPI.SetTrackUIMute(tr, mute, isend), "OK", "ERROR:Set failed")
                ' ── GET_TRACKS_SENDS|sendIndex|category|trackIdx0|trackIdx1|... ──
                ' Retourne 10 champs par piste séparés par | :
                ' vol|pan|panLaw|mute|phase|mono|sendMode|srcChan|dstChan|midiFlags
                ' Si une piste est absente → "0|0|-1|0|0|0|0|0|0|0"
                Case "GET_TRACKS_SENDS"
                    If parts.Length < 4 Then Return "ERROR:Usage GET_TRACKS_SENDS|sendIdx|cat|trackIdx0..."
                    If ReaperAPI.GetTrackSendInfo_Value Is Nothing Then Return "ERROR:API unavailable"
                    Dim sendIdx As Integer = 0, cat As Integer = 0
                    Integer.TryParse(parts(1), sendIdx)
                    Integer.TryParse(parts(2), cat)
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim results As New List(Of String)
                    For i = 3 To parts.Length - 1
                        Dim tIdx As Integer = 0
                        Integer.TryParse(parts(i), tIdx)
                        Dim tr = GetTrackByIndex(tIdx)
                        If tr = IntPtr.Zero Then
                            results.AddRange({"0", "0", "-1", "0", "0", "0", "0", "0", "0", "0"})
                            Continue For
                        End If
                        Dim vol = ReaperAPI.GetTrackSendInfo_Value(tr, cat, sendIdx, "D_VOL")
                        Dim pan = ReaperAPI.GetTrackSendInfo_Value(tr, cat, sendIdx, "D_PAN")
                        Dim panLaw = ReaperAPI.GetTrackSendInfo_Value(tr, cat, sendIdx, "D_PANLAW")
                        Dim mute = ReaperAPI.GetTrackSendInfo_Value(tr, cat, sendIdx, "D_MUTE")
                        Dim phase = ReaperAPI.GetTrackSendInfo_Value(tr, cat, sendIdx, "B_PHASE")
                        Dim mono = ReaperAPI.GetTrackSendInfo_Value(tr, cat, sendIdx, "B_MONO")
                        Dim sendMode = CInt(ReaperAPI.GetTrackSendInfo_Value(tr, cat, sendIdx, "I_SENDMODE"))
                        Dim srcChan = CInt(ReaperAPI.GetTrackSendInfo_Value(tr, cat, sendIdx, "I_SRCCHAN"))
                        Dim dstChan = CInt(ReaperAPI.GetTrackSendInfo_Value(tr, cat, sendIdx, "I_DSTCHAN"))
                        Dim midiFlags = CInt(ReaperAPI.GetTrackSendInfo_Value(tr, cat, sendIdx, "I_MIDIFLAGS"))
                        results.Add(vol.ToString("G", ic))
                        results.Add(pan.ToString("G", ic))
                        results.Add(panLaw.ToString("G", ic))
                        results.Add(If(mute <> 0, "1", "0"))
                        results.Add(If(phase <> 0, "1", "0"))
                        results.Add(If(mono <> 0, "1", "0"))
                        results.Add(sendMode.ToString(ic))
                        results.Add(srcChan.ToString(ic))
                        results.Add(dstChan.ToString(ic))
                        results.Add(midiFlags.ToString(ic))
                    Next
                    Return String.Join("|", results)


                                    ' ── GET_ENVELOPE_PROPS|trackIndex|envIndex ────────────────────────────
                ' Retourne : name|active|visible|armed|inLane|laneHeight|defShape|
                '            minVal|maxVal|centerVal|faderScaling|autoItemsOptions
                Case "GET_ENVELOPE_PROPS"

                    If parts.Length < 3 Then Return "ERROR:Usage GET_ENVELOPE_PROPS|trackIndex|envIndex"
                    If ReaperAPI.GetTrackEnvelope Is Nothing Then Return "ERROR:API unavailable"
                    Dim tIdx As Integer = 0, eIdx As Integer = 0
                    Integer.TryParse(parts(1), tIdx)
                    Integer.TryParse(parts(2), eIdx)
                    Dim tr = GetTrackByIndex(tIdx)
                    If tr = IntPtr.Zero Then Return "ERROR:track not found"
                    Dim env? = ReaperAPI.GetTrackEnvelope(tr, eIdx)
                    If env Is Nothing OrElse env = IntPtr.Zero Then Return "ERROR:envelope not found"
                    Dim ic = Globalization.CultureInfo.InvariantCulture

                    Dim buf As New System.Text.StringBuilder(256)
                    ReaperAPI.GetEnvelopeName(env, buf, buf.Capacity)
                    Dim envName = buf.ToString
                    Dim active As Double = ReaperAPI.GetSetEnvelopeInfo_Value(env, "ACTIVE", 0, False)
                    Dim visible As Double = ReaperAPI.GetSetEnvelopeInfo_Value(env, "VISIBLE", 0, False)
                    Dim armed As Double = ReaperAPI.GetSetEnvelopeInfo_Value(env, "ARMED", 0, False)
                    Dim inLane As Double = ReaperAPI.GetSetEnvelopeInfo_Value(env, "IN_LANE", 0, False)
                    Dim laneH As Double = ReaperAPI.GetSetEnvelopeInfo_Value(env, "LANEHEIGHT", 0, False)
                    Dim defShape As Double = ReaperAPI.GetSetEnvelopeInfo_Value(env, "DEFSHAPE", 0, False)
                    Dim minVal As Double = ReaperAPI.GetSetEnvelopeInfo_Value(env, "MIN_VALUE", 0, False)
                    Dim maxVal As Double = ReaperAPI.GetSetEnvelopeInfo_Value(env, "MAX_VALUE", 0, False)
                    Dim centerVal As Double = ReaperAPI.GetSetEnvelopeInfo_Value(env, "CENTER_VALUE", 0, False)
                    Dim faderScl As Double = ReaperAPI.GetSetEnvelopeInfo_Value(env, "FADER_SCALING", 0, False)
                    Dim autoOpts As Double = ReaperAPI.GetSetEnvelopeInfo_Value(env, "AUTO_ITEMS_OPTIONS", 0, False)
                    Return String.Join("|", New String() {
                        envName,
                        If(active <> 0, "1", "0"),
                        If(visible <> 0, "1", "0"),
                        If(armed <> 0, "1", "0"),
                        If(inLane <> 0, "1", "0"),
                        CInt(laneH).ToString(ic),
                        CInt(defShape).ToString(ic),
                        minVal.ToString("G", ic),
                        maxVal.ToString("G", ic),
                        centerVal.ToString("G", ic),
                        If(faderScl <> 0, "1", "0"),
                        CInt(autoOpts).ToString(ic)
                    })

                ' ── SET_ENVELOPE_PROP|trackIndex|envIndex|param|value ─────────────────
                ' Retourne "OK" ou "ERROR:..."
                Case "SET_ENVELOPE_PROP"
                                        If parts.Length < 5 Then Return "ERROR:Usage SET_ENVELOPE_PROP|trackIndex|envIndex|param|value"
                    If ReaperAPI.GetSetEnvelopeInfo_Value Is Nothing Then Return "ERROR:API unavailable"
                    Dim tIdx As Integer = 0, eIdx As Integer = 0
                    Integer.TryParse(parts(1), tIdx)
                    Integer.TryParse(parts(2), eIdx)
                    Dim tr = GetTrackByIndex(tIdx)
                    If tr = IntPtr.Zero Then Return "ERROR:track not found"
                    Dim env = ReaperAPI.GetTrackEnvelope(tr, eIdx)
                    If env = IntPtr.Zero Then Return "ERROR:envelope not found"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim v As Double = 0
                    Double.TryParse(parts(4), Globalization.NumberStyles.Any, ic, v)
                    ReaperAPI.GetSetEnvelopeInfo_Value(env, parts(3), v, True)
                    Return "OK"

                                    ' ── GET_ENVELOPE_GUID|trackIndex|envIndex ─────────────────────
                ' Retourne le GUID stable de l'enveloppe ex: "{A1B2C3D4-…}".
                ' Ce GUID est persistant entre les sessions (sauvegardé dans le .rpp).
                ' Réponse : chaîne GUID ou "ERROR:..."
                Case "GET_ENVELOPE_GUID"
                    If parts.Length < 3 Then Return "ERROR:Usage GET_ENVELOPE_GUID|trackIndex|envIndex"
                    If ReaperAPI.GetTrackEnvelope Is Nothing OrElse
                       ReaperAPI.GetSetEnvelopeInfo_String Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim env = ReaperAPI.GetTrackEnvelope(tr, Integer.Parse(parts(2)))
                    If env = IntPtr.Zero Then Return "ERROR:Envelope not found"
                    Dim buf = Marshal.AllocHGlobal(64)
                    Try
                        ReaperAPI.GetSetEnvelopeInfo_String(env, "GUID", buf, 64, False)
                        Return If(Marshal.PtrToStringAnsi(buf), String.Empty)
                    Finally
                        Marshal.FreeHGlobal(buf)
                    End Try

                ' ── GET_ENVELOPE_EXT|trackIndex|envIndex|key ──────────────────
                ' Lit l'état d'extension personnalisé ("P_EXT:key") de l'enveloppe.
                ' Utile pour stocker des métadonnées arbitraires attachées à l'enveloppe.
                ' Réponse : valeur chaîne (vide si la clé n'existe pas), ou "ERROR:..."
                Case "GET_ENVELOPE_EXT"
                    If parts.Length < 4 Then Return "ERROR:Usage GET_ENVELOPE_EXT|trackIndex|envIndex|key"
                    If ReaperAPI.GetTrackEnvelope Is Nothing OrElse
                       ReaperAPI.GetSetEnvelopeInfo_String Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim env = ReaperAPI.GetTrackEnvelope(tr, Integer.Parse(parts(2)))
                    If env = IntPtr.Zero Then Return "ERROR:Envelope not found"
                    Dim key = $"P_EXT:{parts(3)}"
                    Dim buf = Marshal.AllocHGlobal(4096)
                    Try
                        ReaperAPI.GetSetEnvelopeInfo_String(env, key, buf, 4096, False)
                        Return If(Marshal.PtrToStringAnsi(buf), String.Empty)
                    Finally
                        Marshal.FreeHGlobal(buf)
                    End Try

                ' ── SET_ENVELOPE_EXT|trackIndex|envIndex|key|value ────────────
                ' Écrit l'état d'extension personnalisé ("P_EXT:key") de l'enveloppe.
                ' La valeur est persistée dans le fichier .rpp.
                ' Réponse : "OK" ou "ERROR:..."
                Case "SET_ENVELOPE_EXT"
                    If parts.Length < 5 Then Return "ERROR:Usage SET_ENVELOPE_EXT|trackIndex|envIndex|key|value"
                    If ReaperAPI.GetTrackEnvelope Is Nothing OrElse
                       ReaperAPI.GetSetEnvelopeInfo_String Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim env = ReaperAPI.GetTrackEnvelope(tr, Integer.Parse(parts(2)))
                    If env = IntPtr.Zero Then Return "ERROR:Envelope not found"
                    Dim key = $"P_EXT:{parts(3)}"
                    Dim value = String.Join("|", parts, 4, parts.Length - 4)
                    Dim buf = Marshal.StringToHGlobalAnsi(value)
                    Try
                        Dim ok = ReaperAPI.GetSetEnvelopeInfo_String(
                                     env, key, buf, value.Length + 1, True)
                        Return If(ok, "OK", "ERROR:Set failed")
                    Finally
                        Marshal.FreeHGlobal(buf)
                    End Try
                                    ' ── GET_ENVELOPE_PROP_VAL|trackIndex|envIndex|paramName ────────
                ' Lit une propriété numérique d'une enveloppe via GetSetEnvelopeInfo_Value.
                ' Paramètres courants : "ACTIVE" "VISIBLE" "ARMED" "IN_LANE"
                '   "LANEHEIGHT" "DEFSHAPE" "MINVALUE" "MAXVALUE" "CENTERVALUE"
                '   "FADERSCALING" "AUTO_ITEMS_OPTIONS"
                ' Réponse : valeur double (InvariantCulture) ou "ERROR:..."
                Case "GET_ENVELOPE_PROP_VAL"
                    If parts.Length < 4 Then Return "ERROR:Usage GET_ENVELOPE_PROP_VAL|trackIndex|envIndex|paramName"
                    If ReaperAPI.GetTrackEnvelope Is Nothing OrElse
                       ReaperAPI.GetSetEnvelopeInfo_Value Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim envIdx As Integer = 0
                    If Not Integer.TryParse(parts(2), envIdx) Then Return "ERROR:Invalid envIndex"
                    Dim env = ReaperAPI.GetTrackEnvelope(tr, envIdx)
                    If env = IntPtr.Zero Then Return "ERROR:Envelope not found"
                    Dim value = ReaperAPI.GetSetEnvelopeInfo_Value(env, parts(3), 0, False)
                    Return value.ToString("G", Globalization.CultureInfo.InvariantCulture)

                                    ' ── GET_ENVELOPE_PROP_VAL|trackIndex|envIndex|paramName ────────
                ' Lit une propriété numérique d'une enveloppe.
                ' Priorité : GetEnvelopeInfo_Value (read-only, Reaper 6+)
                '          → fallback GetSetEnvelopeInfo_Value(…, 0, False)
                ' Réponse : valeur double (InvariantCulture) ou "ERROR:..."
                Case "GET_ENVELOPE_PROP_VAL2"
                    If parts.Length < 4 Then Return "ERROR:Usage GET_ENVELOPE_PROP_VAL|trackIndex|envIndex|paramName"
                    If ReaperAPI.GetTrackEnvelope Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim envIdx As Integer = 0
                    If Not Integer.TryParse(parts(2), envIdx) Then Return "ERROR:Invalid envIndex"
                    Dim env = ReaperAPI.GetTrackEnvelope(tr, envIdx)
                    If env = IntPtr.Zero Then Return "ERROR:Envelope not found"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim value As Double
                    If ReaperAPI.GetEnvelopeInfo_Value IsNot Nothing Then
                        ' ── Reaper 6+ : variante read-only ────────────────────
                        value = ReaperAPI.GetEnvelopeInfo_Value(env, parts(3))
                    ElseIf ReaperAPI.GetSetEnvelopeInfo_Value IsNot Nothing Then
                        ' ── Fallback : fonction combinée get/set ───────────────
                        value = ReaperAPI.GetSetEnvelopeInfo_Value(env, parts(3), 0, False)
                    Else
                        Return "ERROR:API unavailable"
                    End If
                    Return value.ToString("G", ic)

                                    ' ── GET_FX_ENVELOPE|trackIndex|fxIndex|paramIndex|create ────────
                ' Obtient (ou crée) l'enveloppe d'un paramètre FX et retourne
                ' son index 0-based dans la liste des enveloppes de la piste.
                ' create : "1"=créer si absente  "0"=lire seulement
                ' Retourne l'index entier, ou "-1" si absente/impossible.
                Case "GET_FX_ENVELOPE"
                    If parts.Length < 5 Then Return "ERROR:Usage GET_FX_ENVELOPE|trackIndex|fxIndex|paramIndex|create"
                    If ReaperAPI.GetTrackEnvelope Is Nothing OrElse
                       ReaperAPI.GetFXEnvelope Is Nothing OrElse
                       ReaperAPI.CountTrackEnvelopes Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim fxIdx As Integer = 0, paramIdx As Integer = 0
                    If Not Integer.TryParse(parts(2), fxIdx) Then Return "ERROR:Invalid fxIndex"
                    If Not Integer.TryParse(parts(3), paramIdx) Then Return "ERROR:Invalid paramIndex"
                    Dim createEnv = parts(4) = "1"

                    ' ── Obtenir / créer le pointeur d'enveloppe (natif) ───────────
                    Dim envPtr = ReaperAPI.GetFXEnvelope(tr, fxIdx, paramIdx, createEnv)
                    If envPtr = IntPtr.Zero Then Return "-1"

                    ' ── Résoudre le pointeur en index dans la liste des enveloppes ─
                    ' (GetFXEnvelope ne retourne pas directement l'index)
                    Dim envCount = ReaperAPI.CountTrackEnvelopes(tr)
                    For i = 0 To envCount - 1
                        If ReaperAPI.GetTrackEnvelope(tr, i) = envPtr Then
                            Return i.ToString(Globalization.CultureInfo.InvariantCulture)
                        End If
                    Next
                    Return "-1"
                                    ' ── GET_ENVELOPE_STATE_CHUNK|trackIndex|envIndex ──────────────
                ' Sérialise l'état de l'enveloppe au format .rpp.
                ' Les vrais sauts de ligne sont encodés en "\n" littéral
                ' pour le transport pipe (protocole 1 ligne par réponse).
                ' Réponse : chunk encodé, ou "ERROR:..."
                Case "GET_ENVELOPE_STATE_CHUNK"
                    If parts.Length < 3 Then Return "ERROR:Usage GET_ENVELOPE_STATE_CHUNK|trackIndex|envIndex"
                    If ReaperAPI.GetTrackEnvelope Is Nothing OrElse
                       ReaperAPI.GetEnvelopeStateChunk Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim envIdx As Integer = 0
                    If Not Integer.TryParse(parts(2), envIdx) Then Return "ERROR:Invalid envIndex"
                    Dim env = ReaperAPI.GetTrackEnvelope(tr, envIdx)
                    If env = IntPtr.Zero Then Return "ERROR:Envelope not found"
                    Const ChunkBufSize As Integer = 65536
                    Dim buf = Marshal.AllocHGlobal(ChunkBufSize)
                    Try
                        Dim ok = ReaperAPI.GetEnvelopeStateChunk(env, buf, ChunkBufSize, False)
                        If Not ok Then Return "ERROR:GetEnvelopeStateChunk failed"
                        Dim chunk = If(Marshal.PtrToStringAnsi(buf), String.Empty)
                        ' CR+LF → LF seul → \n littéral pour le pipe
                        Return chunk.Replace(vbCrLf, vbLf).Replace(vbCr, "").Replace(vbLf, "\n")
                    Finally
                        Marshal.FreeHGlobal(buf)
                    End Try

                ' ── SET_ENVELOPE_STATE_CHUNK|trackIndex|envIndex|chunk ────────
                ' Restaure l'état de l'enveloppe depuis un state chunk .rpp.
                ' Le chunk doit avoir ses sauts de ligne encodés en "\n" littéral
                ' (tel que retourné par GET_ENVELOPE_STATE_CHUNK).
                ' isundopoint = False — le caller gère le bloc d'annulation.
                ' Réponse : "OK" ou "ERROR:..."
                Case "SET_ENVELOPE_STATE_CHUNK"
                    If parts.Length < 4 Then Return "ERROR:Usage SET_ENVELOPE_STATE_CHUNK|trackIndex|envIndex|chunk"
                    If ReaperAPI.GetTrackEnvelope Is Nothing OrElse
                       ReaperAPI.SetEnvelopeStateChunk Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim envIdx As Integer = 0
                    If Not Integer.TryParse(parts(2), envIdx) Then Return "ERROR:Invalid envIndex"
                    Dim env = ReaperAPI.GetTrackEnvelope(tr, envIdx)
                    If env = IntPtr.Zero Then Return "ERROR:Envelope not found"
                    ' Reconstituer le chunk : rejoindre les "|" puis décoder \n → LF
                    Dim chunk = String.Join("|", parts, 3, parts.Length - 3).Replace("\n", vbLf)
                    Return If(ReaperAPI.SetEnvelopeStateChunk(env, chunk, False), "OK", "ERROR:SetEnvelopeStateChunk failed")

                                    ' ── SET_TRACK_UI_VOL|trackIndex|vol[|isend] ───────────────────
                ' Écrit le volume via SetTrackUIVolume (UI-safe).
                ' Déclenche l'enregistrement d'automation si Touch/Write/Latch.
                ' vol   : 0..4  (1.0 = 0 dB)
                ' isend : 0=drag en cours  1=fin de drag/commit (défaut)  -1=sans undo
                ' Réponse : "OK" ou "ERROR:..."
                Case "SET_TRACK_UI_VOL"
                    If parts.Length < 3 Then Return "ERROR:Usage SET_TRACK_UI_VOL|trackIndex|vol[|isend]"
                    If ReaperAPI.SetTrackUIVolume Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim ic = Globalization.CultureInfo.InvariantCulture
                    Dim vol As Double = 0
                    Double.TryParse(parts(2), Globalization.NumberStyles.Any, ic, vol)
                    Dim isend As Integer = 1
                    If parts.Length >= 4 Then Integer.TryParse(parts(3), isend)
                    Return If(ReaperAPI.SetTrackUIVolume(tr, vol, isend), "OK", "ERROR:Set failed")

                                    ' ── COUNT_TRACK_ENVELOPES|trackIndex ─────────────────────────
                ' Retourne le nombre d'enveloppes présentes sur la piste.
                ' Inclut : vol, pan, mute, width + toutes les enveloppes FX activées.
                ' Réponse : nombre entier ≥ 0, ou "ERROR:..."
                Case "COUNT_TRACK_ENVELOPES"
                    If parts.Length < 2 Then Return "ERROR:Usage COUNT_TRACK_ENVELOPES|trackIndex"
                    If ReaperAPI.CountTrackEnvelopes Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Return ReaperAPI.CountTrackEnvelopes(tr).ToString()

                                    ' ── GET_ENVELOPE_NAME|trackIndex|envIndex ────────────────────
                ' Retourne le nom affiché de l'enveloppe (ex: "Volume", "Pan",
                ' ou le nom du paramètre FX associé).
                ' Réponse : nom brut (chaîne) ou "ERROR:..."
                Case "GET_ENVELOPE_NAME"
                    If parts.Length < 3 Then Return "ERROR:Usage GET_ENVELOPE_NAME|trackIndex|envIndex"
                    If ReaperAPI.GetTrackEnvelope Is Nothing OrElse
                       ReaperAPI.GetEnvelopeName Is Nothing Then Return "ERROR:API unavailable"
                    Dim tr = GetTrackByIndex(Integer.Parse(parts(1)))
                    If tr = IntPtr.Zero Then Return "ERROR:Track not found"
                    Dim env = ReaperAPI.GetTrackEnvelope(tr, Integer.Parse(parts(2)))
                    If env = IntPtr.Zero Then Return "ERROR:Envelope not found"
                    Dim buf As New System.Text.StringBuilder(256)
                    ReaperAPI.GetEnvelopeName(env, buf, buf.Capacity)
                    Return buf.ToString()


                Case Else
                    Return $"ERROR:Unknown {parts(0)}"


            End Select
        Catch ex As Exception
            Return $"ERROR:{ex.Message}"
        End Try
        Return "ERROR:Internal"   ' ✅ jamais atteint, mais requis par le compilateur VB.NET

    End Function

    ''' <summary>Retourne le pointeur de piste (index 0-based, projet actif).</summary>
    Private Shared Function GetTrackByIndex(index As Integer) As IntPtr
        If ReaperAPI.GetTrack Is Nothing Then Return IntPtr.Zero
        Return ReaperAPI.GetTrack(IntPtr.Zero, index)
    End Function

End Class