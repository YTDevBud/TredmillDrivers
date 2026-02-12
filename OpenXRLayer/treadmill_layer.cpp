// ═══════════════════════════════════════════════════════════════════
// Treadmill Driver — OpenXR Implicit API Layer
// ═══════════════════════════════════════════════════════════════════
// Intercepts OpenXR input calls and injects treadmill velocity
// into the left thumbstick Y axis. Reads velocity from a named
// memory-mapped file written by the WPF companion app.
// ═══════════════════════════════════════════════════════════════════

#include "openxr_defs.h"

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string.h>
#include <mutex>
#include <unordered_set>

// ─── Layer Identity ─────────────────────────────────────────────

#define LAYER_NAME "XR_APILAYER_TREADMILL_driver"

// ─── Shared Memory Protocol ────────────────────────────────────
// Must match the layout in TreadmillDriver/Services/SharedMemoryService.cs

#define SHARED_MEM_NAME     "TreadmillDriverVelocity"
#define SHARED_MEM_RETRY_MS 2000

#pragma pack(push, 1)
struct TreadmillSharedData {
    float       velocity;       // -1.0 to 1.0, normalised
    uint32_t    active;         // nonzero = WPF app is running
};
#pragma pack(pop)

// ─── Global State ───────────────────────────────────────────────

static XrInstance                   g_instance                          = XR_NULL_HANDLE;
static PFN_xrGetInstanceProcAddr    g_nextGetInstanceProcAddr           = nullptr;

// Chained function pointers (next layer / runtime)
static PFN_xrDestroyInstance                        g_xrDestroyInstance                     = nullptr;
static PFN_xrPathToString                           g_xrPathToString                        = nullptr;
static PFN_xrSuggestInteractionProfileBindings      g_xrSuggestInteractionProfileBindings   = nullptr;
static PFN_xrGetActionStateFloat                    g_xrGetActionStateFloat                 = nullptr;
static PFN_xrGetActionStateVector2f                 g_xrGetActionStateVector2f              = nullptr;

// Action tracking — which actions are left thumbstick?
static std::mutex                       g_actionMutex;
static std::unordered_set<uintptr_t>    g_leftThumbstickVector2fActions;
static std::unordered_set<uintptr_t>    g_leftThumbstickYFloatActions;
static bool                             g_bindingsReceived = false;

// Shared memory
static HANDLE                   g_sharedMemHandle       = NULL;
static TreadmillSharedData*     g_sharedData            = nullptr;
static ULONGLONG                g_lastSharedMemAttempt   = 0;

// ─── Shared Memory Helpers ──────────────────────────────────────

static void OpenSharedMemory()
{
    if (g_sharedMemHandle) return;

    g_sharedMemHandle = OpenFileMappingA(FILE_MAP_READ, FALSE, SHARED_MEM_NAME);
    if (g_sharedMemHandle) {
        g_sharedData = (TreadmillSharedData*)MapViewOfFile(
            g_sharedMemHandle, FILE_MAP_READ, 0, 0, sizeof(TreadmillSharedData));
    }
}

static void CloseSharedMemory()
{
    if (g_sharedData) {
        UnmapViewOfFile(g_sharedData);
        g_sharedData = nullptr;
    }
    if (g_sharedMemHandle) {
        CloseHandle(g_sharedMemHandle);
        g_sharedMemHandle = NULL;
    }
}

