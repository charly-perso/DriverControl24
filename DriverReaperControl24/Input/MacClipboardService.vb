Imports System.Runtime.InteropServices

Namespace DriverReaperControl24.Input
    Friend NotInheritable Class MacClipboardService
        Implements IClipboardService

        Private Const PasteboardTypeString As String = "public.utf8-plain-text"

        <DllImport("/usr/lib/libobjc.A.dylib")>
        Private Shared Function objc_getClass(name As String) As IntPtr
        End Function

        <DllImport("/usr/lib/libobjc.A.dylib")>
        Private Shared Function sel_registerName(name As String) As IntPtr
        End Function

        <DllImport("/usr/lib/libobjc.A.dylib", EntryPoint:="objc_msgSend")>
        Private Shared Function objc_msgSend(receiver As IntPtr, selector As IntPtr) As IntPtr
        End Function

        <DllImport("/usr/lib/libobjc.A.dylib", EntryPoint:="objc_msgSend")>
        Private Shared Function objc_msgSend_IntPtr(receiver As IntPtr, selector As IntPtr, arg1 As IntPtr) As IntPtr
        End Function

        <DllImport("/usr/lib/libobjc.A.dylib", EntryPoint:="objc_msgSend")>
        Private Shared Function objc_msgSend_Bool(receiver As IntPtr, selector As IntPtr, arg1 As IntPtr, arg2 As IntPtr) As Boolean
        End Function

        ''' <summary>
        ''' Lit le texte courant via NSPasteboard.
        ''' </summary>
        Public Function GetText() As String Implements IClipboardService.GetText
            Dim pool As IntPtr = CreateAutoreleasePool()

            Try
                Dim pasteboard As IntPtr = GetGeneralPasteboard()
                If pasteboard = IntPtr.Zero Then
                    Return String.Empty
                End If

                Dim typeString As IntPtr = CreateNSString(PasteboardTypeString)
                If typeString = IntPtr.Zero Then
                    Return String.Empty
                End If

                Dim selStringForType As IntPtr = sel_registerName("stringForType:")
                Dim nsString As IntPtr = objc_msgSend_IntPtr(pasteboard, selStringForType, typeString)

                If nsString = IntPtr.Zero Then
                    Return String.Empty
                End If

                Dim selUtf8 As IntPtr = sel_registerName("UTF8String")
                Dim utf8Ptr As IntPtr = objc_msgSend(nsString, selUtf8)

                If utf8Ptr = IntPtr.Zero Then
                    Return String.Empty
                End If

                Dim txt As String = Marshal.PtrToStringUTF8(utf8Ptr)
                If txt Is Nothing Then
                    Return String.Empty
                End If

                Return txt
            Finally
                DrainAutoreleasePool(pool)
            End Try
        End Function

        ''' <summary>
        ''' Écrit un texte via NSPasteboard.
        ''' </summary>
        Public Function SetText(text As String) As Boolean Implements IClipboardService.SetText
            If text Is Nothing Then
                text = String.Empty
            End If

            Dim pool As IntPtr = CreateAutoreleasePool()

            Try
                Dim pasteboard As IntPtr = GetGeneralPasteboard()
                If pasteboard = IntPtr.Zero Then
                    Return False
                End If

                Dim selClear As IntPtr = sel_registerName("clearContents")
                objc_msgSend(pasteboard, selClear)

                Dim nsText As IntPtr = CreateNSString(text)
                If nsText = IntPtr.Zero Then
                    Return False
                End If

                Dim nsType As IntPtr = CreateNSString(PasteboardTypeString)
                If nsType = IntPtr.Zero Then
                    Return False
                End If

                Dim selSetStringForType As IntPtr = sel_registerName("setString:forType:")
                Dim ok As Boolean = objc_msgSend_Bool(pasteboard, selSetStringForType, nsText, nsType)

                Return ok
            Finally
                DrainAutoreleasePool(pool)
            End Try
        End Function

        Private Function GetGeneralPasteboard() As IntPtr
            Dim cls As IntPtr = objc_getClass("NSPasteboard")
            If cls = IntPtr.Zero Then
                Return IntPtr.Zero
            End If

            Dim sel As IntPtr = sel_registerName("generalPasteboard")
            Return objc_msgSend(cls, sel)
        End Function

        Private Function CreateNSString(value As String) As IntPtr
            Dim cls As IntPtr = objc_getClass("NSString")
            If cls = IntPtr.Zero Then
                Return IntPtr.Zero
            End If

            Dim sel As IntPtr = sel_registerName("stringWithUTF8String:")
            Dim _cstr As IntPtr = Marshal.StringToCoTaskMemUTF8(value)

            Try
                Return objc_msgSend_IntPtr(cls, sel, _cstr)
            Finally
                Marshal.FreeCoTaskMem(_cstr)
            End Try
        End Function

        Private Function CreateAutoreleasePool() As IntPtr
            Dim cls As IntPtr = objc_getClass("NSAutoreleasePool")
            If cls = IntPtr.Zero Then
                Return IntPtr.Zero
            End If

            Dim selAlloc As IntPtr = sel_registerName("alloc")
            Dim selInit As IntPtr = sel_registerName("init")

            Dim pool As IntPtr = objc_msgSend(cls, selAlloc)
            If pool = IntPtr.Zero Then
                Return IntPtr.Zero
            End If

            Return objc_msgSend(pool, selInit)
        End Function

        Private Sub DrainAutoreleasePool(pool As IntPtr)
            If pool = IntPtr.Zero Then
                Return
            End If

            Dim selDrain As IntPtr = sel_registerName("drain")
            objc_msgSend(pool, selDrain)
        End Sub
    End Class
End Namespace