Imports System.Runtime.InteropServices
Imports System.Threading

Namespace DriverReaperControl24.Input
    ''' <summary>
    ''' Fabrique d'injecteurs clavier en fonction de l'OS courant.
    ''' </summary>
    Public NotInheritable Class KeyboardInjectorFactory
        ''' <summary>
        ''' Crée un injecteur clavier adapté à la plateforme.
        ''' </summary>
        Public Shared Function Create() As IKeyboardInjector
            If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
                Return New WindowsKeyboardInjector()
            End If

            If RuntimeInformation.IsOSPlatform(OSPlatform.OSX) Then
                Return New MacKeyboardInjector()
            End If

            Return New NullKeyboardInjector()
        End Function
    End Class

    Friend NotInheritable Class NullKeyboardInjector
        Implements IKeyboardInjector

        Public Sub KeyDown(key As KeyboardKey) Implements IKeyboardInjector.KeyDown
        End Sub

        Public Sub KeyUp(key As KeyboardKey) Implements IKeyboardInjector.KeyUp
        End Sub

        Public Sub PressKey(key As KeyboardKey, Optional holdMilliseconds As Integer = 20) Implements IKeyboardInjector.PressKey
            If holdMilliseconds > 0 Then
                Thread.Sleep(holdMilliseconds)
            End If
        End Sub
        Public Sub PressCtrlAndKey(key As KeyboardKey, Optional holdMilliseconds As Integer = 20) Implements IKeyboardInjector.PressCtrlAndKey
            If holdMilliseconds > 0 Then
                Thread.Sleep(holdMilliseconds)
            End If
        End Sub
        Public Sub PressShiftAndKey(key As KeyboardKey, Optional holdMilliseconds As Integer = 20) Implements IKeyboardInjector.PressShiftAndKey
            If holdMilliseconds > 0 Then
                Thread.Sleep(holdMilliseconds)
            End If
        End Sub
    End Class
End Namespace