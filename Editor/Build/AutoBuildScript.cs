using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AutoBuildTool.Editor.Build
{
	public static class AutoBuildScript
	{
		#region Constants

		private const string BUILDS_FOLDER = "Builds";
		private const string SERVER_FOLDER = "Server";
		private const string CLIENT_FOLDER = "Client";
		private const char   VERSION_SEPARATOR = ':';
		private const char   VERSION_DOT       = '.';

		#endregion

		#region Version Management

		private enum BumpType { Build, Patch, Minor, Major }

		private readonly struct VersionInfo
		{
			private readonly int major;
			private readonly int minor;
			private readonly int patch;
			private readonly int build;

			public VersionInfo(int major, int minor, int patch, int build)
			{
				this.major = major;
				this.minor = minor;
				this.patch = patch;
				this.build = build;
			}

			public static VersionInfo Parse(string versionString)
			{
				int major = 0, minor = 0, patch = 0, build = 0;
				
				if (string.IsNullOrEmpty(versionString)) return new VersionInfo(0, 0, 0, 0);

				var parts       = versionString.Split(VERSION_SEPARATOR);
				var coreVersion = parts[0];
				if (parts.Length > 1) int.TryParse(parts[1], out build);

				var coreParts = coreVersion.Split(VERSION_DOT);
				if (coreParts.Length > 0) int.TryParse(coreParts[0], out major);
				if (coreParts.Length > 1) int.TryParse(coreParts[1], out minor);
				if (coreParts.Length > 2) int.TryParse(coreParts[2], out patch);

				return new VersionInfo(major, minor, patch, build);
			}

			public override string ToString() => $"{major}.{minor}.{patch}:{build}";

			public VersionInfo Bump(BumpType bumpType)
			{
				return bumpType switch
				{
					BumpType.Major => new VersionInfo(major + 1, 0, 0, 0),
					BumpType.Minor => new VersionInfo(major, minor + 1, 0, 0),
					BumpType.Patch => new VersionInfo(major, minor, patch + 1, 0),
					BumpType.Build => new VersionInfo(major, minor, patch, build + 1),
					_              => throw new ArgumentOutOfRangeException(nameof(bumpType), bumpType, null)
				};
			}
		}

		private static void BumpVersion(BumpType bumpType)
		{
			var         currentVersion = PlayerSettings.bundleVersion;
			VersionInfo versionInfo    = VersionInfo.Parse(currentVersion);
			var         newVersion     = versionInfo.Bump(bumpType).ToString();

			PlayerSettings.bundleVersion = newVersion;
			AssetDatabase.SaveAssets();

			Debug.Log($"Version successfully bumped from [{currentVersion}] to [{newVersion}]");
		}

		#endregion

		#region Menu Items

		[MenuItem("Build/Build (Bump Build)")] public static void BuildBumpBuild() => BuildBoth(BumpType.Build);
		[MenuItem("Build/Build (Bump Patch)")] public static void BuildBumpPatch() => BuildBoth(BumpType.Patch);
		[MenuItem("Build/Build (Bump Minor)")] public static void BuildBumpMinor() => BuildBoth(BumpType.Minor);
		[MenuItem("Build/Build (Bump Major)")] public static void BuildBumpMajor() => BuildBoth(BumpType.Major);

		#endregion

		#region Build Logic

		private static void BuildBoth(BumpType bumpType)
		{
			BumpVersion(bumpType);
			var version      = PlayerSettings.bundleVersion;
			var safeVersion  = version.Replace(VERSION_SEPARATOR, '_');
			var basePath     = Path.Combine(BUILDS_FOLDER, $"v.{safeVersion}");
			var autoSettings = AutoBuildSettings.GetAutoBuildSettings();

			// Force a sync right before building to ensure lists reflect reality
			autoSettings.SyncProfiles();
			EditorUtility.SetDirty(autoSettings);
			AssetDatabase.SaveAssets();

			// Store the current profile so we don't mess up the user's Editor state
			var originalProfile = BuildProfile.GetActiveBuildProfile();

			try
			{
				if (autoSettings.GetEnableServerBuild())
				{
					// GetActiveServerProfiles() filters out unchecked profiles
					foreach (BuildProfile profile in autoSettings.GetActiveServerProfiles())
					{
						BuildProfileTarget(basePath, SERVER_FOLDER, profile, true, autoSettings.GetAdditionalServerFolders(), autoSettings.GetAdditionalServerFiles());
					}
				}

				// GetActiveClientProfiles() filters out unchecked profiles
				foreach (BuildProfile profile in autoSettings.GetActiveClientProfiles())
				{
					BuildProfileTarget(basePath, CLIENT_FOLDER, profile, false, autoSettings.GetAdditionalClientFolders(), autoSettings.GetAdditionalClientFiles());
				}
			}
			finally
			{
				// Safely restore the original profile once the builds finish
				BuildProfile.SetActiveBuildProfile(originalProfile);
			}

			Debug.Log($"Build process finished for v.{version}");
			EditorUtility.RevealInFinder(basePath);
		}
		
		private static void BuildProfileTarget(string basePath, string typeFolder, BuildProfile profile, bool isServer, List<CustomFolder> folders, List<CustomFile> files)
		{
			// CRITICAL FIX: Force the profile to become Active.
			// Failing to do this in Unity 6 causes script compilation cache issues across different targets.
			BuildProfile.SetActiveBuildProfile(profile);

			BuildTarget platform = GetBuildTarget(profile);
			string platformName = platform.ToString();
			string outputDir = Path.Combine(basePath, typeFolder, platformName);
			
			string ext = GetExtension(platform);
			string exeName = isServer ? $"{PlayerSettings.productName}_Server{ext}" : $"{PlayerSettings.productName}{ext}";
			
			bool isFolderBuild = string.IsNullOrEmpty(ext);
			string buildPath = isFolderBuild ? outputDir : Path.Combine(outputDir, exeName);

			var buildOptions = new BuildPlayerWithProfileOptions
			{
				buildProfile     = profile,
				locationPathName = buildPath,
				// CleanBuildCache prevents UGUI "Missing LifecycleManagement" caching errors
				options          = BuildOptions.CleanBuildCache
			};

			BuildReport report = BuildPipeline.BuildPlayer(buildOptions);

			if (report.summary.result != BuildResult.Succeeded)
			{
				Debug.LogError($"Build failed for {buildPath}: {report.SummarizeErrors()}");
				return;
			}

			Debug.Log($"Build succeeded: {buildPath}");

			string customFilesDir = isFolderBuild ? buildPath : Path.GetDirectoryName(buildPath);
			CreateFolderTree(customFilesDir, folders);
			CreateRootFiles(customFilesDir, files);
		}
		
		private static BuildTarget GetBuildTarget(BuildProfile profile)
		{
			PropertyInfo prop = typeof(BuildProfile).GetProperty("buildTarget", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (prop != null) return (BuildTarget)prop.GetValue(profile);

			using var so = new SerializedObject(profile);
			return (BuildTarget)so.FindProperty("m_BuildTarget").intValue;
		}

		private static string GetExtension(BuildTarget platform)
		{
			return platform switch
			{
				BuildTarget.StandaloneWindows => ".exe",
				BuildTarget.StandaloneWindows64 => ".exe",
				BuildTarget.StandaloneOSX => ".app",
				BuildTarget.StandaloneLinux64 => ".x86_64",
				BuildTarget.Android => ".apk",
				_ => "" 
			};
		}

		private static void CreateFolderTree(string parentDir, List<CustomFolder> folders)
		{
			if (folders == null) return;
			foreach (CustomFolder folder in folders)
			{
				var folderPath = Path.Combine(parentDir, folder.Name);
				Directory.CreateDirectory(folderPath);

				CreateRootFiles(folderPath, folder.Files);
				CreateFolderTree(folderPath, folder.SubFolders);
			}
		}

		private static void CreateRootFiles(string parentDir, List<CustomFile> files)
		{
			if (files == null) return;
			foreach (CustomFile file in files)
			{
				var filePath = Path.Combine(parentDir, file.Name);
				if (!File.Exists(filePath)) File.WriteAllText(filePath, file.FileContent);
			}
		}

		#endregion
	}
}