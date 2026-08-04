using System;
using System.Collections.Generic;
using System.Linq;
using AutoBuildTool.Editor.Build;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace ABS.Build
{
	public class AutoBuildSettings : ScriptableObject
	{
		[SerializeField] private bool enableServerBuild;
		
		[Header("Client")] 
		[SerializeReference] private List<CustomFolder> additionalClientFolders = new();
		[SerializeField] private List<CustomFile>   additionalClientFiles;

		[Header("Server")] 
		[SerializeReference] private List<CustomFolder> additionalServerFolders = new();
		[SerializeField] private List<CustomFile>   additionalServerFiles;

		public bool GetEnableServerBuild() => enableServerBuild;
	
		// Dynamically fetch all build profiles that do NOT have "Server" in their name
		public List<BuildProfile> GetClientBuildProfiles()
		{
			return BuildProfile.GetAllBuildProfiles()
				.Where(p => p != null && p.name.IndexOf("Server", StringComparison.OrdinalIgnoreCase) < 0)
				.ToList();
		}

		public List<CustomFolder> GetAdditionalClientFolders() => additionalClientFolders;
		public List<CustomFile> GetAdditionalClientFiles() => additionalClientFiles;

		// Dynamically fetch all build profiles that DO have "Server" in their name
		public List<BuildProfile> GetServerBuildProfiles()
		{
			return BuildProfile.GetAllBuildProfiles()
				.Where(p => p != null && p.name.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0)
				.ToList();
		}

		public List<CustomFolder> GetAdditionalServerFolders() => additionalServerFolders;
		public List<CustomFile> GetAdditionalServerFiles() => additionalServerFiles;
		
		public static AutoBuildSettings GetAutoBuildSettings()
		{
			const string defaultPath = "Assets/Editor/AutoBuildSettings.asset";

			// 1. Try loading by exact path first (Reliable during recompilation)
			var settings = AssetDatabase.LoadAssetAtPath<AutoBuildSettings>(defaultPath);
			if (settings != null) return settings;

			// 2. Fallback to FindAssets in case the user moved it manually
			var guids = AssetDatabase.FindAssets("t:AutoBuildSettings");
			if (guids.Length > 0)
			{
				if (guids.Length > 1) Debug.LogWarning("Multiple AutoBuildSettings assets found! Using the first one.");
				var path = AssetDatabase.GUIDToAssetPath(guids[0]);
				return AssetDatabase.LoadAssetAtPath<AutoBuildSettings>(path);
			}

			// 3. Create it safely if it truly doesn't exist
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