using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

class Program
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    static extern IntPtr LoadLibraryA(string path);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    static extern IntPtr GetProcAddress(IntPtr hModule, string name);

    [DllImport("kernel32.dll")]
    static extern bool FreeLibrary(IntPtr hModule);

    // OpenXR structures for xrCreateInstance
    [StructLayout(LayoutKind.Sequential)]
    struct XrApplicationInfo
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public byte[] applicationName;
        public uint applicationVersion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public byte[] engineName;
        public uint engineVersion;
        public ulong apiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XrInstanceCreateInfo
    {
        public int type;              // XR_TYPE_INSTANCE_CREATE_INFO = 1
        public IntPtr next;
        public ulong createFlags;
        public XrApplicationInfo applicationInfo;
        public uint enabledApiLayerCount;
        public IntPtr enabledApiLayerNames;
        public uint enabledExtensionCount;
        public IntPtr enabledExtensionNames;
    }

    // xrCreateInstance(const XrInstanceCreateInfo*, XrInstance*)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int xrCreateInstanceDelegate(ref XrInstanceCreateInfo createInfo, out IntPtr instance);

    // xrDestroyInstance(XrInstance)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int xrDestroyInstanceDelegate(IntPtr instance);

    static void Main()
    {
        // Use a real game's OpenXR loader
        string loaderPath = @"D:\SteamLibrary\steamapps\common\Bigscreen\Bigscreen_Data\Plugins\x86_64\openxr_loader.dll";
        if (!File.Exists(loaderPath))
        {
            // Fallback: try another
            loaderPath = @"C:\Program Files (x86)\Steam\steamapps\common\Outward\Outward_Data\Plugins\x86_64\openxr_loader.dll";
        }

        string log = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TreadmillDriver", "OpenXRLayer", "layer_log.txt");

        // Delete old log
        if (File.Exists(log))
        {
            try { File.Delete(log); } catch { }
        }

        Console.WriteLine($"OpenXR Loader: {loaderPath}");
        Console.WriteLine($"Loader exists: {File.Exists(loaderPath)}");
        if (!File.Exists(loaderPath))
        {
            Console.WriteLine("No OpenXR loader found!");
            return;
        }

        // Set debug env var for this process
        Environment.SetEnvironmentVariable("XR_LOADER_DEBUG", "all");

        IntPtr h = LoadLibraryA(loaderPath);
        if (h == IntPtr.Zero)
        {
            Console.WriteLine($"LoadLibrary(loader) FAILED: {Marshal.GetLastWin32Error()}");
            return;
        }
        Console.WriteLine("OpenXR loader loaded OK");

        IntPtr pCreate = GetProcAddress(h, "xrCreateInstance");
        IntPtr pDestroy = GetProcAddress(h, "xrDestroyInstance");
        Console.WriteLine($"xrCreateInstance: {(pCreate != IntPtr.Zero ? "found" : "NOT FOUND")}");

        if (pCreate == IntPtr.Zero)
        {
            FreeLibrary(h);
            return;
        }

        var createInstance = Marshal.GetDelegateForFunctionPointer<xrCreateInstanceDelegate>(pCreate);

        // Build XrInstanceCreateInfo
        var appInfo = new XrApplicationInfo();
        appInfo.applicationName = new byte[128];
        appInfo.engineName = new byte[128];
        Encoding.UTF8.GetBytes("TreadmillTest").CopyTo(appInfo.applicationName, 0);
        Encoding.UTF8.GetBytes("TestEngine").CopyTo(appInfo.engineName, 0);
        appInfo.applicationVersion = 1;
        appInfo.engineVersion = 1;
        appInfo.apiVersion = (1UL << 48); // XR_MAKE_VERSION(1,0,0)

        var createInfo = new XrInstanceCreateInfo();
        createInfo.type = 1; // XR_TYPE_INSTANCE_CREATE_INFO
        createInfo.next = IntPtr.Zero;
        createInfo.createFlags = 0;
        createInfo.applicationInfo = appInfo;
        createInfo.enabledApiLayerCount = 0;
        createInfo.enabledApiLayerNames = IntPtr.Zero;
        createInfo.enabledExtensionCount = 0;
        createInfo.enabledExtensionNames = IntPtr.Zero;

        Console.WriteLine("\nCalling xrCreateInstance...");
        IntPtr instance = IntPtr.Zero;
        try
        {
            int result = createInstance(ref createInfo, out instance);
            Console.WriteLine($"xrCreateInstance returned: {result}");
            if (instance != IntPtr.Zero && pDestroy != IntPtr.Zero)
            {
                var destroyInstance = Marshal.GetDelegateForFunctionPointer<xrDestroyInstanceDelegate>(pDestroy);
                destroyInstance(instance);
                Console.WriteLine("Instance destroyed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
        }

        Console.WriteLine($"\nLayer log created: {File.Exists(log)}");
        if (File.Exists(log))
        {
            try
            {
                using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                Console.WriteLine("--- LAYER LOG ---");
                Console.WriteLine(sr.ReadToEnd());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not read log: {ex.Message}");
            }
        }

        FreeLibrary(h);
        Console.WriteLine("Done.");
    }
}
