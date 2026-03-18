using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ABS.Build
{
	/// <summary>
	///     Static class for handling client and server builds with version management.
	/// </summary>
	public static class AutoBuildScript
	{
		#region Constants

		private const  string       BUILDS_FOLDER = "Builds";
		private const  string       SERVER_FOLDER = "Server";
		private const  string       CLIENT_FOLDER = "Client";
		private static string       ServerExeName => $"{PlayerSettings.productName}_Server.exe";
		private static string       ClientExeName => $"{PlayerSettings.productName}.exe";
		private const  char         VERSION_SEPARATOR = ':';
		private const  char         VERSION_DOT       = '.';
		private const  BuildOptions OPTIONS          = BuildOptions.ShowBuiltPlayer;

		#endregion

		#region Version Management

		private enum BumpType
		{
			Build,
			Patch,
			Minor,
			Major
		}

		/// <summary>
		///     Represents a version with major, minor, patch, and build numbers.
		/// </summary>
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

				var parts       = versionString.Split(VERSION_SEPARATOR);
				var coreVersion = parts[0];
				if (parts.Length > 1) int.TryParse(parts[1], out build);

				var coreParts = coreVersion.Split(VERSION_DOT);
				if (coreParts.Length > 0) int.TryParse(coreParts[0], out major);
				if (coreParts.Length > 1) int.TryParse(coreParts[1], out minor);
				if (coreParts.Length > 2) int.TryParse(coreParts[2], out patch);

				return new VersionInfo(major, minor, patch, build);
			}

			public override string ToString()
			{
				return $"{major}.{minor}.{patch}:{build}";
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
			VersionInfo newVersionInfo = versionInfo.Bump(bumpType);
			var         newVersion     = newVersionInfo.ToString();

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
			BumpVersion(bumpType);
			var version      = PlayerSettings.bundleVersion;
			var safeVersion  = version.Replace(VERSION_SEPARATOR, '_');
			var basePath     = Path.Combine(BUILDS_FOLDER, $"v.{safeVersion}");
			var autoSettings = AutoBuildSettings.GetAutoBuildSettings();

			Debug.Log($"Enable server build: {autoSettings.GetEnableServerBuild()}");

			if (autoSettings.GetEnableServerBuild())
			{
				Debug.Log($"Server build profiles count: {autoSettings.GetServerBuildProfiles().Count}");
				var serverPath = Path.Combine(basePath, SERVER_FOLDER, ServerExeName);
				foreach (BuildProfile profile in autoSettings.GetServerBuildProfiles())
					BuildTarget(serverPath, profile, autoSettings.GetAdditionalServerFolders(),
					            autoSettings.GetAdditionalServerFiles());
			}

			Debug.Log($"Client build profiles count: {autoSettings.GetClientBuildProfiles().Count}");
			var clientPath = Path.Combine(basePath, CLIENT_FOLDER, ClientExeName);
			foreach (BuildProfile profile in autoSettings.GetClientBuildProfiles())
				BuildTarget(clientPath, profile, autoSettings.GetAdditionalClientFolders(),
				            autoSettings.GetAdditionalClientFiles());

			Debug.Log($"Build process finished for v.{version}");
		}

		private static void BuildTarget(
			string             buildPath,
			BuildProfile       profile,
			List<CustomFolder> folders,
			List<CustomFile>   files)
		{
			var buildOptions = new BuildPlayerWithProfileOptions
			                   {
				                   buildProfile     = profile,
				                   locationPathName = buildPath,
				                   options          = OPTIONS
			                   };

			BuildReport report = BuildPipeline.BuildPlayer(buildOptions);

			if (report.summary.result != BuildResult.Succeeded)
			{
				Debug.LogError($"Build failed for {buildPath}: {report.SummarizeErrors()}");
				return;
			}

			Debug.Log($"Build succeeded: {buildPath}");

			var buildDir = Path.GetDirectoryName(buildPath);
			CreateFolderTree(buildDir, folders);
			CreateRootFiles(buildDir, files);
		}

		private static void CreateFolderTree(string parentDir, List<CustomFolder> folders)
		{
			if (folders == null) return;

			foreach (CustomFolder folder in folders)
			{
				var folderPath = Path.Combine(parentDir, folder.Name);
				Directory.CreateDirectory(folderPath);

				CreateFilesInFolder(folderPath, folder.Files);
				CreateFolderTree(folderPath, folder.SubFolders);
			}
		}

		private static void CreateFilesInFolder(string folderPath, List<CustomFile> files)
		{
			if (files == null) return;

			foreach (CustomFile file in files)
			{
				var filePath = Path.Combine(folderPath, file.Name);
				if (!File.Exists(filePath)) File.WriteAllText(filePath, file.FileContent);
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