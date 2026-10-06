# Screenshots, QR sharing and hide/open

The user expanded the current scope to add screenshots, QR screen sharing, a simple/cute interface and a hide/open toggle. The implemented QR feature shares a captured screenshot on the local network. A clarification about snapshot versus live view received no answer, so screenshot sharing was the stated default.

## User flow

- Click the camera icon. ScreenInk captures the monitor containing its toolbar, including visible annotations. The toolbar disappears briefly during capture and then returns beside a small preview.
- Save chooses a PNG destination with the native Windows App SDK save picker. Cancel writes nothing. Copy places the PNG image on the clipboard.
- The camera reopens the latest snapshot/QR so its link stays available. New takes another screenshot and replaces the previous share.
- Share QR opens a compact QR view. Scan using a phone on the same Wi-Fi to open a mobile page with the screenshot and a Save image link. Multiple available network adapters expose a small selector. Copy link and Stop sharing remain available below the QR.
- Sharing ends after ten minutes, Stop sharing, a replacement screenshot, or Exit. Closing the preview leaves an active share running for its remaining lifetime. The camera stays purple while sharing; expiry clears that state.
- Hide conceals the ink and collapses the toolbar into a rounded purple pen button. Click it to reopen and restore the previous ink/input mode. Ctrl+Alt+F10 also collapses; Ctrl+Alt+F9 reopens through the existing recovery shortcut. Hiding does not delete ink or its history.

The toolbar uses round buttons and one purple selection accent. Capture actions live in a flyout; the main strip gains only the camera icon. A QR view replaces the preview thumbnail while sharing so the code remains prominent without a long panel.

## Implementation

| File | Purpose |
| --- | --- |
| `src/ScreenInk.Native/DesktopCapture.cs` | Monitor bounds and native top-down BGRA capture with opaque alpha |
| `src/ScreenInk.Native/NativeMethods.cs` | Documented BitBlt, GdiFlush and DwmFlush interop |
| `src/ScreenInk.App/Services/ScreenshotPng.cs` | Windows Imaging PNG encoding and memory streams |
| `src/ScreenInk.App/Services/ScreenshotImage.cs` | Native WinUI image previews |
| `src/ScreenInk.App/ScreenshotPanel.xaml` / `.xaml.cs` | Save/copy, QR display, adapter selection, status and stop controls |
| `src/ScreenInk.App/MainWindow.xaml` / `.xaml.cs` | Camera, temporary toolbar removal, mode restoration and collapsed pen button |
| `src/ScreenInk.Sharing/ShareNetwork.cs` | Active private IPv4 Wi-Fi/Ethernet adapter choices |
| `src/ScreenInk.Sharing/ScreenshotShareServer.cs` | Bounded local HTTP screenshot session, random link and expiration |
| `src/ScreenInk.Sharing/ShareQrCode.cs` | QR PNG with a quiet zone and Q error correction |
| `tests/ScreenInk.Overlay.Tests/Program.cs` | Synthetic native screenshot pixels/crop regression |
| `tests/ScreenInk.Sharing.Tests/Program.cs` | PNG/QR round trips and loopback server lifecycle tests |
| `scripts/Test-Sharing.ps1` | Build/run the sharing test runner |

Capture uses BitBlt with CAPTUREBLT, so layered annotation windows are included. DwmFlush synchronizes desktop composition after the toolbar is hidden. Memory/DC/bitmap handles are released on success or failure. Native desktop coordinates include negative monitor origins, and image dimensions use physical pixels. The screenshot has opaque alpha because GDI's fourth byte is reserved. Input changes to click-through during capture and preview; the previous mode returns on dismissal. The save picker keeps desktop interception paused until it closes.

Sharing uses a TcpListener bound to the selected private adapter and an OS-assigned port. It requires no HTTP URL ACL or administrator account. Only two exact routes exist: the random 128-bit-token URL and its PNG image. The PNG is cloned into the session; there is no file browsing, upload, or continuous capture. GET/HEAD are supported, other methods rejected. At most eight clients are active, request headers are limited to 8 KiB with a five-second deadline, and image transfers have a thirty-second deadline. All requests and the listener are canceled on stop/expiry. Responses have no-store, no-referrer and restrictive resource headers.

The link is plain HTTP on the same LAN. Anyone with that link and network reachability can view that snapshot until it expires. No remote hosting, account, cloud upload, or Internet sharing is involved. Windows may require the user to allow ScreenInk through the firewall on their private network; ScreenInk does not change firewall rules. Guest/client isolation can prevent phones from reaching the computer. Previously saved images remain on the receiving device after sharing stops.

QRCoder 1.8.0 is the sole new application package and runs locally. ZXing.Net 0.16.11 is test-only, providing an independent QR decoder. Central package versions and lock files record both. The project remains a native WinUI application; the served HTML is only the phone's screenshot viewer.

## Commands and verification

```powershell
Set-Location 'C:\Users\Venkatesh\Documents\ChatGPT\Pen_ven'
.\scripts\Build.ps1 -Configuration Debug
.\scripts\Build.ps1 -Configuration Release
.\scripts\Test-Overlay.ps1
.\scripts\Test-Sharing.ps1
.\scripts\Run.ps1 -Configuration Debug -NoBuild
```

Close a running preview before rebuilding Debug or running overlay tests; closing ends its temporary ink session. Sharing tests use only synthetic pixels and loopback networking.

Verification on October 6, 2026:

- Debug and Release builds passed without warnings or errors.
- Native synthetic capture preserved crop dimensions, top-down row ordering, BGRA channels and opaque alpha. Drawing, shapes, pressure, erasing and history regressions passed.
- The production PNG encoder round-tripped synthetic pixels through Windows Imaging. An independent ZXing decoder recovered the exact share URL from the generated QR PNG.
- Loopback requests verified the mobile page, exact PNG content, immutable snapshot ownership, unknown/tokenless routes, rejected write methods and HEAD semantics.
- Stopping sharing closed the listener. Short expiration canceled a client holding a partial request. Replacement sessions received different tokens.
- UI verification confirmed camera preview, a native PNG Save dialog and Cancel, collapse to the little purple pen, reopening the toolbar, reopening the same snapshot, and New taking a replacement snapshot.

A real phone on Wi-Fi/firewall configurations, additional displays/DPI changes and Windows 10 require hands-on verification. Capture takes the current monitor with visible ink; region selection and live streaming are separate future work. Protected video surfaces may appear black in native desktop capture.

## Primary references

- [BitBlt and CAPTUREBLT](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-bitblt)
- [DwmFlush](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmflush)
- [Windows App SDK FileSavePicker](https://learn.microsoft.com/en-us/windows/apps/develop/files/pickers-save-file)
- [BitmapEncoder.SetPixelData](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.imaging.bitmapencoder.setpixeldata)
- [QRCoder 1.8.0](https://www.nuget.org/packages/QRCoder/1.8.0)
- [ZXing.Net 0.16.11](https://www.nuget.org/packages/ZXing.Net/0.16.11)
