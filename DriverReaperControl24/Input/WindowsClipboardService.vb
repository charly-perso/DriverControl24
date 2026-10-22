Imports System.Runtime.InteropServices
Imports System.Text

Namespace DriverReaperControl24.Input
    Friend NotInheritable Class WindowsClipboardService
        Implements IClipboardService

        Private Const CF_UNICODETEXT As UInteger = 13UI
        Private Const GMEM_MOVEABLE As UInteger = &H2UI

        <DllImport("user32.dll", SetLastError:=True)>
        Private Shared Function OpenClipboard(hWndNewOwner As IntPtr) As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Private Shared Function CloseClipboard() As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Private Shared Function IsClipboardFormatAvailable(format As UInteger) As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Private Shared Function GetClipboardData(uFormat As UInteger) As IntPtr
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Private Shared Function EmptyClipboard() As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Private Shared Function SetClipboardData(uFormat As UInteger, hMem As IntPtr) As IntPtr
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Shared Function GlobalAlloc(uFlags As UInteger, dwBytes As UIntPtr) As IntPtr
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Shared Function GlobalLock(hMem As IntPtr) As IntPtr
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Shared Function GlobalUnlock(hMem As IntPtr) As Boolean
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Shared Function GlobalFree(hMem As IntPtr) As IntPtr
        End Function

        ''' <summary>
        ''' Lit le texte courant du presse-papiers Windows.
        ''' </summary>
        Public Function GetText() As String Implements IClipboardService.GetText
            If OpenClipboard(IntPtr.Zero) = False Then
                Return String.Empty
            End If

            Try
                If IsClipboardFormatAvailable(CF_UNICODETEXT) = False Then
                    Return String.Empty
                End If

                Dim hMem As IntPtr = GetClipboardData(CF_UNICODETEXT)
                If hMem = IntPtr.Zero Then
                    Return String.Empty
                End If

                Dim pMem As IntPtr = GlobalLock(hMem)
                If pMem = IntPtr.Zero Then
                    Return String.Empty
                End If

                Try
                    Dim txt As String = Marshal.PtrToStringUni(pMem)
                    If txt Is Nothing Then
                        Return String.Empty
                    End If

                    Return txt
                Finally
                    GlobalUnlock(hMem)
                End Try
            Finally
                CloseClipboard()
            End Try
        End Function

        ''' <summary>
        ''' Écrit un texte dans le presse-papiers Windows.
        ''' </summary>
        Public Function SetText(text As String) As Boolean Implements IClipboardService.SetText
            If text Is Nothing Then
                text = String.Empty
            End If

            If OpenClipboard(IntPtr.Zero) = False Then
                Return False
            End If

            Try
                If EmptyClipboard() = False Then
                    Return False
                End If

                Dim bytes() As Byte = Encoding.Unicode.GetBytes(text & ChrW(0))
                Dim hGlobal As IntPtr = GlobalAlloc(GMEM_MOVEABLE, CType(bytes.Length, UIntPtr))

                If hGlobal = IntPtr.Zero Then
                    Return False
                End If

                Dim pGlobal As IntPtr = GlobalLock(hGlobal)
                If pGlobal = IntPtr.Zero Then
                    GlobalFree(hGlobal)
                    Return False
                End If

                Try
                    Marshal.Copy(bytes, 0, pGlobal, bytes.Length)
                Finally
                    GlobalUnlock(hGlobal)
                End Try

                Dim result As IntPtr = SetClipboardData(CF_UNICODETEXT, hGlobal)
                If result = IntPtr.Zero Then
                    GlobalFree(hGlobal)
                    Return False
                End If

                Return True
            Finally
                CloseClipboard()
            End Try
        End Function
    End Class
End Namespace