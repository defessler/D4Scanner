using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace D4Scanner.App.Capture;

/// <summary>
/// Grabs a single frame of the Diablo IV window via Windows.Graphics.Capture (WGC).
/// Works in borderless and exclusive fullscreen. Falls back to PrintWindow if WGC is unavailable.
/// </summary>
public static class WindowsGraphicsCapture
{
    // ---- PrintWindow fallback ----
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint flags);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT lpRect);
    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }
    const uint PW_RENDERFULLCONTENT = 2;

    // ---- D3D11 ----
    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    static extern int RoGetActivationFactory(string activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(IntPtr pAdapter, int driverType, IntPtr software, uint flags,
        IntPtr pFeatureLevels, uint nFeatureLevels, uint sdkVersion,
        out IntPtr ppDevice, IntPtr pFeatureLevel, out IntPtr ppContext);
    const int D3D_DRIVER_TYPE_HARDWARE = 1;
    const uint D3D11_SDK_VERSION = 7;
    const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
    static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr d3dInteropDevice);

    static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    // ---- WGC factory interop ----
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr hwnd, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr hmon, [In] ref Guid iid);
    }

    // ---- SoftwareBitmap pixel access ----
    [ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    /// <summary>A grabbed frame plus which path produced it (the capture path matters for the diagnostic's
    /// coordinate space: WGC frames are the full window, PrintWindow frames are the client area).</summary>
    public readonly record struct GrabResult(System.Drawing.Bitmap? Bitmap, string Path);

    /// <summary>Grab a frame of Diablo IV. Bitmap is null if D4 is not running or capture fails.</summary>
    public static async Task<GrabResult> GrabAsync()
    {
        var procs = Process.GetProcessesByName("Diablo IV");
        try { return await GrabAsync(procs.FirstOrDefault()?.MainWindowHandle ?? IntPtr.Zero).ConfigureAwait(false); }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    /// <summary>Grab from an already-resolved D4 window handle — lets a caller that already enumerated the
    /// process (e.g. for a foreground check) avoid a second Process.GetProcessesByName per frame.</summary>
    public static async Task<GrabResult> GrabAsync(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return new(null, "none");
        try { return new(await GrabViaWgcAsync(hwnd).ConfigureAwait(false), "wgc"); }
        catch { return new(GrabViaPrintWindow(hwnd), "printwindow"); }
    }

    static async Task<System.Drawing.Bitmap?> GrabViaWgcAsync(IntPtr hwnd)
    {
        // 1. Build capture item via RoGetActivationFactory → IGraphicsCaptureItemInterop
        var captureItemGuid = typeof(GraphicsCaptureItem).GUID;
        var interopGuid = typeof(IGraphicsCaptureItemInterop).GUID;
        var roHr = RoGetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", ref interopGuid, out var factoryPtr);
        if (roHr < 0) throw new InvalidOperationException($"RoGetActivationFactory: 0x{roHr:X8}");
        var factory = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
        Marshal.Release(factoryPtr);
        var itemPtr = factory.CreateForWindow(hwnd, ref captureItemGuid);
        var item = GraphicsCaptureItem.FromAbi(itemPtr);
        Marshal.Release(itemPtr);

        // 2. D3D11 device → IDirect3DDevice (WinRT)
        var hr = D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0, D3D11_SDK_VERSION,
            out var d3d11Ptr, IntPtr.Zero, out var ctxPtr);
        if (hr < 0) throw new InvalidOperationException($"D3D11CreateDevice: 0x{hr:X8}");
        Marshal.Release(ctxPtr);

        var dxgiIid = IID_IDXGIDevice;
        hr = Marshal.QueryInterface(d3d11Ptr, ref dxgiIid, out var dxgiPtr);
        Marshal.Release(d3d11Ptr);
        if (hr < 0) throw new InvalidOperationException($"QI IDXGIDevice: 0x{hr:X8}");

        hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiPtr, out var d3dInteropPtr);
        Marshal.Release(dxgiPtr);
        if (hr < 0) throw new InvalidOperationException($"CreateDirect3D11DeviceFromDXGIDevice: 0x{hr:X8}");

        // `using`: this WinRT device wrapper is built fresh on EVERY grab (up to ~1.5s apart while gear is on
        // screen) — without disposal its native D3D/DXGI handles leaked until finalization. Declared before
        // `pool`, so it disposes LAST (the frame pool holds its own ref while capturing).
        using var d3dDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(d3dInteropPtr);
        Marshal.Release(d3dInteropPtr);

        // 3. Frame pool + one capture session
        using var pool = Direct3D11CaptureFramePool.Create(
            d3dDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
        using var session = pool.CreateCaptureSession(item);
        // IsBorderRequired added in Win11 22000; set via reflection so the SDK 19041 TFM compiles
        try { typeof(GraphicsCaptureSession).GetProperty("IsBorderRequired")?.SetValue(session, false); } catch { }

        var tcs = new TaskCompletionSource<Direct3D11CaptureFrame?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Windows.Foundation.TypedEventHandler<Direct3D11CaptureFramePool, object> handler = null!;
        handler = (p, _) => { p.FrameArrived -= handler; tcs.TrySetResult(p.TryGetNextFrame()); };
        pool.FrameArrived += handler;
        session.StartCapture();

        using var frame = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        if (frame == null) return null;

        // 4. Surface → SoftwareBitmap → System.Drawing.Bitmap.
        // `sb` is a CreateCopy we own; the helper consumes it synchronously into an independent
        // System.Drawing.Bitmap (its own copied pixel buffer), so dispose it deterministically at
        // method exit rather than leaking a full-screen-sized native bitmap to the finalizer on
        // every periodic OCR grab.
        using var sb = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).AsTask().ConfigureAwait(false);
        if (sb == null) return null;
        return await SoftwareBitmapToBitmapAsync(sb).ConfigureAwait(false);
    }

    internal static Task<System.Drawing.Bitmap?> SoftwareBitmapToBitmapAsync(SoftwareBitmap sb)
    {
        // Convert SoftwareBitmap → pixel array → System.Drawing.Bitmap directly (no stream extension needed)
        var converted = SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        int w = converted.PixelWidth, h = converted.PixelHeight;
        var buf = new byte[w * h * 4];
        converted.CopyToBuffer(buf.AsBuffer());
        converted.Dispose();
        var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bits = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(buf, 0, bits.Scan0, buf.Length);
        bmp.UnlockBits(bits);
        var mid = bmp.GetPixel(w / 2, h / 2);
        if (mid.R < 5 && mid.G < 5 && mid.B < 5) { bmp.Dispose(); return Task.FromResult<System.Drawing.Bitmap?>(null); }
        return Task.FromResult<System.Drawing.Bitmap?>(bmp);
    }

    static System.Drawing.Bitmap? GrabViaPrintWindow(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out var rect)) return null;
        int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
        if (w < 100 || h < 100) return null;
        using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            bool ok = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
            g.ReleaseHdc(hdc);
            if (!ok) return null;
        }
        var mid = bmp.GetPixel(w / 2, h / 2);
        if (mid.R < 5 && mid.G < 5 && mid.B < 5) return null;
        return (System.Drawing.Bitmap)bmp.Clone();
    }
}
