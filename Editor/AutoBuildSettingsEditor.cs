using System;
using ABS.Build;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ABS
{
	[CustomEditor(typeof(AutoBuildSettings))]
	public class AutoBuildSettingsEditor : UnityEditor.Editor
	{
		private SerializedProperty  clientFiles;
		private SerializedProperty  clientFolders;
		private SerializedProperty  clientProfiles;
		private BuildFolderTreeView clientTree;
		private TreeViewState<int>  clientTreeState;

		private SerializedProperty  enableServerBuild;
		private SerializedProperty  serverFiles;
		private SerializedProperty  serverFolders;
		private SerializedProperty  serverProfiles;
		private BuildFolderTreeView serverTree;
		private TreeViewState<int>  serverTreeState;

		private void OnEnable()
		{
			enableServerBuild = serializedObject.FindProperty("enableServerBuild");

			clientProfiles = serializedObject.FindProperty("clientBuildProfiles");
			serverProfiles = serializedObject.FindProperty("serverBuildProfiles");

			clientFolders = serializedObject.FindProperty("additionalClientFolders");
			serverFolders = serializedObject.FindProperty("additionalServerFolders");

			clientFiles = serializedObject.FindProperty("additionalClientFiles");
			serverFiles = serializedObject.FindProperty("additionalServerFiles");

			CleanUpNullReferences(clientFolders);
			CleanUpNullReferences(serverFolders);
			serializedObject.ApplyModifiedProperties();

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
				if (elem.managedReferenceValue == null) arrayProp.DeleteArrayElementAtIndex(i);
			}
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			EditorGUILayout.PropertyField(enableServerBuild, new GUIContent("Enable Server Build"));
			GUILayout.Space(5);
			DrawClientSection();
			GUILayout.Space(20);
			if (enableServerBuild.boolValue)
			{
				GUILayout.Space(10);
				DrawServerSection();
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

		private void DrawClientSection()
		{
			EditorGUILayout.PropertyField(clientProfiles, true);

			GUILayout.Space(10);
			GUILayout.Label("Client Folder Tree", EditorStyles.boldLabel);

			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Add Root Folder"))
			{
				AddRootFolder(clientFolders);
				clientTree.Reload();
			}

			if (GUILayout.Button("Add Root File"))
			{
				AddRootFile(clientFiles);
				clientTree.Reload();
			}

			GUILayout.EndHorizontal();

			Rect rect = GUILayoutUtility.GetRect(0, 150, GUILayout.ExpandWidth(true));
			clientTree?.OnGUI(rect);
		}

		private void DrawServerSection()
		{
			EditorGUILayout.PropertyField(serverProfiles, true);

			GUILayout.Space(10);
			GUILayout.Label("Server Folder Tree", EditorStyles.boldLabel);

			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Add Root Folder"))
			{
				AddRootFolder(serverFolders);
				serverTree.Reload();
			}

			if (GUILayout.Button("Add Root File"))
			{
				AddRootFile(serverFiles);
				serverTree.Reload();
			}

			GUILayout.EndHorizontal();

			Rect rect = GUILayoutUtility.GetRect(0, 150, GUILayout.ExpandWidth(true));
			serverTree?.OnGUI(rect);
		}

		private void DrawFileEditor()
		{
			BuildFolderTreeItem item = clientTree.GetSelectedItem() ?? serverTree.GetSelectedItem();

			if (item is not { IsFile: true } || string.IsNullOrEmpty(item.PropertyPath))
				return;

			SerializedProperty file = serializedObject.FindProperty(item.PropertyPath);
			if (file == null) return;

			GUILayout.Space(10);
			GUILayout.Label("File Editor", EditorStyles.boldLabel);

			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(file.FindPropertyRelative("Name"));
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
				clientTree.Reload();
				serverTree.Reload();
				serializedObject.Update();
			}

			EditorGUILayout.PropertyField(file.FindPropertyRelative("FileContent"));
		}

		private void AddRootFolder(SerializedProperty folders)
		{
			folders.InsertArrayElementAtIndex(folders.arraySize);

			SerializedProperty folder = folders.GetArrayElementAtIndex(folders.arraySize - 1);

			folder.managedReferenceValue = new CustomFolder();

			folder.FindPropertyRelative("Name").stringValue = "New Folder";
			folder.FindPropertyRelative("Files").ClearArray();
			folder.FindPropertyRelative("SubFolders").ClearArray();

			serializedObject.ApplyModifiedProperties();
		}

		private void AddRootFile(SerializedProperty files)
		{
			files.InsertArrayElementAtIndex(files.arraySize);

			SerializedProperty file = files.GetArrayElementAtIndex(files.arraySize - 1);

			file.FindPropertyRelative("Name").stringValue        = "NewFile.txt";
			file.FindPropertyRelative("FileContent").stringValue = string.Empty;

			serializedObject.ApplyModifiedProperties();
		}
	}
}