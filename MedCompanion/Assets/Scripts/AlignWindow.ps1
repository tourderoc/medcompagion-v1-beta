param (
    [string]$WindowTitle,
    [int]$X,
    [int]$Y,
    [int]$Width,
    [int]$Height
)

$code = @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public class WindowMover {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("kernel32.dll")]
    public static extern int GetLastError();

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_RESTORE = 9;
    private const int SW_SHOWNORMAL = 1;
    private const int GWL_STYLE = -16;
    private const int WS_MAXIMIZE = 0x01000000;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const uint SWP_SHOWWINDOW = 0x0040;

    public static IntPtr FindVisibleWindowByTitle(string title, out string debugLog) {
        IntPtr found = IntPtr.Zero;
        StringBuilder log = new StringBuilder();
        log.AppendLine("--- Fenetres visibles detectees ---");

        EnumWindows(delegate (IntPtr hWnd, IntPtr lParam) {
            if (IsWindowVisible(hWnd)) {
                StringBuilder sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, 256);
                string currentTitle = sb.ToString();

                if (!string.IsNullOrEmpty(currentTitle)) {
                    log.AppendLine("- " + currentTitle);

                    if (currentTitle.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0) {
                        if (currentTitle.IndexOf("MedCompanion", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            !title.Equals("MedCompanion", StringComparison.OrdinalIgnoreCase)) {
                            return true;
                        }

                        found = hWnd;
                        return false;
                    }
                }
            }
            return true;
        }, IntPtr.Zero);

        debugLog = log.ToString();
        return found;
    }

    public static string PrepareAndMove(IntPtr hWnd, int x, int y, int w, int h) {
        int style = GetWindowLong(hWnd, GWL_STYLE);
        bool wasMaximized = (style & WS_MAXIMIZE) != 0;

        if (wasMaximized) {
            ShowWindow(hWnd, SW_RESTORE);
            Thread.Sleep(100);
        }

        ShowWindow(hWnd, SW_SHOWNORMAL);
        Thread.Sleep(50);

        SetForegroundWindow(hWnd);

        bool result = SetWindowPos(hWnd, HWND_TOP, x, y, w, h, SWP_SHOWWINDOW);

        if (!result) {
            int error = GetLastError();
            return "SetWindowPos echec (code " + error + ")";
        }

        return "OK";
    }
}
'@

Add-Type -TypeDefinition $code

$debugInfo = ""
$hWnd = [WindowMover]::FindVisibleWindowByTitle($WindowTitle, [ref]$debugInfo)

if ($hWnd -ne [IntPtr]::Zero) {
    $moveResult = [WindowMover]::PrepareAndMove($hWnd, $X, $Y, $Width, $Height)
    if ($moveResult -eq "OK") {
        Write-Host "Succès: Fenêtre '$WindowTitle' déplacée vers ($X, $Y) taille ${Width}x${Height}."
    } else {
        Write-Error "Échec du déplacement: $moveResult"
        exit 1
    }
} else {
    Write-Host $debugInfo
    Write-Error "Action impossible : Aucune fenêtre visible contenant '$WindowTitle' n'a été trouvée."
    exit 1
}
