Imports System.Collections.Concurrent
Imports System.Diagnostics
Imports System.IO
Imports System.IO.Pipes
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Mesure le nombre de requêtes pipe émises par la construction de ReaperTrackEnvelopes
''' (chemin du démarrage). Un faux serveur répond sur "c24_reaper_bridge".
''' Reaper doit être fermé pendant l'exécution.
''' </summary>
<TestClass>
<DoNotParallelize>
Public Class TrackEnvelopesStartupTests

    Private Const PipeName As String = "c24_reaper_bridge"
    Private Const FxCount As Integer = 4
    Private Const ParamsPerFx As Integer = 40

    Private _cts As CancellationTokenSource
    Private _serverTask As Task
    Private ReadOnly _counts As New ConcurrentDictionary(Of String, Integer)

    ''' <summary>
    ''' Démarre le faux serveur du bridge.
    ''' </summary>
    <TestInitialize>
    Public Sub Setup()
        _counts.Clear()
        _cts = New CancellationTokenSource()
        Dim token As CancellationToken = _cts.Token
        _serverTask = Task.Run(Sub()
                                   RunFakeServer(token)
                               End Sub)
    End Sub

    ''' <summary>
    ''' Arrête le faux serveur.
    ''' </summary>
    <TestCleanup>
    Public Sub Cleanup()
        _cts.Cancel()
        Try
            Using client As New NamedPipeClientStream(".", PipeName, PipeDirection.InOut)
                client.Connect(500)
            End Using
        Catch ex As Exception
            Console.WriteLine(ex.Message)
        End Try
        _serverTask.Wait(2000)
        _cts.Dispose()
    End Sub

    ''' <summary>
    ''' Piste avec 3 enveloppes standard (Volume, Pan, Mute) et des FX chargés de paramètres,
    ''' sans aucune enveloppe de paramètre FX. Le nombre de requêtes est écrit dans un fichier.
    ''' </summary>
    <TestMethod>
    Public Sub Envelopes_Construction_WithoutFxEnvelopes_CountRequests()
        Dim sw As Stopwatch = Stopwatch.StartNew()
        Dim envs As New Global.DriverReaperControl24.Reaper.ReaperTrackEnvelopes(0)
        sw.Stop()

        Dim total As Integer = _counts.Values.Sum()
        Dim lines As New List(Of String)
        lines.Add($"Temps : {sw.Elapsed.TotalMilliseconds:F1} ms")
        lines.Add($"Requêtes total : {total}")
        For Each kvp In _counts.OrderBy(Function(k) k.Key)
            lines.Add($"  {kvp.Key} : {kvp.Value}")
        Next
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "envelopes_startup_metrics.txt"), lines)

        Assert.IsTrue(total > 0)
    End Sub

    Private Function HandleRequest(request As String) As String
        Dim parts As String() = request.Split("|"c)
        Dim cmd As String = parts(0)
        _counts.AddOrUpdate(cmd, 1, Function(k, v) v + 1)

        Select Case cmd
            Case "COUNT_TRACK_ENVELOPES"
                Return "3"
            Case "GET_ENVELOPE_NAME"
                Select Case parts(2)
                    Case "0"
                        Return "Volume"
                    Case "1"
                        Return "Pan"
                    Case Else
                        Return "Mute"
                End Select
            Case "GET_FX_COUNT"
                Return FxCount.ToString()
            Case "GET_FX_ALL_INFO"
                Return $"FX{parts(2)}|1|{ParamsPerFx}"
            Case "GET_FX_ENVELOPE"
                Return "-1"
            Case "GET_ENVELOPE_PROP_VAL", "GET_ENVELOPE_PROP_VAL2"
                Return "1"
            Case Else
                Return "0"
        End Select
    End Function

    Private Sub RunFakeServer(token As CancellationToken)
        While Not token.IsCancellationRequested
            Try
                Using server As New NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte)
                    server.WaitForConnection()
                    Using reader As New StreamReader(server, leaveOpen:=True)
                        Using writer As New StreamWriter(server, leaveOpen:=True) With {.AutoFlush = True}
                            Dim line As String = reader.ReadLine()
                            If line IsNot Nothing Then
                                writer.WriteLine(HandleRequest(line))
                            End If
                        End Using
                    End Using
                End Using
            Catch ex As Exception
                Console.WriteLine(ex.Message)
            End Try
        End While
    End Sub

End Class
