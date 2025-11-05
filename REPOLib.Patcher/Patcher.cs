using BepInEx;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.IO;

namespace REPOLib.Patcher;

internal static class Patcher
{
    // Enable this to simulate patching without modifying any files
    private const bool DRY_RUN = false;

    public static IEnumerable<string> TargetDLLs { get; } = [];

    private static IEnumerable<string> GetAllPluginDLLs()
    {
        return Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories);
    }

    public static void Initialize()
    {
        Logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME} initialized! (Dry run = {DRY_RUN})");

        foreach (var filePath in GetAllPluginDLLs())
        {
            try
            {
                CheckPlugin(filePath);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to check plugin \"{filePath}\": {ex}");
            }
        }

        Logger.LogInfo("Finished scanning all plugins.");
    }

    private static void CheckPlugin(string filePath)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(filePath, new ReaderParameters { ReadWrite = true });

        if (!IsAllowedAssembly(assembly))
            return;

        if (!IsAssemblyDependentOn(assembly, "REPOLib"))
            return;

        bool modified = false;

        var module = assembly.MainModule;

        foreach (var type in module.Types)
        {
            CheckType(module, type, ref modified);
        }

        if (modified)
        {
            if (DRY_RUN)
            {
                Logger.LogInfo($"[Dry Run] {Path.GetFileName(filePath)} would be patched.");
            }
            else
            {
                assembly.Write();
                Logger.LogInfo($"Patched and saved: {Path.GetFileName(filePath)}");
            }
        }
    }

    private static void CheckType(ModuleDefinition module, TypeDefinition type, ref bool modified)
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
                continue;

            CheckMethod(module, type, method, ref modified);
        }
    }

    private static void CheckMethod(ModuleDefinition module, TypeDefinition type, MethodDefinition method, ref bool modified)
    {
        var processor = method.Body.GetILProcessor();
        var instructions = method.Body.Instructions;

        for (int i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            if (instr.OpCode.Code != Code.Call &&
                instr.OpCode.Code != Code.Callvirt)
                continue;

            if (instr.Operand is not MethodReference calledMethod)
                continue;

            // Find old REPOLib calls
            if (IsOldRepolibCall(calledMethod))
            {
                Logger.LogInfo($"[{module.Name}] Found old call: {calledMethod.FullName}");

                var newMethod = GetNewApiMethod(module, calledMethod);
                if (newMethod != null)
                {
                    Logger.LogInfo($"Would replace with: {newMethod.FullName}");
                    modified = true;

                    if (!DRY_RUN)
                    {
                        instr.Operand = newMethod;

                        // Insert POP since new method returns PrefabRef
                        var next = (i + 1 < instructions.Count) ? instructions[i + 1] : null;
                        if (next == null || next.OpCode.Code != Code.Pop)
                        {
                            var popInstr = processor.Create(OpCodes.Pop);
                            processor.InsertAfter(instr, popInstr);
                            i++;
                        }
                    }
                }
            }
        }
    }

    private static bool IsAllowedAssembly(AssemblyDefinition assembly)
    {
        return assembly.Name.Name != "REPOLib";
    }

    private static bool IsAssemblyDependentOn(AssemblyDefinition assembly, string dependencyName)
    {
        foreach (var reference in assembly.MainModule.AssemblyReferences)
        {
            if (reference.Name == dependencyName)
                return true;
        }
        return false;
    }

    private static bool IsOldRepolibCall(MethodReference method)
    {
        if (method.DeclaringType == null)
            return false;

        var declaringType = method.DeclaringType.FullName;
        var name = method.Name;

        // Must be REPOLib.Modules.* methods
        if (!declaringType.StartsWith("REPOLib.Modules."))
            return false;

        // Old methods all returned void
        if (method.ReturnType.FullName != "System.Void")
            return false;

        // RegisterItem(...) – any overload
        if (declaringType == "REPOLib.Modules.Items" && name == "RegisterItem")
            return true;

        // RegisterValuable(...) – any overload
        if (declaringType == "REPOLib.Modules.Valuables" && name == "RegisterValuable")
            return true;

        return false;
    }

    private static MethodReference? GetNewApiMethod(ModuleDefinition targetModule, MethodReference oldMethod)
    {
        // These are the old signatures we’re replacing (minus the return type)
        var replacements = new Dictionary<string, string>
        {
            // Valuables
            { "REPOLib.Modules.Valuables::RegisterValuable(UnityEngine.GameObject)",
              "REPOLib.Modules.Valuables::RegisterValuable(UnityEngine.GameObject)" },

            { "REPOLib.Modules.Valuables::RegisterValuable(UnityEngine.GameObject,System.Collections.Generic.List`1<LevelValuables>)",
              "REPOLib.Modules.Valuables::RegisterValuable(UnityEngine.GameObject,System.Collections.Generic.List`1<LevelValuables>)" },

            { "REPOLib.Modules.Valuables::RegisterValuable(UnityEngine.GameObject,System.Collections.Generic.List`1<System.String>)",
              "REPOLib.Modules.Valuables::RegisterValuable(UnityEngine.GameObject,System.Collections.Generic.List`1<System.String>)" },

            { "REPOLib.Modules.Valuables::RegisterValuable(ValuableObject)",
              "REPOLib.Modules.Valuables::RegisterValuable(ValuableObject)" },

            { "REPOLib.Modules.Valuables::RegisterValuable(ValuableObject,System.Collections.Generic.List`1<LevelValuables>)",
              "REPOLib.Modules.Valuables::RegisterValuable(ValuableObject,System.Collections.Generic.List`1<LevelValuables>)" },

            { "REPOLib.Modules.Valuables::RegisterValuable(ValuableObject,System.Collections.Generic.List`1<System.String>)",
              "REPOLib.Modules.Valuables::RegisterValuable(ValuableObject,System.Collections.Generic.List`1<System.String>)" },

            // Items
            { "REPOLib.Modules.Items::RegisterItem(ItemAttributes)",
              "REPOLib.Modules.Items::RegisterItem(ItemAttributes)" },
        };

        var key = oldMethod.FullName.Substring(oldMethod.FullName.IndexOf(' ') + 1);

        if (!replacements.TryGetValue(key, out var newSig))
            return null;

        var repolibPath = Path.Combine(Paths.PluginPath, "Zehs-REPOLib", "REPOLib.dll");

        if (!File.Exists(repolibPath))
        {
            Logger.LogWarning("REPOLib.dll not found; cannot resolve new API methods.");
            return null;
        }
        
        using var repolib = AssemblyDefinition.ReadAssembly(repolibPath);

        foreach (var type in repolib.MainModule.Types)
        {
            foreach (var method in type.Methods)
            {
                string methodSignature = method.FullName.Substring(method.FullName.IndexOf(' ') + 1);

                if (methodSignature.StartsWith("REPOLib.Modules.Items"))
                {
                    Logger.LogInfo($"");
                    Logger.LogInfo($"newSig: \"{newSig}\"");
                    Logger.LogInfo($"REPOLib method: \"{method.FullName}\"");
                    Logger.LogInfo($"ReturnType: \"{method.ReturnType.FullName}\"");
                    Logger.LogInfo($"");
                }

                if (methodSignature == newSig)
                {
                    if (method.ReturnType.FullName.EndsWith("PrefabRef"))
                    {
                        return targetModule.ImportReference(method);
                    }
                }
            }
        }

        return null;
    }

    private static string GetSignatureNoReturn(this MethodReference method)
    {
        return method.FullName.Substring(method.FullName.IndexOf(' ') + 1);
    }

    public static void Patch(AssemblyDefinition _) { }
}
