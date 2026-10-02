using System;
using System.Collections.Generic;
using System.Linq;
using AutoBuildTool.Editor.Build;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AutoBuildTool.Editor
{
	public class BuildFolderTreeView : TreeView<int>
	{
		// Static on purpose: sharing one clipboard between the client and server trees is what
		// makes copying a folder from one side and pasting it into the other work.
		private static readonly List<ClipboardData> clipboard = new();

		private readonly HashSet<int> usedIds = new();
		private readonly Texture      fileIcon;

		private readonly Texture            folderIcon;
		private readonly SerializedProperty rootFiles;
		private readonly SerializedProperty rootFolders;
		private readonly SerializedObject   so;
		public           Action             OnSelectionChangedCallback;

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
			usedIds.Clear();

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
						rows.Add(new BuildFolderTreeItem(StableId(file.propertyPath), 0, nameProp.stringValue, true,
						                                 file.propertyPath));
				}

			if (rows.Count == 0)
				rows.Add(new BuildFolderTreeItem(int.MaxValue, 0, "(Empty)", false, string.Empty));

			SetupParentsAndChildrenFromDepths(root, rows);
			return root;
		}

		private void AddFolderRecursive(List<TreeViewItem<int>> rows, SerializedProperty folder, string path, int depth)
		{
			SerializedProperty nameProp = folder.FindPropertyRelative("Name");
			if (nameProp == null) return;

			var name = nameProp.stringValue;

			rows.Add(new BuildFolderTreeItem(StableId(path), depth, name, false, path));

			SerializedProperty files = folder.FindPropertyRelative("Files");
			if (files != null)
				for (var i = 0; i < files.arraySize; i++)
				{
					SerializedProperty file         = files.GetArrayElementAtIndex(i);
					SerializedProperty fileNameProp = file.FindPropertyRelative("Name");
					if (fileNameProp != null)
						rows.Add(new BuildFolderTreeItem(
						                                 StableId(file.propertyPath),
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

		// The element's own index is the LAST bracket in the path. Reading the first bracket
		// returns an ancestor's index, which resolves to the wrong sibling for nested items.
		private static int GetElementIndex(string propertyPath)
		{
			if (string.IsNullOrEmpty(propertyPath)) return -1;

			var open  = propertyPath.LastIndexOf('[');
			var close = propertyPath.LastIndexOf(']');
			if (open < 0 || close < open) return -1;

			return int.TryParse(propertyPath.Substring(open + 1, close - open - 1), out var index) ? index : -1;
		}

		// Derived from the property path so a stored selection still resolves to the same element
		// after Reload(); a traversal-order counter would remap selections on any structural change.
		// Ids must stay unique for TreeView, so a collision probes to the next free value.
		private int StableId(string propertyPath)
		{
			unchecked
			{
				var hash = 5381;
				foreach (var c in propertyPath) hash = (hash * 33) ^ c;

				// 0 is the root and int.MaxValue is the placeholder row.
				while (hash == 0 || hash == int.MaxValue || !usedIds.Add(hash)) hash++;

				return hash;
			}
		}

		// Positions of two elements in the serialized data, parents before their contents.
		private static int CompareDocumentOrder(string pathA, string pathB)
		{
			List<int> indicesA = GetPathIndices(pathA);
			List<int> indicesB = GetPathIndices(pathB);

			var shared = Math.Min(indicesA.Count, indicesB.Count);
			for (var i = 0; i < shared; i++)
				if (indicesA[i] != indicesB[i])
					return indicesA[i].CompareTo(indicesB[i]);

			// Same prefix: the deeper path is nested inside the shallower one.
			return indicesA.Count != indicesB.Count
				       ? indicesA.Count.CompareTo(indicesB.Count)
				       : string.CompareOrdinal(pathA, pathB);
		}

		private static List<int> GetPathIndices(string propertyPath)
		{
			var indices = new List<int>();
			if (string.IsNullOrEmpty(propertyPath)) return indices;

			for (var i = 0; i < propertyPath.Length; i++)
			{
				if (propertyPath[i] != '[') continue;

				var close = propertyPath.IndexOf(']', i + 1);
				if (close < 0) break;

				if (int.TryParse(propertyPath.Substring(i + 1, close - i - 1), out var value)) indices.Add(value);
				i = close;
			}

			return indices;
		}

		// Selected items ordered by position in the serialized data. Mutating operations must run
		// in reverse: touching an array shifts the indices of everything after it, and removing a
		// folder invalidates every path nested inside it. Ordering by id would only work while ids
		// were assigned in traversal order.
		private List<BuildFolderTreeItem> GetSelectedItems(bool reverse)
		{
			List<BuildFolderTreeItem> items = GetSelection()
			                                  .Select(id => FindItem(id, rootItem) as BuildFolderTreeItem)
			                                  .Where(i => i != null && !string.IsNullOrEmpty(i.PropertyPath))
			                                  .ToList();

			items.Sort((a, b) => CompareDocumentOrder(a.PropertyPath, b.PropertyPath));
			if (reverse) items.Reverse();

			return items;
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
				// A stale PropertyPath resolves to null after a structural change.
				SerializedProperty prop = so.FindProperty(item.PropertyPath);
				if (prop != null) prop.FindPropertyRelative("Name").stringValue = args.newName;
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

			ResetFolderElement(folders.GetArrayElementAtIndex(index));

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

			ResetFileElement(files.GetArrayElementAtIndex(index));

			so.ApplyModifiedProperties();
			Reload();
		}

		// CustomFile is a struct, and InsertArrayElementAtIndex duplicates the preceding element
		// rather than zero-initialising it. Every field must be reset explicitly or a new entry
		// silently inherits the previous one's operation type and source asset.
		internal static void ResetFileElement(SerializedProperty file)
		{
			file.FindPropertyRelative("Name").stringValue                 = "NewFile.txt";
			file.FindPropertyRelative("FileContent").stringValue          = string.Empty;
			file.FindPropertyRelative("OperationType").enumValueIndex     = (int)FileOperationType.CreateTextFile;
			file.FindPropertyRelative("SourceAsset").objectReferenceValue = null;
		}

		// Shared with the inspector's "Add Root Folder" button so both paths stay in step.
		internal static void ResetFolderElement(SerializedProperty folder)
		{
			folder.managedReferenceValue = new CustomFolder();

			folder.FindPropertyRelative("Name").stringValue = "New Folder";
			folder.FindPropertyRelative("Files").ClearArray();
			folder.FindPropertyRelative("SubFolders").ClearArray();
		}

		private void DuplicateSelection()
		{
			so.Update();

			foreach (BuildFolderTreeItem item in GetSelectedItems(true))
			{
				var parentPath = item.PropertyPath[..item.PropertyPath.LastIndexOf(".Array", StringComparison.Ordinal)];
				SerializedProperty parent = so.FindProperty(parentPath);
				if (parent == null) continue;

				var index = GetElementIndex(item.PropertyPath);
				if (index < 0) continue;

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

			foreach (BuildFolderTreeItem item in GetSelectedItems(true))
			{
				var parentPath = item.PropertyPath[..item.PropertyPath.LastIndexOf(".Array", StringComparison.Ordinal)];
				SerializedProperty parent = so.FindProperty(parentPath);
				if (parent == null) continue;

				var index = GetElementIndex(item.PropertyPath);
				if (index < 0) continue;
				parent.DeleteArrayElementAtIndex(index);
			}

			so.ApplyModifiedProperties();
			Reload();
		}

		private void CopySelection()
		{
			clipboard.Clear();
			// Document order, so a multi-selection pastes back in the order it appears on screen.
			foreach (BuildFolderTreeItem item in GetSelectedItems(false))
			{
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
				return so.FindProperty(targetItem.PropertyPath)
				         .FindPropertyRelative(isFilePaste ? "Files" : "SubFolders");

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
				data.FileContent   = prop.FindPropertyRelative("FileContent").stringValue;
				data.OperationType = prop.FindPropertyRelative("OperationType").enumValueIndex;
				data.SourceAsset   = prop.FindPropertyRelative("SourceAsset").objectReferenceValue;
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
				prop.FindPropertyRelative("FileContent").stringValue          = data.FileContent;
				prop.FindPropertyRelative("OperationType").enumValueIndex     = data.OperationType;
				prop.FindPropertyRelative("SourceAsset").objectReferenceValue = data.SourceAsset;
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
					// ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
					switch (e.keyCode)
					{
						case KeyCode.Delete:
							DeleteSelection();
							e.Use();
							break;
						// The context menu advertises F2; R is kept as the existing shortcut.
						case KeyCode.F2:
						case KeyCode.R:
							BeginRename(item);
							e.Use();
							break;
					}

					break;
			}
		}

		private class ClipboardData
		{
			public readonly List<ClipboardData> Files      = new();
			public readonly List<ClipboardData> SubFolders = new();
			public          string              FileContent;
			public          bool                IsFile;
			public          string              Name;
			public          int                 OperationType;
			public          Object              SourceAsset;
		}
	}
}