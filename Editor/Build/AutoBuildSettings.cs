using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace AutoBuildTool.Editor.Build
{
	public class AutoBuildSettings : ScriptableObject
	{
		[SerializeField] private bool enableServerBuild;
		[Header("Client")] 
		[SerializeField] private List<BuildProfile> clientBuildProfiles;
		[SerializeReference] private List<CustomFolder> additionalClientFolders = new();
		[SerializeField] private List<CustomFile>   additionalClientFiles;

		[Header("Server")] 
		[SerializeField] private List<BuildProfile> serverBuildProfiles;
		[SerializeReference] private List<CustomFolder> additionalServerFolders = new();
		[SerializeField] private List<CustomFile>   additionalServerFiles;

		public bool GetEnableServerBuild() => enableServerBuild;
	
		public List<BuildProfile> GetClientBuildProfiles() => clientBuildProfiles;
		public List<CustomFolder> GetAdditionalClientFolders() => additionalClientFolders;
		public List<CustomFile> GetAdditionalClientFiles() => additionalClientFiles;

		public List<BuildProfile> GetServerBuildProfiles() => serverBuildProfiles;
		public List<CustomFolder> GetAdditionalServerFolders() => additionalServerFolders;
		public List<CustomFile> GetAdditionalServerFiles() => additionalServerFiles;

        // REMOVED [InitializeOnLoadMethod] to prevent the asset from destroying itself during recompilation.
		
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