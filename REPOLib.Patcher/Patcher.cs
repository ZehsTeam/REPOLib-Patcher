using BepInEx;
using Mono.Cecil;
using System.Collections.Generic;
using System.IO;

namespace com.github.zehsteam.REPOLib_Patcher;

internal static class Patcher
{
    public static IEnumerable<string> TargetDLLs { get; } = [];

    private static IEnumerable<string> GetAllPluginDLLs()
    {
        return Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories);
    }

    public static void Initialize()
    {

    }

    private static void CheckPlugin(string filePath)
    {
        
    }

    public static void Patch(AssemblyDefinition _)
    {

    }
}
