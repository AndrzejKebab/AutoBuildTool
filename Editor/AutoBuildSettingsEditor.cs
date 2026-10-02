using System;
using System.IO;
using AutoBuildTool.Editor.Build;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AutoBuildTool.Editor
{
	[CustomEditor(typeof(AutoBuildSettings))]
	public class AutoBuildSettingsEditor : UnityEditor.Editor
	{
		private const float MIN_TREE_HEIGHT = 60f;
		private const float MAX_TREE_HEIGHT = 400f;

		private SerializedProperty  clientFiles;
		private SerializedProperty  clientFolders;
		private SerializedProperty  clientProfiles;
		private BuildFolderTreeView clientTree;
		private TreeViewState<int>  clientTreeState;
		private SerializedProperty  enableBuildRetention;

		private SerializedProperty enableServerBuild;
		private SerializedProperty maxBuildsToKeep;
		private SerializedProperty serverFiles;
		private SerializedProperty serverFolders;

		private SerializedProperty  serverProfiles;
		private BuildFolderTreeView serverTree;
		private TreeViewState<int>  serverTreeState;

		private void OnEnable()
		{
			var settings = (AutoBuildSettings)target;
			// Ensure profiles are up to date when the inspector opens. This runs before the first
			// serializedObject access on purpose, so the cache is built from the synced state.
			if (settings.SyncProfiles()) EditorUtility.SetDirty(settings);

			enableServerBuild    = serializedObject.FindProperty("enableServerBuild");
			enableBuildRetention = serializedObject.FindProperty("enableBuildRetention");
			maxBuildsToKeep      = serializedObject.FindProperty("maxBuildsToKeep");

			clientProfiles = serializedObject.FindProperty("clientProfiles");
			serverProfiles = serializedObject.FindProperty("serverProfiles");

			clientFolders = serializedObject.FindProperty("additionalClientFolders");
			serverFolders = serializedObject.FindProperty("additionalServerFolders");

			clientFiles = serializedObject.FindProperty("additionalClientFiles");
			serverFiles = serializedObject.FindProperty("additionalServerFiles");

			CleanUpNullReferences(clientFolders);
			CleanUpNullReferences(serverFolders);
			serializedObject.ApplyModifiedProperties();
			serializedObject.Update(); // Ensure properties reflect the Sync operation

			clientTreeState ??= new TreeViewState<int>();
			serverTreeState ??= new TreeViewState<int>();

			clientTree = new BuildFolderTreeView(clientTreeState, serializedObject, clientFolders, clientFiles);
			serverTree = new BuildFolderTreeView(serverTreeState, serializedObject, serverFolders, serverFiles);

			clientTree.OnSelectionChangedCallback = () =>
			                                        {
				                                        if (clientTree != null && clientTree.HasSelection())
					                                        serverTree?.SetSelection(Array.Empty<int>());
			                                        };

			serverTree.OnSelectionChangedCallback = () =>
			                                        {
				                                        if (serverTree != null && serverTree.HasSelection())
					                                        clientTree?.SetSelection(Array.Empty<int>());
			                                        };
		}

		private static void CleanUpNullReferences(SerializedProperty arrayProp)
		{
			if (arrayProp == null) return;

			for (var i = arrayProp.arraySize - 1; i >= 0; i--)
			{
				SerializedProperty elem = arrayProp.GetArrayElementAtIndex(i);
				if (elem.managedReferenceValue == null)
				{
					arrayProp.DeleteArrayElementAtIndex(i);
				}
				else
				{
					SerializedProperty subFolders = elem.FindPropertyRelative("SubFolders");
					if (subFolders != null) CleanUpNullReferences(subFolders);
				}
			}
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			GUILayout.Label("General Settings", EditorStyles.boldLabel);
			GUILayout.BeginHorizontal();
			EditorGUILayout.PropertyField(enableServerBuild, new GUIContent("Enable Server Build"));
			if (GUILayout.Button("Refresh Profiles", GUILayout.Width(120)))
			{
				// SyncProfiles mutates the target directly, bypassing serializedObject. Without the
				// Update() below, the stale cache taken at the top of OnInspectorGUI would be written
				// straight back by ApplyModifiedProperties and undo the refresh.
				var settings = (AutoBuildSettings)target;
				if (settings.SyncProfiles()) EditorUtility.SetDirty(settings);
				serializedObject.Update();
			}

			GUILayout.EndHorizontal();

			EditorGUILayout.PropertyField(enableBuildRetention, new GUIContent("Enable Build Retention"));
			if (enableBuildRetention.boolValue)
			{
				EditorGUI.indentLevel++;
				EditorGUILayout.PropertyField(maxBuildsToKeep, new GUIContent("Max Builds To Keep"));
				EditorGUI.indentLevel--;
			}

			GUILayout.Space(15);

			DrawBuildSection("Client", clientProfiles, clientTree, clientFolders, clientFiles);
			GUILayout.Space(20);
			if (enableServerBuild.boolValue)
			{
				GUILayout.Space(10);
				DrawBuildSection("Server", serverProfiles, serverTree, serverFolders, serverFiles);
			}
			else
			{
				if (serverTree != null && serverTree.HasSelection())
					serverTree.SetSelection(Array.Empty<int>());
			}

			GUILayout.Space(10);
			DrawFileEditor();

			serializedObject.ApplyModifiedProperties();
		}

		private void DrawBuildSection(string label, SerializedProperty profiles, BuildFolderTreeView tree,
		                              SerializedProperty folders, SerializedProperty files)
		{
			GUILayout.Label($"{label} Target Profiles", EditorStyles.boldLabel);
			DrawProfileList(profiles);

			GUILayout.Space(15);
			GUILayout.Label($"{label} Folder Tree", EditorStyles.boldLabel);

			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Add Root Folder"))
			{
				AddRootFolder(folders);
				tree?.Reload();
			}

			if (GUILayout.Button("Add Root File"))
			{
				AddRootFile(files);
				tree?.Reload();
			}

			GUILayout.EndHorizontal();

			// Grow with the content instead of a fixed 150px (~7 rows), but stay bounded.
			var  height = tree != null ? Mathf.Clamp(tree.totalHeight, MIN_TREE_HEIGHT, MAX_TREE_HEIGHT) : MIN_TREE_HEIGHT;
			Rect rect   = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
			tree?.OnGUI(rect);
		}

		private void DrawProfileList(SerializedProperty listProp)
		{
			if (listProp.arraySize == 0)
			{
				EditorGUILayout.HelpBox("No profiles discovered.", MessageType.Info);
				return;
			}

			EditorGUI.BeginChangeCheck();
			for (var i = 0; i < listProp.arraySize; i++)
			{
				SerializedProperty elem        = listProp.GetArrayElementAtIndex(i);
				SerializedProperty enabledProp = elem.FindPropertyRelative("IsEnabled");
				SerializedProperty profileProp = elem.FindPropertyRelative("Profile");

				GUILayout.BeginHorizontal();

				// Draw Checkbox (PropertyField so the toggle participates in Undo)
				EditorGUILayout.PropertyField(enabledProp, GUIContent.none, GUILayout.Width(20));

				// Lock the object field so the user cannot modify it
				EditorGUI.BeginDisabledGroup(true);

				Object currentProfile = profileProp.objectReferenceValue;
				EditorGUILayout.ObjectField(GUIContent.none, currentProfile, typeof(BuildProfile), false);

				EditorGUI.EndDisabledGroup(); // Unlock GUI state for the next elements

				GUILayout.EndHorizontal();
			}

			if (EditorGUI.EndChangeCheck()) serializedObject.ApplyModifiedProperties();
		}

		private void DrawFileEditor()
		{
			BuildFolderTreeItem clientItem = clientTree?.GetSelectedItem();
			BuildFolderTreeItem item       = clientItem ?? serverTree?.GetSelectedItem();

			if (item is not { IsFile: true } || string.IsNullOrEmpty(item.PropertyPath))
				return;

			// Only the tree that owns the selection needs rebuilding after a rename.
			BuildFolderTreeView owningTree = clientItem != null ? clientTree : serverTree;

			SerializedProperty file = serializedObject.FindProperty(item.PropertyPath);
			if (file == null) return;

			GUILayout.Space(10);
			GUILayout.Label("File / Asset Editor", EditorStyles.boldLabel);

			EditorGUI.BeginChangeCheck();

			SerializedProperty opTypeProp = file.FindPropertyRelative("OperationType");
			EditorGUILayout.PropertyField(opTypeProp, new GUIContent("Operation Type"));

			if (opTypeProp.enumValueIndex == (int)FileOperationType.CreateTextFile)
			{
				EditorGUILayout.PropertyField(file.FindPropertyRelative("Name"), new GUIContent("File Name"));
				EditorGUILayout.PropertyField(file.FindPropertyRelative("FileContent"));
			}
			else
			{
				SerializedProperty sourceAssetProp = file.FindPropertyRelative("SourceAsset");

				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(sourceAssetProp, new GUIContent("Asset to Copy"));
				if (EditorGUI.EndChangeCheck())
					// Auto-fill the Name field based on the selected asset
					if (sourceAssetProp.objectReferenceValue != null)
					{
						var assetPath = AssetDatabase.GetAssetPath(sourceAssetProp.objectReferenceValue);
						if (!string.IsNullOrEmpty(assetPath))
							file.FindPropertyRelative("Name").stringValue = Path.GetFileName(assetPath);
					}

				EditorGUILayout.PropertyField(file.FindPropertyRelative("Name"), new GUIContent("Destination Name"));
				// lang=none
				EditorGUILayout
					.HelpBox("Select any file or folder from your project. It will be copied exactly as it is into the build folder. (.meta files are ignored)",
					         MessageType.Info);
			}

			if (!EditorGUI.EndChangeCheck()) return;
			serializedObject.ApplyModifiedProperties();
			owningTree?.Reload();
			serializedObject.Update();
		}

		// Both add-paths delegate to the tree view's initialisers so a new field on CustomFile /
		// CustomFolder only has to be handled in one place.
		private void AddRootFolder(SerializedProperty folders)
		{
			folders.InsertArrayElementAtIndex(folders.arraySize);
			BuildFolderTreeView.ResetFolderElement(folders.GetArrayElementAtIndex(folders.arraySize - 1));

			serializedObject.ApplyModifiedProperties();
		}

		private void AddRootFile(SerializedProperty files)
		{
			files.InsertArrayElementAtIndex(files.arraySize);
			BuildFolderTreeView.ResetFileElement(files.GetArrayElementAtIndex(files.arraySize - 1));

			serializedObject.ApplyModifiedProperties();
		}
	}
}