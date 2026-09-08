using System;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.Services;

public static class WasmInterop
{
    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger(typeof(WasmInterop).FullName ?? nameof(WasmInterop));

    public static string GetCurrentOrigin()
    {
#if __WASM__
        try
        {
            string origin = Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                "window.CantusInterop ? window.CantusInterop.getOrigin() : window.location.origin");
            if (!string.IsNullOrWhiteSpace(origin) && origin != "null" && origin != "undefined")
            {
                return origin.Trim().TrimEnd('/');
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCurrentOrigin failed");
        }
#endif
        return string.Empty;
    }

    public static void NavigateTo(string url)
    {
#if __WASM__
        try
        {
            Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                $"window.CantusInterop ? window.CantusInterop.navigateTo('{url}') : (window.location.href = '{url}')");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NavigateTo failed");
        }
#endif
    }

    public static string GetAuthQueryParameter()
    {
#if __WASM__
        try
        {
            string token = Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                "window.CantusInterop && window.CantusInterop.getAuthQuery ? window.CantusInterop.getAuthQuery() : (new URLSearchParams(window.location.search).get('auth') || '')");
            if (!string.IsNullOrWhiteSpace(token) && token != "null" && token != "undefined")
            {
                return token.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetAuthQueryParameter failed");
        }
#endif
        return string.Empty;
    }

    public static void CleanAuthQuery()
    {
#if __WASM__
        try
        {
            Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                "window.CantusInterop && window.CantusInterop.cleanAuthQuery && window.CantusInterop.cleanAuthQuery()");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CleanAuthQuery failed");
        }
#endif
    }

    public static bool IsDocumentVisible()
    {
#if __WASM__
        try
        {
            string val = Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                "window.CantusInterop && window.CantusInterop.isDocumentVisible ? (window.CantusInterop.isDocumentVisible() ? 'true' : 'false') : 'true'");
            return bool.TryParse(val, out bool isVis) ? isVis : true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IsDocumentVisible check failed");
            return true;
        }
#else
        return true;
#endif
    }
}
