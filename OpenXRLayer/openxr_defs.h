#pragma once
// ═══════════════════════════════════════════════════════════════════
// Minimal OpenXR type definitions for the Treadmill Driver API Layer.
// Based on the OpenXR 1.0 specification (Khronos).
// Only includes types required by this layer — not a complete header.
// ═══════════════════════════════════════════════════════════════════

#include <stdint.h>
#include <stddef.h>

#ifdef _WIN32
#define XR_LAYER_EXPORT __declspec(dllexport)
#else
#define XR_LAYER_EXPORT __attribute__((visibility("default")))
#endif

#define XRAPI_CALL  __stdcall
#define XRAPI_PTR   __stdcall

// ─── Fundamental Types ──────────────────────────────────────────

typedef int32_t   XrResult;
typedef uint64_t  XrVersion;
typedef uint64_t  XrPath;
typedef uint32_t  XrBool32;
typedef int64_t   XrTime;

#define XR_DEFINE_HANDLE(name) typedef struct name##_T* name;

XR_DEFINE_HANDLE(XrInstance)
XR_DEFINE_HANDLE(XrSession)
XR_DEFINE_HANDLE(XrAction)
XR_DEFINE_HANDLE(XrActionSet)

// ─── Constants ──────────────────────────────────────────────────

#define XR_TRUE                         1
#define XR_FALSE                        0
#define XR_NULL_PATH                    0
#define XR_NULL_HANDLE                  nullptr
#define XR_SUCCESS                      0
#define XR_ERROR_FUNCTION_UNSUPPORTED   (-1)
#define XR_ERROR_HANDLE_INVALID         (-12)
#define XR_ERROR_INITIALIZATION_FAILED  (-38)
#define XR_MAX_API_LAYER_NAME_SIZE      256

#define XR_SUCCEEDED(result) ((result) >= 0)
#define XR_FAILED(result)    ((result) < 0)

#define XR_MAKE_VERSION(major, minor, patch) \
    ((((uint64_t)(major) & 0xffffULL) << 48) | \
     (((uint64_t)(minor) & 0xffffULL) << 32) | \
     ((uint64_t)(patch) & 0xffffffffULL))

#define XR_CURRENT_API_VERSION XR_MAKE_VERSION(1, 0, 0)

// ─── XrStructureType (partial — values from openxr.h) ───────────

typedef enum XrStructureType {
    XR_TYPE_UNKNOWN                                = 0,
    XR_TYPE_API_LAYER_PROPERTIES                   = 1,
    XR_TYPE_EXTENSION_PROPERTIES                   = 2,
    XR_TYPE_INSTANCE_CREATE_INFO                   = 3,
    XR_TYPE_ACTION_STATE_BOOLEAN                   = 23,
    XR_TYPE_ACTION_STATE_FLOAT                     = 24,
    XR_TYPE_ACTION_STATE_VECTOR2F                  = 25,
    XR_TYPE_ACTION_STATE_POSE                      = 27,
    XR_TYPE_ACTION_STATE_GET_INFO                  = 44,
    XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING  = 51,
} XrStructureType;

// ─── Core Structures ────────────────────────────────────────────

typedef struct XrVector2f {
    float x;
    float y;
} XrVector2f;

typedef struct XrApplicationInfo {
    char        applicationName[128];
    uint32_t    applicationVersion;
    char        engineName[128];
    uint32_t    engineVersion;
    XrVersion   apiVersion;
} XrApplicationInfo;

typedef struct XrInstanceCreateInfo {
    XrStructureType         type;
    const void*             next;
    uint64_t                createFlags;
    XrApplicationInfo       applicationInfo;
    uint32_t                enabledApiLayerCount;
    const char* const*      enabledApiLayerNames;
    uint32_t                enabledExtensionCount;
    const char* const*      enabledExtensionNames;
} XrInstanceCreateInfo;

typedef struct XrActionStateGetInfo {
    XrStructureType     type;
    const void*         next;
    XrAction            action;
    XrPath              subactionPath;
} XrActionStateGetInfo;

typedef struct XrActionStateFloat {
    XrStructureType     type;
    void*               next;
    float               currentState;
    XrBool32            changedSinceLastSync;
    XrTime              lastChangeTime;
    XrBool32            isActive;
} XrActionStateFloat;

typedef struct XrActionStateVector2f {
    XrStructureType     type;
    void*               next;
    XrVector2f          currentState;
    XrBool32            changedSinceLastSync;
    XrTime              lastChangeTime;
    XrBool32            isActive;
} XrActionStateVector2f;

