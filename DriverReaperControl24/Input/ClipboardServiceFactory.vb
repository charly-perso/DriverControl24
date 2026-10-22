Imports System.Runtime.InteropServices

Namespace DriverReaperControl24.Input
    ''' <summary>
    ''' Fabrique de services presse-papiers selon l'OS courant.
    ''' </summary>
    Public NotInheritable Class ClipboardServiceFactory
        ''' <summary>
        ''' Crée le service presse-papiers adapté à la plateforme.
        ''' </summary>
        ''' <returns>Une implémentation de <see cref="IClipboardService"/>.</returns>
        Public Shared Function Create() As IClipboardService
            If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
                Return New WindowsClipboardService()
            End If

            If RuntimeInformation.IsOSPlatform(OSPlatform.OSX) Then
                Return New MacClipboardService()
            End If

            Return New NullClipboardService()
        End Function
    End Class

    Friend NotInheritable Class NullClipboardService
        Implements IClipboardService

        Public Function GetText() As String Implements IClipboardService.GetText
            Return String.Empty
        End Function

        Public Function SetText(text As String) As Boolean Implements IClipboardService.SetText
            Return False
        End Function
    End Class
End Namespace