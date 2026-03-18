using System;
using System.Collections.Generic;
using System.Linq;
using ABS.Build;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ABS
{
	public class BuildFolderTreeView : TreeView<int>
	{
		private class ClipboardData
		{
			public          bool                IsFile;
			public          string              Name;
			public          string              FileContent;
			public readonly List<ClipboardData> SubFolders = new();
			public readonly List<ClipboardData> Files      = new();
		}

		private static readonly List<ClipboardData> clipboard = new();
		private readonly        Texture             fileIcon;

		private readonly Texture            folderIcon;
		private readonly SerializedProperty rootFiles;
		private readonly SerializedProperty rootFolders;
		private readonly SerializedObject   so;

		private int    idCounter;
		public  Action OnSelectionChangedCallback;

		public BuildFolderTreeView(TreeViewState<int> state, SerializedObject so, SerializedProperty folders,
		                           SerializedProperty files)
			: base(state)
		{
			this.so     = so;
			rootFolders = folders;
			rootFiles   = files;

			rowHeight  = 20;
			showBorder = true;

			folderIcon = EditorGUIUtility.IconContent("Folder Icon").image;
			fileIcon   = EditorGUIUtility.IconContent("TextAsset Icon").image;

			Reload();
		}

		protected override TreeViewItem<int> BuildRoot()
		{
			idCounter = 1;

			var root = new TreeViewItem<int> { id = 0, depth = -1, displayName = "Root" };
			var rows = new List<TreeViewItem<int>>();

			if (rootFolders != null)
				for (var i = 0; i < rootFolders.arraySize; i++)
				{
					SerializedProperty folder = rootFolders.GetArrayElementAtIndex(i);
					AddFolderRecursive(rows, folder, folder.propertyPath, 0);
				}

			if (rootFiles != null)
				for (var i = 0; i < rootFiles.arraySize; i++)
				{
					SerializedProperty file     = rootFiles.GetArrayElementAtIndex(i);
					SerializedProperty nameProp = file.FindPropertyRelative("Name");
					if (nameProp != null)
						rows.Add(new BuildFolderTreeItem(idCounter++, 0, nameProp.stringValue, true, file.propertyPath));
				}

			if (rows.Count == 0)
				rows.Add(new BuildFolderTreeItem(idCounter++, 0, "(Empty)", false, string.Empty));

			SetupParentsAndChildrenFromDepths(root, rows);
			return root;
		}

		private void AddFolderRecursive(List<TreeViewItem<int>> rows, SerializedProperty folder, string path, int depth)
		{
			SerializedProperty nameProp = folder.FindPropertyRelative("Name");
			if (nameProp == null) return;

			var name = nameProp.stringValue;

			rows.Add(new BuildFolderTreeItem(idCounter++, depth, name, false, path));

			SerializedProperty files = folder.FindPropertyRelative("Files");
			if (files != null)
				for (var i = 0; i < files.arraySize; i++)
				{
					SerializedProperty file         = files.GetArrayElementAtIndex(i);
					SerializedProperty fileNameProp = file.FindPropertyRelative("Name");
					if (fileNameProp != null)
						rows.Add(new BuildFolderTreeItem(
						                                 idCounter++,
						                                 depth + 1,
						                                 fileNameProp.stringValue,
						                                 true,
						                                 file.propertyPath));
				}

			SerializedProperty sub = folder.FindPropertyRelative("SubFolders");
			if (sub == null) return;
			for (var i = 0; i < sub.arraySize; i++)
			{
				SerializedProperty subElement = sub.GetArrayElementAtIndex(i);
				AddFolderRecursive(
				                   rows,
				                   subElement,
				                   subElement.propertyPath,
				                   depth + 1);
			}
		}

		protected override void SelectionChanged(IList<int> selectedIds)
		{
			base.SelectionChanged(selectedIds);
			OnSelectionChangedCallback?.Invoke();
		}

		public override void OnGUI(Rect rect)
		{
			base.OnGUI(rect);

			Event e = Event.current;
			if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition)) return;
			SetSelection(Array.Empty<int>());
			OnSelectionChangedCallback?.Invoke();
			e.Use();
		}

		private BuildFolderTreeItem GetItem(int id)
		{
			return FindItem(id, rootItem) as BuildFolderTreeItem;
		}

		public BuildFolderTreeItem GetSelectedItem()
		{
			IList<int> sel = GetSelection();

			if (sel == null || sel.Count == 0)
				return null;

			return GetItem(sel[0]);
		}

		protected override void RowGUI(RowGUIArgs args)
		{
			var item = (BuildFolderTreeItem)args.item;

			Rect rect = args.rowRect;
			rect.x += GetContentIndent(item);

			if (!string.IsNullOrEmpty(item.PropertyPath))
			{
				GUI.DrawTexture(
				                new Rect(rect.x, rect.y + 2, 16, 16),
				                item.IsFile ? fileIcon : folderIcon);

				EditorGUI.LabelField(new Rect(rect.x + 20, rect.y, rect.width, rect.height), item.displayName);
			}
			else
			{
				EditorGUI.LabelField(new Rect(rect.x, rect.y, rect.width, rect.height), item.displayName);
			}
		}

		protected override void DoubleClickedItem(int id)
		{
			if (FindItem(id, rootItem) is BuildFolderTreeItem item && !string.IsNullOrEmpty(item.PropertyPath))
				BeginRename(item);
		}

		protected override bool CanRename(TreeViewItem<int> item)
		{
			return true;
		}

		protected override void RenameEnded(RenameEndedArgs args)
		{
			if (!args.acceptedRename)
				return;

			var item = FindItem(args.itemID, rootItem) as BuildFolderTreeItem;

			so.Update();

			if (item != null)
			{
				SerializedProperty prop = so.FindProperty(item.PropertyPath);
				prop.FindPropertyRelative("Name").stringValue = args.newName;
			}

			so.ApplyModifiedProperties();

			Reload();
		}

		protected override void ContextClickedItem(int id)
		{
			if (!GetSelection().Contains(id))
			{
				SetSelection(new List<int> { id });
				SelectionChanged(GetSelection());
			}

			var item         = FindItem(id, rootItem) as BuildFolderTreeItem;
			var hasValidItem = item != null;
			var hasValidPath = hasValidItem && !string.IsNullOrEmpty(item.PropertyPath);

			var menu = new GenericMenu();

			menu.AddItem(new GUIContent("Add Folder"), false, () => AddFolder(hasValidPath ? item : null));
			menu.AddItem(new GUIContent("Add File"), false, () => AddFile(hasValidPath ? item : null));

			menu.AddSeparator("");

			if (hasValidPath)
			{
				menu.AddItem(new GUIContent("Rename\tF2"), false, () => BeginRename(item));
				menu.AddItem(new GUIContent("Copy\tCtrl+C"), false, CopySelection);

				if (clipboard.Count > 0)
					menu.AddItem(new GUIContent("Paste\tCtrl+V"), false, PasteSelection);
				else
					menu.AddDisabledItem(new GUIContent("Paste\tCtrl+V"));

				menu.AddItem(new GUIContent("Duplicate\tCtrl+D"), false, DuplicateSelection);
				menu.AddItem(new GUIContent("Delete\tDel"), false, DeleteSelection);
			}
			else
			{
				var reason = !hasValidItem ? "Item is null" : "PropertyPath is empty";
				menu.AddDisabledItem(new GUIContent($"Rename (Unavailable: {reason})"));
				menu.AddDisabledItem(new GUIContent($"Copy (Unavailable: {reason})"));
				menu.AddDisabledItem(new GUIContent($"Paste (Unavailable: {reason})"));
				menu.AddDisabledItem(new GUIContent($"Duplicate (Unavailable: {reason})"));
				menu.AddDisabledItem(new GUIContent($"Delete (Unavailable: {reason})"));
			}

			menu.ShowAsContext();
			Event.current.Use();
		}

		protected override void ContextClicked()
		{
			var menu = new GenericMenu();

			menu.AddItem(new GUIContent("Add Root Folder"), false, () => AddFolder(null));
			menu.AddItem(new GUIContent("Add Root File"), false, () => AddFile(null));

			menu.AddSeparator("");

			if (clipboard.Count > 0)
				menu.AddItem(new GUIContent("Paste\tCtrl+V"), false, PasteSelection);
			else
				menu.AddDisabledItem(new GUIContent("Paste\tCtrl+V"));

			menu.ShowAsContext();
			Event.current.Use();
		}

		private void AddFolder(BuildFolderTreeItem targetItem)
		{
			so.Update();

			SerializedProperty folders = GetTargetArray(targetItem, false);
			if (folders == null) return;

			var index = folders.arraySize;
			folders.InsertArrayElementAtIndex(index);

			SerializedProperty folder = folders.GetArrayElementAtIndex(index);
			folder.managedReferenceValue = new CustomFolder();

			folder.FindPropertyRelative("Name").stringValue = "New Folder";
			folder.FindPropertyRelative("Files").ClearArray();
			folder.FindPropertyRelative("SubFolders").ClearArray();

			so.ApplyModifiedProperties();
			Reload();
		}

		private void AddFile(BuildFolderTreeItem targetItem)
		{
			so.Update();

			SerializedProperty files = GetTargetArray(targetItem, true);
			if (files == null) return;

			var index = files.arraySize;
			files.InsertArrayElementAtIndex(index);

			SerializedProperty file = files.GetArrayElementAtIndex(index);

			file.FindPropertyRelative("Name").stringValue        = "NewFile.txt";
			file.FindPropertyRelative("FileContent").stringValue = "";

			so.ApplyModifiedProperties();
			Reload();
		}

		private void DuplicateSelection()
		{
			so.Update();

			List<int> selection = GetSelection().ToList();
			selection.Sort();
			selection.Reverse();

			foreach (var id in selection)
			{
				if (FindItem(id, rootItem) is not BuildFolderTreeItem item) continue;
				var parentPath = item.PropertyPath[..item.PropertyPath.LastIndexOf(".Array", StringComparison.Ordinal)];
				SerializedProperty parent = so.FindProperty(parentPath);

				var index = int.Parse(item.PropertyPath.Split('[', ']')[1]);

				ClipboardData originalData = CopyPropertyToData(parent.GetArrayElementAtIndex(index), item.IsFile);

				parent.InsertArrayElementAtIndex(index);
				parent.MoveArrayElement(index, index + 1);

				SerializedProperty newElement                      = parent.GetArrayElementAtIndex(index + 1);
				if (!item.IsFile) newElement.managedReferenceValue = new CustomFolder();
				PasteDataToProperty(originalData, newElement);
			}

			so.ApplyModifiedProperties();
			Reload();
		}

		private void DeleteSelection()
		{
			so.Update();

			List<int> selection = GetSelection().ToList();
			selection.Sort();
			selection.Reverse();

			foreach (var id in selection)
			{
				if (FindItem(id, rootItem) is not BuildFolderTreeItem item) continue;
				var parentPath = item.PropertyPath[..item.PropertyPath.LastIndexOf(".Array", StringComparison.Ordinal)];
				SerializedProperty parent = so.FindProperty(parentPath);

				var index = int.Parse(item.PropertyPath.Split('[', ']')[1]);
				parent.DeleteArrayElementAtIndex(index);
			}

			so.ApplyModifiedProperties();
			Reload();
		}

		private void CopySelection()
		{
			clipboard.Clear();
			foreach (var id in GetSelection())
			{
				if (FindItem(id, rootItem) is not BuildFolderTreeItem item ||
				    string.IsNullOrEmpty(item.PropertyPath)) continue;

				SerializedProperty prop = so.FindProperty(item.PropertyPath);
				if (prop != null) clipboard.Add(CopyPropertyToData(prop, item.IsFile));
			}
		}

		private void PasteSelection()
		{
			if (clipboard.Count == 0) return;

			so.Update();

			BuildFolderTreeItem targetItem = GetSelectedItem();

			if (targetItem != null && string.IsNullOrEmpty(targetItem.PropertyPath))
				targetItem = null;

			foreach (ClipboardData data in clipboard)
			{
				SerializedProperty targetArray = GetTargetArray(targetItem, data.IsFile);
				if (targetArray == null) continue;

				var index = targetArray.arraySize;
				targetArray.InsertArrayElementAtIndex(index);
				SerializedProperty newElement = targetArray.GetArrayElementAtIndex(index);

				if (!data.IsFile) newElement.managedReferenceValue = new CustomFolder();

				PasteDataToProperty(data, newElement);
			}

			so.ApplyModifiedProperties();
			Reload();
		}

		private SerializedProperty GetTargetArray(BuildFolderTreeItem targetItem, bool isFilePaste)
		{
			if (targetItem == null || string.IsNullOrEmpty(targetItem.PropertyPath))
				return isFilePaste ? rootFiles : rootFolders;

			if (!targetItem.IsFile)
				return so.FindProperty(targetItem.PropertyPath).FindPropertyRelative(isFilePaste ? "Files" : "SubFolders");

			var filesIndex = targetItem.PropertyPath.IndexOf(".Files", StringComparison.Ordinal);
			if (filesIndex == -1) return isFilePaste ? rootFiles : rootFolders;

			if (isFilePaste)
			{
				var arrayPath =
					targetItem.PropertyPath[..targetItem.PropertyPath.LastIndexOf(".Array", StringComparison.Ordinal)];
				return so.FindProperty(arrayPath);
			}

			var folderPath = targetItem.PropertyPath[..filesIndex];
			return so.FindProperty(folderPath).FindPropertyRelative("SubFolders");
		}

		private static ClipboardData CopyPropertyToData(SerializedProperty prop, bool isFile)
		{
			var data = new ClipboardData
			           {
				           IsFile = isFile,
				           Name   = prop.FindPropertyRelative("Name").stringValue
			           };

			if (isFile)
			{
				data.FileContent = prop.FindPropertyRelative("FileContent").stringValue;
			}
			else
			{
				SerializedProperty filesProp = prop.FindPropertyRelative("Files");
				for (var i = 0; i < filesProp.arraySize; i++)
					data.Files.Add(CopyPropertyToData(filesProp.GetArrayElementAtIndex(i), true));

				SerializedProperty subProp = prop.FindPropertyRelative("SubFolders");
				for (var i = 0; i < subProp.arraySize; i++)
					data.SubFolders.Add(CopyPropertyToData(subProp.GetArrayElementAtIndex(i), false));
			}

			return data;
		}

		private static void PasteDataToProperty(ClipboardData data, SerializedProperty prop)
		{
			prop.FindPropertyRelative("Name").stringValue = data.Name;

			if (data.IsFile)
			{
				prop.FindPropertyRelative("FileContent").stringValue = data.FileContent;
			}
			else
			{
				SerializedProperty filesProp = prop.FindPropertyRelative("Files");
				filesProp.ClearArray();
				for (var i = 0; i < data.Files.Count; i++)
				{
					filesProp.InsertArrayElementAtIndex(i);
					PasteDataToProperty(data.Files[i], filesProp.GetArrayElementAtIndex(i));
				}

				SerializedProperty subProp = prop.FindPropertyRelative("SubFolders");
				subProp.ClearArray();
				for (var i = 0; i < data.SubFolders.Count; i++)
				{
					subProp.InsertArrayElementAtIndex(i);
					SerializedProperty subElement = subProp.GetArrayElementAtIndex(i);
					subElement.managedReferenceValue = new CustomFolder();
					PasteDataToProperty(data.SubFolders[i], subElement);
				}
			}
		}

		protected override void KeyEvent()
		{
			if (EditorGUIUtility.editingTextField || !HasFocus()) return;

			Event e = Event.current;
			if (e.type != EventType.KeyDown) return;

			BuildFolderTreeItem item         = GetSelectedItem();
			var                 hasValidItem = item != null && !string.IsNullOrEmpty(item.PropertyPath);

			switch (e.control)
			{
				case true when e.keyCode == KeyCode.V:
					PasteSelection();
					e.Use();
					return;
				case true when e.shift && e.keyCode == KeyCode.N:
					AddFolder(hasValidItem ? item : null);
					e.Use();
					return;
				case true when !e.shift && e.keyCode == KeyCode.N:
					AddFile(hasValidItem ? item : null);
					e.Use();
					return;
			}

			if (!hasValidItem) return;

			switch (e.control)
			{
				case true when e.keyCode == KeyCode.C:
					CopySelection();
					e.Use();
					break;
				case true when e.keyCode == KeyCode.D:
					DuplicateSelection();
					e.Use();
					break;
				default:
					switch (e.keyCode)
					{
						case KeyCode.Delete:
							DeleteSelection();
							e.Use();
							break;
						case KeyCode.R:
							BeginRename(item);
							e.Use();
							break;
					}

					break;
			}
		}
	}
}