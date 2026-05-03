using System.Runtime.InteropServices;

namespace AZERTYGlobal;

static class GdiImageLoader
{
    public static IntPtr LoadFromEmbeddedResource(Type ownerType, string resourceName)
    {
        try
        {
            using var stream = ownerType.Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return IntPtr.Zero;

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);

            IntPtr hGlobal = Win32.GlobalAlloc(0x0042, (nuint)bytes.Length);
            if (hGlobal == IntPtr.Zero)
                return IntPtr.Zero;

            IntPtr pGlobal = Win32.GlobalLock(hGlobal);
            if (pGlobal == IntPtr.Zero)
            {
                Win32.GlobalFree(hGlobal);
                return IntPtr.Zero;
            }

            Marshal.Copy(bytes, 0, pGlobal, bytes.Length);
            Win32.GlobalUnlock(hGlobal);

            // CreateStreamOnHGlobal avec fDeleteOnRelease=true : si la creation reussit,
            // hGlobal sera GlobalFree quand pStream est release (Marshal.Release ci-dessous).
            // Si la creation echoue (rare, OOM extreme), pStream reste a IntPtr.Zero, le
            // fDeleteOnRelease ne s'applique pas → on doit liberer hGlobal manuellement
            // pour eviter un leak qui survit jusqu'a la fin du process.
            int hr = Win32.CreateStreamOnHGlobal(hGlobal, true, out IntPtr pStream);
            if (hr != 0 || pStream == IntPtr.Zero)
            {
                Win32.GlobalFree(hGlobal);
                return IntPtr.Zero;
            }
            try
            {
                return Win32.GdipCreateBitmapFromStream(pStream, out IntPtr image) == 0
                    ? image
                    : IntPtr.Zero;
            }
            finally
            {
                Marshal.Release(pStream);
            }
        }
        catch (Exception ex) when (ex is ExternalException or IOException or ArgumentException)
        {
            return IntPtr.Zero;
        }
    }
}