typedef struct XrActionSuggestedBinding {
    XrAction    action;
    XrPath      binding;
} XrActionSuggestedBinding;

typedef struct XrInteractionProfileSuggestedBinding {
    XrStructureType                     type;
    const void*                         next;
    XrPath                              interactionProfile;
    uint32_t                            countSuggestedBindings;
    const XrActionSuggestedBinding*     suggestedBindings;
} XrInteractionProfileSuggestedBinding;

// ─── Function Pointer Types ─────────────────────────────────────

typedef XrResult(XRAPI_PTR* PFN_xrVoidFunction)(void);

typedef XrResult(XRAPI_PTR* PFN_xrGetInstanceProcAddr)(
    XrInstance instance, const char* name, PFN_xrVoidFunction* function);

typedef XrResult(XRAPI_PTR* PFN_xrDestroyInstance)(XrInstance instance);

typedef XrResult(XRAPI_PTR* PFN_xrPathToString)(
    XrInstance instance, XrPath path,
    uint32_t bufferCapacityInput, uint32_t* bufferCountOutput, char* buffer);

typedef XrResult(XRAPI_PTR* PFN_xrSuggestInteractionProfileBindings)(
    XrInstance instance,
    const XrInteractionProfileSuggestedBinding* suggestedBindings);

typedef XrResult(XRAPI_PTR* PFN_xrGetActionStateFloat)(
    XrSession session,
    const XrActionStateGetInfo* getInfo,
    XrActionStateFloat* state);

typedef XrResult(XRAPI_PTR* PFN_xrGetActionStateVector2f)(
    XrSession session,
    const XrActionStateGetInfo* getInfo,
    XrActionStateVector2f* state);

// ─── Loader Negotiation Types ───────────────────────────────────

typedef enum XrLoaderInterfaceStructs {
    XR_LOADER_INTERFACE_STRUCT_UNINTIALIZED          = 0,
    XR_LOADER_INTERFACE_STRUCT_LOADER_INFO           = 1,
    XR_LOADER_INTERFACE_STRUCT_API_LAYER_REQUEST     = 2,
    XR_LOADER_INTERFACE_STRUCT_RUNTIME_REQUEST       = 3,
    XR_LOADER_INTERFACE_STRUCT_API_LAYER_CREATE_INFO = 4,
    XR_LOADER_INTERFACE_STRUCT_API_LAYER_NEXT_INFO   = 5,
} XrLoaderInterfaceStructs;

typedef struct XrNegotiateLoaderInfo {
    XrLoaderInterfaceStructs    structType;
    uint32_t                    structVersion;
    size_t                      structSize;
    uint32_t                    minInterfaceVersion;
    uint32_t                    maxInterfaceVersion;
    XrVersion                   minApiVersion;
    XrVersion                   maxApiVersion;
} XrNegotiateLoaderInfo;

// Forward declarations for circular references
struct XrApiLayerCreateInfo;
struct XrApiLayerNextInfo;

typedef XrResult(XRAPI_PTR* PFN_xrCreateApiLayerInstance)(
    const XrInstanceCreateInfo* info,
    const struct XrApiLayerCreateInfo* layerInfo,
    XrInstance* instance);

typedef struct XrApiLayerNextInfo {
    XrLoaderInterfaceStructs        structType;
    uint32_t                        structVersion;
    size_t                          structSize;
    char                            layerName[XR_MAX_API_LAYER_NAME_SIZE];
    PFN_xrGetInstanceProcAddr       nextGetInstanceProcAddr;
    PFN_xrCreateApiLayerInstance    nextCreateApiLayerInstance;
    struct XrApiLayerNextInfo*      next;
} XrApiLayerNextInfo;

typedef struct XrApiLayerCreateInfo {
    XrLoaderInterfaceStructs    structType;
    uint32_t                    structVersion;
    size_t                      structSize;
    void*                       loaderInstance;
    char                        nextInfoName[XR_MAX_API_LAYER_NAME_SIZE];
    PFN_xrGetInstanceProcAddr   nextGetInstanceProcAddr;
    XrApiLayerNextInfo*         nextInfo;
} XrApiLayerCreateInfo;

typedef struct XrNegotiateApiLayerRequest {
    XrLoaderInterfaceStructs        structType;
    uint32_t                        structVersion;
    size_t                          structSize;
    uint32_t                        layerInterfaceVersion;
    XrVersion                       layerApiVersion;
    PFN_xrGetInstanceProcAddr       getInstanceProcAddr;
    PFN_xrCreateApiLayerInstance    createApiLayerInstance;
} XrNegotiateApiLayerRequest;
