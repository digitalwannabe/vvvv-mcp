using System.Collections;
using System.Reflection;
using VL.Core;
using VL.Model;

namespace VL.MCP;

/// <summary>
/// Sets a pin's default value on the LIVE vvvv model via the editor's model API —
/// undo-integrated, no file reload flash. Uses the MODEL-based edit path
/// (DevEnvHost.CurrentSolution → GetDescendent → replace pin with new CompileTimeValue →
/// ModelExtensions.ReplaceDescendent + MakeCurrent), which works for ANY open document,
/// not just the active canvas (unlike SessionNodes.CurrentSolution.SetPinValue, which is
/// scoped to the active canvas's recorder).
///
/// Template: prt-prt/VL.Agent's pad-value path. Reflection over VL.Lang (no compile-time
/// ref for DevEnvHost/ModelExtensions); direct call for SolutionUpdateKind (VL.Core).
/// Runs on the vvvv main thread via AppHost's SynchronizationContext.
/// </summary>
internal class LivePinWriter
{
    private static readonly SolutionUpdateKind SetPinUpdateKind =
        SolutionUpdateKind.CommitToValue | SolutionUpdateKind.UpdateUIAndRuntime;

    // Cached reflection handles
    private static bool _discovered;
    private static string? _discoveryError;
    private static object? _devEnvHost;
    private static PropertyInfo? _currentSolutionProp;
    private static MethodInfo? _getDescendent;
    private static MethodInfo? _replaceDescendent;
    private static MethodInfo? _makeCurrent;
    private static MethodInfo? _compileTimeValueFrom;

    /// <summary>
    /// Sets a pin default value live. elementId is the NODE's element id (pin by name).
    /// documentId resolved from filePath by the endpoint when only that is given.
    /// </summary>
    public async Task<object> SetPinValueAsync(
        string documentId, string elementId, string pinName, string value, string? typeHint,
        AppHost? appHost)
    {
        object parsed;
        try { parsed = ParseValue(value, typeHint); }
        catch (Exception ex) { return new { success = false, error = $"could not parse value '{value}': {ex.Message}" }; }

        var syncCtx = appHost?.SynchronizationContext;
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Work()
        {
            try
            {
                if (!Discover())
                {
                    tcs.SetResult(new { success = false, error = $"editor API discovery failed: {_discoveryError}" });
                    return;
                }

                var solution = _currentSolutionProp!.GetValue(_devEnvHost);
                if (solution is null)
                {
                    tcs.SetResult(new { success = false, error = "no current model solution" });
                    return;
                }

                var uid = new UniqueId(documentId, elementId, 0);

                // Find the node element in the model solution (works for ANY open document)
                var element = _getDescendent!.Invoke(solution, new object[] { uid });
                if (element is null)
                {
                    tcs.SetResult(new { success = false, error = $"element '{elementId}' not found in the current solution (is the document open?)" });
                    return;
                }

                // Find the pin element by name
                object? pin = null;
                if (ReadMember(element, "Pins") is IEnumerable pins)
                {
                    foreach (var p in pins)
                    {
                        var name = ReadMember(p!, "Name")?.ToString();
                        if (string.Equals(name, pinName, StringComparison.OrdinalIgnoreCase)) { pin = p; break; }
                    }
                }
                if (pin is null)
                {
                    tcs.SetResult(new { success = false, error = $"pin '{pinName}' not found on {element.GetType().Name} '{ReadMember(element, "Name")}'" });
                    return;
                }

                var before = ReadMember(pin, "SerializedValue") ?? ReadMember(pin, "Value");

                // Build the new CompileTimeValue and an updated pin carrying it
                var withValue = FindWithValue(pin);
                if (withValue is null)
                {
                    tcs.SetResult(new { success = false, error = $"no WithValue method on pin type {pin.GetType().Name}" });
                    return;
                }
                var clrType = parsed.GetType();
                var ctv = _compileTimeValueFrom!.Invoke(null, new object?[] { parsed, true, uid, clrType });
                var newPin = withValue.Invoke(pin, new object?[] { ctv });

                // Replace in the solution and commit (undo-integrated).
                // ReplaceDescendent<TContainer> is generic — close it with the solution's type.
                var replaceMethod = _replaceDescendent!;
                if (replaceMethod.ContainsGenericParameters)
                    replaceMethod = replaceMethod.MakeGenericMethod(solution.GetType());
                var nextSolution = replaceMethod.Invoke(null, new[] { solution, newPin });
                var canvas = ReadMember(pin, "ParentCanvas") ?? ReadMember(element, "ParentCanvas")
                          ?? ReadMember(solution, "Canvas");
                _makeCurrent!.Invoke(null, new[] { nextSolution, SetPinUpdateKind, canvas });

                var after = ReadMember(newPin, "SerializedValue") ?? ReadMember(newPin, "Value");

                tcs.SetResult(new
                {
                    success = true,
                    documentId,
                    elementId,
                    pinName,
                    value = parsed,
                    before = FormatValue(before),
                    after = FormatValue(after),
                    method = "ModelExtensions.ReplaceDescendent + MakeCurrent(CommitToValue | UpdateUIAndRuntime)"
                });
            }
            catch (Exception ex)
            {
                tcs.SetResult(new { success = false, error = ex.GetBaseException().Message });
            }
        }

        if (syncCtx is not null)
            syncCtx.Post(_ => Work(), null);
        else
            Work();

        return await tcs.Task ?? new { success = false, error = "unknown" };
    }

