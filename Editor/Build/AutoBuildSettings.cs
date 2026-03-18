using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace ABS.Build
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

		[InitializeOnLoadMethod]
		private static void InitializeOnLoad()
		{
			GetAutoBuildSettings();	
		}
		
		public static AutoBuildSettings GetAutoBuildSettings()
		{
			var guids = AssetDatabase.FindAssets("t:AutoBuildSettings");
			
			if (guids.Length > 0)
			{
				if (guids.Length > 1)
				{
					Debug.LogWarning("Multiple AutoBuildSettings assets found in the project! Using the first one. Please delete the duplicates.");
				}
				
				var path = AssetDatabase.GUIDToAssetPath(guids[0]);
				return AssetDatabase.LoadAssetAtPath<AutoBuildSettings>(path);
			}

			var settings = CreateInstance<AutoBuildSettings>();

			if (!AssetDatabase.IsValidFolder("Assets/Editor"))
			{
				AssetDatabase.CreateFolder("Assets", "Editor");
			}

			const string assetPath = "Assets/Editor/AutoBuildSettings.asset";
			AssetDatabase.CreateAsset(settings, assetPath);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			Debug.Log($"Auto-created missing AutoBuildSettings at '{assetPath}'");
			
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