static float ReadTreadmillVelocity()
{
    // Lazy connect / reconnect with cooldown
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

// ─── Intercepted: xrSuggestInteractionProfileBindings ───────────
// Scans binding suggestions to identify which actions are bound
// to the left hand thumbstick so we only inject into those.

static XrResult XRAPI_CALL
TreadmillLayer_xrSuggestInteractionProfileBindings(
    XrInstance instance,
    const XrInteractionProfileSuggestedBinding* suggestedBindings)
{
    XrResult result = g_xrSuggestInteractionProfileBindings(instance, suggestedBindings);
    if (XR_FAILED(result)) return result;

    if (!g_xrPathToString) return result;

    std::lock_guard<std::mutex> lock(g_actionMutex);

    for (uint32_t i = 0; i < suggestedBindings->countSuggestedBindings; i++) {
        char pathStr[256] = {0};
        uint32_t pathLen = 0;
        XrResult pr = g_xrPathToString(
            instance,
            suggestedBindings->suggestedBindings[i].binding,
            sizeof(pathStr), &pathLen, pathStr);

        if (XR_FAILED(pr) || pathLen == 0) continue;

        bool isLeft       = strstr(pathStr, "/user/hand/left") != nullptr;
        bool isThumbstick = strstr(pathStr, "thumbstick")      != nullptr;

        if (isLeft && isThumbstick) {
            uintptr_t key = (uintptr_t)suggestedBindings->suggestedBindings[i].action;

            if (strstr(pathStr, "thumbstick/y")) {
                // Float Y-axis variant
                g_leftThumbstickYFloatActions.insert(key);
            } else if (!strstr(pathStr, "thumbstick/x")) {
                // Full 2D thumbstick (not X-only)
                g_leftThumbstickVector2fActions.insert(key);
            }
            g_bindingsReceived = true;
        }
    }

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

    float velocity = ReadTreadmillVelocity();
    if (velocity == 0.0f) return result;

    bool shouldInject = false;
    {
        std::lock_guard<std::mutex> lock(g_actionMutex);
        uintptr_t key = (uintptr_t)getInfo->action;

        if (g_leftThumbstickVector2fActions.count(key) > 0) {
            // Exact match — we know this is left thumbstick
            shouldInject = true;
        }
        else if (!g_bindingsReceived) {
            // Fallback: no bindings detected yet, inject into all vector2f
            shouldInject = true;
        }
    }

    if (shouldInject) {
        state->currentState.y += velocity;
        // Clamp
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

    float velocity = ReadTreadmillVelocity();
    if (velocity == 0.0f) return result;

    bool shouldInject = false;
    {
        std::lock_guard<std::mutex> lock(g_actionMutex);
        uintptr_t key = (uintptr_t)getInfo->action;
        shouldInject = g_leftThumbstickYFloatActions.count(key) > 0;
    }

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
    CloseSharedMemory();
    {
        std::lock_guard<std::mutex> lock(g_actionMutex);
        g_leftThumbstickVector2fActions.clear();
        g_leftThumbstickYFloatActions.clear();
        g_bindingsReceived = false;
    }
    g_instance = XR_NULL_HANDLE;
    return g_xrDestroyInstance(instance);
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

    // Everything else → chain to next
    return g_nextGetInstanceProcAddr(instance, name, function);
}

// ─── CreateApiLayerInstance (loader chain) ───────────────────────

static XrResult XRAPI_CALL
TreadmillLayer_xrCreateApiLayerInstance(
    const XrInstanceCreateInfo* info,
    const XrApiLayerCreateInfo* layerInfo,
    XrInstance* instance)
{
    // Grab next pointers from chain
    XrApiLayerNextInfo* nextInfo = layerInfo->nextInfo;
    PFN_xrGetInstanceProcAddr       nextGIPA    = nextInfo->nextGetInstanceProcAddr;
    PFN_xrCreateApiLayerInstance    nextCreate  = nextInfo->nextCreateApiLayerInstance;

    // Build modified layer info for the next layer
    XrApiLayerCreateInfo nextLayerInfo = *layerInfo;
    nextLayerInfo.nextInfo = nextInfo->next;

    // Chain instance creation to next layer / runtime
    XrResult result = nextCreate(info, &nextLayerInfo, instance);
    if (XR_FAILED(result)) return result;

    // Save instance and next-layer getInstanceProcAddr
    g_instance               = *instance;
    g_nextGetInstanceProcAddr = nextGIPA;

    // Resolve chained function pointers
    PFN_xrVoidFunction pfn = nullptr;

    g_nextGetInstanceProcAddr(*instance, "xrDestroyInstance", &pfn);
    g_xrDestroyInstance = (PFN_xrDestroyInstance)pfn;

    g_nextGetInstanceProcAddr(*instance, "xrPathToString", &pfn);
    g_xrPathToString = (PFN_xrPathToString)pfn;

    g_nextGetInstanceProcAddr(*instance, "xrSuggestInteractionProfileBindings", &pfn);
    g_xrSuggestInteractionProfileBindings = (PFN_xrSuggestInteractionProfileBindings)pfn;

    g_nextGetInstanceProcAddr(*instance, "xrGetActionStateVector2f", &pfn);
    g_xrGetActionStateVector2f = (PFN_xrGetActionStateVector2f)pfn;

    g_nextGetInstanceProcAddr(*instance, "xrGetActionStateFloat", &pfn);
    g_xrGetActionStateFloat = (PFN_xrGetActionStateFloat)pfn;

    // Attempt to open shared memory (may not exist yet)
    OpenSharedMemory();

    return XR_SUCCESS;
}

// ─── Loader Negotiation (exported entry point) ──────────────────

extern "C" XR_LAYER_EXPORT XrResult XRAPI_CALL
xrNegotiateLoaderApiLayerInterface(
    const XrNegotiateLoaderInfo*     loaderInfo,
    const char*                      layerName,
    XrNegotiateApiLayerRequest*      apiLayerRequest)
{
    if (!loaderInfo || !layerName || !apiLayerRequest)
        return XR_ERROR_INITIALIZATION_FAILED;

    if (loaderInfo->structType != XR_LOADER_INTERFACE_STRUCT_LOADER_INFO)
        return XR_ERROR_INITIALIZATION_FAILED;

    // We implement interface version 1
    if (loaderInfo->minInterfaceVersion > 1 || loaderInfo->maxInterfaceVersion < 1)
        return XR_ERROR_INITIALIZATION_FAILED;

    apiLayerRequest->layerInterfaceVersion  = 1;
    apiLayerRequest->layerApiVersion        = XR_CURRENT_API_VERSION;
    apiLayerRequest->getInstanceProcAddr    = TreadmillLayer_xrGetInstanceProcAddr;
    apiLayerRequest->createApiLayerInstance = TreadmillLayer_xrCreateApiLayerInstance;

    return XR_SUCCESS;
}
