using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Core;
using Terraria.ModLoader.Exceptions;
using Terraria.ModLoader.UI;

namespace BuildWithDotnetBuild;

public class BuildWithDotnetBuild : Mod
{
    public static BuildWithDotnetBuild Instance => ModContent.GetInstance<BuildWithDotnetBuild>();

    public override void Load()
    {
        MonoModHooks.Add(typeof(ModCompile).GetMethod("Build",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [typeof(ModCompile.BuildingMod)],
            null)!, Build);
    }

    private static void Build(ModCompile self, ModCompile.BuildingMod mod)
    {
        try
        {
            self.status.SetStatus(Language.GetTextValue("tModLoader.Building", mod.Name));
            if (ModLoader.TryGetMod(mod.Name, out var existingMod))
            {
                existingMod.Close();
            }

            var errorLogPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Logging.LogPath))!,
                "dotnetBuildErrorLog.log");
            var modDirectoryInfo = new DirectoryInfo(mod.path);

            var dotnetPath = UIModSources.GetSystemDotnetPath();
            var psi = new ProcessStartInfo
            {
                FileName = dotnetPath,
                Arguments = BuildArguments(
                    "build",
                    "-c:Release",
                    "/flp:v=q;logfile=\"" + errorLogPath + "\";errorsonly",
                    mod.path + "\\" + modDirectoryInfo.Name + ".csproj"
                ),
                Environment =
                {
                    ["DOTNET_ROLL_FORWARD"] = null,
                    ["dotnet_dir"] = null,
                    ["dotnet_version"] = null,
                }
            };

            // Fix for exit code 127 on macOS/Linux: the post-build Exec in tMLMod.targets
            // runs bare `dotnet` via /bin/sh, so dotnet's directory must be on the child's PATH.
            if (!OperatingSystem.IsWindows())
            {
                var dotnetDir = Path.GetDirectoryName(dotnetPath);
                if (!string.IsNullOrEmpty(dotnetDir))
                    psi.Environment["PATH"] = dotnetDir + ":" + Environment.GetEnvironmentVariable("PATH");
            }

            using var process = ModCompile.StartOnHost(psi)!;
            if (!process.WaitForExit(10000))
            {
                var instance = Instance;
                instance?.Logger.Error(Language.GetTextValue("Mods.BuildWithDotnetBuild.Misc.MaybeStuckLogMessage",
                    process.Id));

                self.status.SetStatus(Language.GetTextValue("tModLoader.Building", mod.Name) +
                                      Language.GetTextValue("Mods.BuildWithDotnetBuild.Misc.MaybeStuckStatusHint"));
            }

            process.WaitForExit();
            var errorLogContent = System.IO.File.Exists(errorLogPath) ? System.IO.File.ReadAllText(errorLogPath) : "";
            if (!string.IsNullOrWhiteSpace(errorLogContent))
            {
                Exception ex = new BuildException(errorLogContent);
                throw ex;
            }

            process.WaitForExit();
            ModLoader.EnableMod(mod.Name);
            LocalizationLoader.HandleModBuilt(mod.Name);
        }
        catch (Exception ex)
        {
            ex.Data["mod"] = mod.Name;
            throw;
        }
    }

    // Quick arguments string building implementation
    private static string BuildArguments(params string[] args)
    {
        var sb = new StringBuilder();
        foreach (var arg in args)
        {
            if (sb.Length > 0)
                sb.Append(' ');

            if (arg.Length == 0 || arg.IndexOfAny([' ', '"', '\t']) >= 0)
            {
                sb.Append('"');
                foreach (var c in arg)
                {
                    if (c == '"')
                        sb.Append('\\');
                    sb.Append(c);
                }

                sb.Append('"');
            }
            else
            {
                sb.Append(arg);
            }
        }

        return sb.ToString();
    }
}
