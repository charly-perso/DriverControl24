Imports System

Module Program
    Sub Main(args As String())
        If args IsNot Nothing AndAlso args.Count > 0 Then
            For i = 0 To args.Count - 1
                Dim arg = args(i).ToLower
                Select Case arg
                    Case "-d"
                        DriverReaperControl24.Main.DisplayInterfaceName()
                    Case "-s"
                        If i + 1 <= args.Count - 1 Then
                            'DriverReaperControl24.Main.StartFromConsole({args(i + 1)})
                            DriverReaperControl24.Main.Main
                        End If
                End Select

            Next


        End If



    End Sub
End Module
