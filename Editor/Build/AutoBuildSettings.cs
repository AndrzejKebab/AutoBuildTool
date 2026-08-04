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

		public ProfileState(BuildProfile profile)
		{
			Profile   = profile;
			IsEnabled = true;
		}
	}

	public class AutoBuildSettings : ScriptableObject
	{
		[SerializeField] private bool enableServerBuild;
		
		[Header("Client")] 
		[SerializeField] private List<ProfileState> clientProfiles = new();
		[SerializeReference] private List<CustomFolder> additionalClientFolders = new();
		[SerializeField] private List<CustomFile>   additionalClientFiles;

		[Header("Server")] 
		[SerializeField] private List<ProfileState> serverProfiles = new();
		[SerializeReference] private List<CustomFolder> additionalServerFolders = new();
		[SerializeField] private List<CustomFile>   additionalServerFiles;

		public bool GetEnableServerBuild() => enableServerBuild;

		// Automatically synchronizes the serialized lists with the actual assets in your project
		public void SyncProfiles()
		{
			List<BuildProfile> allProfiles = BuildProfile.GetAllBuildProfiles().Where(p => p != null).ToList();

			List<BuildProfile> actualClientProfiles = allProfiles.Where(p => p.name.IndexOf("Server", StringComparison.OrdinalIgnoreCase) < 0).ToList();
			List<BuildProfile> actualServerProfiles = allProfiles.Where(p => p.name.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0).ToList();

			SyncList(clientProfiles, actualClientProfiles);
			SyncList(serverProfiles, actualServerProfiles);
		}

		private static void SyncList(List<ProfileState> states, List<BuildProfile> actualProfiles)
		{
			// Remove any profiles that were deleted from the project
			states.RemoveAll(s => s.Profile == null || !actualProfiles.Contains(s.Profile));

			// Add any newly created profiles that aren't in the list yet
			foreach (BuildProfile p in actualProfiles)
			{
				if (states.All(s => s.Profile != p))
				{
					states.Add(new ProfileState(p));
				}
			}
		}

		public List<BuildProfile> GetActiveClientProfiles()
		{
			return clientProfiles.Where(p => p.IsEnabled && p.Profile != null).Select(p => p.Profile).ToList();
		}

		public List<BuildProfile> GetActiveServerProfiles()
		{
			return serverProfiles.Where(p => p.IsEnabled && p.Profile != null).Select(p => p.Profile).ToList();
		}

		public List<CustomFolder> GetAdditionalClientFolders() => additionalClientFolders;
		public List<CustomFile> GetAdditionalClientFiles() => additionalClientFiles;
		public List<CustomFolder> GetAdditionalServerFolders() => additionalServerFolders;
		public List<CustomFile> GetAdditionalServerFiles() => additionalServerFiles;

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

			if (!AssetDatabase.IsValidFolder("Assets/Editor"))
			{
				AssetDatabase.CreateFolder("Assets", "Editor");
			}

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