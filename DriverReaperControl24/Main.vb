Imports System.Net.NetworkInformation

Public Class Main

    ''' <summary>
    ''' Entrée principale — interface réseau passée en argument CLI
    ''' Usage : DriverReaperControl24.exe "Ethernet 2"
    ''' </summary>
    Public Shared Sub Main(args As String())
        Dim interfaceName As String = If(args.Length > 0, args(0), LoadConfigInterfaceName())

        If String.IsNullOrWhiteSpace(interfaceName) Then
            Console.Error.WriteLine("ERROR: No network interface specified.")
            Console.Error.WriteLine("Usage: DriverReaperControl24.exe ""Ethernet 2""")
            Environment.Exit(1)
        End If

        Dim selectedInterface = NetworkInterface.GetAllNetworkInterfaces() _
            .FirstOrDefault(Function(n) n.Name = interfaceName)

        If selectedInterface Is Nothing Then
            Console.Error.WriteLine($"ERROR: Interface '{interfaceName}' not found.")
            Console.Error.WriteLine("Available interfaces:")
            For Each iface In NetworkInterface.GetAllNetworkInterfaces()
                Console.Error.WriteLine($"  - {iface.Name}")
            Next
            Environment.Exit(1)
        End If

        Console.WriteLine($"Starting Control24 driver on '{interfaceName}'...")

        Dim desk As New Control24.Hardware.C24_Desk(selectedInterface)
        desk.StartNetworkCommunication()

        ' Boucle d'attente — processus headless
        AddHandler Console.CancelKeyPress, Sub(s, e)
                                               e.Cancel = True
                                               Console.WriteLine("Stopping...")
                                               Environment.Exit(0)
                                           End Sub

        Threading.Thread.Sleep(Threading.Timeout.Infinite)
    End Sub

    Private Shared Function LoadConfigInterfaceName() As String
        Dim configPath = IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ReaControl24", "control24_config.json")

        If Not IO.File.Exists(configPath) Then Return String.Empty
        Dim json = IO.File.ReadAllText(configPath)
        Dim match = Text.RegularExpressions.Regex.Match(json, """interface""\s*:\s*""([^""]+)""")
        Return If(match.Success, match.Groups(1).Value, String.Empty)
    End Function

    Public Shared Sub DisplayInterfaceName()
        For Each Net_interface In NetworkInterface.GetAllNetworkInterfaces()
            Console.WriteLine(Net_interface.Name)
        Next

    End Sub
End Class
