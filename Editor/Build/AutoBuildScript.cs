using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AutoBuildTool.Editor.Build
{
	public static class AutoBuildScript
	{
		#region Constants

		private const string BUILDS_FOLDER     = "Builds";
		private const string SERVER_FOLDER     = "Server";
		private const string CLIENT_FOLDER     = "Client";
		private const char   VERSION_SEPARATOR = ':';
		private const char   VERSION_DOT       = '.';
		private const string VERSION_PREFIX    = "v.";

		// BuildProfile.buildTarget is not public, so it has to be read reflectively.
		// Cached once: this lookup would otherwise run per profile per build.
		private static readonly PropertyInfo BuildTargetProperty =
			typeof(BuildProfile).GetProperty("buildTarget",
			                                 BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

		#endregion

		#region Version Management

		private enum BumpType
		{
			Build,
			Patch,
			Minor,
			Major
		}

		private readonly struct VersionInfo : IComparable<VersionInfo>
		{
			private readonly int major;
			private readonly int minor;
			private readonly int patch;
			private readonly int build;

			private VersionInfo(int major, int minor, int patch, int build)
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

			// Turns a build directory name ("v.1.2.0_15") back into a comparable version.
			public static VersionInfo ParseFolderName(string folderName)
			{
				if (string.IsNullOrEmpty(folderName)) return new VersionInfo(0, 0, 0, 0);

				var raw = folderName.StartsWith(VERSION_PREFIX, StringComparison.Ordinal)
					          ? folderName[VERSION_PREFIX.Length..]
					          : folderName;

				return Parse(raw.Replace('_', VERSION_SEPARATOR));
			}

			public override string ToString()
			{
				return $"{major}.{minor}.{patch}:{build}";
			}

			public int CompareTo(VersionInfo other)
			{
				var result = major.CompareTo(other.major);
				if (result != 0) return result;

				result = minor.CompareTo(other.minor);
				if (result != 0) return result;

				result = patch.CompareTo(other.patch);
				return result != 0 ? result : build.CompareTo(other.build);
			}

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

		[MenuItem("Build/Build (Bump Build)")]
		public static void BuildBumpBuild()
		{
			BuildBoth(BumpType.Build);
		}

		[MenuItem("Build/Build (Bump Patch)")]
		public static void BuildBumpPatch()
		{
			BuildBoth(BumpType.Patch);
		}

		[MenuItem("Build/Build (Bump Minor)")]
		public static void BuildBumpMinor()
		{
			BuildBoth(BumpType.Minor);
		}

		[MenuItem("Build/Build (Bump Major)")]
		public static void BuildBumpMajor()
		{
			BuildBoth(BumpType.Major);
		}

		#endregion

		#region Build Logic

		private static void BuildBoth(BumpType bumpType)
		{
			var autoSettings = AutoBuildSettings.GetAutoBuildSettings();

			// Force a sync right before building to ensure lists reflect reality
			if (autoSettings.SyncProfiles())
			{
				EditorUtility.SetDirty(autoSettings);
				AssetDatabase.SaveAssets();
			}

			var                serverEnabled = autoSettings.GetEnableServerBuild();
			List<BuildProfile> clientTargets = autoSettings.GetActiveClientProfiles();
			List<BuildProfile> serverTargets = serverEnabled
				                                   ? autoSettings.GetActiveServerProfiles()
				                                   : new List<BuildProfile>();

			// Validate before bumping: an empty target list must not consume a version number.
			if (clientTargets.Count == 0 && serverTargets.Count == 0)
			{
				Debug.LogError("[ABS] No enabled build profiles. Aborting build (version not bumped).");
				return;
			}

			if (clientTargets.Count == 0)
				Debug.LogWarning("[ABS] No enabled client build profiles. Skipping client builds.");

			if (serverEnabled && serverTargets.Count == 0)
				Debug.LogWarning("[ABS] Server build is enabled but no server profiles are enabled. Skipping server builds.");

			BumpVersion(bumpType);
			var version      = PlayerSettings.bundleVersion;
			var safeVersion  = version.Replace(VERSION_SEPARATOR, '_');
			var buildDirName = $"{VERSION_PREFIX}{safeVersion}";
			var basePath     = Path.Combine(BUILDS_FOLDER, buildDirName);

			var originalProfile = BuildProfile.GetActiveBuildProfile();
			var failures        = new List<string>();
			var succeeded       = 0;

			try
			{
				foreach (BuildProfile profile in serverTargets)
					if (TryBuildProfile(basePath, SERVER_FOLDER, profile, true,
					                    autoSettings.GetAdditionalServerFolders(),
					                    autoSettings.GetAdditionalServerFiles(), failures))
						succeeded++;

				foreach (BuildProfile profile in clientTargets)
					if (TryBuildProfile(basePath, CLIENT_FOLDER, profile, false,
					                    autoSettings.GetAdditionalClientFolders(),
					                    autoSettings.GetAdditionalClientFiles(), failures))
						succeeded++;
			}
			finally
			{
				BuildProfile.SetActiveBuildProfile(originalProfile);
			}

			if (failures.Count > 0)
				Debug.LogError($"[ABS] {failures.Count} build(s) failed:\n{string.Join("\n", failures)}");

			if (succeeded == 0)
			{
				Debug.LogError($"[ABS] Build process finished for v.{version} but no build succeeded.");
				return;
			}

			// Clean up old builds if retention is enabled
			CleanupOldBuilds(autoSettings, buildDirName);

			Debug.Log($"[ABS] Build process finished for v.{version} ({succeeded} succeeded, {failures.Count} failed)");
			EditorUtility.RevealInFinder(basePath);
		}

		// Isolates a single profile so one failure cannot abort the remaining profiles in the batch.
		private static bool TryBuildProfile(string basePath, string typeFolder, BuildProfile profile, bool isServer,
		                                    List<CustomFolder> folders, List<CustomFile> files, List<string> failures)
		{
			if (profile == null)
			{
				failures.Add($"{typeFolder}/<null>: profile reference is missing.");
				return false;
			}

			try
			{
				return BuildProfileTarget(basePath, typeFolder, profile, isServer, folders, files);
			}
			catch (Exception e)
			{
				failures.Add($"{typeFolder}/{profile.name}: {e.Message}");
				Debug.LogException(e);
				return false;
			}
		}

		private static bool BuildProfileTarget(string basePath, string typeFolder, BuildProfile profile, bool isServer,
		                                       List<CustomFolder> folders, List<CustomFile> files)
		{
			BuildProfile.SetActiveBuildProfile(profile);

			BuildTarget platform     = GetBuildTarget(profile);
			var         platformName = platform.ToString();

			// Keyed on the profile name as well as the platform: several profiles can target the
			// same platform, and a platform-only path makes them overwrite each other's output.
			var outputDir = Path.Combine(basePath, typeFolder, platformName, SanitizeFileName(profile.name));

			var ext     = GetExtension(platform);
			var exeName = isServer ? $"{PlayerSettings.productName}_Server{ext}" : $"{PlayerSettings.productName}{ext}";

			var isFolderBuild = string.IsNullOrEmpty(ext);
			var buildPath     = isFolderBuild ? outputDir : Path.Combine(outputDir, exeName);

			var buildOptions = new BuildPlayerWithProfileOptions
			                   {
				                   buildProfile     = profile,
				                   locationPathName = buildPath
			                   };

			BuildReport report = BuildPipeline.BuildPlayer(buildOptions);

			if (report.summary.result != BuildResult.Succeeded)
			{
				Debug.LogError($"Build failed for {buildPath}: {report.SummarizeErrors()}");
				return false;
			}

			Debug.Log($"Build succeeded: {buildPath}");

			var customFilesDir = isFolderBuild ? buildPath : Path.GetDirectoryName(buildPath);
			CreateFolderTree(customFilesDir, folders);
			WriteFiles(customFilesDir, files);
			return true;
		}

		private static void CleanupOldBuilds(AutoBuildSettings settings, string currentBuildDirName)
		{
			if (!settings.GetEnableBuildRetention()) return;
			if (!Directory.Exists(BUILDS_FOLDER)) return;

			var maxBuilds = settings.GetMaxBuildsToKeep();
			if (maxBuilds <= 0) return;

			var dirInfo = new DirectoryInfo(BUILDS_FOLDER);

			// Get all directories that match our version naming format ("v.*") and order them by the
			// version encoded in the name, newest first. CreationTime is not usable here: a rebuilt or
			// restored directory keeps a stale timestamp, which can sort the build we just produced
			// last and delete it. CreationTime only breaks ties between identical versions.
			List<DirectoryInfo> buildDirs = dirInfo.GetDirectories($"{VERSION_PREFIX}*")
			                                       .OrderByDescending(d => VersionInfo.ParseFolderName(d.Name))
			                                       .ThenByDescending(d => d.CreationTime)
			                                       .ToList();

			if (buildDirs.Count <= maxBuilds) return;
			// Delete all items starting from index `maxBuilds`
			for (var i = maxBuilds; i < buildDirs.Count; i++)
			{
				// Never delete the build that was just produced.
				if (string.Equals(buildDirs[i].Name, currentBuildDirName, StringComparison.OrdinalIgnoreCase))
					continue;

				try
				{
					buildDirs[i].Delete(true);
					Debug.Log($"Deleted old build to free up space: {buildDirs[i].Name}");
				}
				catch (Exception e)
				{
					Debug.LogWarning($"Failed to delete old build '{buildDirs[i].Name}'. Make sure it isn't opened by another program.\n{e.Message}");
				}
			}
		}

		// Both the reflected property and the serialized field name are tied to Unity's internals,
		// so each step falls through rather than throwing. The active build target is a safe last
		// resort here because the only caller sets this profile active immediately beforehand.
		private static BuildTarget GetBuildTarget(BuildProfile profile)
		{
			if (BuildTargetProperty != null)
				try
				{
					return (BuildTarget)BuildTargetProperty.GetValue(profile);
				}
				catch (Exception e)
				{
					Debug.LogWarning($"[ABS] Could not read BuildProfile.buildTarget reflectively: {e.Message}");
				}

			using (var so = new SerializedObject(profile))
			{
				SerializedProperty targetProp = so.FindProperty("m_BuildTarget");
				if (targetProp != null) return (BuildTarget)targetProp.intValue;
			}

			Debug.LogWarning($"[ABS] Could not resolve the build target for profile '{profile.name}'; falling back to the active build target. This Unity version may have renamed BuildProfile's build target member.");
			return EditorUserBuildSettings.activeBuildTarget;
		}

		private static string GetExtension(BuildTarget platform)
		{
			return platform switch
			       {
				       BuildTarget.StandaloneWindows   => ".exe",
				       BuildTarget.StandaloneWindows64 => ".exe",
				       BuildTarget.StandaloneOSX       => ".app",
				       BuildTarget.StandaloneLinux64   => ".x86_64",
				       BuildTarget.Android             => ".apk",
				       _                               => ""
			       };
		}

		// Profile names are user-authored and end up in a directory path.
		private static string SanitizeFileName(string name)
		{
			if (string.IsNullOrWhiteSpace(name)) return "Unnamed";

			var invalid = Path.GetInvalidFileNameChars();
			var sb      = new StringBuilder(name.Length);
			foreach (var c in name) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

			return sb.ToString();
		}

		private static void CreateFolderTree(string parentDir, List<CustomFolder> folders)
		{
			if (folders == null) return;
			foreach (CustomFolder folder in folders)
			{
				var folderPath = Path.Combine(parentDir, folder.Name);
				Directory.CreateDirectory(folderPath);

				WriteFiles(folderPath, folder.Files);
				CreateFolderTree(folderPath, folder.SubFolders);
			}
		}

		private static void WriteFiles(string parentDir, List<CustomFile> files)
		{
			if (files == null) return;
			foreach (CustomFile file in files)
				switch (file.OperationType)
				{
					case FileOperationType.CreateTextFile:
					{
						var finalName = string.IsNullOrEmpty(file.Name) ? "NewFile.txt" : file.Name;
						var filePath  = Path.Combine(parentDir, finalName);

						EnsureParentDirectory(filePath);
						if (!File.Exists(filePath)) File.WriteAllText(filePath, file.FileContent);
						break;
					}
					case FileOperationType.CopyProjectAsset:
					{
						// An entry switched to CopyProjectAsset before an asset was picked is an
						// ordinary UI state, not a programming error - warn and move on.
						if (file.SourceAsset == null)
						{
							Debug.LogWarning($"[ABS] Entry '{file.Name}' is set to CopyProjectAsset but has no source asset assigned. Skipping.");
							break;
						}

						var assetPath = AssetDatabase.GetAssetPath(file.SourceAsset);
						if (string.IsNullOrEmpty(assetPath))
						{
							Debug.LogWarning($"[ABS] Source asset for entry '{file.Name}' is not a project asset. Skipping.");
							break;
						}

						var fullSourcePath = Path.GetFullPath(assetPath);
						var finalName      = string.IsNullOrEmpty(file.Name) ? Path.GetFileName(assetPath) : file.Name;
						var destPath       = Path.Combine(parentDir, finalName);

						EnsureParentDirectory(destPath);

						if (AssetDatabase.IsValidFolder(assetPath))
							CopyDirectoryContents(fullSourcePath, destPath);
						else
							File.Copy(fullSourcePath, destPath, true);
						break;
					}
					default:
						Debug.LogWarning($"[ABS] Unsupported file operation '{file.OperationType}' on entry '{file.Name}'. Skipping.");
						break;
				}
		}

		// A destination name may contain a relative sub-path, so the parent may not exist yet.
		private static void EnsureParentDirectory(string path)
		{
			var dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		}

		// Helper to recursively copy an entire folder while skipping Unity's internal .meta files
		private static void CopyDirectoryContents(string sourceDir, string destDir)
		{
			if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

			foreach (var file in Directory.GetFiles(sourceDir))
			{
				if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
				var destFile = Path.Combine(destDir, Path.GetFileName(file));
				File.Copy(file, destFile, true);
			}

			foreach (var dir in Directory.GetDirectories(sourceDir))
			{
				var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
				CopyDirectoryContents(dir, destSubDir);
			}
		}

		#endregion
	}
}
