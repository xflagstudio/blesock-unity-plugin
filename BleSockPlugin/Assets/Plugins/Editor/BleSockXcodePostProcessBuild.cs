#if UNITY_IOS

using System.IO;

using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

public static class BleSockXcodePostProcessBuild
{
    [PostProcessBuild]
    static void OnPostProcessBuild(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS)
        {
            return;
        }

        // PBXProject

        string projectPath = PBXProject.GetPBXProjectPath(path);
        var project = new PBXProject();
        project.ReadFromFile(projectPath);

        string targetGuid = project.TargetGuidByName(PBXProject.GetUnityTargetName());
        project.AddCapability(targetGuid, PBXCapabilityType.BackgroundModes);

        project.SetBuildProperty(targetGuid, "SWIFT_OBJC_INTERFACE_HEADER_NAME", "Unity-Swift.h");
        project.SetBuildProperty(targetGuid, "SWIFT_VERSION", "4.0");
        project.SetBuildProperty(targetGuid, "LD_RUNPATH_SEARCH_PATHS", "@executable_path/Frameworks");

        project.WriteToFile(projectPath);

        // Info.plist

        string plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);

        plist.root.SetString("NSBluetoothPeripheralUsageDescription", "To ad-hoc communicating");

        var backgroundModes = plist.root.CreateArray("UIBackgroundModes");
        backgroundModes.AddString("bluetooth-central");
        backgroundModes.AddString("bluetooth-peripheral");

        plist.WriteToFile(plistPath);
    }
}

#endif
