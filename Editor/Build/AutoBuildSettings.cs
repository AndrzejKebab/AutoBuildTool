using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace AutoBuildTool.Editor.Build
{
	[Serializable]
	public class ProfileState
	{
		public BuildProfile Profile;
		public bool         IsEnabled;

		// Unity's serializer needs a parameterless constructor to rebuild list elements.
		public ProfileState() : this(null) { }

		public ProfileState(BuildProfile profile)
		{
			Profile   = profile;
			IsEnabled = true;
		}
	}

	public class AutoBuildSettings : ScriptableObject
	{
		private const string SERVER_KEYWORD = "Server";

		[Header("General")] [SerializeField] private bool enableServerBuild;

		[Header("Build Retention")] [SerializeField]
		private bool enableBuildRetention;

		[SerializeField] [Min(1)] private int maxBuildsToKeep = 3;

		[Header("Client")] [SerializeField] private List<ProfileState> clientProfiles = new();

		[SerializeReference] private List<CustomFolder> additionalClientFolders = new();
		[SerializeField]     private List<CustomFile>   additionalClientFiles;

		[Header("Server")] [SerializeField] private List<ProfileState> serverProfiles = new();

		[SerializeReference] private List<CustomFolder> additionalServerFolders = new();
		[SerializeField]     private List<CustomFile>   additionalServerFiles;

		public bool GetEnableServerBuild()
		{
			return enableServerBuild;
		}

		public bool GetEnableBuildRetention()
		{
			return enableBuildRetention;
		}

		public int GetMaxBuildsToKeep()
		{
			return maxBuildsToKeep;
		}

		// Automatically synchronizes the serialized lists with the actual assets in your project.
		// Returns true when anything changed, so callers know whether the asset needs saving.
		public bool SyncProfiles()
		{
			List<BuildProfile> allProfiles = BuildProfile.GetAllBuildProfiles().Where(p => p != null).ToList();

			List<BuildProfile> actualServerProfiles = allProfiles.Where(p => IsServerProfile(p.name)).ToList();
			List<BuildProfile> actualClientProfiles = allProfiles.Where(p => !IsServerProfile(p.name)).ToList();

			var changed = SyncList(clientProfiles, actualClientProfiles);
			changed |= SyncList(serverProfiles, actualServerProfiles);

			return changed;
		}

		// Matches "Server" only as a whole word, so names that merely contain the letters -
		// "Observer", "Serverless" - are not misclassified as server profiles.
		private static bool IsServerProfile(string profileName)
		{
			if (string.IsNullOrEmpty(profileName)) return false;

			var index = 0;
			while ((index = profileName.IndexOf(SERVER_KEYWORD, index, StringComparison.OrdinalIgnoreCase)) >= 0)
			{
				var after         = index + SERVER_KEYWORD.Length;
				var boundedBefore = index == 0 || !char.IsLetter(profileName[index - 1]);
				var boundedAfter  = after >= profileName.Length || !char.IsLower(profileName[after]);

				if (boundedBefore && boundedAfter) return true;
				index = after;
			}

			return false;
		}

		private static bool SyncList(List<ProfileState> states, List<BuildProfile> actualProfiles)
		{
			// Remove any profiles that were deleted from the project
			var changed = states.RemoveAll(s => s.Profile == null || !actualProfiles.Contains(s.Profile)) > 0;

			// Add any newly created profiles that aren't in the list yet
			foreach (BuildProfile p in actualProfiles)
				if (states.All(s => s.Profile != p))
				{
					states.Add(new ProfileState(p));
					changed = true;
				}

			return changed;
		}

		public List<BuildProfile> GetActiveClientProfiles()
		{
			return clientProfiles.Where(p => p.IsEnabled && p.Profile != null).Select(p => p.Profile).ToList();
		}

		public List<BuildProfile> GetActiveServerProfiles()
		{
			return serverProfiles.Where(p => p.IsEnabled && p.Profile != null).Select(p => p.Profile).ToList();
		}

		public List<CustomFolder> GetAdditionalClientFolders()
		{
			return additionalClientFolders;
		}

		public List<CustomFile> GetAdditionalClientFiles()
		{
			return additionalClientFiles;
		}

		public List<CustomFolder> GetAdditionalServerFolders()
		{
			return additionalServerFolders;
		}

		public List<CustomFile> GetAdditionalServerFiles()
		{
			return additionalServerFiles;
		}

		public static AutoBuildSettings GetAutoBuildSettings()
		{
			const string defaultPath = "Assets/Editor/AutoBuildSettings.asset";

			var settings = AssetDatabase.LoadAssetAtPath<AutoBuildSettings>(defaultPath);
			if (settings != null) return settings;

			var guids = AssetDatabase.FindAssets("t:AutoBuildSettings");
			if (guids.Length > 0)
			{
				if (guids.Length > 1) Debug.LogWarning("Multiple AutoBuildSettings assets found! Using the first one.");
				var path = AssetDatabase.GUIDToAssetPath(guids[0]);
				return AssetDatabase.LoadAssetAtPath<AutoBuildSettings>(path);
			}

			settings = CreateInstance<AutoBuildSettings>();

			if (!AssetDatabase.IsValidFolder("Assets/Editor")) AssetDatabase.CreateFolder("Assets", "Editor");

			AssetDatabase.CreateAsset(settings, defaultPath);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			Debug.Log($"Auto-created missing AutoBuildSettings at '{defaultPath}'");
			return settings;
		}

		[MenuItem("Build/Auto Build Settings", priority = -1)]
		public static void SelectSettings()
		{
			AutoBuildSettings settings = GetAutoBuildSettings();
			Selection.activeObject = settings;
			EditorGUIUtility.PingObject(settings);
		}
	}
}