    // ── Discovery (reflection over VL.Lang, once) ─────────────────────────────

    private static bool Discover()
    {
        if (_discovered) return _getDescendent is not null;
        _discovered = true;
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            // DevEnvHost.Instance.CurrentSolution (VL.Lang)
            var devEnvType = assemblies.Select(a => a.GetType("VL.Lang.DevEnvHost")).FirstOrDefault(t => t is not null);
            _devEnvHost = devEnvType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (_devEnvHost is null) { _discoveryError = "DevEnvHost.Instance not found"; return false; }
            _currentSolutionProp = _devEnvHost.GetType().GetProperty("CurrentSolution");
            if (_currentSolutionProp is null) { _discoveryError = "DevEnvHost.CurrentSolution not found"; return false; }

            // Solution.GetDescendent(UniqueId)
            var solutionType = _currentSolutionProp.PropertyType;
            _getDescendent = solutionType.GetMethod("GetDescendent", new[] { typeof(UniqueId) });
            if (_getDescendent is null)
            {
                // Maybe on the runtime type instead of the declared type
                var runtimeSolution = _currentSolutionProp.GetValue(_devEnvHost);
                _getDescendent = runtimeSolution?.GetType().GetMethod("GetDescendent", new[] { typeof(UniqueId) });
            }
            if (_getDescendent is null) { _discoveryError = "Solution.GetDescendent not found"; return false; }

            // ModelExtensions.ReplaceDescendent + MakeCurrent (static, VL.Lang)
            var modelExt = assemblies.Select(a => a.GetType("VL.Model.ModelExtensions")).FirstOrDefault(t => t is not null);
            if (modelExt is null) { _discoveryError = "ModelExtensions not found"; return false; }
            _replaceDescendent = modelExt.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "ReplaceDescendent" && m.GetParameters().Length == 2);
            _makeCurrent = modelExt.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "MakeCurrent" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 3);
            if (_replaceDescendent is null || _makeCurrent is null) { _discoveryError = "ReplaceDescendent/MakeCurrent not found"; return false; }

            // CompileTimeValue.From(object, bool wrapNull, UniqueId, Type)
            var ctvType = assemblies.Select(a => a.GetType("VL.Model.CompileTimeValue")).FirstOrDefault(t => t is not null);
            _compileTimeValueFrom = ctvType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "From" && m.GetParameters().Length == 4);
            if (_compileTimeValueFrom is null) { _discoveryError = "CompileTimeValue.From not found"; return false; }
            return true;
        }
        catch (Exception ex)
        {
            _discoveryError = ex.Message;
            return false;
        }
    }

    // WithValue is found per-pin-type (DataHub.WithValue)
    private static MethodInfo? FindWithValue(object pin)
    {
        return pin.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "WithValue" && m.GetParameters().Length == 1);
    }

    private static object? ReadMember(object source, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
        var type = source.GetType();
        return type.GetProperty(name, flags)?.GetValue(source)
            ?? type.GetField(name, flags)?.GetValue(source);
    }

    private static string FormatValue(object? value)
    {
        if (value is null) return "<null>";
        // Unwrap CompileTimeValue → its boxed inner value for a readable display
        if (value.GetType().Name == "CompileTimeValue")
        {
            var inner = ReadMember(value, "Value") ?? ReadMember(value, "Object")
                     ?? ReadMember(value, "ValueOrDefault");
            if (inner is not null) return FormatValue(inner);
            return "<CompileTimeValue>";
        }
        return value switch
        {
            string s => s,
            IEnumerable e when value is not string => string.Join(",", e.Cast<object>().Select(o => o?.ToString() ?? "<null>")),
            _ => value.ToString() ?? ""
        };
    }

    private static object ParseValue(string value, string? typeHint)
    {
        var v = value.Trim();
        var hint = (typeHint ?? "").Trim().ToLowerInvariant();
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        return hint switch
        {
            "bool" or "boolean"          => v is "true" or "1" or "True",
            "int" or "int32" or "integer" or "integer32" => int.Parse(v, inv),
            "float" or "float32" or "single" => float.Parse(v, inv),
            "double" or "float64"        => double.Parse(v, inv),
            "string"                     => v,
            _ => SmartParse(v, inv)
        };
    }

    private static object SmartParse(string v, System.Globalization.CultureInfo inv)
    {
        if (bool.TryParse(v, out var b)) return b;
        if (int.TryParse(v, System.Globalization.NumberStyles.Integer, inv, out var i)) return i;
        if (float.TryParse(v, System.Globalization.NumberStyles.Float, inv, out var f)) return f;
        return v;
    }
}
