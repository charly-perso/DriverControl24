Imports System.Diagnostics
Imports System.IO
Imports System.IO.Pipes
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Tests de performance de Reaper.utility.CallReaperBridge (une connexion pipe par requête).
''' Un faux serveur répond sur le pipe "c24_reaper_bridge". Reaper doit être fermé pendant l'exécution.
''' </summary>
<TestClass>
Public Class CallReaperBridgePerfTests

    Private Const PipeName As String = "c24_reaper_bridge"
    Private Const WarmupCount As Integer = 50
    Private Const IterationCount As Integer = 2000

    Private _cts As CancellationTokenSource
    Private _serverTask As Task

    ''' <summary>
    ''' Démarre le faux serveur du bridge.
    ''' </summary>
    <TestInitialize>
    Public Sub Setup()
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
    ''' Vérifie que CallReaperBridge retourne bien la réponse du serveur.
    ''' </summary>
    <TestMethod>
    Public Sub CallReaperBridge_ReturnsServerResponse()
        Dim result As String = Global.DriverReaperControl24.Reaper.utility.CallReaperBridge("PING")
        Assert.AreEqual("PONG", result)
    End Sub

    ''' <summary>
    ''' Mesure le temps et les allocations (thread appelant) de CallReaperBridge pour une requête courte.
    ''' </summary>
    <TestMethod>
    Public Sub CallReaperBridge_Ping_MeasureTimeAndAllocations()
        MeasureCalls("PING")
    End Sub

    ''' <summary>
    ''' Mesure le temps et les allocations (thread appelant) de CallReaperBridge pour une lecture de propriété de piste.
    ''' </summary>
    <TestMethod>
    Public Sub CallReaperBridge_GetTrackInfo_MeasureTimeAndAllocations()
        MeasureCalls("GET_TRACK_INFO|0|D_VOL")
    End Sub

    Private Sub MeasureCalls(request As String)
        For i As Integer = 1 To WarmupCount
            Global.DriverReaperControl24.Reaper.utility.CallReaperBridge(request)
        Next

        GC.Collect()
        GC.WaitForPendingFinalizers()
        GC.Collect()

        Dim gen0Before As Integer = GC.CollectionCount(0)
        Dim allocBefore As Long = GC.GetAllocatedBytesForCurrentThread()
        Dim sw As Stopwatch = Stopwatch.StartNew()
        Dim okCount As Integer = 0

        For i As Integer = 1 To IterationCount
            Dim result As String = Global.DriverReaperControl24.Reaper.utility.CallReaperBridge(request)
            If result = "PONG" Then
                okCount += 1
            End If
        Next

        sw.Stop()
        Dim allocAfter As Long = GC.GetAllocatedBytesForCurrentThread()
        Dim gen0After As Integer = GC.CollectionCount(0)

        Dim allocPerCall As Double = (allocAfter - allocBefore) / IterationCount
        Dim msPerCall As Double = sw.Elapsed.TotalMilliseconds / IterationCount

        Console.WriteLine($"Requête            : {request}")
        Console.WriteLine($"Itérations         : {IterationCount}")
        Console.WriteLine($"Temps / appel      : {msPerCall:F3} ms")
        Console.WriteLine($"Alloc. / appel     : {allocPerCall:F0} octets")
        Console.WriteLine($"Collections gen0   : {gen0After - gen0Before}")

        Assert.AreEqual(IterationCount, okCount)
    End Sub

    Private Shared Sub RunFakeServer(token As CancellationToken)
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
                                writer.WriteLine("PONG")
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
