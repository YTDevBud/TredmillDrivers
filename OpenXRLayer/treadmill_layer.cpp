// ═══════════════════════════════════════════════════════════════════
// Treadmill Driver — OpenXR Implicit API Layer
// ═══════════════════════════════════════════════════════════════════
// Intercepts OpenXR input calls and injects treadmill velocity
// into the left thumbstick Y axis. Reads velocity from a named
// memory-mapped file written by the WPF companion app.
//
// Pure C + Win32 — no STL, no static constructors. v3
// ═══════════════════════════════════════════════════════════════════

#include "openxr_defs.h"

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <stdio.h>
#include <string.h>

// ─── Layer Identity ─────────────────────────────────────────────

#define LAYER_NAME "XR_APILAYER_TREADMILL_driver"

// ─── Debug Log ──────────────────────────────────────────────────

static HANDLE g_logFile = INVALID_HANDLE_VALUE;

static void LogOpen()
{
    if (g_logFile != INVALID_HANDLE_VALUE) return;

    char path[MAX_PATH];
    if (SUCCEEDED(SHGetFolderPathA(NULL, CSIDL_LOCAL_APPDATA, NULL, 0, path))) {
        strcat_s(path, "\\TreadmillDriver\\OpenXRLayer\\layer_log.txt");
        g_logFile = CreateFileA(path, GENERIC_WRITE, FILE_SHARE_READ,
                                NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    }
}

static void Log(const char* msg)
{
    if (g_logFile == INVALID_HANDLE_VALUE) return;
    DWORD written;
    WriteFile(g_logFile, msg, (DWORD)strlen(msg), &written, NULL);
    WriteFile(g_logFile, "\r\n", 2, &written, NULL);
    FlushFileBuffers(g_logFile);
}

static void LogClose()
{
    if (g_logFile != INVALID_HANDLE_VALUE) {
        CloseHandle(g_logFile);
        g_logFile = INVALID_HANDLE_VALUE;
    }
}

// ─── Shared Memory Protocol ────────────────────────────────────

#define SHARED_MEM_NAME     "TreadmillDriverVelocity"
#define SHARED_MEM_RETRY_MS 2000

#pragma pack(push, 1)
struct TreadmillSharedData {
    float       velocity;
    uint32_t    active;
};
#pragma pack(pop)

// ─── Action Tracking (fixed-size, no STL) ───────────────────────

#define MAX_TRACKED_ACTIONS 64

struct TrackedActions {
    uintptr_t   vec2f[MAX_TRACKED_ACTIONS];
    int         vec2fCount;
    uintptr_t   floatY[MAX_TRACKED_ACTIONS];
    int         floatYCount;
    BOOL        bindingsReceived;
};

// ─── Global State (all POD — no static constructors) ────────────

static XrInstance                   g_instance                          = XR_NULL_HANDLE;
static PFN_xrGetInstanceProcAddr    g_nextGetInstanceProcAddr           = NULL;

static PFN_xrDestroyInstance                        g_xrDestroyInstance                     = NULL;
static PFN_xrPathToString                           g_xrPathToString                        = NULL;
static PFN_xrStringToPath                           g_xrStringToPath                        = NULL;
static PFN_xrSuggestInteractionProfileBindings      g_xrSuggestInteractionProfileBindings   = NULL;
static PFN_xrGetActionStateFloat                    g_xrGetActionStateFloat                 = NULL;
static PFN_xrGetActionStateVector2f                 g_xrGetActionStateVector2f              = NULL;

static XrPath                                       g_leftHandPath                          = XR_NULL_PATH;

static CRITICAL_SECTION     g_cs;
static BOOL                 g_csInitialized = FALSE;
static TrackedActions       g_tracked       = {};

static HANDLE               g_sharedMemHandle       = NULL;
static TreadmillSharedData* g_sharedData            = NULL;
static ULONGLONG            g_lastSharedMemAttempt   = 0;

// ─── Helpers ────────────────────────────────────────────────────

static void EnsureCritSec()
{
    if (!g_csInitialized) {
        InitializeCriticalSection(&g_cs);
        g_csInitialized = TRUE;
    }
}

static void OpenSharedMemory()
{
    if (g_sharedMemHandle) return;

    g_sharedMemHandle = OpenFileMappingA(FILE_MAP_READ, FALSE, SHARED_MEM_NAME);
    if (g_sharedMemHandle) {
        g_sharedData = (TreadmillSharedData*)MapViewOfFile(
            g_sharedMemHandle, FILE_MAP_READ, 0, 0, sizeof(TreadmillSharedData));
        Log(g_sharedData ? "SharedMem: mapped OK" : "SharedMem: MapViewOfFile failed");
    } else {
        Log("SharedMem: not available (WPF app not running?)");
    }
}

static void CloseSharedMemory()
{
    if (g_sharedData)    { UnmapViewOfFile(g_sharedData); g_sharedData = NULL; }
    if (g_sharedMemHandle) { CloseHandle(g_sharedMemHandle); g_sharedMemHandle = NULL; }
}

static float ReadTreadmillVelocity()
{
    if (!g_sharedData) {
        ULONGLONG now = GetTickCount64();
        if (now - g_lastSharedMemAttempt >= SHARED_MEM_RETRY_MS) {
            g_lastSharedMemAttempt = now;
            OpenSharedMemory();
        }
    }
    if (g_sharedData && g_sharedData->active) {
        return g_sharedData->velocity;
    }
    return 0.0f;
}

static BOOL ContainsAction(const uintptr_t* arr, int count, uintptr_t key)
{
    for (int i = 0; i < count; i++) {
        if (arr[i] == key) return TRUE;
    }
    return FALSE;
}

static void AddAction(uintptr_t* arr, int* count, uintptr_t key)
{
    if (*count >= MAX_TRACKED_ACTIONS) return;
    if (!ContainsAction(arr, *count, key)) {
        arr[*count] = key;
        (*count)++;
    }
}

// ─── Intercepted: xrSuggestInteractionProfileBindings ───────────

static XrResult XRAPI_CALL
TreadmillLayer_xrSuggestInteractionProfileBindings(
    XrInstance instance,
    const XrInteractionProfileSuggestedBinding* suggestedBindings)
{
    Log("xrSuggestInteractionProfileBindings called");

    XrResult result = g_xrSuggestInteractionProfileBindings(instance, suggestedBindings);
    if (XR_FAILED(result)) {
        Log("  -> chained call FAILED");
        return result;
    }

    if (!g_xrPathToString) {
        Log("  -> no xrPathToString, skipping binding scan");
        return result;
    }

    EnterCriticalSection(&g_cs);

    for (uint32_t i = 0; i < suggestedBindings->countSuggestedBindings; i++) {
        char pathStr[256] = {0};
        uint32_t pathLen = 0;
        XrResult pr = g_xrPathToString(
            instance,
            suggestedBindings->suggestedBindings[i].binding,
            sizeof(pathStr), &pathLen, pathStr);

        if (XR_FAILED(pr) || pathLen == 0) continue;

        BOOL isLeft       = strstr(pathStr, "/user/hand/left") != NULL;
        BOOL isThumbstick = strstr(pathStr, "thumbstick")      != NULL;

        if (isLeft && isThumbstick) {
            uintptr_t key = (uintptr_t)suggestedBindings->suggestedBindings[i].action;

            char logBuf[320];
            sprintf_s(logBuf, "  Tracked binding: %s (action=%p)", pathStr, (void*)key);
            Log(logBuf);

            if (strstr(pathStr, "thumbstick/y")) {
                AddAction(g_tracked.floatY, &g_tracked.floatYCount, key);
            } else if (!strstr(pathStr, "thumbstick/x")) {
                AddAction(g_tracked.vec2f, &g_tracked.vec2fCount, key);
            }
            g_tracked.bindingsReceived = TRUE;
        }
    }

    LeaveCriticalSection(&g_cs);
    return result;
}

// ─── Intercepted: xrGetActionStateVector2f ──────────────────────

static XrResult XRAPI_CALL
TreadmillLayer_xrGetActionStateVector2f(
    XrSession session,
    const XrActionStateGetInfo* getInfo,
    XrActionStateVector2f* state)
{
    XrResult result = g_xrGetActionStateVector2f(session, getInfo, state);
    if (XR_FAILED(result)) return result;

    // Only inject on left hand subaction (or XR_NULL_PATH which means "any")
    if (getInfo->subactionPath != XR_NULL_PATH && getInfo->subactionPath != g_leftHandPath)
        return result;

    float velocity = ReadTreadmillVelocity();
    if (velocity == 0.0f) return result;

    BOOL shouldInject = FALSE;
    EnterCriticalSection(&g_cs);
    {
        uintptr_t key = (uintptr_t)getInfo->action;
        if (ContainsAction(g_tracked.vec2f, g_tracked.vec2fCount, key)) {
            shouldInject = TRUE;
        } else if (!g_tracked.bindingsReceived) {
            shouldInject = TRUE;   // fallback: inject all left-hand
        }
    }
    LeaveCriticalSection(&g_cs);

    if (shouldInject) {
        state->currentState.y += velocity;
        if (state->currentState.y >  1.0f) state->currentState.y =  1.0f;
        if (state->currentState.y < -1.0f) state->currentState.y = -1.0f;
        state->isActive = XR_TRUE;
        state->changedSinceLastSync = XR_TRUE;
    }

    return result;
}

// ─── Intercepted: xrGetActionStateFloat ─────────────────────────

static XrResult XRAPI_CALL
TreadmillLayer_xrGetActionStateFloat(
    XrSession session,
    const XrActionStateGetInfo* getInfo,
    XrActionStateFloat* state)
{
    XrResult result = g_xrGetActionStateFloat(session, getInfo, state);
    if (XR_FAILED(result)) return result;

    // Only inject on left hand subaction (or XR_NULL_PATH which means "any")
    if (getInfo->subactionPath != XR_NULL_PATH && getInfo->subactionPath != g_leftHandPath)
        return result;

    float velocity = ReadTreadmillVelocity();
    if (velocity == 0.0f) return result;

    BOOL shouldInject = FALSE;
    EnterCriticalSection(&g_cs);
    {
        uintptr_t key = (uintptr_t)getInfo->action;
        shouldInject = ContainsAction(g_tracked.floatY, g_tracked.floatYCount, key);
    }
    LeaveCriticalSection(&g_cs);

    if (shouldInject) {
        state->currentState += velocity;
        if (state->currentState >  1.0f) state->currentState =  1.0f;
        if (state->currentState < -1.0f) state->currentState = -1.0f;
        state->isActive = XR_TRUE;
        state->changedSinceLastSync = XR_TRUE;
    }

    return result;
}

// ─── Intercepted: xrDestroyInstance ─────────────────────────────

static XrResult XRAPI_CALL
TreadmillLayer_xrDestroyInstance(XrInstance instance)
{
    Log("xrDestroyInstance");
    CloseSharedMemory();

    EnterCriticalSection(&g_cs);
    memset(&g_tracked, 0, sizeof(g_tracked));
    LeaveCriticalSection(&g_cs);

    g_instance = XR_NULL_HANDLE;
    XrResult r = g_xrDestroyInstance(instance);
    LogClose();
    return r;
}

// ─── GetInstanceProcAddr (layer dispatch) ───────────────────────

static XrResult XRAPI_CALL
TreadmillLayer_xrGetInstanceProcAddr(
    XrInstance instance,
    const char* name,
    PFN_xrVoidFunction* function)
{
    if (strcmp(name, "xrGetInstanceProcAddr") == 0) {
        *function = (PFN_xrVoidFunction)TreadmillLayer_xrGetInstanceProcAddr;
        return XR_SUCCESS;
    }
    if (strcmp(name, "xrDestroyInstance") == 0) {
        *function = (PFN_xrVoidFunction)TreadmillLayer_xrDestroyInstance;
        return XR_SUCCESS;
    }
    if (strcmp(name, "xrSuggestInteractionProfileBindings") == 0) {
        *function = (PFN_xrVoidFunction)TreadmillLayer_xrSuggestInteractionProfileBindings;
        return XR_SUCCESS;
    }
    if (strcmp(name, "xrGetActionStateVector2f") == 0) {
        *function = (PFN_xrVoidFunction)TreadmillLayer_xrGetActionStateVector2f;
        return XR_SUCCESS;
    }
    if (strcmp(name, "xrGetActionStateFloat") == 0) {
        *function = (PFN_xrVoidFunction)TreadmillLayer_xrGetActionStateFloat;
        return XR_SUCCESS;
    }

    return g_nextGetInstanceProcAddr(instance, name, function);
}

// ─── CreateApiLayerInstance (loader chain) ───────────────────────

static XrResult XRAPI_CALL
TreadmillLayer_xrCreateApiLayerInstance(
    const XrInstanceCreateInfo* info,
    const XrApiLayerCreateInfo* layerInfo,
    XrInstance* instance)
{
    Log("xrCreateApiLayerInstance entered");

    // Grab next pointers from chain
    XrApiLayerNextInfo* nextInfo = layerInfo->nextInfo;
    if (!nextInfo) {
        Log("  ERROR: nextInfo is NULL");
        return XR_ERROR_INITIALIZATION_FAILED;
    }

    PFN_xrGetInstanceProcAddr       nextGIPA    = nextInfo->nextGetInstanceProcAddr;
    PFN_xrCreateApiLayerInstance    nextCreate  = nextInfo->nextCreateApiLayerInstance;

    if (!nextGIPA || !nextCreate) {
        Log("  ERROR: next function pointers are NULL");
        return XR_ERROR_INITIALIZATION_FAILED;
    }

    // Build modified layer info for the next layer
    XrApiLayerCreateInfo nextLayerInfo = *layerInfo;
    nextLayerInfo.nextInfo = nextInfo->next;

    Log("  Chaining to next layer/runtime...");
    XrResult result = nextCreate(info, &nextLayerInfo, instance);
    if (XR_FAILED(result)) {
        char buf[64];
        sprintf_s(buf, "  Chain returned error: %d", (int)result);
        Log(buf);
        return result;
    }

    Log("  Instance created successfully");

    g_instance               = *instance;
    g_nextGetInstanceProcAddr = nextGIPA;

    // Resolve chained function pointers
    PFN_xrVoidFunction pfn = NULL;

    g_nextGetInstanceProcAddr(*instance, "xrDestroyInstance", &pfn);
    g_xrDestroyInstance = (PFN_xrDestroyInstance)pfn;

    g_nextGetInstanceProcAddr(*instance, "xrPathToString", &pfn);
    g_xrPathToString = (PFN_xrPathToString)pfn;

    g_nextGetInstanceProcAddr(*instance, "xrStringToPath", &pfn);
    g_xrStringToPath = (PFN_xrStringToPath)pfn;

    // Resolve left hand path for subaction filtering
    if (g_xrStringToPath) {
        g_xrStringToPath(*instance, "/user/hand/left", &g_leftHandPath);
        char buf[64];
        sprintf_s(buf, "  Left hand path resolved: %llu", (unsigned long long)g_leftHandPath);
        Log(buf);
    }

    g_nextGetInstanceProcAddr(*instance, "xrSuggestInteractionProfileBindings", &pfn);
    g_xrSuggestInteractionProfileBindings = (PFN_xrSuggestInteractionProfileBindings)pfn;

    g_nextGetInstanceProcAddr(*instance, "xrGetActionStateVector2f", &pfn);
    g_xrGetActionStateVector2f = (PFN_xrGetActionStateVector2f)pfn;

    g_nextGetInstanceProcAddr(*instance, "xrGetActionStateFloat", &pfn);
    g_xrGetActionStateFloat = (PFN_xrGetActionStateFloat)pfn;

    Log("  Function pointers resolved");

    OpenSharedMemory();

    Log("  Layer initialization complete");
    return XR_SUCCESS;
}

// ─── Loader Negotiation (exported entry point) ──────────────────

extern "C" XR_LAYER_EXPORT XrResult XRAPI_CALL
xrNegotiateLoaderApiLayerInterface(
    const XrNegotiateLoaderInfo*     loaderInfo,
    const char*                      layerName,
    XrNegotiateApiLayerRequest*      apiLayerRequest)
{
    LogOpen();
    Log("=== Treadmill OpenXR Layer loaded ===");

    if (!loaderInfo || !layerName || !apiLayerRequest) {
        Log("ERROR: null parameter");
        return XR_ERROR_INITIALIZATION_FAILED;
    }

    char buf[384];
    sprintf_s(buf, "Loader info: structType=%d minIface=%u maxIface=%u",
              (int)loaderInfo->structType,
              loaderInfo->minInterfaceVersion,
              loaderInfo->maxInterfaceVersion);
    Log(buf);

    if (loaderInfo->structType != XR_LOADER_INTERFACE_STRUCT_LOADER_INFO) {
        Log("ERROR: wrong structType");
        return XR_ERROR_INITIALIZATION_FAILED;
    }

    if (loaderInfo->minInterfaceVersion > 1 || loaderInfo->maxInterfaceVersion < 1) {
        Log("ERROR: interface version mismatch");
        return XR_ERROR_INITIALIZATION_FAILED;
    }

    EnsureCritSec();

    apiLayerRequest->layerInterfaceVersion  = 1;
    apiLayerRequest->layerApiVersion        = XR_CURRENT_API_VERSION;
    apiLayerRequest->getInstanceProcAddr    = TreadmillLayer_xrGetInstanceProcAddr;
    apiLayerRequest->createApiLayerInstance = TreadmillLayer_xrCreateApiLayerInstance;

    sprintf_s(buf, "Negotiation OK for layer '%s'", layerName);
    Log(buf);

    return XR_SUCCESS;
}

// ─── DllMain (minimal — just ensure we don't do anything unsafe) ─

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID lpReserved)
{
    (void)hModule; (void)lpReserved;
    switch (reason) {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule);
        break;
    case DLL_PROCESS_DETACH:
        if (g_csInitialized) {
            DeleteCriticalSection(&g_cs);
            g_csInitialized = FALSE;
        }
        LogClose();
        break;
    }
    return TRUE;
}
