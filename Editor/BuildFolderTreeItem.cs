using UnityEditor.IMGUI.Controls;

namespace AutoBuildTool.Editor
{
	public class BuildFolderTreeItem : TreeViewItem<int>
	{
		public readonly bool   IsFile;
		public readonly string PropertyPath;

		public BuildFolderTreeItem(int id, int depth, string name, bool isFile, string propertyPath)
			: base(id, depth, name)
		{
			PropertyPath = propertyPath;
			IsFile       = isFile;
		}
	}
}